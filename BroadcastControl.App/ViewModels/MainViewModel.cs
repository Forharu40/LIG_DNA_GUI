using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

namespace BroadcastControl.App.ViewModels;

// 파일 역할:
// 메인 화면의 표시 상태와 사용자 명령을 관리하는 ViewModel입니다.
// 카메라, 모터, 녹화, 네트워크 설정을 한곳에서 조율합니다.
// 화면 바인딩용 속성과 명령은 이 클래스에서 갱신됩니다.

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    // 메인 화면의 크기, 색상, 상태 표시 기본값을 정의합니다.
    // 화면 바인딩에 쓰이는 기본 상수입니다.
    private const double MiniMapWidth = 130;
    private const double MiniMapHeight = 74;

    private static readonly SolidColorBrush LowThreatBrush = CreateBrush(0x7B, 0xD8, 0x8F);
    private static readonly SolidColorBrush MediumThreatBrush = CreateBrush(0xFF, 0xC1, 0x45);
    private static readonly SolidColorBrush HighThreatBrush = CreateBrush(0xFF, 0x6B, 0x6B);
    private static readonly SolidColorBrush RecordingOnBrush = CreateBrush(0x64, 0xC5, 0x9A);
    private static readonly SolidColorBrush RecordingOffBrush = CreateBrush(0x41, 0x49, 0x55);
    private static readonly SolidColorBrush RecordingTextOffBrush = CreateBrush(0x92, 0x9D, 0xAA);

    private bool _isEoPrimary = true;
    private bool _isSettingsOpen;
    private bool _isSystemPoweredOn = true;
    private string _currentMode = "\uC790\uB3D9";
    private string _selectedPrimaryTarget = "\uBCF5\uD569";
    private string _currentThreatLevel = "\uB0AE\uC74C";
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    private double _brightness = 50;
    private double _contrast = 50;
    private bool _isManualRecordingEnabled;
    private bool _isRecordingSuppressed;
    private bool _isAutoRecordingLatched;
    private bool _isJetsonConnected;
    private AppThemeMode _currentThemeMode;
    private double _eoDisplayRotationAngle;
    private double _irDisplayRotationAngle;
    private double _zoomLevel = 1.0;
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;
    private double _motorPan;
    private double _motorTilt;
    private ushort _motorPanRaw;
    private ushort _motorTiltRaw;
    private ushort? _panMotorFeedbackRaw;
    private ushort? _tiltMotorFeedbackRaw;
    private int _autoMotorAngleSize = DefaultMotorAngleSize;
    private int _manualMotorAngleSize = DefaultMotorAngleSize;
    private double _panMotorPositionDegrees;
    private double _tiltMotorPositionDegrees;
    private bool _isMotorDetailsOpen;
    private UiLanguage _uiLanguage = UiLanguage.English;
    private string _motorTargetPanText = string.Empty;
    private string _motorTargetTiltText = string.Empty;
    private bool _hasTrackedTarget;
    private bool _isTrackingModeEnabled = true;
    private int _yoloObjectId = -1;
    private bool _isUserSelectedTrackId;
    private DateTime _lastAutomaticTrackingPacketSentAt = DateTime.MinValue;
    private readonly UdpMotorControlService _motorControlService;
    private const double MotorPanLimitDegrees = 360;
    private const double MotorTiltLimitDegrees = 360;
    private const int MotorRawMinimum = 0;
    private const int MotorRawMaximum = 4095;
    private const double MotorRawResolution = 4096.0;
    private const int DefaultMotorAngleSize = 8;
    private const int AutomaticTrackingResendMilliseconds = 250;
    private const int VisibleLogItemLimit = 30;
    private const int StoredLogItemLimit = 100;
    private readonly List<AnalysisItem> _analysisHistory = new();
    private readonly List<SystemLogItem> _systemLogHistory = new();
    private string? _lastAnalysisMessage;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private readonly ImageSource _eoPlaceholderFrame = CreateCameraPlaceholderFrame(string.Empty, Color.FromRgb(51, 94, 160));
    private readonly ImageSource _irPlaceholderFrame = CreateCameraPlaceholderFrame(string.Empty, Color.FromRgb(192, 109, 40));

    public MainViewModel(UdpMotorControlService? motorControlService = null)
    {
        _motorControlService = motorControlService ?? new UdpMotorControlService();
        Text = new LocalizedTextProvider(() => _uiLanguage);
        Camera = new CameraViewModel();
        Motor = new MotorControlViewModel();
        Operation = new OperationControlViewModel();
        Monitoring = new MonitoringViewModel();
        Recording = new RecordingViewModel();
        Vlm = new VlmViewModel();
        Mobile = new MobileWebAppViewModel();

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        if (Application.Current is App app)
        {
            _currentThemeMode = app.CurrentThemeMode;
        }

        AnalysisItems = new ObservableCollection<AnalysisItem>();
        DetectionTargets = new ObservableCollection<DetectionTargetItem>();
        SystemLogs = new ObservableCollection<SystemLogItem>();
        PanMotorStatusItems = new ObservableCollection<MotorStatusItem>(CreateDefaultMotorStatusItems());
        TiltMotorStatusItems = new ObservableCollection<MotorStatusItem>(CreateDefaultMotorStatusItems());

        AddSystemLogItem(new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), Text["SystemStarted"]));

        PrimaryTargets = new ObservableCollection<PrimaryTargetOption>(CreatePrimaryTargetOptions());

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
        ResetBrightnessCommand = new RelayCommand(_ => Brightness = 50, _ => IsSystemPoweredOn);
        ResetContrastCommand = new RelayCommand(_ => Contrast = 50, _ => IsSystemPoweredOn);
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        ResetZoomCommand = new RelayCommand(_ => ZoomLevel = 1.0, _ => CanUseZoomControls);
        ToggleManualRecordingCommand = new RelayCommand(_ => ToggleManualRecording(), _ => IsSystemPoweredOn);
        SetThemeCommand = new RelayCommand(SetTheme);
        SetLanguageCommand = new RelayCommand(SetLanguage);
        SaveAnalysisLogsCommand = new RelayCommand(_ => ManualAnalysisSaveRequested?.Invoke(this, EventArgs.Empty));
        SaveSystemLogsCommand = new RelayCommand(_ => ManualSystemLogSaveRequested?.Invoke(this, EventArgs.Empty));
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        MoveMotorCommand = new RelayCommand(MoveMotor, _ => CanUseMotorControls);
        SendMotorTargetCommand = new RelayCommand(_ => SendMotorTargetAngles(), _ => CanUseMotorTargetControls);
        AdjustMotorStepCommand = new RelayCommand(AdjustMotorStep, _ => IsSystemPoweredOn);
        ToggleTrackingModeCommand = new RelayCommand(_ => ToggleTrackingMode(), _ => IsSystemPoweredOn);
        ToggleMotorDetailsCommand = new RelayCommand(_ => IsMotorDetailsOpen = !IsMotorDetailsOpen);
        CloseMotorDetailsCommand = new RelayCommand(_ => IsMotorDetailsOpen = false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ManualAnalysisSaveRequested;

    public event EventHandler? ManualSystemLogSaveRequested;

    public ObservableCollection<AnalysisItem> AnalysisItems { get; }

    public ObservableCollection<DetectionTargetItem> DetectionTargets { get; }

    public ObservableCollection<SystemLogItem> SystemLogs { get; }

    public ObservableCollection<MotorStatusItem> PanMotorStatusItems { get; }

    public ObservableCollection<MotorStatusItem> TiltMotorStatusItems { get; }

    public ObservableCollection<PrimaryTargetOption> PrimaryTargets { get; }

    public LocalizedTextProvider Text { get; }

    public CameraViewModel Camera { get; }

    public MotorControlViewModel Motor { get; }

    public OperationControlViewModel Operation { get; }

    public MonitoringViewModel Monitoring { get; }

    public RecordingViewModel Recording { get; }

    public VlmViewModel Vlm { get; }

    public MobileWebAppViewModel Mobile { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand SetModeCommand { get; }

    public ICommand ToggleSettingsCommand { get; }

    public ICommand SelectPrimaryTargetCommand { get; }

    public ICommand ResetBrightnessCommand { get; }

    public ICommand ResetContrastCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand ToggleManualRecordingCommand { get; }

    public ICommand SetThemeCommand { get; }

    public ICommand SetLanguageCommand { get; }

    public ICommand SaveAnalysisLogsCommand { get; }

    public ICommand SaveSystemLogsCommand { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand MoveMotorCommand { get; }

    public ICommand SendMotorTargetCommand { get; }

    public ICommand AdjustMotorStepCommand { get; }

    public ICommand ToggleTrackingModeCommand { get; }

    public ICommand ToggleMotorDetailsCommand { get; }

    public ICommand CloseMotorDetailsCommand { get; }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsSystemPoweredOn
    {
        get => _isSystemPoweredOn;
        private set
        {
            if (SetProperty(ref _isSystemPoweredOn, value))
            {
                OnPropertyChanged(nameof(IsManualMode));
                OnPropertyChanged(nameof(ManualRecordingButtonOpacity));
                OnPropertyChanged(nameof(ManualRecordingButtonText));
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseMotorTargetControls));
                OnPropertyChanged(nameof(MotorControlsOpacity));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(TrackingModeOpacity));
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                RaiseAllCommandStates();
            }
        }
    }

    public bool IsEoPrimary => _isEoPrimary;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string PowerButtonText => Text["PowerExit"];

    public string CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(CurrentModeText));
                OnPropertyChanged(nameof(AutoModeOpacity));
                OnPropertyChanged(nameof(ManualModeOpacity));
                OnPropertyChanged(nameof(IsManualMode));
                OnPropertyChanged(nameof(ManualRecordingButtonOpacity));
                OnPropertyChanged(nameof(ManualRecordingButtonText));
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseMotorTargetControls));
                OnPropertyChanged(nameof(MotorControlsOpacity));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(ShowZoomMiniMap));
                OnPropertyChanged(nameof(TrackingModeOpacity));
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"{Text["CameraMode"]}: {TranslateMode(CurrentMode)}";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public double AutoModeOpacity => CurrentMode == "\uC790\uB3D9" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "\uC218\uB3D9" ? 1.0 : 0.35;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.

    public bool IsRecordingActive =>
        IsSystemPoweredOn &&
        !_isRecordingSuppressed &&
        (IsManualRecordingEnabled || _isAutoRecordingLatched);

    public Brush RecordingIndicatorBrush => IsSystemPoweredOn && IsJetsonConnected ? RecordingOnBrush : RecordingOffBrush;

    public Brush RecordingTextBrush => IsSystemPoweredOn && IsJetsonConnected ? RecordingOnBrush : RecordingTextOffBrush;

    public double RecordingIndicatorOpacity => IsSystemPoweredOn && IsJetsonConnected ? 1.0 : 0.36;

    public bool IsJetsonConnected
    {
        get => _isJetsonConnected;
        private set
        {
            if (SetProperty(ref _isJetsonConnected, value))
            {
                OnPropertyChanged(nameof(JetsonConnectionText));
                OnPropertyChanged(nameof(JetsonConnectionBrush));
                OnPropertyChanged(nameof(JetsonConnectionOpacity));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
            }
        }
    }

    public string JetsonConnectionText => IsJetsonConnected ? Text["Connected"] : Text["Connecting"];

    public Brush JetsonConnectionBrush => IsJetsonConnected ? RecordingOnBrush : RecordingTextBrush;

    public double JetsonConnectionOpacity => RecordingIndicatorOpacity;

    public bool IsManualMode => IsSystemPoweredOn && CurrentMode == "\uC218\uB3D9";

    public bool IsAutoMode => IsSystemPoweredOn && CurrentMode == "\uC790\uB3D9";

    public bool CanSelectAutoMode => IsSystemPoweredOn && CurrentMode != "\uC790\uB3D9";

    public bool CanSelectManualMode => IsSystemPoweredOn && CurrentMode != "\uC218\uB3D9";

    public bool CanUseMotorControls => IsManualMode;

    public bool CanUseMotorTargetControls => IsManualMode;

    public double MotorControlsOpacity => CanUseMotorControls ? 1.0 : 0.38;

    public int AutoMotorAngleSize
    {
        get => _autoMotorAngleSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _autoMotorAngleSize, normalized))
            {
                OnPropertyChanged(nameof(AutoMotorAngleSizeText));
            }
        }
    }

    public int ManualMotorAngleSize
    {
        get => _manualMotorAngleSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _manualMotorAngleSize, normalized))
            {
                OnPropertyChanged(nameof(ManualMotorAngleSizeText));
            }
        }
    }

    public string AutoMotorAngleSizeText => AutoMotorAngleSize.ToString(CultureInfo.InvariantCulture);

    public string ManualMotorAngleSizeText => ManualMotorAngleSize.ToString(CultureInfo.InvariantCulture);

    public bool IsTrackingModeEnabled
    {
        get => _isTrackingModeEnabled;
        private set
        {
            if (SetProperty(ref _isTrackingModeEnabled, value))
            {
                OnPropertyChanged(nameof(TrackingModeText));
                OnPropertyChanged(nameof(TrackingModeOpacity));
            }
        }
    }

    public string TrackingModeText => IsTrackingModeEnabled ? Text["TrackingOn"] : Text["TrackingOff"];

    public double TrackingModeOpacity => IsSystemPoweredOn
        ? (IsTrackingModeEnabled ? 1.0 : 0.42)
        : 0.32;

    public string PanMotorPositionText => (_panMotorPositionDegrees - 180).ToString("0.0", CultureInfo.InvariantCulture);

    public string TiltMotorPositionText => (_tiltMotorPositionDegrees - 90).ToString("0.0", CultureInfo.InvariantCulture);

    public bool IsMotorDetailsOpen
    {
        get => _isMotorDetailsOpen;
        set => SetProperty(ref _isMotorDetailsOpen, value);
    }

    public bool CanUseZoomControls => IsSystemPoweredOn;

    public double ManualRecordingButtonOpacity => IsSystemPoweredOn ? 1.0 : 0.45;

    public bool IsDarkThemeActive => _currentThemeMode == AppThemeMode.Dark;

    public bool IsLightThemeActive => _currentThemeMode == AppThemeMode.Light;

    public double DarkThemeButtonOpacity => IsDarkThemeActive ? 1.0 : 0.55;

    public double LightThemeButtonOpacity => IsLightThemeActive ? 1.0 : 0.55;

    public bool IsEnglishLanguage => _uiLanguage == UiLanguage.English;

    public bool IsKoreanLanguage => _uiLanguage == UiLanguage.Korean;

    public double EnglishLanguageButtonOpacity => IsEnglishLanguage ? 1.0 : 0.55;

    public double KoreanLanguageButtonOpacity => IsKoreanLanguage ? 1.0 : 0.55;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public bool IsManualRecordingEnabled
    {
        get => _isManualRecordingEnabled;
        private set
        {
            if (SetProperty(ref _isManualRecordingEnabled, value))
            {
                OnPropertyChanged(nameof(ManualRecordingButtonText));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
            }
        }
    }

    public string ManualRecordingButtonText => IsRecordingActive ? Text["StopRecording"] : Text["StartRecording"];

    public string CurrentThreatLevel
    {
        get => _currentThreatLevel;
        private set
        {
            if (SetProperty(ref _currentThreatLevel, value))
            {
                if (value == "\uB192\uC74C" && IsAutoMode)
                {
                    _isAutoRecordingLatched = true;
                }

                OnPropertyChanged(nameof(CurrentThreatText));
                OnPropertyChanged(nameof(CurrentThreatBrush));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                OnPropertyChanged(nameof(ManualRecordingButtonText));
            }
        }
    }

    public string CurrentThreatText => $"{Text["ThreatLevel"]}: {TranslateThreatLevel(CurrentThreatLevel)}";

    public Brush CurrentThreatBrush => CurrentThreatLevel switch
    {
        "\uB0AE\uC74C" => LowThreatBrush,
        "\uC911\uAC04" => MediumThreatBrush,
        _ => HighThreatBrush,
    };

    public string SelectedPrimaryTarget
    {
        get => _selectedPrimaryTarget;
        private set
        {
            if (SetProperty(ref _selectedPrimaryTarget, value))
            {
                OnPropertyChanged(nameof(PrimaryTargetText));
                OnPropertyChanged(nameof(PrimaryTargetShortText));
            }
        }
    }

    public string PrimaryTargetText => $"{Text["PrimaryTarget"]}: {TranslatePrimaryTarget(SelectedPrimaryTarget)}";

    public string PrimaryTargetShortText => $"{Text["PrimaryTarget"]}: {GetShortPrimaryTargetName(SelectedPrimaryTarget)}";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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

    public string MotorPanText => $"모터 좌우: {_motorPan:0.0}°";

    public string MotorTiltText => $"모터 상하: {_motorTilt:0.0}°";

    public string MotorTargetPanText
    {
        get => _motorTargetPanText;
        set => SetProperty(ref _motorTargetPanText, value);
    }

    public string MotorTargetTiltText
    {
        get => _motorTargetTiltText;
        set => SetProperty(ref _motorTargetTiltText, value);
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
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

    public void UpdateDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        var highThreatCandidates = detections
            .Select((detection, index) => new TrackingCandidate(
                detection.ObjectId,
                GetThreatWeight(detection.ThreatLevel),
                index))
            .Where(candidate => candidate.ThreatWeight >= 3 && candidate.ObjectId >= 0)
            .OrderByDescending(candidate => candidate.ThreatWeight)
            .ThenBy(candidate => candidate.Order)
            .ToArray();

        var hasTrackedTarget = highThreatCandidates.Length > 0;
        var yoloObjectId = -1;
        if (hasTrackedTarget)
        {
            var highestThreatWeight = highThreatCandidates[0].ThreatWeight;
            var currentTarget = highThreatCandidates
                .Where(candidate =>
                    candidate.ObjectId == _yoloObjectId &&
                    candidate.ThreatWeight == highestThreatWeight)
                .Select(candidate => (TrackingCandidate?)candidate)
                .FirstOrDefault();
            yoloObjectId = currentTarget?.ObjectId ?? highThreatCandidates[0].ObjectId;
        }

        var targetChanged = _hasTrackedTarget != hasTrackedTarget || _yoloObjectId != yoloObjectId;
        var shouldRefreshAutomaticTracking =
            IsAutoMode &&
            DateTime.Now - _lastAutomaticTrackingPacketSentAt >= TimeSpan.FromMilliseconds(AutomaticTrackingResendMilliseconds);
        if (!targetChanged && !shouldRefreshAutomaticTracking)
        {
            return;
        }

        _hasTrackedTarget = hasTrackedTarget;
        _yoloObjectId = yoloObjectId;
        _isUserSelectedTrackId = false;

        if (targetChanged && hasTrackedTarget)
        {
            AppendImportantLog($"큰 화면 위험 객체 추적 ID 선택: object {yoloObjectId}");
        }

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"자동 모드 상태 전송에 실패했습니다: {modeError}");
            return;
        }

        if (IsAutoMode)
        {
            _lastAutomaticTrackingPacketSentAt = DateTime.Now;
        }
    }

    public void SelectYoloObject(int objectId, string threatLevel)
    {
        // 사용자가 큰 화면에서 특정 바운딩 박스를 클릭했을 때 호출합니다.
        // 현재는 선택한 객체 ID를 추적 대상으로 Jetson에 전송합니다.
        if (!IsSystemPoweredOn || objectId < 0)
        {
            return;
        }

        var isHighThreat = IsHighThreatLevel(threatLevel);
        //_hasTrackedTarget = isHighThreat;
        //_yoloObjectId = isHighThreat ?
        //_isUserSelectedTrackId = isHighThreat;
        //IsTrackingModeEnabled = true;
        _hasTrackedTarget = true;
        _yoloObjectId = objectId;
        _isUserSelectedTrackId = true;

        if (!TrySendMotorCommandPacket(out var error))
        {
            AppendImportantLog($"YOLO 객체 ID 전송에 실패했습니다: {error}");
            return;
        }

        AppendImportantLog(isHighThreat
            ? $"위험 객체 추적 ID 전송: object {objectId}"
            : $"선택한 객체가 위험 등급 높음이 아니므로 tracking=0으로 전송했습니다: object {objectId}");
    }

    public void ApplyVlmAnalysisResult(string threatLevel, string analysisMessage)
    {
        // VLM 결과를 상황 분석 창과 시스템 위험도에 반영합니다.
        // 위험도가 높음으로 올라가면 자동 모드 녹화 latch가 켜져 녹화 주기가 끝날 때까지 유지됩니다.
        var normalizedThreatLevel = NormalizeThreatLevel(threatLevel);
        var threatChanged = !string.Equals(CurrentThreatLevel, normalizedThreatLevel, StringComparison.Ordinal);
        CurrentThreatLevel = normalizedThreatLevel;

        if (!string.IsNullOrWhiteSpace(analysisMessage) &&
            !string.Equals(_lastAnalysisMessage, analysisMessage, StringComparison.Ordinal))
        {
            _lastAnalysisMessage = analysisMessage;
            AppendAnalysisLog(analysisMessage);
        }

        if (threatChanged)
        {
            AppendImportantLog($"위험 등급이 {CurrentThreatLevel}(으)로 변경되었습니다.");
        }
    }

    public void InitializeMotorControlState()
    {
        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"초기 모터 제어 패킷 전송에 실패했습니다: {modeError}");
            return;
        }
    }

    public void MoveMotorStep(string direction)
    {
        if (!TryMapDirectionToButton(direction, out var buttons))
        {
            return;
        }

        UpdateManualButtonState(buttons);
    }

    public void SetMotorPosition(double panDegrees, double tiltDegrees)
    {
        _motorPan = NormalizeMotorDegrees(panDegrees, MotorPanLimitDegrees);
        _motorTilt = NormalizeMotorDegrees(tiltDegrees, MotorTiltLimitDegrees);
        _motorPanRaw = DegreesToDynamixelPosition(_motorPan);
        _motorTiltRaw = DegreesToDynamixelPosition(_motorTilt);
        _panMotorPositionDegrees = _motorPan;
        _tiltMotorPositionDegrees = _motorTilt;

        OnPropertyChanged(nameof(MotorPanText));
        OnPropertyChanged(nameof(MotorTiltText));
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(TiltMotorPositionText));
    }

    public void UpdateMotorStatus(MotorStatusSnapshot snapshot)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        UpdateMotorStatusItems(PanMotorStatusItems, snapshot.Pan);
        _panMotorFeedbackRaw = ClampMotorRaw((int)Math.Min(snapshot.Pan.PresentPosition, (uint)MotorRawMaximum));
        _panMotorPositionDegrees = DynamixelPositionToDegrees(snapshot.Pan.PresentPosition);
        _motorPanRaw = _panMotorFeedbackRaw.Value;
        _motorPan = NormalizeMotorDegrees(_panMotorPositionDegrees, MotorPanLimitDegrees);
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(MotorPanText));
        if (snapshot.Tilt is { } tilt)
        {
            UpdateMotorStatusItems(TiltMotorStatusItems, tilt);
            _tiltMotorFeedbackRaw = ClampMotorRaw((int)Math.Min(tilt.PresentPosition, (uint)MotorRawMaximum));
            _tiltMotorPositionDegrees = DynamixelPositionToDegrees(tilt.PresentPosition);
            _motorTiltRaw = _tiltMotorFeedbackRaw.Value;
            _motorTilt = NormalizeMotorDegrees(_tiltMotorPositionDegrees, MotorTiltLimitDegrees);
            OnPropertyChanged(nameof(TiltMotorPositionText));
            OnPropertyChanged(nameof(MotorTiltText));
        }
    }

    private static void UpdateMotorStatusItems(ObservableCollection<MotorStatusItem> items, MotorStatusPacket packet)
    {
        SetMotorStatusValue(items, "Motor Value", packet.PresentPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Value", $"{DynamixelPositionToDegrees(packet.PresentPosition):0.0} deg");
        SetMotorStatusValue(items, "Motor Change Value", packet.GoalPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Change Value", $"{DynamixelPositionToDegrees(packet.GoalPosition):0.0} deg");
        SetMotorStatusValue(items, "Velocity", packet.PresentVelocity.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Current", packet.PresentCurrentRaw.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "PWM", packet.PresentPwm.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Temperature", $"{packet.PresentTemperature} C");
        SetMotorStatusValue(items, "Voltage", $"{packet.PresentInputVoltage:0.0} V");
        SetMotorStatusValue(items, "Moving", packet.Moving == 0 ? "Stop" : "Moving");
        SetMotorStatusValue(items, "Error Status", $"0x{packet.HardwareErrorStatus:X2}");
        SetMotorStatusValue(items, "Moving Status", $"0x{packet.MovingStatus:X2}");
        SetMotorStatusValue(items, "Last Update", packet.ReceivedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    public void UpdateManualButtonState(MotorButtonMask buttons)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        if (!IsManualMode)
        {
            return;
        }

        ApplyMotorButtonStateToCommandTarget(buttons);

        if (!TrySendMotorCommandPacket(out var modeError, buttons, syncFromFeedback: false))
        {
            AppendImportantLog($"모터 수동 제어 패킷 전송에 실패했습니다: {modeError}");
            return;
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
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

    public void AppendImportantLog(string message)
    {
        AddSystemLogItem(new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
    }

    public void AppendAnalysisLog(string message)
    {
        AddAnalysisItem(new AnalysisItem(DateTime.Now.ToString("HH:mm:ss"), message));
    }

    public string BuildAnalysisLogSnapshot(DateTime startInclusive, DateTime endExclusive, bool includeAll)
    {
        var items = includeAll
            ? _analysisHistory.OrderBy(item => item.CreatedAt).ToArray()
            : _analysisHistory
                .Where(item => item.CreatedAt >= startInclusive && item.CreatedAt < endExclusive)
                .OrderBy(item => item.CreatedAt)
                .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("LIG DNA GUI VLM Analysis Result");
        builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!includeAll)
        {
            builder.AppendLine($"Window: {startInclusive:yyyy-MM-dd HH:mm:ss} - {endExclusive:yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();

        if (items.Length == 0)
        {
            builder.AppendLine("No VLM analysis result in this period.");
        }
        else
        {
            foreach (var item in items)
            {
                builder.AppendLine($"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.Message}");
            }
        }

        return builder.ToString();
    }

    public string BuildSystemLogSnapshot(DateTime startInclusive, DateTime endExclusive, bool includeAll)
    {
        var items = includeAll
            ? _systemLogHistory.OrderBy(item => item.CreatedAt).ToArray()
            : _systemLogHistory
                .Where(item => item.CreatedAt >= startInclusive && item.CreatedAt < endExclusive)
                .OrderBy(item => item.CreatedAt)
                .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("LIG DNA GUI System Log");
        builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!includeAll)
        {
            builder.AppendLine($"Window: {startInclusive:yyyy-MM-dd HH:mm:ss} - {endExclusive:yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();

        if (items.Length == 0)
        {
            builder.AppendLine("No system log in this period.");
        }
        else
        {
            foreach (var item in items)
            {
                builder.AppendLine($"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.Message}");
            }
        }

        return builder.ToString();
    }

    private void SaveAnalysisLogsToDesktop()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(desktopPath, $"analysis_log_{timestamp}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("LIG DNA GUI Situation Analysis Log");
            builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            foreach (var item in _analysisHistory)
            {
                builder.AppendLine($"[{item.Time}] {item.Message}");
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
            AppendImportantLog($"\uC0C1\uD669 \uBD84\uC11D \uAE30\uB85D\uC744 \uC800\uC7A5\uD588\uC2B5\uB2C8\uB2E4: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"\uC0C1\uD669 \uBD84\uC11D \uAE30\uB85D \uC800\uC7A5\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4: {ex.Message}");
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SaveSystemLogsToDesktop()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(desktopPath, $"system_log_{timestamp}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("LIG DNA GUI System Log");
            builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            foreach (var log in _systemLogHistory)
            {
                builder.AppendLine($"[{log.Time}] {log.Message}");
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8\uB97C \uC800\uC7A5\uD588\uC2B5\uB2C8\uB2E4: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8 \uC800\uC7A5\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4: {ex.Message}");
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void TogglePower()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        void ShutdownApplication()
        {
            app.MainWindow?.Close();
            app.Shutdown();
        }

        if (app.Dispatcher.CheckAccess())
        {
            ShutdownApplication();
            return;
        }

        app.Dispatcher.BeginInvoke(ShutdownApplication);
    }

    /// <summary>
     /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
     /// </summary>
    private void SetMode(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string mode)
        {
            return;
        }

        if (mode == CurrentMode)
        {
            return;
        }

        CurrentMode = mode;

        if (IsAutoMode && CurrentThreatLevel == "\uB192\uC74C" && !_isRecordingSuppressed)
        {
            _isAutoRecordingLatched = true;
        }

        if (!IsManualMode)
        {
            if (IsManualRecordingEnabled)
            {
                // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
                IsManualRecordingEnabled = false;
            }
        }

        OnRecordingStateChanged();

        if (IsAutoMode)
        {
            SyncMotorRawFromFeedback();
        }

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"모터 모드 전송에 실패했습니다: {modeError}");
        }
        else if (IsAutoMode)
        {
            _lastAutomaticTrackingPacketSentAt = DateTime.Now;
        }

        AppendImportantLog($"\uCE74\uBA54\uB77C \uC81C\uC5B4 \uBAA8\uB4DC\uAC00 {CurrentMode}(\uC73C)\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    private void ToggleTrackingMode()
    {
        if (!IsSystemPoweredOn)
        {
            return;
        }

        IsTrackingModeEnabled = !IsTrackingModeEnabled;

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"추적 모드 전송에 실패했습니다: {modeError}");
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void ToggleManualRecording()
    {
        if (!IsSystemPoweredOn)
        {
            return;
        }

        if (IsRecordingActive)
        {
            _isRecordingSuppressed = true;
            _isAutoRecordingLatched = false;
            IsManualRecordingEnabled = false;
        }
        else
        {
            _isRecordingSuppressed = false;
            IsManualRecordingEnabled = true;
        }

        OnRecordingStateChanged();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SetTheme(object? parameter)
    {
        if (parameter is not string themeName || Application.Current is not App app)
        {
            return;
        }

        var nextTheme = themeName == "Light" ? AppThemeMode.Light : AppThemeMode.Dark;
        app.ApplyTheme(nextTheme);
        _currentThemeMode = nextTheme;
        OnPropertyChanged(nameof(IsDarkThemeActive));
        OnPropertyChanged(nameof(IsLightThemeActive));
        OnPropertyChanged(nameof(DarkThemeButtonOpacity));
        OnPropertyChanged(nameof(LightThemeButtonOpacity));
    }

    private void SetLanguage(object? parameter)
    {
        if (parameter is not string languageName)
        {
            return;
        }

        var nextLanguage = string.Equals(languageName, "Korean", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Korean
            : UiLanguage.English;

        if (_uiLanguage == nextLanguage)
        {
            return;
        }

        _uiLanguage = nextLanguage;
        Text.Refresh();
        RefreshPrimaryTargetLabels();
        RaiseLocalizedTextProperties();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SelectPrimaryTarget(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string target)
        {
            return;
        }

        SelectedPrimaryTarget = target;
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
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

    private string GetShortPrimaryTargetName(string target)
    {
        if (_uiLanguage == UiLanguage.English)
        {
            return target switch
            {
                "\uBB34\uAE30\uCCB4\uACC4" => "Weapon",
                "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4" => "Air",
                "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "Ground",
                "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "Sea",
                "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
                "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
                "\uC0AC\uB78C" => "Person",
                "\uBCF5\uD569" => "Composite",
                _ => target,
            };
        }

        return target switch
        {
            "\uBB34\uAE30\uCCB4\uACC4" => "\uBB34\uAE30\uCCB4\uACC4",
            "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4" => "\uACF5\uC911",
            "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uC721\uC0C1",
            "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uD574\uC0C1",
            "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
            "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
            _ => target,
        };
    }

    private string TranslateMode(string mode)
    {
        return mode switch
        {
            "\uC790\uB3D9" => Text["AutoMode"],
            "\uC218\uB3D9" => Text["ManualMode"],
            _ => mode,
        };
    }

    private string TranslateThreatLevel(string threatLevel)
    {
        return threatLevel switch
        {
            "\uB192\uC74C" => Text["ThreatHigh"],
            "\uC911\uAC04" => Text["ThreatMedium"],
            _ => Text["ThreatLow"],
        };
    }

    private string TranslatePrimaryTarget(string target)
    {
        return target switch
        {
            "\uBCF5\uD569" => Text["TargetComposite"],
            "\uC0AC\uB78C" => Text["TargetPerson"],
            "\uBB34\uAE30\uCCB4\uACC4" => Text["TargetWeapon"],
            "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
            "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
            _ => target,
        };
    }

    private IEnumerable<PrimaryTargetOption> CreatePrimaryTargetOptions()
    {
        var targets = new[]
        {
            "\uBCF5\uD569",
            "\uC0AC\uB78C",
            "\uBB34\uAE30\uCCB4\uACC4",
            "\uD1B5\uC2E0 \uC7A5\uBE44",
            "\uBE44\uAD70\uC0AC \uD45C\uC801",
        };

        return targets.Select(target => new PrimaryTargetOption(target, TranslatePrimaryTarget(target))).ToArray();
    }

    private void RefreshPrimaryTargetLabels()
    {
        foreach (var option in PrimaryTargets)
        {
            option.DisplayName = TranslatePrimaryTarget(option.Value);
        }
    }

    private void RaiseLocalizedTextProperties()
    {
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(IsKoreanLanguage));
        OnPropertyChanged(nameof(EnglishLanguageButtonOpacity));
        OnPropertyChanged(nameof(KoreanLanguageButtonOpacity));
        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(CurrentModeText));
        OnPropertyChanged(nameof(ManualRecordingButtonText));
        OnPropertyChanged(nameof(TrackingModeText));
        OnPropertyChanged(nameof(JetsonConnectionText));
        OnPropertyChanged(nameof(CurrentThreatText));
        OnPropertyChanged(nameof(PrimaryTargetText));
        OnPropertyChanged(nameof(PrimaryTargetShortText));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ContrastText));
    }

    private void OnRecordingStateChanged()
    {
        OnPropertyChanged(nameof(ManualRecordingButtonText));
        OnPropertyChanged(nameof(IsRecordingActive));
        OnPropertyChanged(nameof(RecordingIndicatorBrush));
        OnPropertyChanged(nameof(RecordingTextBrush));
        OnPropertyChanged(nameof(RecordingIndicatorOpacity));
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
     /// </summary>
    private void MoveMotor(object? parameter)
    {
        if (!CanUseMotorControls || parameter is not string direction)
        {
            return;
        }

        if (!TryMapDirectionToButton(direction, out var buttons))
        {
            return;
        }

        UpdateManualButtonState(buttons);
    }

    private void SendMotorTargetAngles()
    {
        if (!CanUseMotorTargetControls)
        {
            return;
        }

        if (!double.TryParse(MotorTargetPanText, NumberStyles.Float, CultureInfo.InvariantCulture, out var panDegrees) ||
            !double.TryParse(MotorTargetTiltText, NumberStyles.Float, CultureInfo.InvariantCulture, out var tiltDegrees))
        {
            AppendImportantLog("모터 각도 입력값을 확인하세요. 예: 0, 45.5, 360");
            return;
        }

        panDegrees = NormalizeMotorDegrees(panDegrees, MotorPanLimitDegrees);
        tiltDegrees = NormalizeMotorDegrees(tiltDegrees, MotorTiltLimitDegrees);

        _motorPanRaw = DegreesToDynamixelPosition(panDegrees);
        _motorTiltRaw = DegreesToDynamixelPosition(tiltDegrees);

        if (!TrySendMotorCommandPacket(out var error, syncFromFeedback: false, forcedMode: 1))
        {
            AppendImportantLog($"모터 각도 전송에 실패했습니다: {error}");
            return;
        }

        MotorTargetPanText = string.Empty;
        MotorTargetTiltText = string.Empty;
        AppendImportantLog($"모터 각도 전송: pan {panDegrees:0.0}°, tilt {tiltDegrees:0.0}°");
    }

    private void AdjustMotorStep(object? parameter)
    {
        if (!TryParseMotorAngleParameter(parameter, out var mode, out var delta, out var resetToDefault))
        {
            return;
        }

        if (mode == MotorStepMode.Auto)
        {
            AutoMotorAngleSize = resetToDefault ? DefaultMotorAngleSize : AutoMotorAngleSize + delta;
        }
        else
        {
            ManualMotorAngleSize = resetToDefault ? DefaultMotorAngleSize : ManualMotorAngleSize + delta;
        }

        if (!TrySendMotorCommandPacket(out var error))
        {
            AppendImportantLog($"모터 속도 전송에 실패했습니다: {error}");
            return;
        }

    }

    private static bool TryParseMotorAngleParameter(
        object? parameter,
        out MotorStepMode mode,
        out int delta,
        out bool resetToDefault)
    {
        mode = MotorStepMode.Auto;
        delta = 0;
        resetToDefault = false;

        if (parameter is string text)
        {
            var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                mode = string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase)
                    ? MotorStepMode.Manual
                    : MotorStepMode.Auto;
                if (string.Equals(parts[2], "Reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetToDefault = true;
                    return true;
                }

                return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
            }

            if (parts.Length == 2)
            {
                if (string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[0], "Auto", StringComparison.OrdinalIgnoreCase))
                {
                    mode = string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase)
                        ? MotorStepMode.Manual
                        : MotorStepMode.Auto;
                    if (string.Equals(parts[1], "Reset", StringComparison.OrdinalIgnoreCase))
                    {
                        resetToDefault = true;
                        return true;
                    }

                    return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
                }

                if (string.Equals(parts[1], "Reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetToDefault = true;
                    return true;
                }

                return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
        }

        delta = parameter switch
        {
            int intValue => intValue,
            _ => 0
        };

        return delta != 0;
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
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

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void RaiseAllCommandStates()
    {
        RaiseCommand(SetModeCommand);
        RaiseCommand(SelectPrimaryTargetCommand);
        RaiseCommand(ResetBrightnessCommand);
        RaiseCommand(ResetContrastCommand);
        RaiseCommand(ResetZoomCommand);
        RaiseCommand(ToggleManualRecordingCommand);
        RaiseCommand(MoveMotorCommand);
        RaiseCommand(SendMotorTargetCommand);
        RaiseCommand(AdjustMotorStepCommand);
        RaiseCommand(ToggleTrackingModeCommand);
    }

    private static void RaiseCommand(ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    private static void TrimCollection<T>(ObservableCollection<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static void TrimList<T>(List<T> items, int maxCount)
    {
        while (items.Count > maxCount)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private void AddAnalysisItem(AnalysisItem item)
    {
        _analysisHistory.Insert(0, item);
        TrimList(_analysisHistory, StoredLogItemLimit);

        AnalysisItems.Insert(0, item);
        TrimCollection(AnalysisItems, VisibleLogItemLimit);
    }

    private void AddSystemLogItem(SystemLogItem item)
    {
        _systemLogHistory.Insert(0, item);
        TrimList(_systemLogHistory, StoredLogItemLimit);

        SystemLogs.Insert(0, item);
        TrimCollection(SystemLogs, VisibleLogItemLimit);
    }

    private static void SetMotorStatusValue(ObservableCollection<MotorStatusItem> items, string name, string value)
    {
        var item = items.FirstOrDefault(status => status.Name == name);
        if (item is not null)
        {
            item.Value = value;
        }
    }

    private static IEnumerable<MotorStatusItem> CreateDefaultMotorStatusItems()
    {
        var names = new[]
        {
            "Motor Value",
            "Actual Value",
            "Motor Change Value",
            "Actual Change Value",
            "Velocity",
            "Current",
            "PWM",
            "Temperature",
            "Voltage",
            "Moving",
            "Error Status",
            "Moving Status",
            "Last Update"
        };

        return names.Select(name => new MotorStatusItem(name, "-")).ToArray();
    }

    private static double NormalizeMotorDegrees(double degrees, double limit)
    {
        var rounded = Math.Round(degrees, 1, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, 0, limit);
    }

    private static double DynamixelPositionToDegrees(uint position)
    {
        return Math.Min(position, (uint)MotorRawMaximum) / MotorRawResolution * 360.0;
    }

    private static ushort DegreesToDynamixelPosition(double degrees)
    {
        var position = (int)Math.Round(Math.Clamp(degrees, 0, 360) / 360.0 * MotorRawResolution, MidpointRounding.AwayFromZero);
        return ClampMotorRaw(position);
    }

    private static ushort ClampMotorRaw(int position)
    {
        return (ushort)Math.Clamp(position, MotorRawMinimum, MotorRawMaximum);
    }

    /// <summary>
    /// 색상 브러시를 만들고 UI 스레드 밖에서도 안전하게 쓰도록 고정합니다.
    /// </summary>
    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string NormalizeThreatLevel(string threatLevel)
    {
        return threatLevel.Trim().ToLowerInvariant() switch
        {
            "high" or "높음" => "\uB192\uC74C",
            "medium" or "mid" or "중간" => "\uC911\uAC04",
            _ => "\uB0AE\uC74C"
        };
    }
    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
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

    private bool TrySendMotorCommandPacket(
        out string? error,
        MotorButtonMask buttons = MotorButtonMask.None,
        bool syncFromFeedback = true,
        byte? forcedMode = null)
    {
        if (syncFromFeedback)
        {
            SyncMotorRawFromFeedback();
        }

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        return _motorControlService.TrySendMotorCommandPacket(
            mode: forcedMode ?? (IsManualMode ? (byte)1 : (byte)0),
            tracking: IsTrackingModeEnabled ? (byte)1 : (byte)0,
            trackId: EncodeTrackId(),
            btnMask: buttons,
            panPos: _motorPanRaw,
            tiltPos: _motorTiltRaw,
            scanStep: (byte)MotorSpeedToStepDelta(AutoMotorAngleSize),
            manualStep: (byte)MotorSpeedToStepDelta(ManualMotorAngleSize),
            isEoPrimary: IsEoPrimary,
            out error);
    }

    private byte EncodeTrackId()
    {
        if (!ShouldSendTrackingToZybo)
        {
            return 0xFF;
        }

        if (_isUserSelectedTrackId && _yoloObjectId is >= 0 and <= 254)
        {
            return 0xFF;
            //return (byte)_yoloObjectId;
        }

        return 0xFF;
    }

    private bool ShouldSendTrackingToZybo =>
        IsTrackingModeEnabled &&
        _hasTrackedTarget &&
        _yoloObjectId >= 0;

    private readonly record struct TrackingCandidate(int ObjectId, int ThreatWeight, int Order);

    private bool SyncMotorRawFromFeedback()
    {
        if (_panMotorFeedbackRaw is not { } panRaw)
        {
            return false;
        }

        _motorPanRaw = panRaw;
        _motorPan = NormalizeMotorDegrees(DynamixelPositionToDegrees(panRaw), MotorPanLimitDegrees);
        if (_tiltMotorFeedbackRaw is { } tiltRaw)
        {
            _motorTiltRaw = tiltRaw;
            _motorTilt = NormalizeMotorDegrees(DynamixelPositionToDegrees(tiltRaw), MotorTiltLimitDegrees);
        }

        return true;
    }

    private static int GetThreatWeight(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "\uB192\uC74C" => 3,
            "\uC911\uAC04" => 2,
            _ => 1
        };
    }

    private static bool IsHighThreatLevel(string threatLevel)
    {
        return GetThreatWeight(threatLevel) >= 3;
    }

    private static int MotorSpeedToStepDelta(int motorSpeed)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        return Math.Clamp(motorSpeed, 1, 10);
    }

    private void ApplyMotorButtonStateToCommandTarget(MotorButtonMask buttons)
    {
        if ((buttons & MotorButtonMask.Center) == MotorButtonMask.Center)
        {
            _motorPanRaw = 0;
            _motorTiltRaw = 0;
        }
        else
        {
            var panRaw = _panMotorFeedbackRaw ?? _motorPanRaw;
            var tiltRaw = _tiltMotorFeedbackRaw ?? _motorTiltRaw;

            if ((buttons & MotorButtonMask.Left) == MotorButtonMask.Left)
            {
                panRaw = ClampMotorRaw(panRaw - MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Right) == MotorButtonMask.Right)
            {
                panRaw = ClampMotorRaw(panRaw + MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Up) == MotorButtonMask.Up)
            {
                tiltRaw = ClampMotorRaw(tiltRaw + MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Down) == MotorButtonMask.Down)
            {
                tiltRaw = ClampMotorRaw(tiltRaw - MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            _motorPanRaw = panRaw;
            _motorTiltRaw = tiltRaw;
        }

    }

    private static bool TryMapDirectionToButton(string direction, out MotorButtonMask buttons)
    {
        buttons = direction switch
        {
            "Left" => MotorButtonMask.Left,
            "Right" => MotorButtonMask.Right,
            "Up" => MotorButtonMask.Up,
            "Down" => MotorButtonMask.Down,
            "Center" => MotorButtonMask.Center,
            _ => MotorButtonMask.None
        };

        return buttons != MotorButtonMask.None;
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
/// </summary>
public sealed record AnalysisItem(string Time, string Message)
{
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
/// </summary>
public sealed record SystemLogItem(string Time, string Message)
{
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

public enum MotorStepMode
{
    Auto,
    Manual
}

public enum UiLanguage
{
    English,
    Korean
}

public sealed class LocalizedTextProvider : INotifyPropertyChanged
{
    private static readonly Dictionary<string, (string English, string Korean)> Values = new()
    {
        ["PowerExit"] = ("Exit", "\uC804\uC6D0 \uC885\uB8CC"),
        ["RecordingOn"] = ("Recording", "\uC601\uC0C1 \uB179\uD654 \uC911"),
        ["RecordingStatus"] = ("Recording Status", "\uC601\uC0C1 \uB179\uD654 \uC0C1\uD0DC"),
        ["Connecting"] = ("System Connecting", "\uC2DC\uC2A4\uD15C \uC5F0\uACB0 \uC911"),
        ["Connected"] = ("System Connected", "\uC2DC\uC2A4\uD15C \uC5F0\uACB0\uB428"),
        ["Brightness"] = ("Bright", "\uBC1D\uAE30"),
        ["Contrast"] = ("Contrast", "\uB300\uC870\uBE44"),
        ["AutoMode"] = ("Scan", "\uC2A4\uCE94"),
        ["ManualMode"] = ("Manual", "\uC218\uB3D9"),
        ["StartRecording"] = ("Start Rec", "\uB179\uD654 \uC2DC\uC791"),
        ["StopRecording"] = ("Stop Rec", "\uB179\uD654 \uC885\uB8CC"),
        ["Settings"] = ("Settings", "\uC124\uC815\uCC3D"),
        ["PrimaryTargetChange"] = ("Primary Target", "\uC8FC \uD0D0\uC9C0\uCCB4 \uBCC0\uACBD"),
        ["PrimaryTarget"] = ("Target", "\uC8FC \uD0D0\uC9C0\uCCB4"),
        ["ThemeChange"] = ("Theme", "\uD14C\uB9C8 \uBCC0\uACBD"),
        ["DarkTheme"] = ("Dark", "\uC5B4\uB450\uC6B4 \uD14C\uB9C8"),
        ["LightTheme"] = ("Light", "\uBC1D\uC740 \uD14C\uB9C8"),
        ["LanguageChange"] = ("Language", "\uC5B8\uC5B4 \uBCC0\uACBD"),
        ["English"] = ("English", "\uC601\uC5B4"),
        ["Korean"] = ("Korean", "\uD55C\uAD6D\uC5B4"),
        ["ScreenMode"] = ("Screen Mode", "\uD654\uBA74 \uBAA8\uB4DC"),
        ["WindowMode"] = ("Window Mode", "\uCC3D\uBAA8\uB4DC\uB85C \uC804\uD658"),
        ["FullscreenMode"] = ("Fullscreen", "\uC804\uCCB4\uD654\uBA74\uC73C\uB85C \uC804\uD658"),
        ["NetworkSettings"] = ("Network", "\uB124\uD2B8\uC6CC\uD06C"),
        ["JetsonIp"] = ("Jetson IP", "Jetson IP"),
        ["PcIp"] = ("GUI IP", "GUI IP"),
        ["RecordedVideoUrl"] = ("Video URL", "\uB179\uD654 URL"),
        ["Details"] = ("Details", "\uC0C1\uC138"),
        ["MotorPosition"] = ("Motor Position", "\uBAA8\uD130 \uC704\uCE58"),
        ["MotorTarget"] = ("Motor Angle Setting", "\uBAA8\uD130 \uAC01\uB3C4 \uC124\uC815"),
        ["MotorSpeed"] = ("Motor Speed", "\uBAA8\uD130 \uC138\uAE30"),
        ["MotorControl"] = ("Motor Control", "\uBAA8\uD130 \uCEE8\uD2B8\uB864"),
        ["SystemStatus"] = ("System Status", "\uC2DC\uC2A4\uD15C \uD604\uD669"),
        ["AnalysisPanel"] = ("YOLO Targets", "YOLO \uD0D0\uC9C0 \uD0C0\uAC9F"),
        ["SystemLog"] = ("System Log", "\uC2DC\uC2A4\uD15C \uB85C\uADF8"),
        ["Save"] = ("Save", "\uC800\uC7A5"),
        ["RecordedVideos"] = ("Recorded Videos", "\uB179\uD654 \uC601\uC0C1 \uBCF4\uAE30"),
        ["Refresh"] = ("Refresh", "\uC0C8\uB85C\uACE0\uCE68"),
        ["Close"] = ("Close", "\uB2EB\uAE30"),
        ["SavedVideos"] = ("Saved Videos", "\uC800\uC7A5\uB41C \uC601\uC0C1"),
        ["RecordedData"] = ("Recording Data", "\uC800\uC7A5 \uC601\uC0C1 \uD655\uC778"),
        ["CamZoom"] = ("Cam ZOOM", "\uC804\uC790 ZOOM"),
        ["ZoomMiniMap"] = ("Zoom Map", "Zoom \uBBF8\uB2C8\uB9F5"),
        ["TrackingOn"] = ("Tracking", "\uCD94\uC801"),
        ["TrackingOff"] = ("Tracking Off", "\uBE44\uCD94\uC801"),
        ["ThreatLevel"] = ("Threat", "\uC704\uD5D8 \uB4F1\uAE09"),
        ["ThreatLow"] = ("Low", "\uB0AE\uC74C"),
        ["ThreatMedium"] = ("Medium", "\uC911\uAC04"),
        ["ThreatHigh"] = ("High", "\uB192\uC74C"),
        ["TargetComposite"] = ("Composite", "\uBCF5\uD569"),
        ["TargetPerson"] = ("Person", "\uC0AC\uB78C"),
        ["TargetWeapon"] = ("Weapon", "\uBB34\uAE30\uCCB4\uACC4"),
        ["TargetComm"] = ("Telecom", "\uD1B5\uC2E0 \uC7A5\uBE44"),
        ["TargetCivil"] = ("Non-military", "\uBE44\uAD70\uC0AC \uD45C\uC801"),
        ["SystemStarted"] = ("System startup started.", "\uC2DC\uC2A4\uD15C \uAC00\uB3D9\uC744 \uC2DC\uC791\uD569\uB2C8\uB2E4."),
        ["LanguageChanged"] = ("Display language changed.", "\uD45C\uC2DC \uC5B8\uC5B4\uAC00 \uBCC0\uACBD\uB418\uC5C8\uC2B5\uB2C8\uB2E4."),
    };

    private readonly Func<UiLanguage> _languageAccessor;

    public LocalizedTextProvider(Func<UiLanguage> languageAccessor)
    {
        _languageAccessor = languageAccessor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get
        {
            if (!Values.TryGetValue(key, out var value))
            {
                return key;
            }

            return _languageAccessor() == UiLanguage.Korean ? value.Korean : value.English;
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

public sealed class PrimaryTargetOption : INotifyPropertyChanged
{
    private string _displayName;

    public PrimaryTargetOption(string value, string displayName)
    {
        Value = value;
        _displayName = displayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (string.Equals(_displayName, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }
}

public sealed class MotorStatusItem : INotifyPropertyChanged
{
    private string _value;

    public MotorStatusItem(string name, string value)
    {
        Name = name;
        _value = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal))
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}

public sealed record DetectionTargetItem(
    int ObjectId,
    string ClassName,
    string ScoreText,
    string ThreatLevel,
    Brush ThreatBrush,
    ImageSource? Thumbnail);

