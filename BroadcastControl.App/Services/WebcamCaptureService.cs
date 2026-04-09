using OpenCvSharp;
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
    private bool _isRunning;
    private double _brightness = 58;
    private double _contrast = 52;

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

        // 대조비는 배율(alpha), 밝기는 오프셋(beta)로 변환해 실제 프레임에 적용한다.
        using var adjusted = new Mat();
        var alpha = 0.5 + (_contrast / 100.0);
        var beta = (_brightness - 50.0) * 2.0;
        frame.ConvertTo(adjusted, MatType.CV_8UC3, alpha, beta);

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

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
