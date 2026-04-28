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
using System.Windows.Threading;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;
using System.Collections.Generic;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private enum DisplayRotation
    {
        None,
        Rotate180,
        RotateLeft90
    }

    private const double SettingsDrawerClosedOffset = 320;
    private const double WindowedWidth = 1600;
    private const double WindowedHeight = 900;
    private const int EoUdpPort = 5000;
    private const int IrUdpPort = 5001;
    private readonly MainViewModel _viewModel;
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;
    private readonly UdpEncodedVideoReceiverService _irUdpCaptureService;
    private readonly ViewportRecordingService _viewportRecordingService;
    private readonly UdpMotorControlService _motorControlService;
    private readonly RosTopicBridgeProcessService _rosTopicBridgeProcessService;
    private readonly DispatcherTimer _motorHoldTimer;

    private bool _isDraggingZoom;
    private Point _lastZoomDragPoint;
    private bool _hasReceivedEoFrame;
    private bool _hasReceivedIrFrame;
    private bool _isFullscreenMode = true;
    private ReceivedVideoFrame? _latestEoFrame;
    private ReceivedVideoFrame? _latestIrFrame;
    private string? _lastStatusSignature;
    private readonly Dictionary<uint, ReceivedVideoFrame> _eoFrameCache = new();
    private readonly Dictionary<uint, ReceivedVideoFrame> _irFrameCache = new();
    private readonly Dictionary<uint, DetectionPacket> _eoDetectionCache = new();
    private readonly Dictionary<uint, DetectionPacket> _irDetectionCache = new();
    private bool _hasReceivedDetectionPacket;
    private bool _hasReceivedNonEmptyDetectionPacket;
    private bool _hasRenderedDetectionOverlay;
    private bool _isRenderingOverlay;
    private string? _lastDetectionAlertSignature;
    private string? _lastFilteredOutTargetSignature;
    private string? _lastOverlaySignature;
    private readonly Dictionary<string, int> _activeMotorDirections = new(StringComparer.Ordinal);
    private readonly HashSet<Key> _pressedMotorKeys = new();
    private const int OverlayCacheLimit = 48;
    private const uint OverlayFrameTolerance = 12;
    private const float DisplayScoreThreshold = 0.60f;
    private static readonly HashSet<string> NonMilitaryTargetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chair",
        "dining table",
        "tv",
        "laptop",
        "cell phone",
        "bottle",
        "couch",
        "bench",
        "refrigerator"
    };

    private static readonly HashSet<string> CompositeTargetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "person",
        "airplane",
        "bicycle",
        "car",
        "motorcycle",
        "bus",
        "truck",
        "train",
        "boat",
        "cell phone",
        "laptop",
        "chair",
        "dining table",
        "tv",
        "couch",
        "bench",
        "bottle",
        "refrigerator"
    };

    public MainWindow()
    {
        InitializeComponent();

        _motorControlService = new UdpMotorControlService();
        _viewModel = new MainViewModel(_motorControlService);
        _eoUdpCaptureService = new UdpEncodedVideoReceiverService();
        _irUdpCaptureService = new UdpEncodedVideoReceiverService();
        _viewportRecordingService = new ViewportRecordingService();
        _rosTopicBridgeProcessService = new RosTopicBridgeProcessService();
        _rosTopicBridgeProcessService.MessageReady += OnRosTopicBridgeMessageReady;
        _motorHoldTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _motorHoldTimer.Tick += MotorHoldTimer_OnTick;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        PreviewKeyUp += MainWindow_OnPreviewKeyUp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        UpdateWindowModeButtonText();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived += OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady += OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived += OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived += OnYoloStatusReceived;

        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
        _irUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _irUdpCaptureService.SetContrast(_viewModel.Contrast);
        _viewModel.InitializeMotorControlState();

        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();
        RenderDetectionOverlay();
        UpdateMotorAutomationState();

        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start(EoUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the EO UDP stream receiver on port {EoUdpPort}.");
        }

        if (_irUdpCaptureService.Start(IrUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the IR UDP stream receiver on port {IrUdpPort}.");
        }

        _rosTopicBridgeProcessService.Start(EoUdpPort, IrUdpPort);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Brightness):
                _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
                _irUdpCaptureService.SetBrightness(_viewModel.Brightness);
                break;

            case nameof(MainViewModel.Contrast):
                _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
                _irUdpCaptureService.SetContrast(_viewModel.Contrast);
                break;

            case nameof(MainViewModel.IsManualRecordingEnabled):
                HandleManualRecordingStateChanged();
                break;

            case nameof(MainViewModel.ZoomLevel):
            case nameof(MainViewModel.ZoomTransformX):
            case nameof(MainViewModel.ZoomTransformY):
            case nameof(MainViewModel.IsEoPrimary):
            case nameof(MainViewModel.SelectedPrimaryTarget):
                UpdateRecordingViewportState();
                RenderDetectionOverlay(forceRefresh: true);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: true);
                break;

            case nameof(MainViewModel.CurrentMode):
            case nameof(MainViewModel.IsSystemPoweredOn):
                UpdateMotorAutomationState();
                break;
        }
    }

    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
        RenderDetectionOverlay(forceRefresh: true);
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
        CacheFrame(frame, _eoFrameCache);

        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
        }

        _viewModel.UpdateEoFrame(frame.Bitmap);
    }

    private void OnEoDetectionsReceived(DetectionPacket detectionPacket)
    {
        HandleDetectionsReceived(detectionPacket, _eoDetectionCache);
    }

    private void OnIrDetectionsReceived(DetectionPacket detectionPacket)
    {
        HandleDetectionsReceived(detectionPacket, _irDetectionCache);
    }

    private void HandleDetectionsReceived(
        DetectionPacket detectionPacket,
        Dictionary<uint, DetectionPacket> detectionCache)
    {
        CacheDetectionPacket(detectionPacket, detectionCache);

        if (!_hasReceivedDetectionPacket)
        {
            _hasReceivedDetectionPacket = true;
        }

        var displayDetections = FilterDisplayDetections(detectionPacket.Detections);

        if (!_hasReceivedNonEmptyDetectionPacket && displayDetections.Count > 0)
        {
            _hasReceivedNonEmptyDetectionPacket = true;
        }

        if (detectionPacket.Detections.Count > 0 && displayDetections.Count == 0)
        {
            var filteredSignature = $"{_viewModel.SelectedPrimaryTarget}:{detectionPacket.FrameId}";
            if (!string.Equals(_lastFilteredOutTargetSignature, filteredSignature, StringComparison.Ordinal))
            {
                _lastFilteredOutTargetSignature = filteredSignature;
            }
        }
        else
        {
            _lastFilteredOutTargetSignature = null;
        }

        NotifyDetectionAlertIfNeeded(detectionPacket.FrameId, displayDetections);
        _viewModel.UpdateDetectionSummary(displayDetections);
        RenderDetectionOverlay(forceRefresh: true);
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

    private void OnIrFrameReady(ReceivedVideoFrame frame)
    {
        _latestIrFrame = frame;
        CacheFrame(frame, _irFrameCache);

        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
        }

        _viewModel.UpdateIrFrame(frame.Bitmap);
    }

    private void OnEoSegmentChanged(PlaybackSegmentInfo segmentInfo)
    {
    }

    private void OnEoSegmentLoopRestarted(PlaybackSegmentInfo segmentInfo)
    {
    }

    private void OnEoDiagnosticsMessageReady(string message)
    {
    }

    private void OnRosTopicBridgeMessageReady(string message)
    {
        Dispatcher.BeginInvoke(() => _viewModel.AppendImportantLog(message));
    }

    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
        _irUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
    }

    private void RenderDetectionOverlay(bool forceRefresh = false)
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

            var latestFrame = _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame;
            if (latestFrame is null)
            {
                _lastOverlaySignature = null;
                return;
            }

            if (!TryGetRenderableFrameAndDetection(out var frameToRender, out var detectionPacket))
            {
                _lastOverlaySignature = null;
                return;
            }

            var displayDetections = FilterDisplayDetections(detectionPacket.Detections);
            if (displayDetections.Count == 0)
            {
                _lastOverlaySignature = null;
                return;
            }

            var rotation = GetCurrentDisplayRotation();
            var originalSourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : frameToRender.Width;
            var originalSourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : frameToRender.Height;
            if (originalSourceWidth <= 0 || originalSourceHeight <= 0)
            {
                return;
            }

            var rotatedSourceWidth = GetRotatedWidth(originalSourceWidth, originalSourceHeight, rotation);
            var rotatedSourceHeight = GetRotatedHeight(originalSourceWidth, originalSourceHeight, rotation);
            var rotatedDetections = displayDetections
                .Select(d => RotateDetectionForDisplay(d, originalSourceWidth, originalSourceHeight, rotation))
                .ToArray();

            var overlaySignature = $"{rotation}:{BuildOverlaySignature(rotatedDetections)}";
            if (!forceRefresh && string.Equals(_lastOverlaySignature, overlaySignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastOverlaySignature = overlaySignature;

            var viewportWidth = Math.Max(CameraViewport.ActualWidth, 1);
            var viewportHeight = Math.Max(CameraViewport.ActualHeight, 1);
            var baseScale = Math.Max(viewportWidth / rotatedSourceWidth, viewportHeight / rotatedSourceHeight);
            var scaledWidth = rotatedSourceWidth * baseScale;
            var scaledHeight = rotatedSourceHeight * baseScale;
            var baseLeft = (viewportWidth - scaledWidth) / 2.0;
            var baseTop = (viewportHeight - scaledHeight) / 2.0;

            foreach (var detection in rotatedDetections)
            {
                var rectLeft = baseLeft + (detection.X1 * baseScale);
                var rectTop = baseTop + (detection.Y1 * baseScale);
                var rectWidth = Math.Max(2, (detection.X2 - detection.X1) * baseScale);
                var rectHeight = Math.Max(2, (detection.Y2 - detection.Y1) * baseScale);

                if (rectWidth < 2 || rectHeight < 2)
                {
                    continue;
                }

                AddDetectionVisualToCanvas(rectLeft, rectTop, rectWidth, rectHeight, detection);
            }

            if (!_hasRenderedDetectionOverlay)
            {
                _hasRenderedDetectionOverlay = true;
            }
        }
        finally
        {
            _isRenderingOverlay = false;
        }
    }

    private DisplayRotation GetCurrentDisplayRotation()
    {
        return _viewModel.IsEoPrimary
            ? DisplayRotation.None
            : DisplayRotation.RotateLeft90;
    }

    private static string BuildOverlaySignature(IReadOnlyList<DetectionInfo> detections)
    {
        return string.Join(
            "|",
            detections.Select(d => $"{d.ObjectId}:{d.ClassName}:{d.X1:0}:{d.Y1:0}:{d.X2:0}:{d.Y2:0}"));
    }

    private static int GetRotatedWidth(int sourceWidth, int sourceHeight, DisplayRotation rotation)
    {
        return rotation == DisplayRotation.RotateLeft90 ? sourceHeight : sourceWidth;
    }

    private static int GetRotatedHeight(int sourceWidth, int sourceHeight, DisplayRotation rotation)
    {
        return rotation == DisplayRotation.RotateLeft90 ? sourceWidth : sourceHeight;
    }

    private static DetectionInfo RotateDetectionForDisplay(
        DetectionInfo detection,
        int sourceWidth,
        int sourceHeight,
        DisplayRotation rotation)
    {
        return rotation switch
        {
            DisplayRotation.Rotate180 => new DetectionInfo(
                detection.ClassName,
                detection.Score,
                (float)(sourceWidth - detection.X2),
                (float)(sourceHeight - detection.Y2),
                (float)(sourceWidth - detection.X1),
                (float)(sourceHeight - detection.Y1),
                detection.ObjectId),
            DisplayRotation.RotateLeft90 => RotateDetectionLeft90(detection, sourceWidth),
            _ => detection
        };
    }

    private static DetectionInfo RotateDetectionLeft90(DetectionInfo detection, int sourceWidth)
    {
        var rotatedCorners = new[]
        {
            RotatePointLeft90(detection.X1, detection.Y1, sourceWidth),
            RotatePointLeft90(detection.X2, detection.Y1, sourceWidth),
            RotatePointLeft90(detection.X2, detection.Y2, sourceWidth),
            RotatePointLeft90(detection.X1, detection.Y2, sourceWidth)
        };

        var x1 = rotatedCorners.Min(point => point.X);
        var y1 = rotatedCorners.Min(point => point.Y);
        var x2 = rotatedCorners.Max(point => point.X);
        var y2 = rotatedCorners.Max(point => point.Y);

        return new DetectionInfo(
            detection.ClassName,
            detection.Score,
            (float)x1,
            (float)y1,
            (float)x2,
            (float)y2,
            detection.ObjectId);
    }

    private static Point RotatePointLeft90(double x, double y, int sourceWidth)
    {
        return new Point(y, sourceWidth - x);
    }

    private static void CacheFrame(ReceivedVideoFrame frame, Dictionary<uint, ReceivedVideoFrame> frameCache)
    {
        frameCache[frame.FrameIndex] = frame;
        TrimCache(frameCache);
    }

    private static void CacheDetectionPacket(
        DetectionPacket detectionPacket,
        Dictionary<uint, DetectionPacket> detectionCache)
    {
        detectionCache[detectionPacket.FrameId] = detectionPacket;
        TrimCache(detectionCache);
    }

    private bool TryGetRenderableDetectionPacket(
        uint currentFrameId,
        Dictionary<uint, DetectionPacket> detectionCache,
        out DetectionPacket detectionPacket)
    {
        if (detectionCache.TryGetValue(currentFrameId, out detectionPacket))
        {
            return true;
        }

        var recentCandidates = detectionCache
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

        var fallbackCandidates = detectionCache
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
        var latestFrame = _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame;
        var frameCache = _viewModel.IsEoPrimary ? _eoFrameCache : _irFrameCache;
        var detectionCache = _viewModel.IsEoPrimary ? _eoDetectionCache : _irDetectionCache;

        if (latestFrame is not null &&
            detectionCache.TryGetValue(latestFrame.Value.FrameIndex, out detectionPacket))
        {
            frame = latestFrame.Value;
            return true;
        }

        var exactPairs = detectionCache
            .Where(pair => pair.Value.Detections.Count > 0 && frameCache.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var pair in exactPairs)
        {
            frame = frameCache[pair.Key];
            detectionPacket = pair.Value;
            return true;
        }

        if (latestFrame is not null &&
            TryGetRenderableDetectionPacket(latestFrame.Value.FrameIndex, detectionCache, out detectionPacket))
        {
            frame = latestFrame.Value;
            return true;
        }

        frame = default;
        detectionPacket = default;
        return false;
    }

    private IReadOnlyList<DetectionInfo> FilterDisplayDetections(IReadOnlyList<DetectionInfo> detections)
    {
        var filtered = detections
            .Where(ShouldDisplayDetectionSafe)
            .ToArray();
        return filtered;
    }

    private bool ShouldDisplayDetectionSafe(DetectionInfo detection)
    {
        var className = detection.ClassName.ToLowerInvariant();
        var primaryTarget = _viewModel.SelectedPrimaryTarget;
        if (detection.Score < DisplayScoreThreshold)
        {
            return false;
        }

        if (primaryTarget == "\uBCF5\uD569")
        {
            return true;
        }

        if (primaryTarget == "\uC0AC\uB78C")
        {
            return className == "person";
        }

        if (primaryTarget == "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className == "airplane";
        }

        if (primaryTarget == "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className is "bicycle" or "car" or "motorcycle" or "bus" or "truck" or "train";
        }

        if (primaryTarget == "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className == "boat";
        }

        if (primaryTarget == "\uD1B5\uC2E0 \uC7A5\uBE44")
        {
            return className is "cell phone" or "laptop";
        }

        if (primaryTarget == "\uBE44\uAD70\uC0AC \uD45C\uC801")
        {
            return NonMilitaryTargetClasses.Contains(className);
        }

        return true;
    }

    private bool ShouldDisplayDetection(DetectionInfo detection) => ShouldDisplayDetectionSafe(detection);

    private void NotifyDetectionAlertIfNeeded(uint frameId, IReadOnlyList<DetectionInfo> detections)
    {
        _lastDetectionAlertSignature = detections.Count == 0 ? null : $"{frameId}:{detections.Count}";
    }

    private static void TrimCache<T>(Dictionary<uint, T> cache)
    {
        while (cache.Count > OverlayCacheLimit)
        {
            var oldestKey = cache.Keys.Min();
            cache.Remove(oldestKey);
        }
    }

    private void AddDetectionVisualToCanvas(
        double rectLeft,
        double rectTop,
        double rectWidth,
        double rectHeight,
        DetectionInfo detection)
    {
        var accentBrush = new SolidColorBrush(Color.FromRgb(105, 255, 132));
        accentBrush.Freeze();
        var mainRectangle = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = accentBrush,
            StrokeThickness = 2,
            RadiusX = 2,
            RadiusY = 2,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(mainRectangle, rectLeft);
        Canvas.SetTop(mainRectangle, rectTop);
        DetectionOverlayCanvas.Children.Add(mainRectangle);

        var cornerLength = Math.Max(12, Math.Min(rectWidth, rectHeight) * 0.18);
        AddCornerToCanvas(rectLeft, rectTop, cornerLength, true, true, accentBrush);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop, cornerLength, false, true, accentBrush);
        AddCornerToCanvas(rectLeft, rectTop + rectHeight, cornerLength, true, false, accentBrush);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop + rectHeight, cornerLength, false, false, accentBrush);

        var labelText = new TextBlock
        {
            Text = detection.LabelText,
            Foreground = accentBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };

        labelText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = labelText.DesiredSize.Width;
        var labelHeight = labelText.DesiredSize.Height;
        var labelLeft = Math.Max(0, Math.Min(rectLeft, Math.Max(0, CameraViewport.ActualWidth - labelWidth - 4)));
        var preferredTop = rectTop - labelHeight - 6;
        var labelTop = preferredTop >= 0 ? preferredTop : Math.Min(CameraViewport.ActualHeight - labelHeight - 4, rectTop + 6);
        Canvas.SetLeft(labelText, labelLeft);
        Canvas.SetTop(labelText, Math.Max(0, labelTop));
        DetectionOverlayCanvas.Children.Add(labelText);
    }

    private void AddCornerToCanvas(
        double anchorX,
        double anchorY,
        double length,
        bool isLeft,
        bool isTop,
        Brush strokeBrush)
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

    private void MotorButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string direction })
        {
            return;
        }

        StartMotorRepeat(direction);
        e.Handled = true;
    }

    private void MotorButton_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string direction })
        {
            return;
        }

        StopMotorRepeat(direction);
        e.Handled = true;
    }

    private void MotorButton_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { Tag: string direction })
        {
            return;
        }

        StopMotorRepeat(direction);
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!TryMapKeyToMotorDirection(e.Key, out var direction))
        {
            return;
        }

        if (_pressedMotorKeys.Add(e.Key))
        {
            StartMotorRepeat(direction);
        }

        e.Handled = true;
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!TryMapKeyToMotorDirection(e.Key, out var direction))
        {
            return;
        }

        _pressedMotorKeys.Remove(e.Key);
        StopMotorRepeat(direction);
        e.Handled = true;
    }

    private void StartMotorRepeat(string direction)
    {
        if (!_viewModel.IsManualMode)
        {
            return;
        }

        if (_activeMotorDirections.TryGetValue(direction, out var count))
        {
            _activeMotorDirections[direction] = count + 1;
        }
        else
        {
            _activeMotorDirections[direction] = 1;
        }

        SendActiveMotorButtons();

        if (_activeMotorDirections.Count > 0)
        {
            _motorHoldTimer.Start();
        }
    }

    private void StopMotorRepeat(string direction)
    {
        if (!_activeMotorDirections.TryGetValue(direction, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _activeMotorDirections.Remove(direction);
        }
        else
        {
            _activeMotorDirections[direction] = count - 1;
        }

        SendActiveMotorButtons();

        if (_activeMotorDirections.Count == 0)
        {
            _motorHoldTimer.Stop();
        }
    }

    private void MotorHoldTimer_OnTick(object? sender, EventArgs e)
    {
        if (_activeMotorDirections.Count == 0 || !_viewModel.IsManualMode)
        {
            _motorHoldTimer.Stop();
            return;
        }

        SendActiveMotorButtons();
    }

    private void SendActiveMotorButtons()
    {
        if (!_viewModel.IsManualMode)
        {
            return;
        }

        _viewModel.UpdateManualButtonState(GetActiveMotorButtons());
    }

    private void UpdateMotorAutomationState()
    {
        if (_viewModel.IsAutoMode)
        {
            StopManualMotorInput();
        }
    }

    private void StopManualMotorInput()
    {
        _motorHoldTimer.Stop();
        _activeMotorDirections.Clear();
        _pressedMotorKeys.Clear();
    }

    private static bool TryMapKeyToMotorDirection(Key key, out string direction)
    {
        direction = key switch
        {
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(direction);
    }

    private MotorButtonMask GetActiveMotorButtons()
    {
        if (_activeMotorDirections.Count == 0)
        {
            return MotorButtonMask.None;
        }

        if (_activeMotorDirections.ContainsKey("Center"))
        {
            return MotorButtonMask.Center;
        }

        var buttons = MotorButtonMask.None;

        if (_activeMotorDirections.ContainsKey("Left"))
        {
            buttons |= MotorButtonMask.Left;
        }

        if (_activeMotorDirections.ContainsKey("Right"))
        {
            buttons |= MotorButtonMask.Right;
        }

        if (_activeMotorDirections.ContainsKey("Up"))
        {
            buttons |= MotorButtonMask.Up;
        }

        if (_activeMotorDirections.ContainsKey("Down"))
        {
            buttons |= MotorButtonMask.Down;
        }

        return buttons;
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
        StopManualMotorInput();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived -= OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady -= OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived -= OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _rosTopicBridgeProcessService.MessageReady -= OnRosTopicBridgeMessageReady;
        _rosTopicBridgeProcessService.Dispose();
        _viewportRecordingService.Dispose();
        _eoUdpCaptureService.Dispose();
        _irUdpCaptureService.Dispose();
        _motorControlService.Dispose();
    }
}

