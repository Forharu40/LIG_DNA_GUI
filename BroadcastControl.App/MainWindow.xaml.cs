using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;
using System.Collections.Generic;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private const double SettingsDrawerClosedOffset = 320;
    private const double WindowedWidth = 1600;
    private const double WindowedHeight = 900;

    private readonly MainViewModel _viewModel;
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;
    private readonly WebcamCaptureService _irWebcamCaptureService;
    private readonly ViewportRecordingService _viewportRecordingService;

    private bool _isDraggingZoom;
    private Point _lastZoomDragPoint;
    private bool _hasReceivedEoFrame;
    private bool _hasReceivedIrFrame;
    private bool _isFullscreenMode = true;
    private ReceivedVideoFrame? _latestEoFrame;
    private DetectionPacket? _latestDetectionPacket;
    private string? _lastStatusSignature;
    private readonly Dictionary<uint, ReceivedVideoFrame> _eoFrameCache = new();
    private readonly Dictionary<uint, DetectionPacket> _detectionCache = new();
    private bool _hasReceivedDetectionPacket;
    private string? _lastDetectionAlertSignature;
    private const int OverlayCacheLimit = 48;
    private const uint OverlayFrameTolerance = 12;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _eoUdpCaptureService = new UdpEncodedVideoReceiverService();
        _irWebcamCaptureService = new WebcamCaptureService();
        _viewportRecordingService = new ViewportRecordingService();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        UpdateWindowModeButtonText();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _eoUdpCaptureService.SegmentChanged += OnEoSegmentChanged;
        _eoUdpCaptureService.SegmentLoopRestarted += OnEoSegmentLoopRestarted;
        _eoUdpCaptureService.DetectionsReceived += OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _eoUdpCaptureService.DiagnosticsMessageReady += OnEoDiagnosticsMessageReady;
        _irWebcamCaptureService.FrameReady += OnIrFrameReady;

        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);

        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();
        RenderDetectionOverlay();

        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start())
        {
            _viewModel.AppendImportantLog($"MEVA YOLO UDP stream receiver is waiting on port {_eoUdpCaptureService.ListeningPort}.");
        }
        else
        {
            _viewModel.AppendImportantLog("Failed to start the MEVA YOLO UDP stream receiver.");
        }

        if (_irWebcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("Connected the laptop camera to the temporary IR panel.");
        }
        else
        {
            _viewModel.AppendImportantLog("Could not connect the laptop camera to the temporary IR panel.");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Brightness):
                _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
                break;

            case nameof(MainViewModel.Contrast):
                _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
                break;

            case nameof(MainViewModel.IsManualRecordingEnabled):
                HandleManualRecordingStateChanged();
                break;

            case nameof(MainViewModel.ZoomLevel):
            case nameof(MainViewModel.ZoomTransformX):
            case nameof(MainViewModel.ZoomTransformY):
            case nameof(MainViewModel.IsEoPrimary):
            case nameof(MainViewModel.LargeFeedImage):
                UpdateRecordingViewportState();
                RenderDetectionOverlay();
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: true);
                break;
        }
    }

    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
        RenderDetectionOverlay();
    }

    private void CameraViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.ShowZoomMiniMap)
        {
            return;
        }

        _isDraggingZoom = true;
        _lastZoomDragPoint = e.GetPosition(CameraViewport);
        CameraViewport.CaptureMouse();
    }

    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        var currentPoint = e.GetPosition(CameraViewport);
        var delta = currentPoint - _lastZoomDragPoint;
        _lastZoomDragPoint = currentPoint;

        _viewModel.PanZoom(delta.X, delta.Y);
    }

    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.CanUseZoomControls)
        {
            return;
        }

        _viewModel.AdjustZoomByWheel(e.Delta / 120.0);
        e.Handled = true;
    }

    private void CameraViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        _isDraggingZoom = false;
        CameraViewport.ReleaseMouseCapture();
    }

    private void OnEoFrameReady(ReceivedVideoFrame frame)
    {
        _latestEoFrame = frame;
        CacheEoFrame(frame);

        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
            _viewModel.AppendImportantLog("EO UDP camera first frame received.");
        }

        _viewModel.UpdateEoFrame(frame.Bitmap);
        RenderDetectionOverlay();
    }

    private void OnEoDetectionsReceived(DetectionPacket detectionPacket)
    {
        _latestDetectionPacket = detectionPacket;
        CacheDetectionPacket(detectionPacket);

        if (!_hasReceivedDetectionPacket)
        {
            _hasReceivedDetectionPacket = true;
            _viewModel.AppendImportantLog(
                $"YOLO detection stream connected. first frameId={detectionPacket.FrameId}");
        }

        NotifyDetectionAlertIfNeeded(detectionPacket);
        _viewModel.UpdateDetectionSummary(detectionPacket.Detections);
        RenderDetectionOverlay();
    }

    private void OnYoloStatusReceived(YoloStatusPacket statusPacket)
    {
        var signature = $"{statusPacket.Enabled}:{statusPacket.ModelLoaded}:{statusPacket.ConfThreshold}:{statusPacket.LastError}:{statusPacket.Source}";
        if (string.Equals(_lastStatusSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusSignature = signature;

        if (!statusPacket.ModelLoaded)
        {
            _viewModel.AppendImportantLog("YOLO 모델이 아직 로드되지 않았습니다.");
        }

        if (!string.IsNullOrWhiteSpace(statusPacket.LastError))
        {
            _viewModel.AppendImportantLog($"YOLO 상태 오류: {statusPacket.LastError}");
        }
    }

    private void OnIrFrameReady(System.Windows.Media.Imaging.BitmapSource frame)
    {
        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
            _viewModel.AppendImportantLog("IR temporary camera first frame received.");
        }

        _viewModel.UpdateIrFrame(frame);
    }

    private void OnEoSegmentChanged(PlaybackSegmentInfo segmentInfo)
    {
        _viewModel.AppendImportantLog(segmentInfo.ToLogMessage());
    }

    private void OnEoSegmentLoopRestarted(PlaybackSegmentInfo segmentInfo)
    {
        _viewModel.AppendImportantLog(segmentInfo.ToLoopRestartLogMessage());
    }

    private void OnEoDiagnosticsMessageReady(string message)
    {
        _viewModel.AppendImportantLog(message);
    }

    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
    }

    private void RenderDetectionOverlay()
    {
        if (DetectionOverlayCanvas is null)
        {
            return;
        }

        DetectionOverlayCanvas.Children.Clear();

        if (!_viewModel.IsEoPrimary || _latestEoFrame is null)
        {
            return;
        }

        if (!TryGetRenderableDetectionPacket(_latestEoFrame.Value.FrameIndex, out var detectionPacket))
        {
            return;
        }

        var sourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : _latestEoFrame.Value.Width;
        var sourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : _latestEoFrame.Value.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        var viewportWidth = Math.Max(CameraViewport.ActualWidth, 1);
        var viewportHeight = Math.Max(CameraViewport.ActualHeight, 1);
        var baseScale = Math.Max(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        var scaledWidth = sourceWidth * baseScale;
        var scaledHeight = sourceHeight * baseScale;
        var baseLeft = (viewportWidth - scaledWidth) / 2.0;
        var baseTop = (viewportHeight - scaledHeight) / 2.0;
        var viewportCenter = new Point(viewportWidth / 2.0, viewportHeight / 2.0);

        foreach (var detection in detectionPacket.Detections)
        {
            var topLeft = TransformOverlayPoint(
                new Point(baseLeft + (detection.X1 * baseScale), baseTop + (detection.Y1 * baseScale)),
                viewportCenter);
            var bottomRight = TransformOverlayPoint(
                new Point(baseLeft + (detection.X2 * baseScale), baseTop + (detection.Y2 * baseScale)),
                viewportCenter);

            var rectLeft = Math.Min(topLeft.X, bottomRight.X);
            var rectTop = Math.Min(topLeft.Y, bottomRight.Y);
            var rectWidth = Math.Abs(bottomRight.X - topLeft.X);
            var rectHeight = Math.Abs(bottomRight.Y - topLeft.Y);

            if (rectWidth < 2 || rectHeight < 2)
            {
                continue;
            }

            AddDetectionVisual(rectLeft, rectTop, rectWidth, rectHeight, detection);
        }
    }

    private void CacheEoFrame(ReceivedVideoFrame frame)
    {
        _eoFrameCache[frame.FrameIndex] = frame;
        TrimCache(_eoFrameCache);
    }

    private void CacheDetectionPacket(DetectionPacket detectionPacket)
    {
        _detectionCache[detectionPacket.FrameId] = detectionPacket;
        TrimCache(_detectionCache);
    }

    private bool TryGetRenderableDetectionPacket(uint currentFrameId, out DetectionPacket detectionPacket)
    {
        if (_detectionCache.TryGetValue(currentFrameId, out detectionPacket))
        {
            return true;
        }

        foreach (var candidate in _detectionCache
                     .Where(pair => pair.Value.Detections.Count > 0 && pair.Key <= currentFrameId)
                     .OrderByDescending(pair => pair.Key))
        {
            if (currentFrameId - candidate.Key > OverlayFrameTolerance)
            {
                break;
            }

            detectionPacket = candidate.Value;
            return true;
        }

        foreach (var candidate in _detectionCache
                     .Where(pair => pair.Value.Detections.Count > 0)
                     .OrderByDescending(pair => pair.Key))
        {
            detectionPacket = candidate.Value;
            return true;
        }

        detectionPacket = default;
        return false;
    }

    private void NotifyDetectionAlertIfNeeded(DetectionPacket detectionPacket)
    {
        if (detectionPacket.Detections.Count == 0)
        {
            _lastDetectionAlertSignature = null;
            return;
        }

        var preview = string.Join(
            ", ",
            detectionPacket.Detections
                .Take(3)
                .Select(d => $"{d.ClassName} object{d.ObjectId}"));
        var signature = $"{detectionPacket.FrameId}:{preview}:{detectionPacket.Detections.Count}";
        if (string.Equals(_lastDetectionAlertSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastDetectionAlertSignature = signature;
        var suffix = detectionPacket.Detections.Count > 3
            ? $" 외 {detectionPacket.Detections.Count - 3}개"
            : string.Empty;
        _viewModel.AppendImportantLog($"YOLO detected: {preview}{suffix}");
    }

    private static void TrimCache<T>(Dictionary<uint, T> cache)
    {
        while (cache.Count > OverlayCacheLimit)
        {
            var oldestKey = cache.Keys.Min();
            cache.Remove(oldestKey);
        }
    }

    private void AddDetectionVisual(double rectLeft, double rectTop, double rectWidth, double rectHeight, DetectionInfo detection)
    {
        var accentBrush = new SolidColorBrush(Color.FromRgb(105, 255, 132));
        accentBrush.Freeze();
        var fillBrush = new SolidColorBrush(Color.FromArgb(36, 105, 255, 132));
        fillBrush.Freeze();
        var shadowBrush = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
        shadowBrush.Freeze();

        var outerShadow = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = shadowBrush,
            StrokeThickness = 4,
            RadiusX = 2,
            RadiusY = 2,
            Fill = fillBrush
        };
        Canvas.SetLeft(outerShadow, rectLeft);
        Canvas.SetTop(outerShadow, rectTop);
        DetectionOverlayCanvas.Children.Add(outerShadow);

        var mainRectangle = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = accentBrush,
            StrokeThickness = 2,
            RadiusX = 2,
            RadiusY = 2,
            Fill = fillBrush
        };
        Canvas.SetLeft(mainRectangle, rectLeft);
        Canvas.SetTop(mainRectangle, rectTop);
        DetectionOverlayCanvas.Children.Add(mainRectangle);

        var cornerLength = Math.Max(12, Math.Min(rectWidth, rectHeight) * 0.18);
        AddCorner(rectLeft, rectTop, cornerLength, true, true, accentBrush);
        AddCorner(rectLeft + rectWidth, rectTop, cornerLength, false, true, accentBrush);
        AddCorner(rectLeft, rectTop + rectHeight, cornerLength, true, false, accentBrush);
        AddCorner(rectLeft + rectWidth, rectTop + rectHeight, cornerLength, false, false, accentBrush);

        var labelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 9, 33, 18)),
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3, 8, 3),
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Color = Colors.Black,
                Opacity = 0.35
            },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = accentBrush,
                        Margin = new Thickness(0, 3, 6, 0)
                    },
                    new TextBlock
                    {
                        Text = detection.LabelText,
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    }
                }
            }
        };

        labelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = labelBorder.DesiredSize.Width;
        var labelHeight = labelBorder.DesiredSize.Height;
        var labelLeft = Math.Max(0, Math.Min(rectLeft, Math.Max(0, CameraViewport.ActualWidth - labelWidth - 4)));
        var preferredTop = rectTop - labelHeight - 6;
        var labelTop = preferredTop >= 0 ? preferredTop : Math.Min(CameraViewport.ActualHeight - labelHeight - 4, rectTop + 6);
        Canvas.SetLeft(labelBorder, labelLeft);
        Canvas.SetTop(labelBorder, Math.Max(0, labelTop));
        DetectionOverlayCanvas.Children.Add(labelBorder);
    }

    private void AddCorner(double anchorX, double anchorY, double length, bool isLeft, bool isTop, Brush strokeBrush)
    {
        var horizontal = new Line
        {
            X1 = anchorX,
            Y1 = anchorY,
            X2 = anchorX + (isLeft ? length : -length),
            Y2 = anchorY,
            Stroke = strokeBrush,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };

        var vertical = new Line
        {
            X1 = anchorX,
            Y1 = anchorY,
            X2 = anchorX,
            Y2 = anchorY + (isTop ? length : -length),
            Stroke = strokeBrush,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };

        DetectionOverlayCanvas.Children.Add(horizontal);
        DetectionOverlayCanvas.Children.Add(vertical);
    }

    private Point TransformOverlayPoint(Point point, Point viewportCenter)
    {
        var zoom = _viewModel.ZoomLevel;
        var x = viewportCenter.X + ((point.X - viewportCenter.X) * zoom) + _viewModel.ZoomTransformX;
        var y = viewportCenter.Y + ((point.Y - viewportCenter.Y) * zoom) + _viewModel.ZoomTransformY;
        return new Point(x, y);
    }

    private void HandleManualRecordingStateChanged()
    {
        if (_viewModel.IsManualRecordingEnabled)
        {
            var filePath = _viewportRecordingService.StartRecordingToDesktop(CameraPanel);
            _viewModel.AppendImportantLog($"Manual recording started: {filePath}");
            return;
        }

        var savedPath = _viewportRecordingService.StopRecording();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            if (System.IO.File.Exists(savedPath))
            {
                _viewModel.AppendImportantLog($"Video saved: {savedPath} ({_viewportRecordingService.RecordedFrameCount} frames)");
            }
            else if (!string.IsNullOrWhiteSpace(_viewportRecordingService.LastRecordingErrorMessage))
            {
                _viewModel.AppendImportantLog($"Video save failed: {_viewportRecordingService.LastRecordingErrorMessage}");
            }
            else
            {
                _viewModel.AppendImportantLog($"Video file was not created: {savedPath} ({_viewportRecordingService.RecordedFrameCount} frames)");
            }
        }
    }

    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    private void WindowModeToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowMode();
    }

    private void ToggleWindowMode()
    {
        if (_isFullscreenMode)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width = WindowedWidth;
            Height = WindowedHeight;
            Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
            Top = Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
            _isFullscreenMode = false;
            _viewModel.AppendImportantLog("화면 모드가 창모드로 전환되었습니다.");
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreenMode = true;
            _viewModel.AppendImportantLog("화면 모드가 전체화면으로 전환되었습니다.");
        }

        UpdateWindowModeButtonText();
    }

    private void UpdateWindowModeButtonText()
    {
        if (WindowModeToggleButton is null)
        {
            return;
        }

        WindowModeToggleButton.Content = _isFullscreenMode
            ? "창모드로 전환"
            : "전체화면으로 전환";
    }

    private void AnimateSettingsDrawer(bool isOpen, bool animate)
    {
        if (!animate)
        {
            SettingsBackdrop.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsBackdrop.IsHitTestVisible = isOpen;
            SettingsBackdrop.Opacity = isOpen ? 1.0 : 0.0;

            SettingsDrawer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsDrawer.Opacity = isOpen ? 1.0 : 0.0;
            SettingsDrawerTransform.X = isOpen ? 0 : SettingsDrawerClosedOffset;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 170);
        var easing = new CubicEase
        {
            EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn
        };

        if (isOpen)
        {
            SettingsBackdrop.Visibility = Visibility.Visible;
            SettingsBackdrop.IsHitTestVisible = true;
            SettingsDrawer.Visibility = Visibility.Visible;
        }

        var backdropAnimation = new DoubleAnimation
        {
            To = isOpen ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        var drawerOpacityAnimation = new DoubleAnimation
        {
            To = isOpen ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        var drawerSlideAnimation = new DoubleAnimation
        {
            To = isOpen ? 0 : SettingsDrawerClosedOffset,
            Duration = duration,
            EasingFunction = easing
        };

        if (!isOpen)
        {
            drawerSlideAnimation.Completed += (_, _) =>
            {
                SettingsBackdrop.Visibility = Visibility.Collapsed;
                SettingsBackdrop.IsHitTestVisible = false;
                SettingsDrawer.Visibility = Visibility.Collapsed;
            };
        }

        SettingsBackdrop.BeginAnimation(OpacityProperty, backdropAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsDrawer.BeginAnimation(OpacityProperty, drawerOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsDrawerTransform.BeginAnimation(TranslateTransform.XProperty, drawerSlideAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _eoUdpCaptureService.SegmentChanged -= OnEoSegmentChanged;
        _eoUdpCaptureService.SegmentLoopRestarted -= OnEoSegmentLoopRestarted;
        _eoUdpCaptureService.DetectionsReceived -= OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _eoUdpCaptureService.DiagnosticsMessageReady -= OnEoDiagnosticsMessageReady;
        _irWebcamCaptureService.FrameReady -= OnIrFrameReady;
        _viewportRecordingService.Dispose();
        _eoUdpCaptureService.Dispose();
        _irWebcamCaptureService.Dispose();
    }
}
