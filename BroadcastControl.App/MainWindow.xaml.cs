using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Models.Network;
using BroadcastControl.App.Models.Vlm;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;
using System.Collections.Generic;
using System.IO;

namespace BroadcastControl.App;

// ?뚯씪 ??븷:
// MainWindow.xaml??肄붾뱶 鍮꾪븯?몃뱶濡? ?붾㈃ 而⑦듃濡??대깽?몄? ?몃? ?듭떊 ?쒕퉬?ㅻ? ?곌껐?쒕떎.
// UDP ?곸긽/?먯?/VLM/紐⑦꽣 ?곹깭瑜?諛쏆븘 ViewModel??諛섏쁺?섍퀬, ?ъ슜?먭? ?꾨Ⅸ 踰꾪듉?대굹 ?ㅼ젙 媛믪쓣 ?쒕퉬?ㅻ줈 ?꾨떖?쒕떎.
// ?붾㈃ ?곹깭 ?먯껜??MainViewModel??愿由ы븯誘濡? ???뚯씪? UI ?대깽?몄? ?쒕퉬???대깽?몃? ?댁뼱二쇰뒗 ?곌껐 怨꾩링?쇰줈 蹂대㈃ ?쒕떎.

public partial class MainWindow : Window
{
    // MainWindow???붾㈃ ?붿냼? ?쒕퉬?ㅻ뱾???곌껐?섎뒗 以묒떖 怨꾩링?대떎.
    // ?ㅼ젣 UDP ?뚯떛? Services媛 留↔퀬, ?ш린?쒕뒗 諛쏆? ?곗씠?곕? ?대뼡 ?붾㈃???쒖떆?좎?? ?대뼡 ?ъ슜???낅젰??蹂대궪吏瑜?寃곗젙?쒕떎.
    private enum DisplayRotation
    {
        None,
        Rotate180,
        RotateLeft90
    }

    private const double SettingsDrawerClosedOffset = 320;
    private const double WindowedWidth = 1600;
    private const double WindowedHeight = 900;
    private const string RecordedVideoCacheFolderName = "LIG_DNA_GUI_recorded_videos";
    private const double RecordedVideoMiniMapWidth = 120;
    private const double RecordedVideoMiniMapHeight = 62;
    private static readonly TimeSpan MobileAlertCooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan JetsonConnectionHoldTime = TimeSpan.FromSeconds(10);
    private static readonly HttpClient RecordedVideoHttpClient = new();
    private readonly AppNetworkSettings _networkSettings;
    private readonly MainViewModel _viewModel;
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;
    private readonly UdpEncodedVideoReceiverService _irUdpCaptureService;
    private readonly UdpEncodedVideoReceiverService _detectionUdpReceiverService;
    private readonly ViewportRecordingService _viewportRecordingService;
    private readonly UdpMotorControlService _motorControlService;
    private readonly UdpMotorStatusReceiverService _motorStatusReceiverService;
    private readonly UdpVlmResultReceiverService _vlmResultReceiverService;
    private readonly MobileAlertHubService _mobileAlertHubService;
    private readonly DispatcherTimer _motorHoldTimer;
    private readonly DispatcherTimer _recordedVideoPositionTimer;
    private readonly DispatcherTimer _recordingMetadataTimer;
    private readonly DispatcherTimer _jetsonConnectionTimer;

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
    private bool _isViewportRecordingActive;
    private bool _isDraggingRecordedVideoPosition;
    private bool _isDraggingRecordedVideoPan;
    private readonly List<RecordedVideoItem> _recordedVideoFiles = new();
    private string? _recordedVideoSelectedFolder;
    private double _recordedVideoZoomLevel = 1.0;
    private double _recordedVideoPanX;
    private double _recordedVideoPanY;
    private Point _lastRecordedVideoPanPoint;
    private string? _lastDetectionAlertSignature;
    private DateTimeOffset _lastMobileAlertAt = DateTimeOffset.MinValue;
    private DateTime _recordingMetadataWindowStart;
    private DateTime _lastJetsonMessageAt = DateTime.MinValue;
    private string? _lastFilteredOutTargetSignature;
    private string? _lastOverlaySignature;
    // VLM??蹂대궡??媛앹껜蹂??꾪뿕?꾨? objectId 湲곗??쇰줈 ??ν븳??
    // 諛붿슫??諛뺤뒪 ?됱긽怨??쒖뒪???꾪뿕?꾨뒗 ??媛믪쓣 ?곗꽑 ?ъ슜?쒕떎.
    private string _latestGlobalVlmThreatLevel = string.Empty;
    private readonly Dictionary<int, string> _objectThreatLevels = new();
    private readonly Dictionary<string, int> _activeMotorDirections = new(StringComparer.Ordinal);
    private readonly HashSet<Key> _pressedMotorKeys = new();
    private const int OverlayCacheLimit = 48;
    private const uint OverlayFrameTolerance = 12;
    private const float DisplayScoreThreshold = 0.60f;
    private sealed class RecordedVideoItem
    {
        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string Folder { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsFolder { get; set; }

        public bool IsBack { get; set; }
    }

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

        _networkSettings = AppNetworkSettings.Load();
        _motorControlService = new UdpMotorControlService(
            _networkSettings.JetsonHost,
            _networkSettings.MotorControlPort,
            _networkSettings.TrackingRecordingControlPort);
        _viewModel = new MainViewModel(_motorControlService);
        _eoUdpCaptureService = new UdpEncodedVideoReceiverService();
        _irUdpCaptureService = new UdpEncodedVideoReceiverService(applyIrFalseColor: true);
        _detectionUdpReceiverService = new UdpEncodedVideoReceiverService();
        _viewportRecordingService = new ViewportRecordingService();
        _motorStatusReceiverService = new UdpMotorStatusReceiverService(_networkSettings.MotorStatusPort);
        _vlmResultReceiverService = new UdpVlmResultReceiverService(_networkSettings.VlmResultPort);
        _mobileAlertHubService = new MobileAlertHubService();
        _motorHoldTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _motorHoldTimer.Tick += MotorHoldTimer_OnTick;
        _recordedVideoPositionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _recordedVideoPositionTimer.Tick += RecordedVideoPositionTimer_OnTick;
        _recordingMetadataTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _recordingMetadataTimer.Tick += RecordingMetadataTimer_OnTick;
        _jetsonConnectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _jetsonConnectionTimer.Tick += JetsonConnectionTimer_OnTick;
        DataContext = _viewModel;
        WireActiveViewEvents();

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        PreviewKeyUp += MainWindow_OnPreviewKeyUp;
    }

