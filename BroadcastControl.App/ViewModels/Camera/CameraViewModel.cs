using System.Windows.Media;
using BroadcastControl.App.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BroadcastControl.App.Infrastructure;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels.Camera;
using BroadcastControl.App.ViewModels.Mobile;
using BroadcastControl.App.ViewModels.Monitoring;
using BroadcastControl.App.ViewModels.Motor;
using BroadcastControl.App.ViewModels.Operation;
using BroadcastControl.App.ViewModels.Recording;
using BroadcastControl.App.ViewModels.Vlm;

namespace BroadcastControl.App.ViewModels.Camera
{

public sealed class CameraViewModel : ViewModelBase
{
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private double _zoomLevel = 1.0;
    private double _brightness = 50;
    private double _contrast = 50;

    public ImageSource? EoFrame
    {
        get => _eoFrame;
        set => SetProperty(ref _eoFrame, value);
    }

    public ImageSource? IrFrame
    {
        get => _irFrame;
        set => SetProperty(ref _irFrame, value);
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetProperty(ref _zoomLevel, value);
    }

    public double Brightness
    {
        get => _brightness;
        set => SetProperty(ref _brightness, value);
    }

    public double Contrast
    {
        get => _contrast;
        set => SetProperty(ref _contrast, value);
    }
}
}

namespace BroadcastControl.App.ViewModels
{
public sealed partial class MainViewModel
{
    public string EoTitle => "EO cam";

    public string IrTitle => "IR cam";

    public string EoSubtitle => "Jetson YOLO EO stream";

    public string IrSubtitle => "ZYBO10 -> Jetson YOLO IR stream";

    public ImageSource? LargeFeedImage => _isEoPrimary
        ? _eoFrame ?? _eoPlaceholderFrame
        : _irFrame ?? _irPlaceholderFrame;

    public ImageSource? InsetFeedImage => _isEoPrimary
        ? _irFrame ?? _irPlaceholderFrame
        : _eoFrame ?? _eoPlaceholderFrame;

    public string LargeFeedTitle => _isEoPrimary ? EoTitle : IrTitle;

    public string InsetFeedTitle => _isEoPrimary ? IrTitle : EoTitle;

    public double LargeFeedRotationAngle => _isEoPrimary ? _eoDisplayRotationAngle : _irDisplayRotationAngle;

    public double InsetFeedRotationAngle => _isEoPrimary ? _irDisplayRotationAngle : _eoDisplayRotationAngle;

    public Stretch LargeFeedStretch => HasLargeFeedFrame ? Stretch.Uniform : Stretch.UniformToFill;

    public Stretch InsetFeedStretch => HasInsetFeedFrame ? Stretch.Uniform : Stretch.UniformToFill;

    private bool HasLargeFeedFrame => _isEoPrimary ? _eoFrame is not null : _irFrame is not null;

    private bool HasInsetFeedFrame => _isEoPrimary ? _irFrame is not null : _eoFrame is not null;

    public string LargeFeedSubtitle => _isEoPrimary ? EoSubtitle : IrSubtitle;

    public string InsetFeedSubtitle => _isEoPrimary ? IrSubtitle : EoSubtitle;

    public double Brightness
    {
        get => _brightness;
        set
        {
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            if (SetProperty(ref _brightness, value))
            {
                OnPropertyChanged(nameof(BrightnessText));
            }
        }
    }

    public string BrightnessText => $"{Text["Brightness"]} {Brightness:0}%";

    public double Contrast
    {
        get => _contrast;
        set
        {
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            if (SetProperty(ref _contrast, value))
            {
                OnPropertyChanged(nameof(ContrastText));
            }
        }
    }

    public string ContrastText => $"{Text["Contrast"]} {Contrast:0}%";

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
                if (_zoomLevel <= 1.0)
                {
                    _zoomPanX = 0;
                    _zoomPanY = 0;
                    OnPropertyChanged(nameof(ZoomTransformX));
                    OnPropertyChanged(nameof(ZoomTransformY));
                }

