using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace BroadcastControl.App.Services;

public sealed class ViewportRecordingService : IDisposable
{
    private readonly DispatcherTimer _timer;

    private FrameworkElement? _target;
    private VideoWriter? _writer;
    private string? _recordingPath;
    private string? _recordingErrorMessage;
    private OpenCvSharp.Size _recordingOutputSize;
    private int _recordedFrameCount;
    private bool _isRecording;

    public ViewportRecordingService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += OnTick;
    }

    public string? LastRecordingErrorMessage => _recordingErrorMessage;

    public int RecordedFrameCount => _recordedFrameCount;

    public string StartRecordingToDesktop(FrameworkElement target)
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
        _recordingPath = Path.Combine(desktopPath, $"camera_view_{timestamp}.avi");
        _recordingErrorMessage = null;
        _recordingOutputSize = default;
        _recordedFrameCount = 0;
        _target = target;
        _isRecording = true;

        CaptureAndWriteFrame();
        _timer.Start();
        return _recordingPath;
    }

    public string? StopRecording()
    {
        _timer.Stop();
        _isRecording = false;

        _writer?.Release();
        _writer?.Dispose();
        _writer = null;

        var savedPath = _recordingPath;
        _recordingPath = null;
        _target = null;
        return savedPath;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        CaptureAndWriteFrame();
    }

    private void CaptureAndWriteFrame()
    {
        if (!_isRecording || _target is null || string.IsNullOrWhiteSpace(_recordingPath))
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(_target.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(_target.ActualHeight));
        if (width < 2 || height < 2)
        {
            return;
        }

        try
        {
            _target.UpdateLayout();

            var renderTarget = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            renderTarget.Render(_target);
            renderTarget.Freeze();

            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            var encodedBytes = stream.ToArray();

            using var frame = Cv2.ImDecode(encodedBytes, ImreadModes.Color);
            if (frame.Empty())
            {
                _recordingErrorMessage = "Viewport capture decoded to an empty frame.";
                return;
            }

            EnsureVideoWriter(frame.Width, frame.Height);
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
        catch (Exception ex)
        {
            _recordingErrorMessage = ex.Message;
        }
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
                    10,
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

        _recordingErrorMessage = "Could not open a video writer for the camera viewport. Tried: "
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

    public void Dispose()
    {
        StopRecording();
        _timer.Tick -= OnTick;
    }
}
