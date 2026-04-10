using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace BroadcastControl.App.Services;

/// <summary>
/// EO 카메라 UDP JPEG 프레임 수신/디코드 서비스임.
/// Python struct.pack("!QIIHH", ...) 기반 20바이트 헤더와 JPEG 바디를 처리함.
/// 현재는 한 UDP 데이터그램에 헤더와 JPEG 한 장이 모두 들어온다는 가정임.
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
    /// 디코드 완료된 EO 프레임을 UI로 전달하는 이벤트임.
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
            // 종료 중 소켓이 이미 닫혀도 자원 정리는 계속함.
        }

        _udpClient?.Dispose();
        _udpClient = null;

        try
        {
            _receiveLoopTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // 수신 루프 종료 대기 중 예외가 나도 종료 흐름은 유지함.
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
    /// EO 화면의 전자 줌/패닝 상태를 녹화 영상 구도에 반영함.
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

        // UTC 시간과 프레임 인덱스는 향후 VLM/YOLO 동기화용으로 파싱만 유지함.
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

            if (declaredWidth > 0 && declaredHeight > 0 &&
                (decoded.Width != declaredWidth || decoded.Height != declaredHeight))
            {
                // 헤더 크기와 실제 디코드 크기가 달라도 디코드 성공 시 화면 표시는 유지함.
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
            // 손상된 JPEG 또는 디코드 불가 프레임은 버리고 다음 프레임을 기다림.
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
