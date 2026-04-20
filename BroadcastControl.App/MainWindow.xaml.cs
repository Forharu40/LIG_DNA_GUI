using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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
    private bool _hasReceivedNonEmptyDetectionPacket;
    private bool _hasRenderedDetectionOverlay;
    private bool _isRenderingOverlay;
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

        if (!_hasReceivedNonEmptyDetectionPacket && detectionPacket.Detections.Count > 0)
        {
            _hasReceivedNonEmptyDetectionPacket = true;
            _viewModel.AppendImportantLog(
                $"YOLO non-empty detection received. frameId={detectionPacket.FrameId}, objects={detectionPacket.Detections.Count}");
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
        if (_isRenderingOverlay)
        {
            return;
        }

        if (DetectionOverlayCanvas is null)
        {
            return;
        }

        _isRenderingOverlay = true;
        try
        {
            DetectionOverlayCanvas.Children.Clear();

            if (_latestEoFrame is null)
            {
                return;
            }

            if (!TryGetRenderableFrameAndDetection(out var frameToRender, out var detectionPacket))
            {
                _viewModel.UpdateEoFrame(_latestEoFrame.Value.Bitmap);
                return;
            }

            var composedBitmap = ComposeDetectionOverlayBitmap(frameToRender, detectionPacket);
            _viewModel.UpdateEoFrame(composedBitmap);

            if (!_hasRenderedDetectionOverlay)
            {
                _hasRenderedDetectionOverlay = true;
                _viewModel.AppendImportantLog("YOLO overlay rendered on GUI.");
            }
        }
        finally
        {
            _isRenderingOverlay = false;
        }
    }

    private BitmapSource ComposeDetectionOverlayBitmap(ReceivedVideoFrame frame, DetectionPacket detectionPacket)
    {
        var pixelWidth = frame.Bitmap.PixelWidth;
        var pixelHeight = frame.Bitmap.PixelHeight;
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return frame.Bitmap;
        }

        var sourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : frame.Width;
        var sourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : frame.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return frame.Bitmap;
        }

        var scaleX = pixelWidth / (double)sourceWidth;
        var scaleY = pixelHeight / (double)sourceHeight;

        var drawingVisual = new DrawingVisual();
        using var drawingContext = drawingVisual.RenderOpen();
        drawingContext.DrawImage(frame.Bitmap, new Rect(0, 0, pixelWidth, pixelHeight));

        foreach (var detection in detectionPacket.Detections)
        {
            var rectLeft = detection.X1 * scaleX;
            var rectTop = detection.Y1 * scaleY;
            var rectWidth = Math.Max(2, (detection.X2 - detection.X1) * scaleX);
            var rectHeight = Math.Max(2, (detection.Y2 - detection.Y1) * scaleY);

            if (rectWidth < 2 || rectHeight < 2)
            {
                continue;
            }

            DrawDetectionOverlayToBitmap(drawingContext, rectLeft, rectTop, rectWidth, rectHeight, detection);
        }

        var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(drawingVisual);
        renderTarget.Freeze();
        return renderTarget;
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

        var recentCandidates = _detectionCache
            .Where(pair => pair.Value.Detections.Count > 0 && pair.Key <= currentFrameId)
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var candidate in recentCandidates)
        {
            var frameGap = currentFrameId >= candidate.Key
                ? currentFrameId - candidate.Key
                : uint.MaxValue;
            if (frameGap > OverlayFrameTolerance)
            {
                break;
            }

            detectionPacket = candidate.Value;
            return true;
        }

        var fallbackCandidates = _detectionCache
            .Where(pair => pair.Value.Detections.Count > 0)
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var candidate in fallbackCandidates)
        {
            detectionPacket = candidate.Value;
            return true;
        }

        detectionPacket = default;
        return false;
    }

    private bool TryGetRenderableFrameAndDetection(out ReceivedVideoFrame frame, out DetectionPacket detectionPacket)
    {
        if (_latestEoFrame is not null &&
            _detectionCache.TryGetValue(_latestEoFrame.Value.FrameIndex, out detectionPacket))
        {
            frame = _latestEoFrame.Value;
            return true;
        }

        var exactPairs = _detectionCache
            .Where(pair => pair.Value.Detections.Count > 0 && _eoFrameCache.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var pair in exactPairs)
        {
            frame = _eoFrameCache[pair.Key];
            detectionPacket = pair.Value;
            return true;
        }

        if (_latestEoFrame is not null &&
            TryGetRenderableDetectionPacket(_latestEoFrame.Value.FrameIndex, out detectionPacket))
        {
            frame = _latestEoFrame.Value;
            return true;
        }

        frame = default;
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

    private static void DrawDetectionOverlayToBitmap(
        DrawingContext drawingContext,
        double rectLeft,
        double rectTop,
        double rectWidth,
        double rectHeight,
        DetectionInfo detection)
    {
        var accentBrush = new SolidColorBrush(Color.FromRgb(105, 255, 132));
        accentBrush.Freeze();
        var shadowBrush = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
        shadowBrush.Freeze();
        var textBrush = Brushes.White;

        drawingContext.DrawRectangle(null, new Pen(shadowBrush, 4), new Rect(rectLeft, rectTop, rectWidth, rectHeight));
        drawingContext.DrawRectangle(null, new Pen(accentBrush, 2), new Rect(rectLeft, rectTop, rectWidth, rectHeight));

        var cornerLength = Math.Max(12, Math.Min(rectWidth, rectHeight) * 0.18);
        DrawCorner(drawingContext, rectLeft, rectTop, cornerLength, true, true, accentBrush);
        DrawCorner(drawingContext, rectLeft + rectWidth, rectTop, cornerLength, false, true, accentBrush);
        DrawCorner(drawingContext, rectLeft, rectTop + rectHeight, cornerLength, true, false, accentBrush);
        DrawCorner(drawingContext, rectLeft + rectWidth, rectTop + rectHeight, cornerLength, false, false, accentBrush);

        var labelText = new FormattedText(
            detection.LabelText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            16,
            textBrush,
            1.0);
        var labelPaddingX = 10.0;
        var labelPaddingY = 5.0;
        var labelWidth = labelText.Width + (labelPaddingX * 2) + 14;
        var labelHeight = labelText.Height + (labelPaddingY * 2);
        var labelLeft = Math.Max(0, rectLeft);
        var labelTop = Math.Max(0, rectTop - labelHeight - 8);

        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(230, 9, 33, 18)),
            new Pen(accentBrush, 1),
            new Rect(labelLeft, labelTop, labelWidth, labelHeight),
            3,
            3);
        drawingContext.DrawEllipse(accentBrush, null, new Point(labelLeft + 10, labelTop + (labelHeight / 2)), 4, 4);
        drawingContext.DrawText(labelText, new Point(labelLeft + 20, labelTop + labelPaddingY - 1));
    }

    private static void DrawCorner(
        DrawingContext drawingContext,
        double anchorX,
        double anchorY,
        double length,
        bool isLeft,
        bool isTop,
        Brush strokeBrush)
    {
        var pen = new Pen(strokeBrush, 3)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square
        };
        drawingContext.DrawLine(pen, new Point(anchorX, anchorY), new Point(anchorX + (isLeft ? length : -length), anchorY));
        drawingContext.DrawLine(pen, new Point(anchorX, anchorY), new Point(anchorX, anchorY + (isTop ? length : -length)));
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
