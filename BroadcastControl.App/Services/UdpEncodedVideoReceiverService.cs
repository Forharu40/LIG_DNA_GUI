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
            _udpClient = new UdpClient();
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

        if (LooksLikeJpeg(packet))
        {
            TryDecodeFrame(packet, 0, 0);
            return;
        }

        if (packet.Length <= HeaderSize)
        {
            return;
        }

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
            using var decoded = Cv2.ImDecode(encodedFrame, ImreadModes.Color);
            if (decoded.Empty())
            {
                return;
            }

            if (declaredWidth > 0 && declaredHeight > 0 &&
                (decoded.Width != declaredWidth || decoded.Height != declaredHeight))
            {
                // If the declared size differs, keep rendering the successfully decoded frame.
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
