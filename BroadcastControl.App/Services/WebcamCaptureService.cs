using OpenCvSharp;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BroadcastControl.App.Services;

/// <summary>
/// 노트북 기본 카메라 영상을 주기적으로 읽어 WPF에서 사용할 BitmapSource로 변환한다.
/// 동시에 밝기와 대조비 값을 적용해 UI 슬라이더가 실제 영상에 반영되도록 한다.
/// </summary>
public sealed class WebcamCaptureService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private VideoCapture? _capture;
    private VideoWriter? _writer;
    private bool _isRunning;
    private bool _isRecording;
    // UI 기본값과 실제 프레임 처리 기본값을 동일하게 유지한다.
    private double _brightness = 50;
    private double _contrast = 50;
    private double _zoomLevel = 1.0;
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;
    private string? _recordingPath;

    public WebcamCaptureService()
    {
        // UI 스레드에서 안전하게 프레임을 전달하기 위해 DispatcherTimer를 사용한다.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 프레임이 준비되면 구독자에게 전달한다.
    /// </summary>
    public event Action<BitmapSource>? FrameReady;

    /// <summary>
    /// 밝기 슬라이더 값(0~100)을 저장한다. 50이 기준값이다.
    /// </summary>
    public void SetBrightness(double value)
    {
        _brightness = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// 대조비 슬라이더 값(0~100)을 저장한다. 50이 기준값이다.
    /// </summary>
    public void SetContrast(double value)
    {
        _contrast = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// 화면에 보이는 전자 줌/이동 상태를 받아 저장 영상에도 같은 구도가 반영되게 한다.
    /// </summary>
    public void UpdateViewportTransform(double zoomLevel, double panX, double panY, double viewportWidth, double viewportHeight)
    {
        _zoomLevel = Math.Clamp(zoomLevel, 1.0, 4.0);
        _zoomPanX = panX;
        _zoomPanY = panY;
        _viewportWidth = Math.Max(viewportWidth, 1);
        _viewportHeight = Math.Max(viewportHeight, 1);
    }

    public bool Start(int cameraIndex = 0)
    {
        if (_isRunning)
        {
            return true;
        }

        _capture = new VideoCapture(cameraIndex);
        if (!_capture.IsOpened())
        {
            _capture.Release();
            _capture.Dispose();
            _capture = null;
            return false;
        }

        _isRunning = true;
        _timer.Start();
        return true;
    }

    public void Stop()
    {
        _timer.Stop();
        _isRunning = false;
        StopRecording();

        if (_capture is null)
        {
            return;
        }

        _capture.Release();
        _capture.Dispose();
        _capture = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_capture is null || !_capture.IsOpened())
        {
            return;
        }

        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty())
        {
            return;
        }

        // 대조비는 배율(alpha), 밝기는 오프셋(beta)로 변환해 실제 프레임에 적용한다.
        using var adjusted = new Mat();
        var alpha = 0.5 + (_contrast / 100.0);
        var beta = (_brightness - 50.0) * 2.0;
        frame.ConvertTo(adjusted, MatType.CV_8UC3, alpha, beta);

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

        // 다른 스레드에서도 안전하게 사용할 수 있도록 Freeze 처리한다.
        bitmap.Freeze();
        FrameReady?.Invoke(bitmap);
    }

    /// <summary>
    /// 수동 녹화를 시작할 때 바탕화면에 저장될 파일 경로를 먼저 만든다.
    /// </summary>
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

    /// <summary>
    /// 녹화를 종료하고 저장된 파일 경로를 반환한다.
    /// </summary>
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

    /// <summary>
    /// 현재 화면에 보이는 구도와 같은 영역을 잘라 저장용 프레임으로 만든다.
    /// </summary>
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

        var fps = _capture?.Fps;
        if (fps is null || fps <= 0 || double.IsNaN(fps.Value))
        {
            fps = 30;
        }

        _writer = new VideoWriter(
            _recordingPath,
            FourCC.MJPG,
            fps.Value,
            new OpenCvSharp.Size(width, height));
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
