using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace BroadcastControl.App.Services;

/// <summary>
/// 외부 EO 카메라가 UDP로 보내는 JPEG 프레임을 받아 디코드하는 서비스이다.
///
/// 현재 지원하는 패킷 형식은 Python의 struct.pack("!QIIHH", ...) 기준이다.
/// - utc time: uint64 (big-endian)
/// - frame index: uint32 (big-endian)
/// - image byte length: uint32 (big-endian)
/// - image width: uint16 (big-endian)
/// - image height: uint16 (big-endian)
/// - image bytes: JPEG 바이트
///
/// 여기서 "!"는 실제 데이터 바이트가 아니라 Python struct의 네트워크 바이트 오더 지정자이다.
/// 따라서 헤더는 20바이트이며, 한 개의 UDP 데이터그램 안에 "20바이트 헤더 + JPEG 한 장"이 모두 들어온다고 가정한다.
/// 만약 송신 쪽이 프레임을 여러 패킷으로 나눠 보낸다면 그에 맞는 재조립 로직이 추가로 필요하다.
/// </summary>
public sealed class UdpEncodedVideoReceiverService : IDisposable
{
    private const int DefaultPort = 5000;
    private const int HeaderSize = 20;

    private readonly Dispatcher _dispatcher;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveLoopTask;
    private VideoWriter? _writer;
    private string? _recordingPath;
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

    /// <summary>
    /// 디코드가 끝난 EO 프레임을 UI로 전달한다.
    /// </summary>
    public event Action<BitmapSource>? FrameReady;

    public int ListeningPort { get; private set; } = DefaultPort;

    public bool Start(int port = DefaultPort)
    {
        if (_udpClient is not null)
        {
            return true;
        }

        try
        {
            ListeningPort = port;
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _udpClient.Client.ReceiveBufferSize = 4 * 1024 * 1024;
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
            // 종료 중 소켓이 이미 닫혀 있더라도 별도 처리 없이 자원 정리를 계속한다.
        }

        _udpClient?.Dispose();
        _udpClient = null;

        try
        {
            _receiveLoopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // 비동기 루프 종료를 기다리는 동안 예외가 나더라도 종료 흐름은 유지한다.
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

    /// <summary>
    /// EO 화면에 적용된 전자 줌/패닝 상태를 받아 녹화 영상에도 같은 구도가 기록되도록 한다.
    /// </summary>
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
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _recordingPath = Path.Combine(desktopPath, $"video_{timestamp}.avi");
        _isRecording = true;
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
            UdpReceiveResult receiveResult;

            try
            {
                if (_udpClient is null)
                {
                    break;
                }

                receiveResult = await _udpClient.ReceiveAsync(cancellationToken);
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
                continue;
            }

            TryProcessPacket(receiveResult.Buffer);
        }
    }

    private void TryProcessPacket(byte[] packet)
    {
        if (packet.Length <= HeaderSize)
        {
            return;
        }

        var header = packet.AsSpan(0, HeaderSize);

        // 현재는 UTC 시간과 프레임 인덱스를 화면에는 쓰지 않지만,
        // 이후 Yolo/VLM 결과 동기화에 사용할 수 있도록 파싱은 유지한다.
        _ = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(0, 8));
        _ = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4));
        var imageByteLength = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4));
        var declaredWidth = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(16, 2));
        var declaredHeight = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(18, 2));

        var payload = packet.AsSpan(HeaderSize);
        if (imageByteLength == 0 || payload.Length < imageByteLength)
        {
            return;
        }

        TryDecodeFrame(payload[..checked((int)imageByteLength)].ToArray(), declaredWidth, declaredHeight);
    }

    private void TryDecodeFrame(byte[] encodedFrame, ushort declaredWidth, ushort declaredHeight)
    {
        try
        {
            using var decoded = Cv2.ImDecode(encodedFrame, ImreadModes.Color);
            if (decoded.Empty())
            {
                return;
            }

            // 송신 헤더에 적힌 크기와 실제 디코드 크기가 다를 수 있어, 문제를 잡기 쉽게 최소 검증만 해둔다.
            if (declaredWidth > 0 && declaredHeight > 0 &&
                (decoded.Width != declaredWidth || decoded.Height != declaredHeight))
            {
                // 크기 불일치가 있어도 실제 프레임이 디코드되면 화면에는 표시한다.
            }

            using var adjusted = new Mat();
            var alpha = 0.5 + (_contrast / 100.0);
            var beta = (_brightness - 50.0) * 2.0;
            decoded.ConvertTo(adjusted, MatType.CV_8UC3, alpha, beta);

            if (_isRecording)
            {
                using var recordingFrame = CreateRecordedFrame(adjusted);
                EnsureVideoWriter(recordingFrame.Width, recordingFrame.Height);
                _writer?.Write(recordingFrame);
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
            // 손상된 JPEG나 디코드 불가 프레임은 버리고 다음 프레임을 기다린다.
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

        _writer = new VideoWriter(
            _recordingPath,
            FourCC.MJPG,
            30,
            new OpenCvSharp.Size(width, height));
    }

    public void Dispose()
    {
        Stop();
    }
}