                OnPropertyChanged(nameof(ZoomLevelText));
                OnPropertyChanged(nameof(LargeFeedScale));
                OnPropertyChanged(nameof(ShowZoomMiniMap));
                UpdateMiniMapViewport();
            }
        }
    }

    public string ZoomLevelText => $"x{ZoomLevel:0.00}";

    public double LargeFeedScale => ZoomLevel;

    public double ZoomTransformX => _zoomPanX;

    public double ZoomTransformY => _zoomPanY;

    public bool ShowZoomMiniMap => IsSystemPoweredOn && ZoomLevel > 1.0;

    public double MiniMapViewportWidth => MiniMapWidth / ZoomLevel;

    public double MiniMapViewportHeight => MiniMapHeight / ZoomLevel;

    public double MiniMapViewportLeft
    {
        get
        {
            var maxPan = GetMaxPanX();
            if (maxPan <= 0)
            {
                return (MiniMapWidth - MiniMapViewportWidth) / 2;
            }

            var normalized = (_zoomPanX + maxPan) / (maxPan * 2);
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            return (1.0 - normalized) * (MiniMapWidth - MiniMapViewportWidth);
        }
    }

    public double MiniMapViewportTop
    {
        get
        {
            var maxPan = GetMaxPanY();
            if (maxPan <= 0)
            {
                return (MiniMapHeight - MiniMapViewportHeight) / 2;
            }

            var normalized = (_zoomPanY + maxPan) / (maxPan * 2);
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            return (1.0 - normalized) * (MiniMapHeight - MiniMapViewportHeight);
        }
    }

    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
        OnPropertyChanged(nameof(LargeFeedStretch));
        OnPropertyChanged(nameof(InsetFeedStretch));
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    public void UpdateIrFrame(ImageSource? frame)
    {
        _irFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
        OnPropertyChanged(nameof(LargeFeedStretch));
        OnPropertyChanged(nameof(InsetFeedStretch));
    }

    public void UpdateJetsonConnectionState(bool isConnected)
    {
        IsJetsonConnected = isConnected;
    }

    public void UpdateDetectionTargets(IReadOnlyList<DetectionTargetItem> targets)
    {
        DetectionTargets.Clear();
        foreach (var target in targets
                     .OrderByDescending(item => GetThreatWeight(item.ThreatLevel))
                     .ThenBy(item => item.ObjectId))
        {
            DetectionTargets.Add(target);
        }
    }

    private static ImageSource? RotateFrame(ImageSource? frame, double angle)
    {
        if (frame is not BitmapSource bitmap)
        {
            return frame;
        }

        if (Math.Abs(angle) < double.Epsilon)
        {
            return bitmap;
        }

        var transformed = new TransformedBitmap(bitmap, new RotateTransform(angle));
        transformed.Freeze();
        return transformed;
    }

    public void UpdateViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(width, 1);
        _viewportHeight = Math.Max(height, 1);
        ClampZoomPan();
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    public void PanZoom(double deltaX, double deltaY)
    {
        if (!ShowZoomMiniMap)
        {
            return;
        }

        _zoomPanX = Math.Clamp(_zoomPanX + deltaX, -GetMaxPanX(), GetMaxPanX());
        _zoomPanY = Math.Clamp(_zoomPanY + deltaY, -GetMaxPanY(), GetMaxPanY());

        OnPropertyChanged(nameof(ZoomTransformX));
        OnPropertyChanged(nameof(ZoomTransformY));
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    public void AdjustZoomByWheel(double wheelSteps)
    {
        if (!CanUseZoomControls || Math.Abs(wheelSteps) < double.Epsilon)
        {
            return;
        }

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        ZoomLevel += wheelSteps * 0.1;
    }

    private void SwapFeeds()
    {
        _isEoPrimary = !_isEoPrimary;
        OnPropertyChanged(nameof(IsEoPrimary));
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
        OnPropertyChanged(nameof(LargeFeedTitle));
        OnPropertyChanged(nameof(InsetFeedTitle));
        OnPropertyChanged(nameof(LargeFeedSubtitle));
        OnPropertyChanged(nameof(InsetFeedSubtitle));
        OnPropertyChanged(nameof(LargeFeedRotationAngle));
        OnPropertyChanged(nameof(InsetFeedRotationAngle));
        OnPropertyChanged(nameof(LargeFeedStretch));
        OnPropertyChanged(nameof(InsetFeedStretch));

    }

    public void RotateLargeFeedClockwise()
    {
        if (_isEoPrimary)
        {
            _eoDisplayRotationAngle = NextRotationAngle(_eoDisplayRotationAngle);
        }
        else
        {
            _irDisplayRotationAngle = NextRotationAngle(_irDisplayRotationAngle);
        }

        OnPropertyChanged(nameof(LargeFeedRotationAngle));
    }

    public void RotateInsetFeedClockwise()
    {
        if (_isEoPrimary)
        {
            _irDisplayRotationAngle = NextRotationAngle(_irDisplayRotationAngle);
        }
        else
        {
            _eoDisplayRotationAngle = NextRotationAngle(_eoDisplayRotationAngle);
        }

        OnPropertyChanged(nameof(InsetFeedRotationAngle));
    }

    private static double NextRotationAngle(double currentAngle) => (currentAngle + 90) % 360;

    private void ClampZoomPan()
    {
        _zoomPanX = Math.Clamp(_zoomPanX, -GetMaxPanX(), GetMaxPanX());
        _zoomPanY = Math.Clamp(_zoomPanY, -GetMaxPanY(), GetMaxPanY());
        OnPropertyChanged(nameof(ZoomTransformX));
        OnPropertyChanged(nameof(ZoomTransformY));
    }

    private double GetMaxPanX() => (_viewportWidth * (ZoomLevel - 1)) / 2;

    private double GetMaxPanY() => (_viewportHeight * (ZoomLevel - 1)) / 2;

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    private static ImageSource CreateCameraPlaceholderFrame(string label, Color accentColor)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(23, 28, 36),
                Color.FromRgb(73, 25, 24),
                new Point(0, 0),
                new Point(1, 1));

            dc.DrawRectangle(background, null, new Rect(0, 0, 320, 240));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B)), null, new Point(220, 92), 46, 32);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 208, 90)), null, new Point(112, 152), 64, 22);

            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), 1);
            for (var x = 0; x <= 320; x += 40)
            {
                dc.DrawLine(gridPen, new Point(x, 0), new Point(x, 240));
            }

            for (var y = 0; y <= 240; y += 40)
            {
                dc.DrawLine(gridPen, new Point(0, y), new Point(320, y));
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                var textBrush = new SolidColorBrush(Color.FromRgb(240, 243, 248));
                textBrush.Freeze();
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var formattedText = new FormattedText(
                    label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    30,
                    textBrush,
                    1.0);
                dc.DrawText(formattedText, new Point(22, 20));
            }
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
}