    private void WireActiveViewEvents()
    {
        CameraActiveView.CameraViewportSizeChanged += CameraViewport_OnSizeChanged;
        CameraActiveView.CameraViewportMouseLeftButtonDown += CameraViewport_OnMouseLeftButtonDown;
        CameraActiveView.CameraViewportMouseWheel += CameraViewport_OnMouseWheel;
        CameraActiveView.CameraViewportMouseMove += CameraViewport_OnMouseMove;
        CameraActiveView.CameraViewportMouseLeftButtonUp += CameraViewport_OnMouseLeftButtonUp;
        CameraActiveView.RotateLargeFeedRequested += RotateLargeFeedButton_OnClick;
        CameraActiveView.RotateInsetFeedRequested += RotateInsetFeedButton_OnClick;
        CameraActiveView.MotorButtonPreviewMouseLeftButtonDownRequested += MotorButton_OnPreviewMouseLeftButtonDown;
        CameraActiveView.MotorButtonPreviewMouseLeftButtonUpRequested += MotorButton_OnPreviewMouseLeftButtonUp;
        CameraActiveView.MotorButtonMouseLeaveRequested += MotorButton_OnMouseLeave;

        MotorActiveView.RotateAuxCameraRequested += RotateAuxCameraButton_OnClick;
        MotorActiveView.MotorTargetEnterClicked += Button_Click_2;
        MotorActiveView.MotorButtonPreviewMouseLeftButtonDownRequested += MotorButton_OnPreviewMouseLeftButtonDown;
        MotorActiveView.MotorButtonPreviewMouseLeftButtonUpRequested += MotorButton_OnPreviewMouseLeftButtonUp;
        MotorActiveView.MotorButtonMouseLeaveRequested += MotorButton_OnMouseLeave;
        MotorDetailsActiveView.BackdropMouseLeftButtonDownRequested += MotorDetailsBackdrop_OnMouseLeftButtonDown;

        OperationActiveView.ManualModeClicked += Button_Click;
        OperationActiveView.SaveNetworkSettingsClicked += SaveNetworkSettingsButton_OnClick;

        MonitoringActiveView.OpenRecordedVideosClicked += OpenRecordedVideosButton_OnClick;
        MonitoringActiveView.SettingsClicked += Button_Click_1;
        MonitoringActiveView.DetectionTargetSelectionChanged += DetectionTargetList_OneSelctionChanged;

        RecordedVideosActiveView.RefreshRecordedVideosClicked += RefreshRecordedVideosButton_OnClick;
        RecordedVideosActiveView.CloseRecordedVideosClicked += CloseRecordedVideosButton_OnClick;
        RecordedVideosActiveView.RecordedVideoSelectionChanged += RecordedVideoList_OnSelectionChanged;
        RecordedVideosActiveView.RecordedVideoViewportMouseWheel += RecordedVideoViewport_OnMouseWheel;
        RecordedVideosActiveView.RecordedVideoViewportMouseLeftButtonDown += RecordedVideoViewport_OnMouseLeftButtonDown;
        RecordedVideosActiveView.RecordedVideoViewportMouseMove += RecordedVideoViewport_OnMouseMove;
        RecordedVideosActiveView.RecordedVideoViewportMouseLeftButtonUp += RecordedVideoViewport_OnMouseLeftButtonUp;
        RecordedVideosActiveView.RecordedVideoMediaOpened += RecordedVideoPlayer_OnMediaOpened;
        RecordedVideosActiveView.RecordedVideoMediaEnded += RecordedVideoPlayer_OnMediaEnded;
        RecordedVideosActiveView.RecordedVideoMediaFailed += (sender, args) => RecordedVideoPlayer_OnMediaFailed(sender ?? RecordedVideosActiveView, args);
        RecordedVideosActiveView.RecordedVideoPositionSliderValueChanged += RecordedVideoPositionSlider_OnValueChanged;
        RecordedVideosActiveView.RecordedVideoPositionSliderPreviewMouseLeftButtonDown += RecordedVideoPositionSlider_OnPreviewMouseLeftButtonDown;
        RecordedVideosActiveView.RecordedVideoPositionSliderPreviewMouseLeftButtonUp += RecordedVideoPositionSlider_OnPreviewMouseLeftButtonUp;
        RecordedVideosActiveView.RecordedVideoPlayClicked += RecordedVideoPlayButton_OnClick;
        RecordedVideosActiveView.RecordedVideoPauseClicked += RecordedVideoPauseButton_OnClick;
        RecordedVideosActiveView.RecordedVideoStopClicked += RecordedVideoStopButton_OnClick;
        RecordedVideosActiveView.RecordedVideoZoomResetClicked += RecordedVideoZoomResetButton_OnClick;
        RecordedVideosActiveView.RecordedVideoZoomSliderValueChanged += RecordedVideoZoomSlider_OnValueChanged;
        RecordedVideosActiveView.PlaybackSpeedSelectionChanged += PlaybackSpeedCombo_OnSelectionChanged;

        SettingsActiveView.BackdropMouseLeftButtonDownRequested += SettingsBackdrop_OnMouseLeftButtonDown;
        SettingsActiveView.WindowModeToggleClicked += WindowModeToggleButton_OnClick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ???쒖옉 ??紐⑤뱺 ?섏떊 ?쒕퉬?ㅻ? ?곌껐?쒕떎.
        // EO/IR ?곸긽, 紐⑦꽣 ?곹깭, VLM 寃곌낵, 紐⑤컮???뚮┝ ?쒕쾭媛 媛곴컖 ?낅┰?곸쑝濡??숈옉?쒕떎.
        WindowState = WindowState.Maximized;
        UpdateWindowModeButtonText();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ManualAnalysisSaveRequested += ViewModel_OnManualAnalysisSaveRequested;
        _viewModel.ManualSystemLogSaveRequested += ViewModel_OnManualSystemLogSaveRequested;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived += OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady += OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived += OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _detectionUdpReceiverService.DetectionsReceived += OnSharedDetectionsReceived;
        _detectionUdpReceiverService.StatusReceived += OnYoloStatusReceived;
        _motorStatusReceiverService.StatusReceived += OnMotorStatusReceived;
        _motorStatusReceiverService.ReceiverError += OnMotorStatusReceiverError;
        _vlmResultReceiverService.ResultReceived += OnVlmResultReceived;
        _vlmResultReceiverService.ReceiverError += OnVlmResultReceiverError;

        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
        _irUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _irUdpCaptureService.SetContrast(_viewModel.Contrast);
        _viewModel.InitializeMotorControlState();
        _motorStatusReceiverService.Start();
        _viewModel.AppendImportantLog($"紐⑦꽣 ?곹깭 ?섏떊 ?湲??ы듃: {_motorStatusReceiverService.Port}");
        _vlmResultReceiverService.Start();
        _viewModel.AppendImportantLog($"VLM 寃곌낵 ?섏떊 ?湲??ы듃: {_vlmResultReceiverService.Port}");

        _viewModel.UpdateViewportSize(CameraActiveView.CameraViewportElement.ActualWidth, CameraActiveView.CameraViewportElement.ActualHeight);
        UpdateRecordingViewportState();
        RenderDetectionOverlay();
        UpdateMotorAutomationState();
        _recordingMetadataWindowStart = DateTime.Now;
        _recordingMetadataTimer.Start();
        _jetsonConnectionTimer.Start();
        UpdateJetsonConnectionState();

        LoadNetworkSettingsEditor();
        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start(_networkSettings.EoUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the EO UDP stream receiver on port {_networkSettings.EoUdpPort}.");
        }

        if (_irUdpCaptureService.Start(_networkSettings.IrUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the IR UDP stream receiver on port {_networkSettings.IrUdpPort}.");
        }

        if (_detectionUdpReceiverService.Start(_networkSettings.DetectionUdpPort))
        {
            _viewModel.AppendImportantLog($"EO/IR ?먯? 寃곌낵 ?섏떊 ?湲??ы듃: {_networkSettings.DetectionUdpPort}");
        }
        else
        {
            _viewModel.AppendImportantLog($"EO/IR ?먯? 寃곌낵 ?섏떊 ?ы듃 {_networkSettings.DetectionUdpPort}瑜??댁? 紐삵뻽?듬땲??");
        }

        if (_mobileAlertHubService.Start(_networkSettings.MobileAlertPort))
        {
            _viewModel.AppendImportantLog($"紐⑤컮???꾪뿕 ?뚮┝ ?깆씠 ?쒖옉?섏뿀?듬땲?? {_mobileAlertHubService.AccessHintUrls}");
        }
        else
        {
            _viewModel.AppendImportantLog($"紐⑤컮???꾪뿕 ?뚮┝ ???쒖옉???ㅽ뙣?덉뒿?덈떎. ?ы듃 {_networkSettings.MobileAlertPort}瑜??뺤씤?섏꽭??");
        }
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

            case nameof(MainViewModel.IsRecordingActive):
                HandleRecordingActiveStateChanged();
                break;

            case nameof(MainViewModel.ZoomLevel):
            case nameof(MainViewModel.ZoomTransformX):
            case nameof(MainViewModel.ZoomTransformY):
            case nameof(MainViewModel.IsEoPrimary):
            case nameof(MainViewModel.SelectedPrimaryTarget):
                UpdateRecordingViewportState();
                RefreshPrimaryTrackingTarget();
                RenderDetectionOverlay(forceRefresh: true);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: true);
                break;

            case nameof(MainViewModel.CurrentMode):
            case nameof(MainViewModel.IsSystemPoweredOn):
                UpdateMotorAutomationState();
                break;

            case nameof(MainViewModel.IsEnglishLanguage):
            case nameof(MainViewModel.IsKoreanLanguage):
                UpdateWindowModeButtonText();
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
        // ???곸긽 ?붾㈃???대┃?덉쓣 ??癒쇱? 諛붿슫??諛뺤뒪 ?좏깮???쒕룄?쒕떎.
        // 諛뺤뒪 ?덉쓣 ?대┃??寃쎌슦 ?대떦 YOLO 媛앹껜 ID瑜?紐⑦꽣 異붿쟻 ??곸쑝濡?蹂대궡怨? 諛뺤뒪媛 ?놁쑝硫?湲곗〈 以??쒕옒洹??숈옉???섑뻾?쒕떎.
        if (TrySelectDetectionAtPoint(e.GetPosition(CameraActiveView.CameraViewportElement)))
        {
            e.Handled = true;
            return;
        }

        if (!_viewModel.ShowZoomMiniMap)
        {
            return;
        }

        _isDraggingZoom = true;
        _lastZoomDragPoint = e.GetPosition(CameraActiveView.CameraViewportElement);
        CameraActiveView.CameraViewportElement.CaptureMouse();
    }

    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        var currentPoint = e.GetPosition(CameraActiveView.CameraViewportElement);
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
        CameraActiveView.CameraViewportElement.ReleaseMouseCapture();
    }

    private void OnEoFrameReady(ReceivedVideoFrame frame)
    {
        MarkJetsonMessageReceived();
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
        MarkJetsonMessageReceived();
        HandleDetectionsReceived(detectionPacket, _eoDetectionCache, _viewModel.IsEoPrimary);
    }

    private void OnIrDetectionsReceived(DetectionPacket detectionPacket)
    {
        MarkJetsonMessageReceived();
        HandleDetectionsReceived(detectionPacket, _irDetectionCache, !_viewModel.IsEoPrimary);
    }

    private void OnSharedDetectionsReceived(DetectionPacket detectionPacket)
    {
        MarkJetsonMessageReceived();
        switch (detectionPacket.Stream)
        {
            case DetectionStream.Eo:
                HandleDetectionsReceived(detectionPacket, _eoDetectionCache, _viewModel.IsEoPrimary);
                break;
            case DetectionStream.Ir:
                HandleDetectionsReceived(detectionPacket, _irDetectionCache, !_viewModel.IsEoPrimary);
                break;
        }
    }

    private void DetectionTargetList_OneSelctionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not DetectionTargetItem item)
        {
            return;
        }

        _viewModel.SelectYoloObject(item.ObjectId, item.ThreatLevel);
        listBox.SelectedItem = null;
    }
    private void HandleDetectionsReceived(
        DetectionPacket detectionPacket,
        Dictionary<uint, DetectionPacket> detectionCache,
        bool isPrimaryCamera)
    {
        // detection? ?곸긽 ?꾨젅?꾨낫??議곌툑 ??쾶 ?꾩갑?????덉쑝誘濡?frame_id 湲곗??쇰줈 罹먯떆??蹂닿??쒕떎.
        // ?뚮뜑留??④퀎?먯꽌 媛??媛源뚯슫 ?꾨젅?꾩쓽 detection??李얠븘 諛뺤뒪瑜?洹몃┛??
        CacheDetectionPacket(detectionPacket, detectionCache);

        if (!_hasReceivedDetectionPacket)
        {
            _hasReceivedDetectionPacket = true;
        }

        var displayDetections = EnsureDetectionObjectIds(
            ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections)));

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
        if (isPrimaryCamera)
        {
            _viewModel.UpdateDetectionSummary(displayDetections);
            _viewModel.UpdateDetectionTargets(BuildDetectionTargetItems(
                displayDetections,
                _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame,
                detectionPacket.Width,
                detectionPacket.Height));
        }

        RenderDetectionOverlay(forceRefresh: true);
        UpdateRiskAndMobileAlert(detectionPacket.FrameId, displayDetections);
    }

    private void RefreshPrimaryTrackingTarget()
    {
        // 紐⑦꽣 異붿쟻 ID???꾩옱 ???붾㈃???쒖떆?섎뒗 移대찓?쇱쓽 媛앹껜留?湲곗??쇰줈 怨좊Ⅸ??
        // VLM 寃곌낵媛 ??쾶 ?꾩갑???꾪뿕?꾧? 媛깆떊?섎뒗 寃쎌슦?먮룄 罹먯떆???꾩옱 ?붾㈃ detection?쇰줈 ?ㅼ떆 ?먮떒?쒕떎.
        if (!TryGetRenderableFrameAndDetection(out var frame, out var detectionPacket))
        {
            _viewModel.UpdateDetectionSummary(Array.Empty<DetectionInfo>());
            _viewModel.UpdateDetectionTargets(Array.Empty<DetectionTargetItem>());
            return;
        }

        var displayDetections = EnsureDetectionObjectIds(
            ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections)));
        _viewModel.UpdateDetectionSummary(displayDetections);
        _viewModel.UpdateDetectionTargets(BuildDetectionTargetItems(
            displayDetections,
            frame,
            detectionPacket.Width,
            detectionPacket.Height));
    }

    private void OnYoloStatusReceived(YoloStatusPacket statusPacket)
    {
        MarkJetsonMessageReceived();
        var signature = $"{statusPacket.Enabled}:{statusPacket.ModelLoaded}:{statusPacket.ConfThreshold}:{statusPacket.LastError}:{statusPacket.Source}";
        if (string.Equals(_lastStatusSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusSignature = signature;

        if (!statusPacket.ModelLoaded)
        {
            _viewModel.AppendImportantLog("YOLO 紐⑤뜽???꾩쭅 濡쒕뱶?섏? ?딆븯?듬땲??");
        }

        if (!string.IsNullOrWhiteSpace(statusPacket.LastError))
        {
            _viewModel.AppendImportantLog($"YOLO ?곹깭 ?ㅻ쪟: {statusPacket.LastError}");
        }
    }

    private void OnMotorStatusReceived(object? sender, MotorStatusSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            MarkJetsonMessageReceived();
            _viewModel.UpdateMotorStatus(snapshot);
        });
    }

    private void OnMotorStatusReceiverError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendImportantLog($"紐⑦꽣 ?곹깭 ?섏떊 ?ㅻ쪟: {message}"));
    }

    private void OnVlmResultReceived(object? sender, VlmResultPacket result)
    {
        Dispatcher.Invoke(() =>
        {
            MarkJetsonMessageReceived();
            // VLM 寃곌낵???꾩껜 ?꾪뿕?꾩? 媛앹껜蹂??꾪뿕?꾨줈 ?섎돏??
            // ?꾩껜 ?꾪뿕?꾨뒗 ?쒖뒪???곹깭李쎌뿉, 媛앹껜蹂??꾪뿕?꾨뒗 媛?諛붿슫??諛뺤뒪 ?됱긽??諛섏쁺?쒕떎.
            if (!string.IsNullOrWhiteSpace(result.ThreatLevel))
            {
                _latestGlobalVlmThreatLevel = NormalizeThreatLevel(result.ThreatLevel);
            }

            // VLM??媛앹껜蹂??꾪뿕?꾨? 蹂대궡硫?track_id/objectId 湲곗??쇰줈 蹂닿??덈떎媛 諛붿슫??諛뺤뒪 ?됯낵 ?쒖뒪???꾪뿕?꾩뿉 諛섏쁺?쒕떎.
            foreach (var pair in result.ObjectThreatLevels)
            {
                _objectThreatLevels[pair.Key] = NormalizeThreatLevel(pair.Value);
            }

            var threatLevel = string.IsNullOrWhiteSpace(result.ThreatLevel)
                ? _viewModel.CurrentThreatLevel
                : result.ThreatLevel;
            var analysisMessage = string.IsNullOrWhiteSpace(result.DetectionSummary)
                ? result.AnalysisMessage
                : $"{result.AnalysisMessage} ?먯? ?댁슜: {result.DetectionSummary}";

            _viewModel.ApplyVlmAnalysisResult(threatLevel, analysisMessage);
            RefreshPrimaryTrackingTarget();
            RenderDetectionOverlay(forceRefresh: true);
        });
    }

    private void OnVlmResultReceiverError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendImportantLog($"VLM 寃곌낵 ?섏떊 ?ㅻ쪟: {message}"));
    }

    private void OnIrFrameReady(ReceivedVideoFrame frame)
    {
        MarkJetsonMessageReceived();
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

    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraActiveView.CameraViewportElement.ActualWidth,
            CameraActiveView.CameraViewportElement.ActualHeight);
        _irUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraActiveView.CameraViewportElement.ActualWidth,
            CameraActiveView.CameraViewportElement.ActualHeight);
    }

    private void MarkJetsonMessageReceived()
    {
        _lastJetsonMessageAt = DateTime.Now;
        if (Dispatcher.CheckAccess())
        {
            UpdateJetsonConnectionState();
        }
        else
        {
            Dispatcher.BeginInvoke(UpdateJetsonConnectionState);
        }
    }

    private void JetsonConnectionTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateJetsonConnectionState();
    }

    private void UpdateJetsonConnectionState()
    {
        var isConnected =
            _lastJetsonMessageAt != DateTime.MinValue &&
            DateTime.Now - _lastJetsonMessageAt <= JetsonConnectionHoldTime;
        _viewModel.UpdateJetsonConnectionState(isConnected);
    }

    private void RenderDetectionOverlay(bool forceRefresh = false)
    {
        // ?꾩옱 ???붾㈃(EO ?먮뒗 IR)???대떦?섎뒗 理쒖떊 ?꾨젅?꾧낵 detection??留욎떠 諛붿슫??諛뺤뒪瑜??ㅼ떆 洹몃┛??
        // 以?李??ш린/EO-IR ?꾪솚??諛붾뚮㈃ forceRefresh濡?罹먯떆???붾㈃ ?쒕챸??臾댁떆?쒕떎.
        if (_isRenderingOverlay)
        {
            return;
        }

        if (CameraActiveView.DetectionOverlayCanvasElement is null)
        {
            return;
        }

        _isRenderingOverlay = true;
        try
        {
            CameraActiveView.DetectionOverlayCanvasElement.Children.Clear();

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

            var displayDetections = ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections));
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
            var activeTrackId = detectionPacket.ActiveTrackId;
            var overlaySignature = $"{rotation}:{activeTrackId}:{BuildOverlaySignature(rotatedDetections)}";
            if (!forceRefresh && string.Equals(_lastOverlaySignature, overlaySignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastOverlaySignature = overlaySignature;

            var viewportWidth = Math.Max(CameraActiveView.CameraViewportElement.ActualWidth, 1);
            var viewportHeight = Math.Max(CameraActiveView.CameraViewportElement.ActualHeight, 1);
            var baseScale = Math.Min(viewportWidth / rotatedSourceWidth, viewportHeight / rotatedSourceHeight);
            var scaleX = baseScale;
            var scaleY = baseScale;
            var scaledWidth = rotatedSourceWidth * scaleX;
            var scaledHeight = rotatedSourceHeight * scaleY;
            var baseLeft = (viewportWidth - scaledWidth) / 2.0;
            var baseTop = (viewportHeight - scaledHeight) / 2.0;

            foreach (var detection in rotatedDetections)
            {
                var rectLeft = baseLeft + (detection.X1 * scaleX);
                var rectTop = baseTop + (detection.Y1 * scaleY);
                var rectWidth = Math.Max(2, (detection.X2 - detection.X1) * scaleX);
                var rectHeight = Math.Max(2, (detection.Y2 - detection.Y1) * scaleY);

                if (rectWidth < 2 || rectHeight < 2)
                {
                    continue;
                }

                var isTrackId = detection.ObjectId == activeTrackId && activeTrackId != 0xFF;
                AddDetectionVisualToCanvas(rectLeft, rectTop, rectWidth, rectHeight, detection, isTrackId);
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
        return DisplayRotation.None;
    }

    private bool TrySelectDetectionAtPoint(Point viewportPoint)
    {
        // ?ъ슜?먭? ?꾨Ⅸ GUI 醫뚰몴瑜??꾩옱 ?곸긽 ?ㅼ????щ갚/以??대룞???곸슜??諛붿슫??諛뺤뒪 醫뚰몴? 鍮꾧탳?쒕떎.
        // ?щ윭 諛뺤뒪媛 寃뱀퀜 ?덉쑝硫????꾪뿕?섍퀬 ?좊ː?꾧? ?믪? 媛앹껜瑜??곗꽑 ?좏깮?쒕떎.
        if (!TryGetRenderableFrameAndDetection(out var frameToRender, out var detectionPacket))
        {
            return false;
        }

        var displayDetections = ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections));
        if (displayDetections.Count == 0)
        {
            return false;
        }

        var rotation = GetCurrentDisplayRotation();
        var originalSourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : frameToRender.Width;
        var originalSourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : frameToRender.Height;
        if (originalSourceWidth <= 0 || originalSourceHeight <= 0)
        {
            return false;
        }

        var rotatedSourceWidth = GetRotatedWidth(originalSourceWidth, originalSourceHeight, rotation);
        var rotatedSourceHeight = GetRotatedHeight(originalSourceWidth, originalSourceHeight, rotation);
        var viewportWidth = Math.Max(CameraActiveView.CameraViewportElement.ActualWidth, 1);
        var viewportHeight = Math.Max(CameraActiveView.CameraViewportElement.ActualHeight, 1);
        var baseScale = Math.Min(viewportWidth / rotatedSourceWidth, viewportHeight / rotatedSourceHeight);
        var scaledWidth = rotatedSourceWidth * baseScale;
        var scaledHeight = rotatedSourceHeight * baseScale;
        var baseLeft = (viewportWidth - scaledWidth) / 2.0;
        var baseTop = (viewportHeight - scaledHeight) / 2.0;

        // ?붾㈃???뺣?/?대룞???곹깭?먯꽌???ъ슜?먭? ?ㅼ젣濡?蹂대뒗 諛붿슫??諛뺤뒪 ?꾩튂瑜?湲곗??쇰줈 ?대┃ ?먯젙???쒕떎.
        var zoomLevel = Math.Max(_viewModel.ZoomLevel, 1.0);
        var viewportCenter = new Point(viewportWidth / 2.0, viewportHeight / 2.0);

        var selectedDetection = displayDetections
            .Select(detection => RotateDetectionForDisplay(detection, originalSourceWidth, originalSourceHeight, rotation))
            .Select(detection => new
            {
                Detection = detection,
                Rect = TransformRectForZoom(
                    new Rect(
                        baseLeft + (detection.X1 * baseScale),
                        baseTop + (detection.Y1 * baseScale),
                        Math.Max(2, (detection.X2 - detection.X1) * baseScale),
                        Math.Max(2, (detection.Y2 - detection.Y1) * baseScale)),
                    viewportCenter,
                    zoomLevel,
                    _viewModel.ZoomTransformX,
                    _viewModel.ZoomTransformY)
            })
            .Where(item => item.Rect.Contains(viewportPoint))
            .OrderByDescending(item => GetThreatWeight(item.Detection.ThreatLevel))
            .ThenByDescending(item => item.Detection.Score)
            .FirstOrDefault();

        if (selectedDetection is null)
        {
            return false;
        }

        _viewModel.SelectYoloObject(selectedDetection.Detection.ObjectId, selectedDetection.Detection.ThreatLevel);
        return true;
    }

    private static Rect TransformRectForZoom(Rect rect, Point center, double scale, double translateX, double translateY)
    {
        var topLeft = TransformPointForZoom(rect.TopLeft, center, scale, translateX, translateY);
        var bottomRight = TransformPointForZoom(rect.BottomRight, center, scale, translateX, translateY);
        return new Rect(topLeft, bottomRight);
    }

    private static Point TransformPointForZoom(Point point, Point center, double scale, double translateX, double translateY)
    {
        return new Point(
            center.X + ((point.X - center.X) * scale) + translateX,
            center.Y + ((point.Y - center.Y) * scale) + translateY);
    }

    private static string BuildOverlaySignature(IReadOnlyList<DetectionInfo> detections)
    {
        return string.Join(
            "|",
            detections.Select(d => $"{d.ObjectId}:{d.ClassName}:{d.ThreatLevel}:{d.X1:0}:{d.Y1:0}:{d.X2:0}:{d.Y2:0}"));
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
                detection.ObjectId,
                detection.ThreatLevel),
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
            detection.ObjectId,
            detection.ThreatLevel);
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

    private IReadOnlyList<DetectionInfo> ApplyThreatLevels(IReadOnlyList<DetectionInfo> detections)
    {
        return detections
            .Select(detection => detection with { ThreatLevel = GetDetectionThreatLevel(detection) })
            .ToArray();
    }

    private static IReadOnlyList<DetectionInfo> EnsureDetectionObjectIds(IReadOnlyList<DetectionInfo> detections)
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        var allIdsLookMissing = detections.All(detection => detection.ObjectId <= 0);
        var usedIds = new HashSet<int>();
        var nextDummyId = 1;
        var normalized = new DetectionInfo[detections.Count];

        for (var index = 0; index < detections.Count; index++)
        {
            var detection = detections[index];
            var objectId = detection.ObjectId;
            if (allIdsLookMissing || objectId < 0 || objectId > 254 || !usedIds.Add(objectId))
            {
                while (usedIds.Contains(nextDummyId) && nextDummyId < 255)
                {
                    nextDummyId++;
                }

                objectId = Math.Clamp(nextDummyId, 1, 254);
                usedIds.Add(objectId);
                nextDummyId++;
            }

            normalized[index] = detection with { ObjectId = objectId };
        }

        return normalized;
    }

    private string GetDetectionThreatLevel(DetectionInfo detection)
    {
        if (_objectThreatLevels.TryGetValue(detection.ObjectId, out var objectThreatLevel))
        {
            return NormalizeThreatLevel(objectThreatLevel);
        }

        if (!string.IsNullOrWhiteSpace(detection.ThreatLevel))
        {
            return NormalizeThreatLevel(detection.ThreatLevel);
        }

        if (_objectThreatLevels.Count == 0 && !string.IsNullOrWhiteSpace(_latestGlobalVlmThreatLevel))
        {
            return NormalizeThreatLevel(_latestGlobalVlmThreatLevel);
        }

        return EstimateThreatLevelFromClass(detection.ClassName);
    }

    private static string EstimateThreatLevelFromClass(string className)
    {
        // VLM 媛앹껜蹂??꾪뿕?꾧? ?꾩쭅 ?ㅼ? ?딆? ?쒓컙?먮룄 ?붾㈃ ?됱씠 ?꾩쟾??鍮꾩뼱 蹂댁씠吏 ?딅룄濡??꾩떆 湲곗????붾떎.
        var normalizedClass = className.Trim().ToLowerInvariant();
        if (normalizedClass is "airplane" or "car" or "motorcycle" or "bus" or "truck" or "train" or "boat" or "tank" or "drone" or "missile" or "weapon")
        {
            return "?믪쓬";
        }

        if (normalizedClass is "person" or "bicycle" or "cell phone" or "laptop")
        {
            return "以묎컙";
        }

        return "??쓬";
    }

    private static string NormalizeThreatLevel(string threatLevel)
    {
        return threatLevel.Trim().ToLowerInvariant() switch
        {
            "high" or "?믪쓬" => "?믪쓬",
            "medium" or "mid" or "以묎컙" => "以묎컙",
            _ => "??쓬"
        };
    }

    private static int GetThreatWeight(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "?믪쓬" => 3,
            "以묎컙" => 2,
            _ => 1
        };
    }

    private static string GetHighestThreatLevel(IReadOnlyList<DetectionInfo> detections)
    {
        return detections
            .OrderByDescending(detection => GetThreatWeight(detection.ThreatLevel))
            .Select(detection => NormalizeThreatLevel(detection.ThreatLevel))
            .FirstOrDefault("??쓬");
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

    private void UpdateRiskAndMobileAlert(uint frameId, IReadOnlyList<DetectionInfo> detections)
    {
        // ?쒖뒪???꾪뿕?꾨뒗 ?붾㈃???쒖떆 以묒씤 媛앹껜?ㅼ쓽 ?꾪뿕??以?媛???믪? 媛믪쑝濡?寃곗젙?쒕떎.
        // ?꾪뿕 ?곹솴??諛섎났?댁꽌 ?ㅼ뼱???紐⑤컮???뚮┝??怨쇰룄?섍쾶 ?몃━吏 ?딅룄濡?cooldown怨?signature瑜??④퍡 ?ъ슜?쒕떎.
        if (detections.Count == 0)
        {
            _viewModel.ApplyVlmAnalysisResult("??쓬", "VLM 遺꾩꽍: ?꾩옱 ?좏깮??二??먯?泥?湲곗? ?꾪뿕 媛앹껜媛 ?뺤씤?섏? ?딆븯?듬땲??");
            return;
        }

        var analysis = BuildVlmStyleAnalysis(detections);
        var detectionSummary = BuildDetectionSummary(detections);
        var systemThreatLevel = GetHighestThreatLevel(detections);
        _viewModel.ApplyVlmAnalysisResult(systemThreatLevel, $"{analysis} ?먯? ?댁슜: {detectionSummary}");

        var alertSignature = $"{_viewModel.SelectedPrimaryTarget}:{frameId}:{BuildOverlaySignature(detections)}";
        var now = DateTimeOffset.Now;
        if (string.Equals(_lastDetectionAlertSignature, alertSignature, StringComparison.Ordinal) ||
            now - _lastMobileAlertAt < MobileAlertCooldown)
        {
            return;
        }

        _lastDetectionAlertSignature = alertSignature;
        _lastMobileAlertAt = now;
        var evidencePng = CaptureElementAsPng(CameraActiveView.CameraPanelElement);
        _ = _mobileAlertHubService.PublishAlertAsync(
            "?댁슜?듭젣 ?꾪뿕 ?뚮┝",
            analysis,
            detectionSummary,
            _viewModel.CurrentThreatLevel,
            evidencePng);
        _viewModel.AppendImportantLog("紐⑤컮???깆쑝濡??꾪뿕 ?뚮┝???꾩넚?덉뒿?덈떎.");
    }

    private string BuildVlmStyleAnalysis(IReadOnlyList<DetectionInfo> detections)
    {
        return
            $"{_viewModel.LargeFeedTitle} ?곸긽?먯꽌 二??먯?泥?'{_viewModel.SelectedPrimaryTarget}' 湲곗? ?꾪뿕 媛앹껜 {detections.Count}媛쒓? ?뺤씤?섏뿀?듬땲?? " +
            "?댁슜?먮뒗 ???붾㈃??諛붿슫??諛뺤뒪 ?꾩튂瑜??뺤씤?섍퀬 異붿쟻/?뱁솕 ?곹깭瑜??좎??섏떗?쒖삤.";
    }

    private static string BuildDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        return string.Join(
            "\n",
            detections
                .OrderByDescending(d => d.Score)
                .Take(8)
                .Select((d, index) => $"{index + 1}. {d.ClassName} object{d.ObjectId} / ?꾪뿕??{d.ThreatLevel} / ?좊ː??{d.Score:0.00} / bbox ({d.X1:0}, {d.Y1:0})-({d.X2:0}, {d.Y2:0})"));
    }

    private static IReadOnlyList<DetectionTargetItem> BuildDetectionTargetItems(
        IReadOnlyList<DetectionInfo> detections,
        ReceivedVideoFrame? frame,
        int sourceWidth,
        int sourceHeight)
    {
        return detections
            .Take(30)
            .Select(detection =>
            {
                var threatBrush = GetDetectionThreatBrush(detection.ThreatLevel);
                threatBrush.Freeze();
                return new DetectionTargetItem(
                    detection.ObjectId,
                    detection.ClassName,
                    $"{detection.Score * 100.0f:0.0}%",
                    NormalizeThreatLevel(detection.ThreatLevel),
                    threatBrush,
                    TryCreateDetectionThumbnail(frame?.Bitmap, detection, sourceWidth, sourceHeight));
            })
            .ToArray();
    }

    private static ImageSource? TryCreateDetectionThumbnail(
        BitmapSource? bitmap,
        DetectionInfo detection,
        int sourceWidth,
        int sourceHeight)
    {
        if (bitmap is null || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        try
        {
            var scaleX = bitmap.PixelWidth / (double)sourceWidth;
            var scaleY = bitmap.PixelHeight / (double)sourceHeight;
            var x = Math.Clamp((int)Math.Floor(detection.X1 * scaleX), 0, Math.Max(0, bitmap.PixelWidth - 1));
            var y = Math.Clamp((int)Math.Floor(detection.Y1 * scaleY), 0, Math.Max(0, bitmap.PixelHeight - 1));
            var right = Math.Clamp((int)Math.Ceiling(detection.X2 * scaleX), x + 1, bitmap.PixelWidth);
            var bottom = Math.Clamp((int)Math.Ceiling(detection.Y2 * scaleY), y + 1, bitmap.PixelHeight);
            var rect = new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
            var cropped = new CroppedBitmap(bitmap, rect);
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CaptureElementAsPng(FrameworkElement element)
    {
        var width = Math.Max(1, (int)Math.Round(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(element.ActualHeight));
        if (width < 2 || height < 2)
        {
            return null;
        }

        try
        {
            element.UpdateLayout();
            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(element);
            renderTarget.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
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
        DetectionInfo detection,
        bool isTracked)
    {
        var accentBrush = GetDetectionThreatBrush(detection.ThreatLevel);
        accentBrush.Freeze();
        var mainRectangle = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = accentBrush,
            StrokeThickness = isTracked ? 4 : 2,
            RadiusX = 2,
            RadiusY = 2,
            Fill = isTracked
                ? new SolidColorBrush(Color.FromArgb(35,255,70,70))
                : Brushes.Transparent
        };
        Canvas.SetLeft(mainRectangle, rectLeft);
        Canvas.SetTop(mainRectangle, rectTop);
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(mainRectangle);

        var cornerLength = Math.Max(12, Math.Min(rectWidth, rectHeight) * 0.18);
        var cornerThickness = isTracked ? 5 : 3;
        AddCornerToCanvas(rectLeft, rectTop, cornerLength, true, true, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop, cornerLength, false, true, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft, rectTop + rectHeight, cornerLength, true, false, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop + rectHeight, cornerLength, false, false, accentBrush, cornerThickness);

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
        var labelLeft = Math.Max(0, Math.Min(rectLeft, Math.Max(0, CameraActiveView.CameraViewportElement.ActualWidth - labelWidth - 4)));
        var preferredTop = rectTop - labelHeight - 6;
        var labelTop = preferredTop >= 0 ? preferredTop : Math.Min(CameraActiveView.CameraViewportElement.ActualHeight - labelHeight - 4, rectTop + 6);
        Canvas.SetLeft(labelText, labelLeft);
        Canvas.SetTop(labelText, Math.Max(0, labelTop));
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(labelText);
    }

    private static SolidColorBrush GetDetectionThreatBrush(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "?믪쓬" => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
            "以묎컙" => new SolidColorBrush(Color.FromRgb(255, 193, 69)),
            _ => new SolidColorBrush(Color.FromRgb(123, 216, 143))
        };
    }

    private void AddCornerToCanvas(
        double anchorX,
        double anchorY,
        double length,
        bool isLeft,
        bool isTop,
        Brush strokeBrush,
        double strokeThickness)
    {
        var horizontal = new Line
        {
            X1 = anchorX,
            Y1 = anchorY,
            X2 = anchorX + (isLeft ? length : -length),
            Y2 = anchorY,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
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
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };

        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(horizontal);
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(vertical);
    }

    private void HandleRecordingActiveStateChanged()
    {
        if (_viewModel.IsRecordingActive)
        {
            if (_isViewportRecordingActive)
            {
                return;
            }

            var filePath = _viewportRecordingService.StartRecordingToDesktop(CameraActiveView.CameraPanelElement);
            _isViewportRecordingActive = true;
            _viewModel.AppendImportantLog($"Recording started: {filePath}");
            return;
        }

        if (!_isViewportRecordingActive)
        {
            return;
        }

        var savedPath = _viewportRecordingService.StopRecording();
        _isViewportRecordingActive = false;
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

    private void RotateLargeFeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RotateLargeFeedClockwise();
        RenderDetectionOverlay(forceRefresh: true);
        e.Handled = true;
    }

    private void RotateInsetFeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RotateInsetFeedClockwise();
        RenderDetectionOverlay(forceRefresh: true);
        e.Handled = true;
    }

    private void RotateAuxCameraButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
    }

    private async void OpenRecordedVideosButton_OnClick(object sender, RoutedEventArgs e)
    {
        RecordedVideosActiveView.RecordedVideosPanelElement.Visibility = Visibility.Visible;
        await LoadRecordedVideosAsync();
        e.Handled = true;
    }

    private async void RefreshRecordedVideosButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadRecordedVideosAsync();
        e.Handled = true;
    }

    private void CloseRecordedVideosButton_OnClick(object sender, RoutedEventArgs e)
    {
        _recordedVideoPositionTimer.Stop();
        RecordedVideosActiveView.RecordedVideoPlayerElement.Stop();
        RecordedVideosActiveView.RecordedVideoPlayerElement.Source = null;
        RecordedVideosActiveView.RecordedVideosPanelElement.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private async void RecordedVideoList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecordedVideosActiveView.RecordedVideoListElement.SelectedItem is not RecordedVideoItem item)
        {
            return;
        }

        if (item.IsBack)
        {
            _recordedVideoSelectedFolder = null;
            ShowRecordedVideoFolderList();
            return;
        }

        if (item.IsFolder)
        {
            _recordedVideoSelectedFolder = item.Folder;
            ShowRecordedVideoFolderContents(item.Folder);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Url))
        {
            return;
        }

        try
        {
            _recordedVideoPositionTimer.Stop();
            RecordedVideosActiveView.RecordedVideoPlayerElement.Stop();
            RecordedVideosActiveView.RecordedVideoPlayerElement.Source = null;
            ResetRecordedVideoPositionUi();
            RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = $"{item.DisplayName} ?대젮諛쏅뒗 以?..";

            var localPath = await EnsureRecordedVideoCachedAsync(item);
            RecordedVideosActiveView.RecordedVideoPlayerElement.Source = new Uri(localPath, UriKind.Absolute);
            ResetRecordedVideoZoom();
            ApplyRecordedVideoPlaybackSpeed();
            RecordedVideosActiveView.RecordedVideoPlayerElement.Play();
            _recordedVideoPositionTimer.Start();
            RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = item.DisplayName;
        }
        catch (Exception ex)
        {
            RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = "?곸긽???ъ깮?????놁뒿?덈떎.";
            _viewModel.AppendImportantLog($"?뱁솕 ?곸긽 ?ъ깮 以鍮꾩뿉 ?ㅽ뙣?덉뒿?덈떎: {ex.Message}");
        }
    }

    private void RecordedVideoPlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyRecordedVideoPlaybackSpeed();
        RecordedVideosActiveView.RecordedVideoPlayerElement.Play();
        _recordedVideoPositionTimer.Start();
    }

    private void RecordedVideoPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        RecordedVideosActiveView.RecordedVideoPlayerElement.Pause();
    }

    private void RecordedVideoStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        RecordedVideosActiveView.RecordedVideoPlayerElement.Stop();
        _recordedVideoPositionTimer.Stop();
        ResetRecordedVideoPositionUi();
    }

    private void PlaybackSpeedCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyRecordedVideoPlaybackSpeed();
    }

    private async Task LoadRecordedVideosAsync()
    {
        var baseUri = GetRecordedVideoBaseUri();
        var apiUri = new Uri(baseUri, "api/videos");
        RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = "紐⑸줉??遺덈윭?ㅻ뒗 以?..";

        try
        {
            using var stream = await RecordedVideoHttpClient.GetStreamAsync(apiUri);
            var videos = await JsonSerializer.DeserializeAsync<List<RecordedVideoItem>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            MarkJetsonMessageReceived();
            videos ??= new List<RecordedVideoItem>();

            foreach (var video in videos)
            {
                video.Folder = GetRecordedVideoFolder(video.Name);
                video.DisplayName = GetRecordedVideoFileName(video.Name);
                if (Uri.TryCreate(video.Url, UriKind.Absolute, out _))
                {
                    continue;
                }

                video.Url = new Uri(baseUri, video.Url).ToString();
            }

            _recordedVideoFiles.Clear();
            _recordedVideoFiles.AddRange(videos);

            if (!string.IsNullOrWhiteSpace(_recordedVideoSelectedFolder) &&
                _recordedVideoFiles.Any(video => string.Equals(video.Folder, _recordedVideoSelectedFolder, StringComparison.Ordinal)))
            {
                ShowRecordedVideoFolderContents(_recordedVideoSelectedFolder);
            }
            else
            {
                _recordedVideoSelectedFolder = null;
                ShowRecordedVideoFolderList();
            }
        }
        catch (Exception ex)
        {
            RecordedVideosActiveView.RecordedVideoListElement.ItemsSource = null;
            RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = "紐⑸줉??遺덈윭?ㅼ? 紐삵뻽?듬땲??";
            _viewModel.AppendImportantLog($"?뱁솕 ?곸긽 紐⑸줉??遺덈윭?ㅼ? 紐삵뻽?듬땲?? {ex.Message}");
        }
    }

    private Uri GetRecordedVideoBaseUri()
    {
        var videoUrl = _networkSettings.RecordedVideoUrl;

        if (!videoUrl.EndsWith("/", StringComparison.Ordinal))
        {
            videoUrl += "/";
        }

        return new Uri(videoUrl, UriKind.Absolute);
    }

    private void ShowRecordedVideoFolderList()
    {
        var folders = _recordedVideoFiles
            .GroupBy(video => video.Folder)
            .OrderByDescending(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RecordedVideoItem
            {
                Folder = group.Key,
                DisplayName = $"[?대뜑] {group.Key} ({group.Count()}媛?",
                IsFolder = true
            })
            .ToList();

        RecordedVideosActiveView.RecordedVideoListElement.ItemsSource = folders;
        RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = folders.Count == 0
            ? "??λ맂 ?곸긽 ?대뜑媛 ?꾩쭅 ?놁뒿?덈떎."
            : $"{folders.Count}媛??대뜑";
    }

    private void ShowRecordedVideoFolderContents(string folder)
    {
        var videos = _recordedVideoFiles
            .Where(video => string.Equals(video.Folder, folder, StringComparison.Ordinal))
            .OrderBy(video => video.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<RecordedVideoItem>
        {
            new()
            {
                DisplayName = "[?곸쐞 ?대뜑濡?",
                IsBack = true
            }
        };
        items.AddRange(videos);

        RecordedVideosActiveView.RecordedVideoListElement.ItemsSource = items;
        RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = $"{folder} / {videos.Count}媛??곸긽";
    }

    private static string GetRecordedVideoFolder(string name)
    {
        var normalized = name.Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex > 0
            ? normalized[..separatorIndex]
            : "湲곗〈 ?곸긽";
    }

    private static string GetRecordedVideoFileName(string name)
    {
        var normalized = name.Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < normalized.Length - 1
            ? normalized[(separatorIndex + 1)..]
            : normalized;
    }

    private async void RecordingMetadataTimer_OnTick(object? sender, EventArgs e)
    {
        var windowEnd = DateTime.Now;
        var windowStart = _recordingMetadataWindowStart == default
            ? windowEnd.AddMinutes(-1)
            : _recordingMetadataWindowStart;
        _recordingMetadataWindowStart = windowEnd;

        await SaveRecordingMetadataAsync(
            windowStart,
            windowEnd,
            manual: false,
            includeAnalysis: true,
            includeSystemLog: true);
    }

    private async void ViewModel_OnManualAnalysisSaveRequested(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        await SaveRecordingMetadataAsync(
            now,
            now,
            manual: true,
            includeAnalysis: true,
            includeSystemLog: false);
    }

    private async void ViewModel_OnManualSystemLogSaveRequested(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        await SaveRecordingMetadataAsync(
            now,
            now,
            manual: true,
            includeAnalysis: false,
            includeSystemLog: true);
    }

    private async Task SaveRecordingMetadataAsync(
        DateTime windowStart,
        DateTime windowEnd,
        bool manual,
        bool includeAnalysis,
        bool includeSystemLog)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["manual"] = manual
            };

            if (includeAnalysis)
            {
                payload["analysisText"] = _viewModel.BuildAnalysisLogSnapshot(windowStart, windowEnd, includeAll: manual);
            }

            if (includeSystemLog)
            {
                payload["systemLogText"] = _viewModel.BuildSystemLogSnapshot(windowStart, windowEnd, includeAll: manual);
            }

            var apiUri = new Uri(GetRecordedVideoBaseUri(), "api/logs");
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await RecordedVideoHttpClient.PostAsync(apiUri, content);
            response.EnsureSuccessStatusCode();

            if (manual)
            {
                var targetName = includeAnalysis ? "VLM 遺꾩꽍 寃곌낵" : "?쒖뒪??濡쒓렇";
                _viewModel.AppendImportantLog($"{targetName}瑜??앹뒯 ?곸긽 ?대뜑??C_ ?뚯씪濡???ν뻽?듬땲??");
            }
        }
        catch (Exception ex)
        {
            var modeText = manual ? "?섎룞" : "?먮룞";
            _viewModel.AppendImportantLog($"{modeText} 濡쒓렇/VLM ??μ뿉 ?ㅽ뙣?덉뒿?덈떎: {ex.Message}");
        }
    }

    private void ApplyRecordedVideoPlaybackSpeed()
    {
        if (RecordedVideosActiveView.PlaybackSpeedComboElement?.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string value ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
        {
            speed = 1.0;
        }

        RecordedVideosActiveView.RecordedVideoPlayerElement.SpeedRatio = speed;
    }

    private void RecordedVideoPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (RecordedVideosActiveView.RecordedVideoPlayerElement.NaturalDuration.HasTimeSpan)
        {
            var duration = RecordedVideosActiveView.RecordedVideoPlayerElement.NaturalDuration.TimeSpan;
            RecordedVideosActiveView.RecordedVideoPositionSliderElement.Maximum = Math.Max(duration.TotalSeconds, 1);
            RecordedVideosActiveView.RecordedVideoDurationTextElement.Text = FormatVideoTime(duration);
        }

        UpdateRecordedVideoPositionUi();
    }

    private void RecordedVideoPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        _recordedVideoPositionTimer.Stop();
        UpdateRecordedVideoPositionUi();
    }

    private void RecordedVideoPlayer_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _recordedVideoPositionTimer.Stop();
        RecordedVideosActiveView.RecordedVideosStatusTextElement.Text = "?곸긽???ъ깮?????놁뒿?덈떎.";
        _viewModel.AppendImportantLog($"?뱁솕 ?곸긽 ?ъ깮???ㅽ뙣?덉뒿?덈떎: {e.ErrorException.Message}");
    }

    private void RecordedVideoPositionTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateRecordedVideoPositionUi();
    }

    private void RecordedVideoPositionSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingRecordedVideoPosition = true;
    }

    private void RecordedVideoPositionSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SeekRecordedVideoToSlider();
        _isDraggingRecordedVideoPosition = false;
    }

    private void RecordedVideoPositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingRecordedVideoPosition)
        {
            RecordedVideosActiveView.RecordedVideoCurrentTimeTextElement.Text = FormatVideoTime(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void SeekRecordedVideoToSlider()
    {
        RecordedVideosActiveView.RecordedVideoPlayerElement.Position = TimeSpan.FromSeconds(RecordedVideosActiveView.RecordedVideoPositionSliderElement.Value);
        UpdateRecordedVideoPositionUi();
    }

    private void UpdateRecordedVideoPositionUi()
    {
        if (_isDraggingRecordedVideoPosition)
        {
            return;
        }

        var position = RecordedVideosActiveView.RecordedVideoPlayerElement.Position;
        RecordedVideosActiveView.RecordedVideoCurrentTimeTextElement.Text = FormatVideoTime(position);

        if (RecordedVideosActiveView.RecordedVideoPlayerElement.NaturalDuration.HasTimeSpan)
        {
            var duration = RecordedVideosActiveView.RecordedVideoPlayerElement.NaturalDuration.TimeSpan;
            RecordedVideosActiveView.RecordedVideoPositionSliderElement.Maximum = Math.Max(duration.TotalSeconds, 1);
            RecordedVideosActiveView.RecordedVideoDurationTextElement.Text = FormatVideoTime(duration);
        }

        RecordedVideosActiveView.RecordedVideoPositionSliderElement.Value = Math.Min(position.TotalSeconds, RecordedVideosActiveView.RecordedVideoPositionSliderElement.Maximum);
    }

    private void ResetRecordedVideoPositionUi()
    {
        _isDraggingRecordedVideoPosition = false;
        RecordedVideosActiveView.RecordedVideoPositionSliderElement.Minimum = 0;
        RecordedVideosActiveView.RecordedVideoPositionSliderElement.Maximum = 1;
        RecordedVideosActiveView.RecordedVideoPositionSliderElement.Value = 0;
        RecordedVideosActiveView.RecordedVideoCurrentTimeTextElement.Text = "00:00";
        RecordedVideosActiveView.RecordedVideoDurationTextElement.Text = "00:00";
    }

    private void RecordedVideoViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustRecordedVideoZoom(e.Delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    private void RecordedVideoViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_recordedVideoZoomLevel <= 1.0)
        {
            return;
        }

        _isDraggingRecordedVideoPan = true;
        _lastRecordedVideoPanPoint = e.GetPosition(RecordedVideosActiveView.RecordedVideoViewportElement);
        RecordedVideosActiveView.RecordedVideoViewportElement.CaptureMouse();
        RecordedVideosActiveView.RecordedVideoViewportElement.Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void RecordedVideoViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingRecordedVideoPan)
        {
            return;
        }

        var point = e.GetPosition(RecordedVideosActiveView.RecordedVideoViewportElement);
        _recordedVideoPanX += point.X - _lastRecordedVideoPanPoint.X;
        _recordedVideoPanY += point.Y - _lastRecordedVideoPanPoint.Y;
        _lastRecordedVideoPanPoint = point;
        ClampRecordedVideoPan();
        UpdateRecordedVideoZoomUi();
    }

    private void RecordedVideoViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopRecordedVideoPanDrag();
    }

    private void RecordedVideoZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RecordedVideosActiveView.RecordedVideoScaleTransformElement is null)
        {
            return;
        }

        SetRecordedVideoZoom(e.NewValue);
    }

    private void RecordedVideoZoomResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        ResetRecordedVideoZoom();
    }

    private void AdjustRecordedVideoZoom(double delta)
    {
        SetRecordedVideoZoom(_recordedVideoZoomLevel + delta);
    }

    private void SetRecordedVideoZoom(double value)
    {
        _recordedVideoZoomLevel = Math.Clamp(value, 1.0, 4.0);
        if (_recordedVideoZoomLevel <= 1.0)
        {
            _recordedVideoPanX = 0;
            _recordedVideoPanY = 0;
            StopRecordedVideoPanDrag();
        }

        ClampRecordedVideoPan();
        UpdateRecordedVideoZoomUi();
    }

    private void ResetRecordedVideoZoom()
    {
        _recordedVideoZoomLevel = 1.0;
        _recordedVideoPanX = 0;
        _recordedVideoPanY = 0;
        StopRecordedVideoPanDrag();
        UpdateRecordedVideoZoomUi();
    }

    private void StopRecordedVideoPanDrag()
    {
        if (!_isDraggingRecordedVideoPan)
        {
            return;
        }

        _isDraggingRecordedVideoPan = false;
        RecordedVideosActiveView.RecordedVideoViewportElement.ReleaseMouseCapture();
        RecordedVideosActiveView.RecordedVideoViewportElement.Cursor = Cursors.Arrow;
    }

    private void ClampRecordedVideoPan()
    {
        var maxPanX = GetRecordedVideoMaxPanX();
        var maxPanY = GetRecordedVideoMaxPanY();
        _recordedVideoPanX = Math.Clamp(_recordedVideoPanX, -maxPanX, maxPanX);
        _recordedVideoPanY = Math.Clamp(_recordedVideoPanY, -maxPanY, maxPanY);
    }

    private double GetRecordedVideoMaxPanX()
    {
        return Math.Max(0, RecordedVideosActiveView.RecordedVideoViewportElement.ActualWidth * (_recordedVideoZoomLevel - 1.0) / 2.0);
    }

    private double GetRecordedVideoMaxPanY()
    {
        return Math.Max(0, RecordedVideosActiveView.RecordedVideoViewportElement.ActualHeight * (_recordedVideoZoomLevel - 1.0) / 2.0);
    }

    private void UpdateRecordedVideoZoomUi()
    {
        if (RecordedVideosActiveView.RecordedVideoScaleTransformElement is null)
        {
            return;
        }

        RecordedVideosActiveView.RecordedVideoScaleTransformElement.ScaleX = _recordedVideoZoomLevel;
        RecordedVideosActiveView.RecordedVideoScaleTransformElement.ScaleY = _recordedVideoZoomLevel;
        RecordedVideosActiveView.RecordedVideoTranslateTransformElement.X = _recordedVideoPanX;
        RecordedVideosActiveView.RecordedVideoTranslateTransformElement.Y = _recordedVideoPanY;

        if (RecordedVideosActiveView.RecordedVideoZoomSliderElement is not null &&
            Math.Abs(RecordedVideosActiveView.RecordedVideoZoomSliderElement.Value - _recordedVideoZoomLevel) > 0.001)
        {
            RecordedVideosActiveView.RecordedVideoZoomSliderElement.Value = _recordedVideoZoomLevel;
        }

        if (RecordedVideosActiveView.RecordedVideoZoomResetButtonElement is not null)
        {
            RecordedVideosActiveView.RecordedVideoZoomResetButtonElement.Content = $"x{_recordedVideoZoomLevel:0.00}";
        }

        UpdateRecordedVideoMiniMap();
    }

    private void UpdateRecordedVideoMiniMap()
    {
        if (RecordedVideosActiveView.RecordedVideoZoomMiniMapElement is null || RecordedVideosActiveView.RecordedVideoMiniMapViewportElement is null)
        {
            return;
        }

        if (_recordedVideoZoomLevel <= 1.0)
        {
            RecordedVideosActiveView.RecordedVideoZoomMiniMapElement.Visibility = Visibility.Collapsed;
            return;
        }

        RecordedVideosActiveView.RecordedVideoZoomMiniMapElement.Visibility = Visibility.Visible;
        var viewportWidth = RecordedVideoMiniMapWidth / _recordedVideoZoomLevel;
        var viewportHeight = RecordedVideoMiniMapHeight / _recordedVideoZoomLevel;
        RecordedVideosActiveView.RecordedVideoMiniMapViewportElement.Width = viewportWidth;
        RecordedVideosActiveView.RecordedVideoMiniMapViewportElement.Height = viewportHeight;

        var maxPanX = GetRecordedVideoMaxPanX();
        var maxPanY = GetRecordedVideoMaxPanY();
        var left = maxPanX <= 0
            ? (RecordedVideoMiniMapWidth - viewportWidth) / 2
            : (1.0 - ((_recordedVideoPanX + maxPanX) / (maxPanX * 2.0))) * (RecordedVideoMiniMapWidth - viewportWidth);
        var top = maxPanY <= 0
            ? (RecordedVideoMiniMapHeight - viewportHeight) / 2
            : (1.0 - ((_recordedVideoPanY + maxPanY) / (maxPanY * 2.0))) * (RecordedVideoMiniMapHeight - viewportHeight);

        Canvas.SetLeft(RecordedVideosActiveView.RecordedVideoMiniMapViewportElement, left);
        Canvas.SetTop(RecordedVideosActiveView.RecordedVideoMiniMapViewportElement, top);
    }

    private static string FormatVideoTime(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static async Task<string> EnsureRecordedVideoCachedAsync(RecordedVideoItem item)
    {
        var cacheDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), RecordedVideoCacheFolderName);
        Directory.CreateDirectory(cacheDirectory);

        var localPath = System.IO.Path.Combine(cacheDirectory, SanitizeFileName(item.Name));
        if (File.Exists(localPath))
        {
            var localSize = new FileInfo(localPath).Length;
            if (item.SizeBytes <= 0 || localSize == item.SizeBytes)
            {
                return localPath;
            }
        }

        var bytes = await RecordedVideoHttpClient.GetByteArrayAsync(item.Url);
        await File.WriteAllBytesAsync(localPath, bytes);
        return localPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var chars = fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "recorded_video" : sanitized;
    }

    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    private void MotorDetailsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsMotorDetailsOpen)
        {
            _viewModel.IsMotorDetailsOpen = false;
            e.Handled = true;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
    }

    private void LoadNetworkSettingsEditor()
    {
        OperationActiveView.JetsonHostTextBoxElement.Text = _networkSettings.JetsonHost;

        var localAddresses = AppNetworkSettings.GetLocalIpv4Addresses();
        OperationActiveView.PcGuiHostComboBoxElement.ItemsSource = localAddresses;
        OperationActiveView.PcGuiHostComboBoxElement.Text = _networkSettings.PcGuiHost;
        if (localAddresses.Count > 0 && !localAddresses.Contains(_networkSettings.PcGuiHost, StringComparer.Ordinal))
        {
            _viewModel.AppendImportantLog($"?꾩옱 GUI IP ?꾨낫: {string.Join(", ", localAddresses)}");
        }
    }

    private void SaveNetworkSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _networkSettings.JetsonHost = OperationActiveView.JetsonHostTextBoxElement.Text;
        _networkSettings.PcGuiHost = OperationActiveView.PcGuiHostComboBoxElement.Text;
        _networkSettings.RecordedVideoUrl = $"http://{_networkSettings.JetsonHost.Trim()}:{_networkSettings.RecordingHttpPort.ToString(CultureInfo.InvariantCulture)}/";
        _networkSettings.Save();
        _motorControlService.ConfigureEndpoint(
            _networkSettings.JetsonHost,
            _networkSettings.MotorControlPort,
            _networkSettings.TrackingRecordingControlPort);

        _viewModel.AppendImportantLog($"?ㅽ듃?뚰겕 ?ㅼ젙????ν븯怨?利됱떆 ?곸슜?덉뒿?덈떎: Jetson {_networkSettings.JetsonHost}, GUI {_networkSettings.PcGuiHost}");
        MessageBox.Show(
            "?ㅽ듃?뚰겕 ?ㅼ젙????ν뻽?듬땲??\n\n紐⑦꽣 紐낅졊怨??뱁솕 ?곸긽 二쇱냼??利됱떆 ??Jetson IP瑜??ъ슜?⑸땲??\nGUI IP??Jetson 釉뚮┸吏???≪텧 ????ㅼ젙?먮룄 諛섏쁺?섏뼱???곸긽 ?섏떊 ??곸씠 諛붾앸땲??",
            "Network",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreenMode = true;
        }

        UpdateWindowModeButtonText();
    }

    private void UpdateWindowModeButtonText()
    {
        if (SettingsActiveView.WindowModeToggleButtonElement is null)
        {
            return;
        }

        SettingsActiveView.WindowModeToggleButtonElement.Content = _isFullscreenMode
            ? _viewModel.Text["WindowMode"]
            : _viewModel.Text["FullscreenMode"];
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
        if (Keyboard.FocusedElement is TextBox && e.Key == Key.C)
        {
            return;
        }

        if (TryHandleMotorStepKey(e))
        {
            return;
        }

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

    private bool TryHandleMotorStepKey(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return false;
        }

        var delta = e.Key switch
        {
            Key.Add or Key.OemPlus => 1,
            Key.Subtract or Key.OemMinus => -1,
            _ => 0
        };

        if (delta == 0)
        {
            return false;
        }

        var isManualStepKey = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var commandParameter = isManualStepKey
            ? $"Manual:{delta}"
            : $"Auto:{delta}";

        if (_viewModel.AdjustMotorStepCommand.CanExecute(commandParameter))
        {
            _viewModel.AdjustMotorStepCommand.Execute(commandParameter);
        }

        e.Handled = true;
        return true;
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox && e.Key == Key.C)
        {
            return;
        }

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
        UpdateMotorPadButtonVisualStates();

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
        UpdateMotorPadButtonVisualStates();

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
        UpdateMotorPadButtonVisualStates();
    }

    private void UpdateMotorPadButtonVisualStates()
    {
        SetMotorPadButtonActive(MotorActiveView.MotorPadLeftButtonElement, _activeMotorDirections.ContainsKey("Left"));
        SetMotorPadButtonActive(MotorActiveView.MotorPadRightButtonElement, _activeMotorDirections.ContainsKey("Right"));
        SetMotorPadButtonActive(MotorActiveView.MotorPadUpButtonElement, _activeMotorDirections.ContainsKey("Up"));
        SetMotorPadButtonActive(MotorActiveView.MotorPadDownButtonElement, _activeMotorDirections.ContainsKey("Down"));
        SetMotorPadButtonActive(MotorActiveView.MotorPadCenterButtonElement, _activeMotorDirections.ContainsKey("Center"));
    }

    private static void SetMotorPadButtonActive(Button? button, bool isActive)
    {
        if (button is null)
        {
            return;
        }

        if (!isActive)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            return;
        }

        button.Background = new SolidColorBrush(Color.FromRgb(0x36, 0x55, 0x64));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
    }

    private static bool TryMapKeyToMotorDirection(Key key, out string direction)
    {
        direction = key switch
        {
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.C => "Center",
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
            SettingsActiveView.SettingsBackdropElement.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = isOpen;
            SettingsActiveView.SettingsBackdropElement.Opacity = isOpen ? 1.0 : 0.0;

            SettingsActiveView.SettingsDrawerElement.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsActiveView.SettingsDrawerElement.Opacity = isOpen ? 1.0 : 0.0;
            SettingsActiveView.SettingsDrawerTransformElement.X = isOpen ? 0 : SettingsDrawerClosedOffset;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 170);
        var easing = new CubicEase
        {
            EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn
        };

        if (isOpen)
        {
            SettingsActiveView.SettingsBackdropElement.Visibility = Visibility.Visible;
            SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = true;
            SettingsActiveView.SettingsDrawerElement.Visibility = Visibility.Visible;
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
                SettingsActiveView.SettingsBackdropElement.Visibility = Visibility.Collapsed;
                SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = false;
                SettingsActiveView.SettingsDrawerElement.Visibility = Visibility.Collapsed;
            };
        }

        SettingsActiveView.SettingsBackdropElement.BeginAnimation(OpacityProperty, backdropAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsActiveView.SettingsDrawerElement.BeginAnimation(OpacityProperty, drawerOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsActiveView.SettingsDrawerTransformElement.BeginAnimation(TranslateTransform.XProperty, drawerSlideAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopManualMotorInput();
        _recordedVideoPositionTimer.Stop();
        _recordingMetadataTimer.Stop();
        _jetsonConnectionTimer.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ManualAnalysisSaveRequested -= ViewModel_OnManualAnalysisSaveRequested;
        _viewModel.ManualSystemLogSaveRequested -= ViewModel_OnManualSystemLogSaveRequested;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived -= OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady -= OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived -= OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _detectionUdpReceiverService.DetectionsReceived -= OnSharedDetectionsReceived;
        _detectionUdpReceiverService.StatusReceived -= OnYoloStatusReceived;
        _motorStatusReceiverService.StatusReceived -= OnMotorStatusReceived;
        _motorStatusReceiverService.ReceiverError -= OnMotorStatusReceiverError;
        _vlmResultReceiverService.ResultReceived -= OnVlmResultReceived;
        _vlmResultReceiverService.ReceiverError -= OnVlmResultReceiverError;
        _mobileAlertHubService.Dispose();
        _viewportRecordingService.Dispose();
        _eoUdpCaptureService.Dispose();
        _irUdpCaptureService.Dispose();
        _detectionUdpReceiverService.Dispose();
        _motorStatusReceiverService.Dispose();
        _vlmResultReceiverService.Dispose();
        _motorControlService.Dispose();
    }

    private void Button_Click_2(object sender, RoutedEventArgs e)
    {

    }
}
