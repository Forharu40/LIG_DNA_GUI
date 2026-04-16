using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace BroadcastControl.App.Services;

public sealed class UdpEncodedVideoReceiverService : IDisposable
{
    private const int DefaultPort = 5000;
    // 송신기 쪽에서 JPEG 데이터 앞에 20바이트 길이의 사용자 정의 헤더를 붙일 수 있다.
    private const int HeaderSize = 20;

    private readonly Dispatcher _dispatcher;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveLoopTask;
    private VideoWriter? _writer;
    private string? _recordingPath;
    private string? _recordingErrorMessage;
    private int _recordedFrameCount;
    private OpenCvSharp.Size _recordingOutputSize;
    private Mat? _latestRecordableFrame;
    private bool _isRecording;
    private double _brightness = 50;
    private double _contrast = 50;
    private double _zoomLevel = 1.0;
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;

    public UdpEncodedVideoReceiverService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public event Action<BitmapSource>? FrameReady;

    public int ListeningPort { get; private set; } = DefaultPort;

    public string? LastRecordingErrorMessage => _recordingErrorMessage;

    public int RecordedFrameCount => _recordedFrameCount;

    public bool Start(int port = DefaultPort)
    {
        if (_udpClient is not null)
        {
            return true;
        }

        try
        {
            ListeningPort = port;
            _udpClient = new UdpClient();
            // 로컬 테스트를 반복 실행하거나 재시작했을 때 포트 바인딩 실패를 줄이기 위한 설정이다.
            _udpClient.Client.ExclusiveAddressUse = false;
            _udpClient.Client.ReceiveBufferSize = 4 * 1024 * 1024;
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            _cancellationTokenSource = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
            return true;
        }
        catch
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();

        try
        {
            _udpClient?.Close();
        }
        catch
        {
        }

        _udpClient?.Dispose();
        _udpClient = null;

        try
        {
            _receiveLoopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _receiveLoopTask = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        StopRecording();
    }

    public void SetBrightness(double value)
    {
        _brightness = Math.Clamp(value, 0, 100);
    }

    public void SetContrast(double value)
    {
        _contrast = Math.Clamp(value, 0, 100);
    }

    public void UpdateViewportTransform(double zoomLevel, double panX, double panY, double viewportWidth, double viewportHeight)
    {
        _zoomLevel = Math.Clamp(zoomLevel, 1.0, 4.0);
        _zoomPanX = panX;
        _zoomPanY = panY;
        _viewportWidth = Math.Max(viewportWidth, 1);
        _viewportHeight = Math.Max(viewportHeight, 1);
    }

    public string StartRecordingToDesktop()
    {
        if (_isRecording && !string.IsNullOrWhiteSpace(_recordingPath))
        {
            return _recordingPath;
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
        {
            desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Desktop");
        }

        Directory.CreateDirectory(desktopPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _recordingPath = Path.Combine(desktopPath, $"video_{timestamp}.avi");
        _recordingErrorMessage = null;
        _recordedFrameCount = 0;
        _recordingOutputSize = default;
        _isRecording = true;

        if (_latestRecordableFrame is not null && !_latestRecordableFrame.Empty())
        {
            using var initialFrame = CreateRecordedFrame(_latestRecordableFrame);
            EnsureVideoWriter(initialFrame.Width, initialFrame.Height);
            WriteRecordingFrame(initialFrame);
        }

        return _recordingPath;
    }

    public string? StopRecording()
    {
        _isRecording = false;
        _writer?.Release();
        _writer?.Dispose();
        _writer = null;

        var savedPath = _recordingPath;
        _recordingPath = null;
        return savedPath;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_udpClient is null)
                {
                    break;
                }

                var receiveResult = await _udpClient.ReceiveAsync(cancellationToken);
                TryProcessPacket(receiveResult.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private void TryProcessPacket(byte[] packet)
    {
        if (packet.Length == 0)
        {
            return;
        }

        // 가장 단순한 경우는 헤더 없이 JPEG 원본만 바로 들어오는 경우다.
        if (LooksLikeJpeg(packet))
        {
            TryDecodeFrame(packet, 0, 0);
            return;
        }

        if (packet.Length <= HeaderSize)
        {
            return;
        }

        // 송신기 구현에 따라 헤더의 바이트 순서가 달라질 수 있어서
        // big-endian / little-endian 두 형식을 모두 시도한다.
        if (TryExtractEncodedFrame(packet, useBigEndian: true, out var bigEndianFrame))
        {
            TryDecodeFrame(bigEndianFrame.EncodedBytes, bigEndianFrame.DeclaredWidth, bigEndianFrame.DeclaredHeight);
            return;
        }

        if (TryExtractEncodedFrame(packet, useBigEndian: false, out var littleEndianFrame))
        {
            TryDecodeFrame(littleEndianFrame.EncodedBytes, littleEndianFrame.DeclaredWidth, littleEndianFrame.DeclaredHeight);
        }
    }

    private void TryDecodeFrame(byte[] encodedFrame, ushort declaredWidth, ushort declaredHeight)
    {
        try
        {
            // 먼저 JPEG 디코딩이 실제로 되는지 확인한다.
            // 헤더에 적힌 크기와 조금 달라도 디코딩에 성공한 프레임은 화면에 표시한다.
            using var decoded = Cv2.ImDecode(encodedFrame, ImreadModes.Color);
            if (decoded.Empty())
            {
                return;
            }

            if (declaredWidth > 0 && declaredHeight > 0 &&
                (decoded.Width != declaredWidth || decoded.Height != declaredHeight))
            {
                // 헤더상의 크기 정보가 실제 영상 크기와 다르더라도,
                // 디코딩된 영상 자체는 정상일 수 있으므로 계속 사용한다.
            }

            using var adjusted = new Mat();
            var alpha = 0.5 + (_contrast / 100.0);
            var beta = (_brightness - 50.0) * 2.0;
            decoded.ConvertTo(adjusted, MatType.CV_8UC3, alpha, beta);

            _latestRecordableFrame?.Dispose();
            _latestRecordableFrame = adjusted.Clone();

            if (_isRecording)
            {
                // 녹화는 항상 원본 전체 화면이 아니라,
                // 사용자가 현재 보고 있는 줌/이동 상태의 화면 기준으로 맞춘다.
                using var recordingFrame = CreateRecordedFrame(adjusted);
                EnsureVideoWriter(recordingFrame.Width, recordingFrame.Height);
                WriteRecordingFrame(recordingFrame);
            }

            using var converted = new Mat();
            Cv2.CvtColor(adjusted, converted, ColorConversionCodes.BGR2BGRA);

            var bufferSize = checked((int)(converted.Step() * converted.Rows));
            var stride = checked((int)converted.Step());

            var bitmap = BitmapSource.Create(
                converted.Width,
                converted.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                converted.Data,
                bufferSize,
                stride);

            bitmap.Freeze();
            _dispatcher.BeginInvoke(() => FrameReady?.Invoke(bitmap));
        }
        catch
        {
        }
    }

    private Mat CreateRecordedFrame(Mat source)
    {
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;
        var viewportAspect = _viewportWidth / _viewportHeight;
        var sourceAspect = (double)sourceWidth / sourceHeight;

        double baseX;
        double baseY;
        double baseWidth;
        double baseHeight;

        // 먼저 현재 화면 비율과 맞는 기준 영역(base 영역)을 계산한다.
        if (sourceAspect > viewportAspect)
        {
            baseHeight = sourceHeight;
            baseWidth = sourceHeight * viewportAspect;
            baseX = (sourceWidth - baseWidth) / 2.0;
            baseY = 0;
        }
        else
        {
            baseWidth = sourceWidth;
            baseHeight = sourceWidth / viewportAspect;
            baseX = 0;
            baseY = (sourceHeight - baseHeight) / 2.0;
        }

        var cropWidth = baseWidth / _zoomLevel;
        var cropHeight = baseHeight / _zoomLevel;
        var maxPanX = (_viewportWidth * (_zoomLevel - 1.0)) / 2.0;
        var maxPanY = (_viewportHeight * (_zoomLevel - 1.0)) / 2.0;

        var remainingX = baseWidth - cropWidth;
        var remainingY = baseHeight - cropHeight;
        var offsetX = GetViewportOffset(_zoomPanX, maxPanX, remainingX);
        var offsetY = GetViewportOffset(_zoomPanY, maxPanY, remainingY);

        // 그 다음 줌/이동 값에 따라 잘라내고,
        // 최종 저장 영상의 해상도가 흔들리지 않도록 기준 크기로 다시 맞춘다.
        var cropRect = new Rect(
            (int)Math.Clamp(Math.Round(baseX + offsetX), 0, sourceWidth - 1),
            (int)Math.Clamp(Math.Round(baseY + offsetY), 0, sourceHeight - 1),
            Math.Max(1, (int)Math.Clamp(Math.Round(cropWidth), 1, sourceWidth)),
            Math.Max(1, (int)Math.Clamp(Math.Round(cropHeight), 1, sourceHeight)));

        if (cropRect.X + cropRect.Width > sourceWidth)
        {
            cropRect.Width = sourceWidth - cropRect.X;
        }

        if (cropRect.Y + cropRect.Height > sourceHeight)
        {
            cropRect.Height = sourceHeight - cropRect.Y;
        }

        using var cropped = new Mat(source, cropRect);
        var output = new Mat();
        Cv2.Resize(
            cropped,
            output,
            new OpenCvSharp.Size(
                Math.Max(1, (int)Math.Round(baseWidth)),
                Math.Max(1, (int)Math.Round(baseHeight))));
        return output;
    }

    private static double GetViewportOffset(double pan, double maxPan, double remainingSize)
    {
        if (maxPan <= 0 || remainingSize <= 0)
        {
            return remainingSize / 2.0;
        }

        var normalized = (pan + maxPan) / (maxPan * 2.0);
        return (1.0 - normalized) * remainingSize;
    }

    private void EnsureVideoWriter(int width, int height)
    {
        if (_writer is not null || string.IsNullOrWhiteSpace(_recordingPath))
        {
            return;
        }

        _recordingOutputSize = NormalizeRecordingSize(width, height);
        var attempts = new List<string>();
        var strategies = new[]
        {
            (Api: VideoCaptureAPIs.OPENCV_MJPEG, Codec: FourCC.MJPG, Name: "OPENCV_MJPEG/MJPG"),
            (Api: VideoCaptureAPIs.MSMF, Codec: FourCC.MJPG, Name: "MSMF/MJPG"),
            (Api: VideoCaptureAPIs.FFMPEG, Codec: FourCC.MJPG, Name: "FFMPEG/MJPG"),
            (Api: VideoCaptureAPIs.ANY, Codec: FourCC.MJPG, Name: "ANY/MJPG"),
            (Api: VideoCaptureAPIs.MSMF, Codec: FourCC.XVID, Name: "MSMF/XVID"),
            (Api: VideoCaptureAPIs.FFMPEG, Codec: FourCC.XVID, Name: "FFMPEG/XVID"),
            (Api: VideoCaptureAPIs.ANY, Codec: FourCC.XVID, Name: "ANY/XVID"),
        };

        foreach (var strategy in strategies)
        {
            VideoWriter? writer = null;
            try
            {
                writer = new VideoWriter(
                    _recordingPath,
                    strategy.Api,
                    strategy.Codec,
                    30,
                    _recordingOutputSize,
                    true);

                if (writer.IsOpened())
                {
                    _writer = writer;
                    _recordingErrorMessage = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                attempts.Add($"{strategy.Name}: {ex.Message}");
            }
            finally
            {
                if (writer is not null && _writer is null)
                {
                    writer.Dispose();
                }
            }

            attempts.Add(strategy.Name);
        }

        _recordingErrorMessage = "Could not open a video writer for the desktop recording path. Tried: "
            + string.Join(", ", attempts);
    }

    private static OpenCvSharp.Size NormalizeRecordingSize(int width, int height)
    {
        var normalizedWidth = Math.Max(2, width);
        var normalizedHeight = Math.Max(2, height);

        if ((normalizedWidth & 1) != 0)
        {
            normalizedWidth--;
        }

        if ((normalizedHeight & 1) != 0)
        {
            normalizedHeight--;
        }

        return new OpenCvSharp.Size(normalizedWidth, normalizedHeight);
    }

    private void WriteRecordingFrame(Mat frame)
    {
        if (_writer is null || !_writer.IsOpened())
        {
            return;
        }

        if (frame.Size() == _recordingOutputSize)
        {
            _writer.Write(frame);
        }
        else
        {
            using var resizedFrame = new Mat();
            Cv2.Resize(frame, resizedFrame, _recordingOutputSize);
            _writer.Write(resizedFrame);
        }

        _recordedFrameCount++;
    }

    public void Dispose()
    {
        Stop();
        _latestRecordableFrame?.Dispose();
        _latestRecordableFrame = null;
    }

    private static bool LooksLikeJpeg(IReadOnlyList<byte> packet)
    {
        return packet.Count >= 4
            && packet[0] == 0xFF
            && packet[1] == 0xD8
            && packet[^2] == 0xFF
            && packet[^1] == 0xD9;
    }

    private static bool TryExtractEncodedFrame(byte[] packet, bool useBigEndian, out EncodedFrame frame)
    {
        frame = default;

        if (packet.Length <= HeaderSize)
        {
            return false;
        }

        var header = packet.AsSpan(0, HeaderSize);
        var payload = packet.AsSpan(HeaderSize);

        // 헤더 구조는 다음과 같이 해석한다.
        // [12..15] JPEG 데이터 길이
        // [16..17] 선언된 영상 너비
        // [18..19] 선언된 영상 높이
        var imageByteLength = useBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));

        var declaredWidth = useBigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(header.Slice(16, 2))
            : BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(16, 2));

        var declaredHeight = useBigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(header.Slice(18, 2))
            : BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(18, 2));

        if (imageByteLength == 0 || imageByteLength > payload.Length)
        {
            return false;
        }

        if ((declaredWidth == 0) != (declaredHeight == 0))
        {
            return false;
        }

        if (declaredWidth > 10000 || declaredHeight > 10000)
        {
            return false;
        }

        // 헤더 값이 멀쩡해 보여도 실제 JPEG가 아니면 잘못된 패킷으로 보고 버린다.
        var encodedBytes = payload[..checked((int)imageByteLength)].ToArray();
        if (!LooksLikeJpeg(encodedBytes))
        {
            return false;
        }

        frame = new EncodedFrame(encodedBytes, declaredWidth, declaredHeight);
        return true;
    }

    private readonly record struct EncodedFrame(byte[] EncodedBytes, ushort DeclaredWidth, ushort DeclaredHeight);
}
