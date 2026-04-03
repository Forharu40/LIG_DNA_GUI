using OpenCvSharp;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BroadcastControl.App.Services;

/// <summary>
/// 노트북 기본 카메라 영상을 주기적으로 읽어서 WPF에서 사용할 BitmapSource로 변환한다.
/// 현재는 GUI 검증용 서비스이며, 추후 RTSP/산업용 카메라 서비스로 교체할 수 있다.
/// </summary>
public sealed class WebcamCaptureService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private VideoCapture? _capture;
    private bool _isRunning;

    public WebcamCaptureService()
    {
        // UI 스레드에서 안전하게 프레임을 갱신하기 위해 DispatcherTimer를 사용한다.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 새 프레임이 준비되면 구독자에게 전달한다.
    /// </summary>
    public event Action<BitmapSource>? FrameReady;

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

        using var converted = new Mat();
        Cv2.CvtColor(frame, converted, ColorConversionCodes.BGR2BGRA);

        // BitmapSource.Create는 WPF Image에 바로 바인딩 가능한 형태로 변환해 준다.
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

        // 다른 스레드 접근에도 안전하도록 Freeze 처리한다.
        bitmap.Freeze();
        FrameReady?.Invoke(bitmap);
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
