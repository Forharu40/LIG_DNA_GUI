using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace BroadcastControl.App.Services;

public sealed class UdpEncodedVideoReceiverService : IDisposable
{
    private const int DefaultPort = 5000;
    private const int LegacyHeaderSize = 20;
    private const int MetadataPacketSize = 36;
    private static readonly byte[] DetectionPacketMagic = "DETS"u8.ToArray();
    private static readonly byte[] StatusPacketMagic = "STAT"u8.ToArray();

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
    private string? _lastSegmentSignature;
    private uint? _lastCycleIndex;
    private long _receivedPacketCount;
    private long _metadataPacketCount;
    private long _decodeFailureCount;
    private long _unknownPacketCount;

    public UdpEncodedVideoReceiverService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public event Action<ReceivedVideoFrame>? FrameReady;
    public event Action<PlaybackSegmentInfo>? SegmentChanged;
    public event Action<PlaybackSegmentInfo>? SegmentLoopRestarted;
    public event Action<DetectionPacket>? DetectionsReceived;
    public event Action<YoloStatusPacket>? StatusReceived;
    public event Action<string>? DiagnosticsMessageReady;

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
            _udpClient.Client.ExclusiveAddressUse = false;
            _udpClient.Client.ReceiveBufferSize = 4 * 1024 * 1024;
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            _lastSegmentSignature = null;
            _lastCycleIndex = null;
            _receivedPacketCount = 0;
            _metadataPacketCount = 0;
            _decodeFailureCount = 0;
            _unknownPacketCount = 0;
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
        _lastSegmentSignature = null;
        _lastCycleIndex = null;
        _receivedPacketCount = 0;
        _metadataPacketCount = 0;
        _decodeFailureCount = 0;
        _unknownPacketCount = 0;
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
                TryProcessPacket(receiveResult.Buffer, receiveResult.RemoteEndPoint);
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

    private void TryProcessPacket(byte[] packet, IPEndPoint? remoteEndPoint)
    {
        if (packet.Length == 0)
        {
            return;
        }

        _receivedPacketCount++;
        if (_receivedPacketCount == 1)
        {
            var sourceText = remoteEndPoint is null
                ? "unknown source"
                : $"{remoteEndPoint.Address}:{remoteEndPoint.Port}";
            PublishDiagnosticMessage($"MEVA UDP 첫 패킷을 수신했습니다. 송신지: {sourceText}, 패킷 크기: {packet.Length} bytes");
        }

        if (TryExtractMetadataPacket(packet, out var segmentInfo))
        {
            _metadataPacketCount++;
            if (_metadataPacketCount == 1)
            {
                PublishDiagnosticMessage("MEVA UDP 구간 메타데이터 패킷을 수신했습니다.");
            }

            NotifySegmentChanged(segmentInfo);
            return;
        }

        if (TryExtractDetectionPacket(packet, out var detectionPacket))
        {
            _dispatcher.BeginInvoke(() => DetectionsReceived?.Invoke(detectionPacket));
            return;
        }

        if (TryExtractStatusPacket(packet, out var statusPacket))
        {
            _dispatcher.BeginInvoke(() => StatusReceived?.Invoke(statusPacket));
            return;
        }

        if (LooksLikeJpeg(packet))
        {
            var decoded = TryDecodeFrame(packet, 0, 0, 0, 0, null);
            if (!decoded)
            {
                _decodeFailureCount++;
                if (_decodeFailureCount == 1 || _decodeFailureCount % 20 == 0)
                {
                    PublishDiagnosticMessage(
                        $"MEVA UDP JPEG 패킷은 도착했지만 화면 디코딩에 실패했습니다. 실패 횟수: {_decodeFailureCount}, 최근 패킷 크기: {packet.Length} bytes");
                }
            }

            return;
        }

        if (packet.Length <= LegacyHeaderSize)
        {
            _unknownPacketCount++;
            if (_unknownPacketCount == 1 || _unknownPacketCount % 20 == 0)
            {
                PublishDiagnosticMessage(
                    $"MEVA UDP 패킷 형식을 해석하지 못했습니다. 미인식 횟수: {_unknownPacketCount}, 최근 패킷 크기: {packet.Length} bytes");
            }
            return;
        }

        if (TryExtractEncodedFrame(packet, useBigEndian: true, out var bigEndianFrame))
        {
            var decoded = TryDecodeFrame(
                bigEndianFrame.EncodedBytes,
                bigEndianFrame.DeclaredWidth,
                bigEndianFrame.DeclaredHeight,
                bigEndianFrame.FrameStampNs,
                bigEndianFrame.FrameIndex,
                null);
            if (!decoded)
            {
                _decodeFailureCount++;
                if (_decodeFailureCount == 1 || _decodeFailureCount % 20 == 0)
                {
                    PublishDiagnosticMessage(
                        $"MEVA legacy big-endian 패킷 디코딩에 실패했습니다. 실패 횟수: {_decodeFailureCount}");
                }
            }
            return;
        }

        if (TryExtractEncodedFrame(packet, useBigEndian: false, out var littleEndianFrame))
        {
            var decoded = TryDecodeFrame(
                littleEndianFrame.EncodedBytes,
                littleEndianFrame.DeclaredWidth,
                littleEndianFrame.DeclaredHeight,
                littleEndianFrame.FrameStampNs,
                littleEndianFrame.FrameIndex,
                null);
            if (!decoded)
            {
                _decodeFailureCount++;
                if (_decodeFailureCount == 1 || _decodeFailureCount % 20 == 0)
                {
                    PublishDiagnosticMessage(
                        $"MEVA legacy little-endian 패킷 디코딩에 실패했습니다. 실패 횟수: {_decodeFailureCount}");
                }
            }

            return;
        }

        _unknownPacketCount++;
        if (_unknownPacketCount == 1 || _unknownPacketCount % 20 == 0)
        {
            PublishDiagnosticMessage(
                $"MEVA UDP 패킷 형식을 해석하지 못했습니다. 미인식 횟수: {_unknownPacketCount}, 최근 패킷 크기: {packet.Length} bytes");
        }
    }

