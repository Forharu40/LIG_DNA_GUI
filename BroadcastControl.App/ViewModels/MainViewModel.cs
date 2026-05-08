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
using BroadcastControl.App.Services;

namespace BroadcastControl.App.ViewModels;

/// 메인 화면에서 사용하는 상태값을 한곳에서 관리하는 ViewModel이다.
/// 화면 모드, 위험 등급, 카메라 영상, 설정 값, 로그, 줌 상태 같은 UI 데이터를 묶어서 제공한다.
/// 현재는 데모 및 UDP 수신 화면 기준으로 동작하지만, 이후 실제 장비나 VLM 결과가 들어와도 같은 구조를 유지할 수 있도록 구성되어 있다.
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    /// 모드, 위험 등급, 밝기/대조비, 줌, 로그, 테마 버튼 상태를 함께 관리한다.
    // 미니맵은 현재 확대된 영역을 간단히 보여주는 용도이므로, 본 화면 비율에 맞춰 작은 크기로 고정한다.
    private const double MiniMapWidth = 130;
    private const double MiniMapHeight = 74;

    private static readonly SolidColorBrush LowThreatBrush = CreateBrush(0x7B, 0xD8, 0x8F);
    private static readonly SolidColorBrush MediumThreatBrush = CreateBrush(0xFF, 0xC1, 0x45);
    private static readonly SolidColorBrush HighThreatBrush = CreateBrush(0xFF, 0x6B, 0x6B);
    private static readonly SolidColorBrush RecordingOnBrush = CreateBrush(0xFF, 0x4D, 0x4F);
    private static readonly SolidColorBrush RecordingOffBrush = CreateBrush(0x41, 0x49, 0x55);
    private static readonly SolidColorBrush RecordingTextOffBrush = CreateBrush(0x92, 0x9D, 0xAA);

    private bool _isEoPrimary = true;
    private bool _isSettingsOpen;
    private bool _isSystemPoweredOn = true;
    private string _currentMode = "\uC790\uB3D9";
    private string _selectedPrimaryTarget = "\uBCF5\uD569";
    private string _currentThreatLevel = "\uB0AE\uC74C";
    // 프로그램을 처음 켰을 때 밝기는 중간값인 50%에서 시작한다.
    private double _brightness = 50;
    private double _contrast = 50;
    private bool _isManualRecordingEnabled;
    private bool _isRecordingSuppressed;
    private bool _isAutoRecordingLatched;
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
    private int _panMotorStepSize = 9;
    private int _tiltMotorStepSize = 9;
    private double _panMotorPositionDegrees;
    private double _tiltMotorPositionDegrees;
    private bool _isMotorDetailsOpen;
    private string _motorTargetPanText = "0.0";
    private string _motorTargetTiltText = "0.0";
    private bool _hasTrackedTarget;
    private readonly UdpMotorControlService _motorControlService;
    private const double MotorPanLimitDegrees = 360;
    private const double MotorTiltLimitDegrees = 360;
    private const int VisibleLogItemLimit = 30;
    private const int StoredLogItemLimit = 100;
    private readonly List<AnalysisItem> _analysisHistory = new();
    private readonly List<SystemLogItem> _systemLogHistory = new();
    private string? _lastAnalysisMessage;

    // EO와 IR 모두 Jetson에서 전달되는 UDP 영상을 표시한다.
    // 실제 프레임을 아직 받지 못한 경우에도 화면이 비어 보이지 않도록 EO/IR 기본 안내 이미지를 미리 준비해둔다.
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private readonly ImageSource _eoPlaceholderFrame = CreateCameraPlaceholderFrame(string.Empty, Color.FromRgb(51, 94, 160));
    private readonly ImageSource _irPlaceholderFrame = CreateCameraPlaceholderFrame(string.Empty, Color.FromRgb(192, 109, 40));

    public MainViewModel(UdpMotorControlService? motorControlService = null)
    {
        _motorControlService = motorControlService ?? new UdpMotorControlService();

        // 앱이 현재 사용 중인 테마를 읽어서 설정 창 버튼 상태와 맞춘다.
        if (Application.Current is App app)
        {
            _currentThemeMode = app.CurrentThemeMode;
        }

        AnalysisItems = new ObservableCollection<AnalysisItem>();
        SystemLogs = new ObservableCollection<SystemLogItem>();
        PanMotorStatusItems = new ObservableCollection<MotorStatusItem>(CreateDefaultMotorStatusItems());
        TiltMotorStatusItems = new ObservableCollection<MotorStatusItem>(CreateDefaultMotorStatusItems());

        AddSystemLogItem(new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), "\uC2DC\uC2A4\uD15C \uAC00\uB3D9\uC744 \uC2DC\uC791\uD569\uB2C8\uB2E4."));

        PrimaryTargets = new ReadOnlyCollection<string>(new[]
        {
            "\uBCF5\uD569",
            "\uC0AC\uB78C",
            "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4",
            "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4",
            "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4",
            "\uD1B5\uC2E0 \uC7A5\uBE44",
            "\uBE44\uAD70\uC0AC \uD45C\uC801",
        });

        // 화면의 모든 버튼은 Command 바인딩으로 연결되므로 생성자에서 한 번에 등록한다.
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
        ResetBrightnessCommand = new RelayCommand(_ => Brightness = 50, _ => IsSystemPoweredOn);
        ResetContrastCommand = new RelayCommand(_ => Contrast = 50, _ => IsSystemPoweredOn);
        // 확대 제목 버튼을 누르면 기본 배율 x1.0으로 즉시 복귀한다.
        ResetZoomCommand = new RelayCommand(_ => ZoomLevel = 1.0, _ => CanUseZoomControls);
        ToggleManualRecordingCommand = new RelayCommand(_ => ToggleManualRecording(), _ => IsSystemPoweredOn);
        SetThemeCommand = new RelayCommand(SetTheme);
        SaveAnalysisLogsCommand = new RelayCommand(_ => ManualAnalysisSaveRequested?.Invoke(this, EventArgs.Empty));
        SaveSystemLogsCommand = new RelayCommand(_ => ManualSystemLogSaveRequested?.Invoke(this, EventArgs.Empty));
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        MoveMotorCommand = new RelayCommand(MoveMotor, _ => CanUseMotorControls);
        SendMotorTargetCommand = new RelayCommand(_ => SendMotorTargetAngles(), _ => CanUseMotorTargetControls);
        AdjustMotorStepCommand = new RelayCommand(AdjustMotorStep, _ => IsSystemPoweredOn);
        ToggleMotorDetailsCommand = new RelayCommand(_ => IsMotorDetailsOpen = !IsMotorDetailsOpen);
        CloseMotorDetailsCommand = new RelayCommand(_ => IsMotorDetailsOpen = false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ManualAnalysisSaveRequested;

    public event EventHandler? ManualSystemLogSaveRequested;

    public ObservableCollection<AnalysisItem> AnalysisItems { get; }

    public ObservableCollection<SystemLogItem> SystemLogs { get; }

    public ObservableCollection<MotorStatusItem> PanMotorStatusItems { get; }

    public ObservableCollection<MotorStatusItem> TiltMotorStatusItems { get; }

    public ReadOnlyCollection<string> PrimaryTargets { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand SetModeCommand { get; }

    public ICommand ToggleSettingsCommand { get; }

    public ICommand SelectPrimaryTargetCommand { get; }

    public ICommand ResetBrightnessCommand { get; }

    public ICommand ResetContrastCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand ToggleManualRecordingCommand { get; }

    public ICommand SetThemeCommand { get; }

    public ICommand SaveAnalysisLogsCommand { get; }

    public ICommand SaveSystemLogsCommand { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand MoveMotorCommand { get; }

    public ICommand SendMotorTargetCommand { get; }

    public ICommand AdjustMotorStepCommand { get; }

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

    // 상단 전원 버튼은 실제 프로그램 종료 버튼으로 사용한다.
    public string PowerButtonText => "\uC804\uC6D0 \uC885\uB8CC";

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
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"\uCE74\uBA54\uB77C \uBAA8\uB4DC: {CurrentMode}";

    // 현재 선택된 모드 버튼만 선명하게 보여서 별도 텍스트 없이도 상태를 알아볼 수 있게 한다.
    public double AutoModeOpacity => CurrentMode == "\uC790\uB3D9" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "\uC218\uB3D9" ? 1.0 : 0.35;

    // 녹화 상태는 현재 수동 녹화 여부와 자동 녹화 조건을 함께 반영한 결과값이다.
    // 자동 모드에서는 위험 등급이 높음일 때만 자동 녹화 상태로 간주하고,
    // 수동 모드에서는 사용자가 직접 녹화를 켠 경우에만 활성화된다.

    public bool IsRecordingActive =>
        IsSystemPoweredOn &&
        !_isRecordingSuppressed &&
        (IsManualRecordingEnabled || _isAutoRecordingLatched);

    public Brush RecordingIndicatorBrush => IsSystemPoweredOn ? RecordingOnBrush : RecordingOffBrush;

    public Brush RecordingTextBrush => IsSystemPoweredOn ? RecordingOnBrush : RecordingTextOffBrush;

    public double RecordingIndicatorOpacity => IsSystemPoweredOn ? 1.0 : 0.42;

    public bool IsManualMode => IsSystemPoweredOn && CurrentMode == "\uC218\uB3D9";

    public bool IsAutoMode => IsSystemPoweredOn && CurrentMode == "\uC790\uB3D9";

    public bool CanSelectAutoMode => IsSystemPoweredOn && CurrentMode != "\uC790\uB3D9";

    public bool CanSelectManualMode => IsSystemPoweredOn && CurrentMode != "\uC218\uB3D9";

    public bool CanUseMotorControls => IsManualMode;

    public bool CanUseMotorTargetControls => IsSystemPoweredOn;

    public double MotorControlsOpacity => CanUseMotorControls ? 1.0 : 0.38;

    public int PanMotorStepSize
    {
        get => _panMotorStepSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _panMotorStepSize, normalized))
            {
                OnPropertyChanged(nameof(PanMotorStepSizeText));
            }
        }
    }

    public int TiltMotorStepSize
    {
        get => _tiltMotorStepSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _tiltMotorStepSize, normalized))
            {
                OnPropertyChanged(nameof(TiltMotorStepSizeText));
            }
        }
    }

    public string PanMotorStepSizeText => PanMotorStepSize.ToString(CultureInfo.InvariantCulture);

    public string TiltMotorStepSizeText => TiltMotorStepSize.ToString(CultureInfo.InvariantCulture);

    public string PanMotorPositionText => $"{_panMotorPositionDegrees:0.0} deg";

    public string TiltMotorPositionText => $"{_tiltMotorPositionDegrees:0.0} deg";

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

    // 수동 녹화는 수동 모드에서만 켜고 끌 수 있도록 제한한다.
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

    public string ManualRecordingButtonText => IsRecordingActive ? "\uB179\uD654 \uC885\uB8CC" : "\uB179\uD654 \uC2DC\uC791";

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

    public string CurrentThreatText => $"\uC704\uD5D8 \uB4F1\uAE09: {CurrentThreatLevel}";

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

    public string PrimaryTargetText => $"\uC8FC \uD0D0\uC9C0\uCCB4: {SelectedPrimaryTarget}";

    public string PrimaryTargetShortText => $"\uC8FC \uD0D0\uC9C0\uCCB4: {GetShortPrimaryTargetName(SelectedPrimaryTarget)}";

    // 카메라 이름은 짧고 명확하게 유지해서 실제 화면을 가리지 않도록 한다.
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

    public Stretch LargeFeedStretch => Stretch.UniformToFill;

    public Stretch InsetFeedStretch => Stretch.UniformToFill;

    public string LargeFeedSubtitle => _isEoPrimary ? EoSubtitle : IrSubtitle;

    public string InsetFeedSubtitle => _isEoPrimary ? IrSubtitle : EoSubtitle;

    public double Brightness
    {
        get => _brightness;
        set
        {
            // 슬라이더 값이 바뀌면 화면에 보이는 텍스트도 바로 갱신한다.
            if (SetProperty(ref _brightness, value))
            {
                OnPropertyChanged(nameof(BrightnessText));
            }
        }
    }

    public string BrightnessText => $"\uBC1D\uAE30 {Brightness:0}%";

    public double Contrast
    {
        get => _contrast;
        set
        {
            // 대조비 숫자 표시와 실제 영상 보정 값이 항상 같은 값을 보이도록 맞춘다.
            if (SetProperty(ref _contrast, value))
            {
                OnPropertyChanged(nameof(ContrastText));
            }
        }
    }

    public string ContrastText => $"\uB300\uC870\uBE44 {Contrast:0}%";

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            // 지나친 확대를 막기 위해 줌 범위는 1.0~4.0 사이로 제한한다.
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 기본 배율로 돌아오면 이전에 이동해둔 화면 위치도 함께 중앙으로 초기화한다.
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
            // 실제 화면 이동 방향과 미니맵 표시 방향을 맞추기 위해 좌표를 반대로 계산한다.
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
            // 실제 화면 이동 방향과 미니맵 표시 방향을 맞추기 위해 좌표를 반대로 계산한다.
            return (1.0 - normalized) * (MiniMapHeight - MiniMapViewportHeight);
        }
    }

    public string MotorPanText => $"모터 좌우: {_motorPan:0.0}도";

    public string MotorTiltText => $"모터 상하: {_motorTilt:0.0}도";

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
    /// EO 카메라 프레임을 ViewModel에 반영한다.
    /// EO가 메인 화면이든 보조 화면이든 관계없이, 바인딩된 이미지가 즉시 갱신되도록 알림을 보낸다.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    /// <summary>
    /// IR 카메라 프레임을 ViewModel에 반영한다.
    /// 현재 장착 방향을 맞추기 위해 수신 프레임을 왼쪽으로 90도 회전한 뒤 화면에 사용한다.
    /// EO/IR 화면이 서로 바뀐 상태여도 메인 화면과 보조 화면 모두 즉시 갱신된다.
    /// </summary>
    public void UpdateIrFrame(ImageSource? frame)
    {
        _irFrame = RotateFrame(frame, -90);
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
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
        // 자동 모드에서는 탐지 유무를 보고 스캔(0)과 추적(1) 모드 패킷을 전환한다.
        var hasTrackedTarget = detections.Count > 0;
        if (_hasTrackedTarget == hasTrackedTarget)
        {
            return;
        }

        _hasTrackedTarget = hasTrackedTarget;

        if (!IsAutoMode)
        {
            return;
        }

        if (!TrySendCurrentModeToController(out var modeError))
        {
            AppendImportantLog($"자동 모드 상태 전송에 실패했습니다: {modeError}");
            return;
        }

    }

    public void ApplyVlmAnalysisResult(string threatLevel, string analysisMessage)
    {
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
        if (!TrySendCurrentModeToController(out var modeError))
        {
            AppendImportantLog($"초기 모터 모드 전송에 실패했습니다: {modeError}");
            return;
        }

        if (!TrySendButtonPacket(MotorButtonMask.None, out var buttonError))
        {
            AppendImportantLog($"초기 버튼 패킷 전송에 실패했습니다: {buttonError}");
        }

        if (!TrySendStepSizePacket(out var stepError))
        {
            AppendImportantLog($"초기 모터 step size 전송에 실패했습니다: {stepError}");
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
        _panMotorPositionDegrees = _motorPan;
        _tiltMotorPositionDegrees = _motorTilt;

        OnPropertyChanged(nameof(MotorPanText));
        OnPropertyChanged(nameof(MotorTiltText));
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(TiltMotorPositionText));
    }

    public void UpdateMotorStatus(MotorStatusSnapshot snapshot)
    {
        UpdateMotorStatusItems(PanMotorStatusItems, snapshot.Pan);
        _panMotorPositionDegrees = DynamixelPositionToDegrees(snapshot.Pan.PresentPosition);
        OnPropertyChanged(nameof(PanMotorPositionText));
        if (snapshot.Tilt is { } tilt)
        {
            UpdateMotorStatusItems(TiltMotorStatusItems, tilt);
            _tiltMotorPositionDegrees = DynamixelPositionToDegrees(tilt.PresentPosition);
            OnPropertyChanged(nameof(TiltMotorPositionText));
        }
    }

    private static void UpdateMotorStatusItems(ObservableCollection<MotorStatusItem> items, MotorStatusPacket packet)
    {
        SetMotorStatusValue(items, "Motor Value", packet.PresentPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Value", $"{DynamixelPositionToDegrees(packet.PresentPosition):0.0} deg");
        SetMotorStatusValue(items, "Motor Change Value", packet.GoalPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Change Value", $"{DynamixelPositionToDegrees(packet.GoalPosition):0.0} deg");
        SetMotorStatusValue(items, "Velocity", packet.PresentVelocity.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Load", packet.PresentLoad.ToString(CultureInfo.InvariantCulture));
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
        if (!IsManualMode)
        {
            return;
        }

        ApplyMotorButtonStateToUi(buttons);

        if (!TrySendCurrentModeToController(out var modeError))
        {
            AppendImportantLog($"모터 수동 모드 전송에 실패했습니다: {modeError}");
            return;
        }

        if (!TrySendButtonPacket(buttons, out var buttonError))
        {
            AppendImportantLog($"모터 버튼 패킷 전송에 실패했습니다: {buttonError}");
        }
    }

    /// <summary>
    /// 카메라 뷰포트의 실제 표시 크기를 받아 확대 이동 한계를 다시 계산한다.
    /// 창 크기나 레이아웃이 바뀌었을 때 줌 이동 범위가 어긋나지 않도록 보정하는 용도다.
    /// </summary>
    public void UpdateViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(width, 1);
        _viewportHeight = Math.Max(height, 1);
        ClampZoomPan();
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// 확대 상태에서 마우스 드래그로 화면 위치를 이동한다.
    /// 확대 중이 아닐 때는 이동할 필요가 없으므로 아무 동작도 하지 않는다.
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
    /// 마우스 휠 입력으로 확대 배율을 조금씩 조절한다.
    /// 수동 모드에서만 동작하며, 한 번 굴릴 때마다 0.1 단위로 배율을 변경한다.
    /// </summary>
    public void AdjustZoomByWheel(double wheelSteps)
    {
        if (!CanUseZoomControls || Math.Abs(wheelSteps) < double.Epsilon)
        {
            return;
        }

        // 휠 한 칸마다 0.1 배씩 조절해서 슬라이더와 비슷한 감도로 맞춘다.
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
    /// 현재 시스템 로그를 바탕화면에 시간 기준 파일명으로 저장한다.
    /// 나중에 테스트 기록이나 장애 추적 자료로 바로 활용할 수 있도록 UTF-8 형식으로 저장한다.
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
    /// 상단 전원 종료 버튼의 실제 동작을 처리한다.
    /// 위험 등급이 높음인 경우에는 운용 중 실수로 프로그램을 닫지 못하도록 종료를 막는다.
    /// </summary>
    private void TogglePower()
    {
        // 위험 등급이 높음이면 사용 중인 프로그램 종료를 차단한다.
        if (CurrentThreatLevel == "\uB192\uC74C")
        {
            AppendImportantLog("\uC704\uD5D8 \uB4F1\uAE09\uC774 \uB192\uC74C \uC0C1\uD0DC\uC5EC\uC11C \uD504\uB85C\uADF8\uB7A8\uC744 \uC885\uB8CC\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
            return;
        }

        Application.Current?.Shutdown();
    }

    /// <summary>
     /// 자동 모드와 수동 모드를 전환한다.
    /// 모드 전환 시 모터 각도는 유지하고, 전송할 모드 패킷만 현재 상태에 맞게 갱신한다.
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
                // 자동 모드로 바뀌면 수동 녹화는 즉시 종료 상태로 맞춘다.
                IsManualRecordingEnabled = false;
            }
        }

        OnRecordingStateChanged();

        if (!TrySendCurrentModeToController(out var modeError))
        {
            AppendImportantLog($"모터 모드 전송에 실패했습니다: {modeError}");
        }

        if (IsAutoMode)
        {
            if (!TrySendButtonPacket(MotorButtonMask.None, out var buttonError))
            {
                AppendImportantLog($"자동 모드 버튼 패킷 전송에 실패했습니다: {buttonError}");
            }
        }

        AppendImportantLog($"\uCE74\uBA54\uB77C \uC81C\uC5B4 \uBAA8\uB4DC\uAC00 {CurrentMode}(\uC73C)\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    /// <summary>
    /// 녹화 버튼을 눌렀을 때 현재 녹화 상태를 기준으로 시작/종료를 전환한다.
    /// 자동 모드 녹화 중에도 사용자가 즉시 종료할 수 있도록 별도 억제 상태를 둔다.
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
    /// 설정 창에서 테마를 직접 바꿀 때 호출되는 명령 처리부다.
    /// 앱 전체 테마를 적용한 뒤 버튼 선택 상태와 로그도 함께 갱신한다.
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
        AppendImportantLog($"\uD14C\uB9C8\uAC00 {(nextTheme == AppThemeMode.Dark ? "\uC5B4\uB450\uC6B4 \uD14C\uB9C8" : "\uBC1D\uC740 \uD14C\uB9C8")}(\uC73C)\uB85C \uBCC0\uACBD\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    /// <summary>
    /// 설정 창에서 주 탐지체를 선택하면 현재 선택 상태를 갱신한다.
    /// 지금 단계에서는 선택된 항목과 로그만 바꾸고, 위험 등급 변화는 이후 VLM 분석 결과와 연동할 때 반영한다.
    /// </summary>
    private void SelectPrimaryTarget(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string target)
        {
            return;
        }

        SelectedPrimaryTarget = target;
        AppendImportantLog($"\uC8FC \uD0D0\uC9C0\uCCB4\uAC00 {SelectedPrimaryTarget}(\uC73C)\uB85C \uBCC0\uACBD\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    /// <summary>
    /// EO와 IR의 메인 화면/보조 화면 위치를 서로 바꾼다.
    /// 사용자가 작은 화면을 눌렀을 때 원하는 영상을 크게 볼 수 있도록 하는 동작이다.
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

        AppendImportantLog($"{LargeFeedTitle}\uAC00 \uBA54\uC778 \uD654\uBA74\uC73C\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
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
        AppendImportantLog($"{LargeFeedTitle} 화면이 회전되었습니다.");
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
        AppendImportantLog($"{InsetFeedTitle} 화면이 회전되었습니다.");
    }

    private static double NextRotationAngle(double currentAngle) => (currentAngle + 90) % 360;

    private static string GetShortPrimaryTargetName(string target)
    {
        return target switch
        {
            "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4" => "\uACF5\uC911",
            "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uC721\uC0C1",
            "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uD574\uC0C1",
            "\uD1B5\uC2E0 \uC7A5\uBE44" => "\uD1B5\uC2E0",
            "\uBE44\uAD70\uC0AC \uD45C\uC801" => "\uBE44\uAD70\uC0AC",
            _ => target,
        };
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
    /// 수동 모드에서 모터 방향 버튼을 누르면 UI 표시용 각도와 버튼 비트마스크를 함께 갱신한다.
    /// 실제 이동량은 미션 PC가 결정하므로 GUI는 0x02 버튼 패킷만 전송한다.
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
            AppendImportantLog("모터 목표 각도 입력값을 확인하세요. 예: 0, 45.5, 360");
            return;
        }

        panDegrees = NormalizeMotorDegrees(panDegrees, MotorPanLimitDegrees);
        tiltDegrees = NormalizeMotorDegrees(tiltDegrees, MotorTiltLimitDegrees);
        MotorTargetPanText = panDegrees.ToString("0.0", CultureInfo.InvariantCulture);
        MotorTargetTiltText = tiltDegrees.ToString("0.0", CultureInfo.InvariantCulture);

        if (!_motorControlService.TrySendTargetAnglePacket(panDegrees, tiltDegrees, out var error))
        {
            AppendImportantLog($"모터 목표 각도 전송에 실패했습니다: {error}");
            return;
        }

        SetMotorPosition(panDegrees, tiltDegrees);
        AppendImportantLog($"모터 목표 각도 전송: pan {panDegrees:0.0}°, tilt {tiltDegrees:0.0}°");
    }

    private void AdjustMotorStep(object? parameter)
    {
        if (!TryParseMotorStepParameter(parameter, out var axis, out var delta))
        {
            return;
        }

        if (axis is MotorStepAxis.Pan or MotorStepAxis.Both)
        {
            PanMotorStepSize += delta;
        }

        if (axis is MotorStepAxis.Tilt or MotorStepAxis.Both)
        {
            TiltMotorStepSize += delta;
        }

        if (!_motorControlService.TrySendStepSizePacket(PanMotorStepSize, TiltMotorStepSize, out var error))
        {
            AppendImportantLog($"모터 step size 전송에 실패했습니다: {error}");
            return;
        }

        AppendImportantLog($"모터 step size 변경: pan {PanMotorStepSize}, tilt {TiltMotorStepSize}");
    }

    private static bool TryParseMotorStepParameter(object? parameter, out MotorStepAxis axis, out int delta)
    {
        axis = MotorStepAxis.Both;
        delta = 0;

        if (parameter is string text)
        {
            var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                axis = string.Equals(parts[0], "Tilt", StringComparison.OrdinalIgnoreCase)
                    ? MotorStepAxis.Tilt
                    : MotorStepAxis.Pan;
                return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
            }

            axis = MotorStepAxis.Both;
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
    /// 현재 확대 이동 값이 허용 범위를 넘지 않도록 보정한다.
    /// 화면 크기나 배율이 바뀐 뒤에도 이동 좌표가 튀지 않도록 정리하는 단계다.
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
    /// 확대 미니맵 사각형의 크기와 위치가 바뀌었음을 UI에 알린다.
    /// 줌 배율이나 이동 좌표가 바뀔 때마다 미니맵 표시도 함께 갱신된다.
    /// </summary>
    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    /// <summary>
    /// 모드, 전원, 줌 가능 여부가 바뀌면 각 버튼의 활성 상태를 다시 계산한다.
    /// 관련된 Command 객체에 CanExecuteChanged를 보내서 버튼이 즉시 켜지거나 꺼지도록 한다.
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
            "Load",
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

    private static double DynamixelPositionToDegrees(ushort position)
    {
        return position * 360.0 / 4095.0;
    }

    /// <summary>
    /// 여러 곳에서 반복해서 사용하는 고정 색상 브러시를 생성한다.
    /// Freeze 처리로 성능과 메모리 사용을 조금 더 안정적으로 유지한다.
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
            "medium" or "중간" => "\uC911\uAC04",
            _ => "\uB0AE\uC74C"
        };
    }

    /// <summary>
    /// 실제 카메라 프레임을 받기 전 화면에 보여줄 플레이스홀더 이미지를 만든다.
    /// UI 테스트 단계나 연결 대기 상태에서 카메라 영역이 완전히 비어 보이지 않도록 하기 위한 용도다.
    /// </summary>
    private static ImageSource CreateCameraPlaceholderFrame(string label, Color accentColor)
    {
        // 실제 입력을 받기 전에도 카메라 위치와 영역을 쉽게 알아볼 수 있도록 안내용 프레임을 만든다.
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

    private bool TrySendCurrentModeToController(out string? error)
    {
        var mode = IsManualMode
            ? MotorPacketMode.Manual
            : _hasTrackedTarget
                ? MotorPacketMode.Tracking
                : MotorPacketMode.Scan;

        return _motorControlService.TrySendModePacket(mode, out error);
    }

    private bool TrySendButtonPacket(MotorButtonMask buttons, out string? error)
    {
        return _motorControlService.TrySendButtonPacket(buttons, out error);
    }

    private bool TrySendStepSizePacket(out string? error)
    {
        return _motorControlService.TrySendStepSizePacket(PanMotorStepSize, TiltMotorStepSize, out error);
    }

    private void ApplyMotorButtonStateToUi(MotorButtonMask buttons)
    {
        if ((buttons & MotorButtonMask.Center) == MotorButtonMask.Center)
        {
            _motorPan = 0;
            _motorTilt = 0;
        }
        else
        {
            if ((buttons & MotorButtonMask.Left) == MotorButtonMask.Left)
            {
                _motorPan = Math.Clamp(_motorPan - PanMotorStepSize, 0, MotorPanLimitDegrees);
            }

            if ((buttons & MotorButtonMask.Right) == MotorButtonMask.Right)
            {
                _motorPan = Math.Clamp(_motorPan + PanMotorStepSize, 0, MotorPanLimitDegrees);
            }

            if ((buttons & MotorButtonMask.Up) == MotorButtonMask.Up)
            {
                _motorTilt = Math.Clamp(_motorTilt + TiltMotorStepSize, 0, MotorTiltLimitDegrees);
            }

            if ((buttons & MotorButtonMask.Down) == MotorButtonMask.Down)
            {
                _motorTilt = Math.Clamp(_motorTilt - TiltMotorStepSize, 0, MotorTiltLimitDegrees);
            }
        }

        _panMotorPositionDegrees = _motorPan;
        _tiltMotorPositionDegrees = _motorTilt;

        OnPropertyChanged(nameof(MotorPanText));
        OnPropertyChanged(nameof(MotorTiltText));
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(TiltMotorPositionText));
    }

    private static bool TryMapDirectionToButton(string direction, out MotorButtonMask buttons)
    {
        buttons = direction switch
        {
            "Left" => MotorButtonMask.Right,
            "Right" => MotorButtonMask.Left,
            "Up" => MotorButtonMask.Up,
            "Down" => MotorButtonMask.Down,
            "Center" => MotorButtonMask.Center,
            _ => MotorButtonMask.None
        };

        return buttons != MotorButtonMask.None;
    }

    /// <summary>
    /// ViewModel 공통 속성 변경 도우미 메서드다.
    /// 값이 실제로 바뀐 경우에만 PropertyChanged를 발생시켜 불필요한 화면 갱신을 줄인다.
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
/// 상황 분석 영역에 표시할 분석 문장 한 줄을 나타낸다.
/// </summary>
public sealed record AnalysisItem(string Time, string Message)
{
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// 시스템 로그 영역에 표시할 주요 상태 변경 항목 한 줄을 나타낸다.
/// </summary>
public sealed record SystemLogItem(string Time, string Message)
{
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

public enum MotorStepAxis
{
    Both,
    Pan,
    Tilt
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