    private bool TryDecodeFrame(
        byte[] encodedFrame,
        ushort declaredWidth,
        ushort declaredHeight,
        ulong frameStampNs,
        uint frameIndex,
        PlaybackSegmentInfo? segmentInfo)
    {
        try
        {
            using var decoded = Cv2.ImDecode(encodedFrame, ImreadModes.Color);
            if (decoded.Empty())
            {
                return false;
            }

            if (declaredWidth > 0 && declaredHeight > 0 &&
                (decoded.Width != declaredWidth || decoded.Height != declaredHeight))
            {
            }

            using var adjusted = new Mat();
            var alpha = 0.5 + (_contrast / 100.0);
            var beta = (_brightness - 50.0) * 2.0;
            decoded.ConvertTo(adjusted, MatType.CV_8UC3, alpha, beta);

            _latestRecordableFrame?.Dispose();
            _latestRecordableFrame = adjusted.Clone();

            if (_isRecording)
            {
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
            NotifySegmentChanged(segmentInfo);
            var receivedFrame = new ReceivedVideoFrame(
                frameStampNs,
                frameIndex,
                declaredWidth > 0 ? declaredWidth : checked((ushort)decoded.Width),
                declaredHeight > 0 ? declaredHeight : checked((ushort)decoded.Height),
                bitmap);
            _dispatcher.BeginInvoke(() => FrameReady?.Invoke(receivedFrame));
            return true;
        }
        catch
        {
            return false;
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

    private void NotifySegmentChanged(PlaybackSegmentInfo? segmentInfo)
    {
        if (!segmentInfo.HasValue)
        {
            return;
        }

        var signature = segmentInfo.Value.GetSignature();
        if (!string.Equals(_lastSegmentSignature, signature, StringComparison.Ordinal))
        {
            _lastSegmentSignature = signature;
            _lastCycleIndex = segmentInfo.Value.CycleIndex;
            _dispatcher.BeginInvoke(() => SegmentChanged?.Invoke(segmentInfo.Value));
            return;
        }

        if (_lastCycleIndex.HasValue &&
            segmentInfo.Value.CycleIndex > _lastCycleIndex.Value)
        {
            _lastCycleIndex = segmentInfo.Value.CycleIndex;
            _dispatcher.BeginInvoke(() => SegmentLoopRestarted?.Invoke(segmentInfo.Value));
            return;
        }

        _lastCycleIndex = segmentInfo.Value.CycleIndex;
    }

    private void PublishDiagnosticMessage(string message)
    {
        _dispatcher.BeginInvoke(() => DiagnosticsMessageReady?.Invoke(message));
    }

    private static bool TryExtractMetadataPacket(byte[] packet, out PlaybackSegmentInfo segmentInfo)
    {
        segmentInfo = default;

        if (packet.Length != MetadataPacketSize)
        {
            return false;
        }

        var header = packet.AsSpan(0, MetadataPacketSize);
        if (header[0] != (byte)'M' ||
            header[1] != (byte)'E' ||
            header[2] != (byte)'V' ||
            header[3] != (byte)'A')
        {
            return false;
        }

        var imageByteLength = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var declaredWidth = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(8, 2));
        var declaredHeight = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(10, 2));
        var clipIndex = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4));
        var clipCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
        var segmentStartSeconds = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
        var segmentEndSeconds = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(24, 4));
        var currentPlaybackSeconds = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(28, 4));
        var cycleIndex = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(32, 4));

        if (imageByteLength != 0)
        {
            return false;
        }

        if (declaredWidth != 0 || declaredHeight != 0)
        {
            return false;
        }

        segmentInfo = new PlaybackSegmentInfo(
            clipIndex,
            clipCount,
            segmentStartSeconds,
            segmentEndSeconds,
            currentPlaybackSeconds,
            cycleIndex);
        return true;
    }

    private static bool TryExtractEncodedFrame(byte[] packet, bool useBigEndian, out EncodedFrame frame)
    {
        frame = default;

        if (packet.Length <= LegacyHeaderSize)
        {
            return false;
        }

        var header = packet.AsSpan(0, LegacyHeaderSize);
        var payload = packet.AsSpan(LegacyHeaderSize);
        var frameStampNs = useBigEndian
            ? BinaryPrimitives.ReadUInt64BigEndian(header.Slice(0, 8))
            : BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(0, 8));
        var frameIndex = useBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));

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

        frame = new EncodedFrame(encodedBytes, declaredWidth, declaredHeight, frameStampNs, frameIndex);
        return true;
    }

    private static bool TryExtractDetectionPacket(byte[] packet, out DetectionPacket detectionPacket)
    {
        detectionPacket = default;

        if (!HasPacketMagic(packet, DetectionPacketMagic))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(packet, DetectionPacketMagic.Length, packet.Length - DetectionPacketMagic.Length);
            var payload = JsonSerializer.Deserialize<DetectionPacketPayload>(json);
            if (payload is null)
            {
                return false;
            }

            var detections = (payload.Detections ?? [])
                .Select(d => new DetectionInfo(
                    d.ClassName ?? "unknown",
                    d.Score,
                    d.X1,
                    d.Y1,
                    d.X2,
                    d.Y2,
                    d.ObjectId))
                .ToArray();

            detectionPacket = new DetectionPacket(
                payload.StampNs,
                payload.FrameId,
                payload.Width,
                payload.Height,
                detections);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractStatusPacket(byte[] packet, out YoloStatusPacket statusPacket)
    {
        statusPacket = default;

        if (!HasPacketMagic(packet, StatusPacketMagic))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(packet, StatusPacketMagic.Length, packet.Length - StatusPacketMagic.Length);
            var payload = JsonSerializer.Deserialize<YoloStatusPacketPayload>(json);
            if (payload is null)
            {
                return false;
            }

            statusPacket = new YoloStatusPacket(
                payload.Enabled,
                payload.ModelLoaded,
                payload.ConfThreshold,
                payload.LastError ?? string.Empty,
                payload.Source ?? string.Empty,
                payload.StampNs,
                payload.FrameId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPacketMagic(byte[] packet, byte[] magic)
    {
        if (packet.Length <= magic.Length)
        {
            return false;
        }

        return packet.AsSpan(0, magic.Length).SequenceEqual(magic);
    }

    private readonly record struct EncodedFrame(
        byte[] EncodedBytes,
        ushort DeclaredWidth,
        ushort DeclaredHeight,
        ulong FrameStampNs,
        uint FrameIndex);

    private sealed record DetectionPacketPayload
    {
        public ulong StampNs { get; init; }
        public uint FrameId { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public DetectionInfoPayload[]? Detections { get; init; }
    }

    private sealed record DetectionInfoPayload
    {
        public string? ClassName { get; init; }
        public float Score { get; init; }
        public float X1 { get; init; }
        public float Y1 { get; init; }
        public float X2 { get; init; }
        public float Y2 { get; init; }
        public int ObjectId { get; init; }
    }

    private sealed record YoloStatusPacketPayload
    {
        public bool Enabled { get; init; }
        public bool ModelLoaded { get; init; }
        public float ConfThreshold { get; init; }
        public string? LastError { get; init; }
        public string? Source { get; init; }
        public ulong StampNs { get; init; }
        public uint FrameId { get; init; }
    }
}

public readonly record struct ReceivedVideoFrame(
    ulong StampNs,
    uint FrameIndex,
    ushort Width,
    ushort Height,
    BitmapSource Bitmap);

public readonly record struct DetectionInfo(
    string ClassName,
    float Score,
    float X1,
    float Y1,
    float X2,
    float Y2,
    int ObjectId)
{
    public string LabelText => $"{ClassName} object{ObjectId} ({Score:0.00})";
}

public readonly record struct DetectionPacket(
    ulong StampNs,
    uint FrameId,
    int Width,
    int Height,
    IReadOnlyList<DetectionInfo> Detections);

public readonly record struct YoloStatusPacket(
    bool Enabled,
    bool ModelLoaded,
    float ConfThreshold,
    string LastError,
    string Source,
    ulong StampNs,
    uint FrameId);

public readonly record struct PlaybackSegmentInfo(
    uint ClipIndex,
    uint ClipCount,
    uint SegmentStartSeconds,
    uint SegmentEndSeconds,
    uint CurrentPlaybackSeconds,
    uint CycleIndex)
{
    public string ToLogMessage()
    {
        return $"MEVA video segment changed: clip {ClipIndex}/{ClipCount} now playing {FormatTime(SegmentStartSeconds)} ~ {FormatTime(SegmentEndSeconds)}";
    }

    public string ToLoopRestartLogMessage()
    {
        return $"MEVA video segment replay restarted: clip {ClipIndex}/{ClipCount} now replaying {FormatTime(SegmentStartSeconds)} ~ {FormatTime(SegmentEndSeconds)}";
    }

    public string GetSignature()
    {
        return $"{ClipIndex}:{ClipCount}:{SegmentStartSeconds}:{SegmentEndSeconds}";
    }

    private static string FormatTime(uint totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }
}
