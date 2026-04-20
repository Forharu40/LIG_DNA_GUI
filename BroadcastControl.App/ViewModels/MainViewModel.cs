using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;
using BroadcastControl.App.Services;

namespace BroadcastControl.App.ViewModels;

/// 메인 화면 상태, 모드, 위험 등급, 설정 패널, 줌/팬 값을 한 곳에서 관리함.
/// 실제 장비와 VLM 연결 시 이 ViewModel에 실시간 값을 넣어 같은 화면 구조를 유지함.
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    /// 모드, 위험 등급, 밝기/대조비, 전자 줌, 로그, 테마 버튼 상태를 관리하는 ViewModel임.
    // 전자 줌 미니맵은 현재 시야를 읽기 쉽도록 약간 크게 유지함.
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
    private string _currentMode = "자동";
    private string _selectedPrimaryTarget = "복합";
    private string _currentThreatLevel = "낮음";
    // 프로그램 시작 시 밝기 기본값은 중립값 50%로 시작함.
    private double _brightness = 50;
    private double _contrast = 50;
    private bool _isManualRecordingEnabled;
    private AppThemeMode _currentThemeMode;
    private double _zoomLevel = 1.0;
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;
    private int _motorPan;
    private int _motorTilt;

    // EO는 외부 UDP 영상, IR은 임시 노트북 카메라 영상을 사용함.
    // 프레임 수신 전에도 화면이 비지 않도록 EO/IR 기본 안내 이미지를 준비함.
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private readonly ImageSource _eoPlaceholderFrame = CreateCameraPlaceholderFrame("MEVA DEMO", Color.FromRgb(51, 94, 160));
    private readonly ImageSource _irPlaceholderFrame = CreateCameraPlaceholderFrame("IR TEMP", Color.FromRgb(192, 109, 40));

    public MainViewModel()
    {
        // 앱의 현재 테마를 읽어 설정창 버튼 상태와 맞춤.
        if (Application.Current is App app)
        {
            _currentThemeMode = app.CurrentThemeMode;
        }

        AnalysisItems = new ObservableCollection<AnalysisItem>
        {
            new("10:05:00", "시스템 초기화 단계에서는 기본 위험 등급을 낮음으로 유지합니다."),
            new("10:05:03", "현재 주 탐지체는 복합이며 운용자가 탐지 조건을 조정할 수 있습니다."),
            new("10:05:05", "VLM 고위험 분석 결과가 들어오기 전까지 경보 단계는 상승하지 않습니다."),
        };

        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "시스템 전원이 켜졌습니다."),
            new("10:05:02", "카메라 제어 모드가 자동으로 설정되었습니다."),
            new("10:05:04", "초기 위험 등급은 낮음으로 설정되었습니다."),
        };

        PrimaryTargets = new ReadOnlyCollection<string>(new[]
        {
            "복합",
            "공중 무기체계",
            "육상 무기체계",
            "해상 무기체계",
            "통신 장비",
            "비군사 표적",
        });

        // 화면의 모든 버튼은 Command 바인딩으로 연결하므로 생성자에서 한 번에 등록함.
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
        ResetBrightnessCommand = new RelayCommand(_ => Brightness = 50, _ => IsSystemPoweredOn);
        ResetContrastCommand = new RelayCommand(_ => Contrast = 50, _ => IsSystemPoweredOn);
        // 전자 줌 제목 버튼 클릭 시 기본 배율 x1.0으로 복귀함.
        ResetZoomCommand = new RelayCommand(_ => ZoomLevel = 1.0, _ => CanUseZoomControls);
        ToggleManualRecordingCommand = new RelayCommand(_ => ToggleManualRecording(), _ => IsManualMode);
        SetThemeCommand = new RelayCommand(SetTheme);
        SaveSystemLogsCommand = new RelayCommand(_ => SaveSystemLogsToDesktop());
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        MoveMotorCommand = new RelayCommand(MoveMotor, _ => CanUseMotorControls);
        InitializeJetsonBridge();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AnalysisItem> AnalysisItems { get; }

    public ObservableCollection<SystemLogItem> SystemLogs { get; }

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

    public ICommand SaveSystemLogsCommand { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand MoveMotorCommand { get; }

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
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomControls));
                RaiseAllCommandStates();
            }
        }
    }

    public bool IsEoPrimary => _isEoPrimary;

    // 상단 전원 버튼은 프로그램 종료 버튼으로 사용함.
    public string PowerButtonText => "전원 종료";

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
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(ShowZoomMiniMap));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"카메라 모드: {CurrentMode}";

    // 현재 모드 버튼만 선명하게 보여 별도 텍스트 없이 상태를 읽게 함.
    public double AutoModeOpacity => CurrentMode == "자동" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "수동" ? 1.0 : 0.35;

    // 위험 등급 상승 시 향후 자동 녹화 표시와 연결할 상태값임.
    // 녹화 표시등은 위험 상황 자동 녹화와 수동 녹화를 모두 반영함.
    public bool IsRecordingActive => CurrentThreatLevel != "낮음" || IsManualRecordingEnabled;

    public Brush RecordingIndicatorBrush => IsRecordingActive ? RecordingOnBrush : RecordingOffBrush;

    public Brush RecordingTextBrush => IsRecordingActive ? RecordingOnBrush : RecordingTextOffBrush;

    public double RecordingIndicatorOpacity => IsRecordingActive ? 1.0 : 0.42;

    public bool IsManualMode => IsSystemPoweredOn && CurrentMode == "수동";

    public bool CanSelectAutoMode => IsSystemPoweredOn && CurrentMode != "자동";

    public bool CanSelectManualMode => IsSystemPoweredOn && CurrentMode != "수동";

    public bool CanUseMotorControls => IsManualMode;

    public bool CanUseZoomControls => IsManualMode;

    public double ManualRecordingButtonOpacity => IsManualMode ? 1.0 : 0.0;

    public bool IsDarkThemeActive => _currentThemeMode == AppThemeMode.Dark;

    public bool IsLightThemeActive => _currentThemeMode == AppThemeMode.Light;

    public double DarkThemeButtonOpacity => IsDarkThemeActive ? 1.0 : 0.55;

    public double LightThemeButtonOpacity => IsLightThemeActive ? 1.0 : 0.55;

    // 수동 녹화는 수동 모드에서만 켜고 끌 수 있게 제한함.
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

    public string ManualRecordingButtonText => IsManualRecordingEnabled ? "녹화 종료" : "녹화 시작";

    public string CurrentThreatLevel
    {
        get => _currentThreatLevel;
        private set
        {
            if (SetProperty(ref _currentThreatLevel, value))
            {
                OnPropertyChanged(nameof(CurrentThreatText));
                OnPropertyChanged(nameof(CurrentThreatBrush));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
            }
        }
    }

    public string CurrentThreatText => $"위험 등급: {CurrentThreatLevel}";

    public Brush CurrentThreatBrush => CurrentThreatLevel switch
    {
        "낮음" => LowThreatBrush,
        "중간" => MediumThreatBrush,
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
            }
        }
    }

    public string PrimaryTargetText => $"주 탐지체: {SelectedPrimaryTarget}";

    // 영상 위 라벨은 짧게 유지해 실제 화면을 덜 가리도록 함.
    public string EoTitle => "EO cam";

    public string IrTitle => "IR cam";

    public string EoSubtitle => "Jetson YOLO MEVA demo stream";

    public string IrSubtitle => "노트북 카메라 임시 입력";

    public ImageSource? LargeFeedImage => _isEoPrimary
        ? _eoFrame ?? _eoPlaceholderFrame
        : _irFrame ?? _irPlaceholderFrame;

    public ImageSource? InsetFeedImage => _isEoPrimary
        ? _irFrame ?? _irPlaceholderFrame
        : _eoFrame ?? _eoPlaceholderFrame;

    public string LargeFeedTitle => _isEoPrimary ? EoTitle : IrTitle;

    public string InsetFeedTitle => _isEoPrimary ? IrTitle : EoTitle;

    public string LargeFeedSubtitle => _isEoPrimary ? EoSubtitle : IrSubtitle;

    public string InsetFeedSubtitle => _isEoPrimary ? IrSubtitle : EoSubtitle;

    public double Brightness
    {
        get => _brightness;
        set
        {
            // 슬라이더 값 변경 시 표시 텍스트도 함께 갱신함.
            if (SetProperty(ref _brightness, value))
            {
                OnPropertyChanged(nameof(BrightnessText));
            }
        }
    }

    public string BrightnessText => $"밝기 {Brightness:0}%";

    public double Contrast
    {
        get => _contrast;
        set
        {
            // 대조비 숫자 표시와 실제 영상 보정 값을 동일하게 유지함.
            if (SetProperty(ref _contrast, value))
            {
                OnPropertyChanged(nameof(ContrastText));
            }
        }
    }

    public string ContrastText => $"대조비 {Contrast:0}%";

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            // 전자 줌은 과확대를 막기 위해 1.0~4.0 범위로 제한함.
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 기본 배율 복귀 시 화면 이동값도 중심으로 초기화함.
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

    public string ZoomLevelText => $"x{ZoomLevel:0.0}";

    public double LargeFeedScale => ZoomLevel;

    public double ZoomTransformX => _zoomPanX;

    public double ZoomTransformY => _zoomPanY;

    public bool ShowZoomMiniMap => CanUseZoomControls && ZoomLevel > 1.0;

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
            // 실제 화면 이동 방향과 미니맵 표시 방향을 맞추기 위해 좌표를 반전함.
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
            // 실제 화면 이동 방향과 미니맵 표시 방향을 맞추기 위해 좌표를 반전함.
            return (1.0 - normalized) * (MiniMapHeight - MiniMapViewportHeight);
        }
    }

    public string MotorPanText => $"모터 좌/우: {_motorPan}도";

    public string MotorTiltText => $"모터 상/하: {_motorTilt}도";

    /// <summary>
    /// EO 프레임을 화면에 반영함.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    /// <summary>
    /// 임시 IR 화면으로 쓰는 노트북 카메라 프레임을 반영함.
    /// EO/IR 스왑 상태에 따라 작은 화면 또는 큰 화면에 즉시 반영함.
    /// </summary>
    public void UpdateIrFrame(ImageSource? frame)
    {
        _irFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    public void UpdateDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        if (detections.Count == 0)
        {
            SelectedPrimaryTarget = "복합";
            CurrentThreatLevel = "낮음";
            return;
        }

        var primaryDetection = detections[0];
        SelectedPrimaryTarget = $"{primaryDetection.ClassName} object{primaryDetection.ObjectId}";
        CurrentThreatLevel = detections.Count >= 3 ? "높음" : "중간";
    }

    /// <summary>
    /// 카메라 뷰포트 크기를 받아 확대 이동 한계를 다시 계산함.
    /// </summary>
    public void UpdateViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(width, 1);
        _viewportHeight = Math.Max(height, 1);
        ClampZoomPan();
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// 확대 상태에서 마우스 드래그로 화면 위치를 이동함.
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
    /// 마우스 휠 입력으로 전자 줌 배율을 조금씩 조절함.
    /// </summary>
    public void AdjustZoomByWheel(double wheelSteps)
    {
        if (!CanUseZoomControls || Math.Abs(wheelSteps) < double.Epsilon)
        {
            return;
        }

        // 휠 한 칸마다 0.1배씩 조절해 슬라이더와 비슷한 감도로 맞춤.
        ZoomLevel += wheelSteps * 0.1;
    }

    public void AppendImportantLog(string message)
    {
        // 가장 최근 로그가 위에 오도록 맨 앞에 추가함.
        SystemLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        TrimCollection(SystemLogs, 8);
    }

    /// <summary>
    /// 시스템 로그를 바탕화면에 시간 기준 파일명으로 저장함.
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

            foreach (var log in SystemLogs)
            {
                builder.AppendLine($"[{log.Time}] {log.Message}");
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
            AppendImportantLog($"시스템 로그를 저장했습니다: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"시스템 로그 저장에 실패했습니다: {ex.Message}");
        }
    }

    /// <summary>
    /// 상단 전원 종료 버튼의 실제 동작임.
    /// 위험 등급이 높음일 때 실수로 프로그램을 닫지 못하게 막음.
    /// </summary>
    private void TogglePower()
    {
        // 위험 등급이 높음이면 운용 중인 프로그램 종료를 차단함.
        if (CurrentThreatLevel == "높음")
        {
            AppendImportantLog("위험 등급이 높음 상태여서 프로그램을 종료할 수 없습니다.");
            return;
        }

        Application.Current?.Shutdown();
    }

    /// <summary>
    /// 자동/수동 모드를 전환함.
    /// 자동 모드 복귀 시 수동 녹화, 모터 값, 줌 배율을 초기화함.
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

        if (!IsManualMode)
        {
            if (IsManualRecordingEnabled)
            {
                // 자동 모드 복귀 시 수동 녹화를 즉시 종료해 저장함.
                IsManualRecordingEnabled = false;
            }

            _motorPan = 0;
            _motorTilt = 0;
            ZoomLevel = 1.0;
            OnPropertyChanged(nameof(MotorPanText));
            OnPropertyChanged(nameof(MotorTiltText));
        }

        AppendImportantLog($"카메라 제어 모드가 {CurrentMode}으로 전환되었습니다.");
    }

    /// <summary>
    /// 수동 녹화 버튼 클릭 시 상태를 반전함.
    /// 실제 파일 저장 시작/종료는 MainWindow가 서비스와 연결해 처리함.
    /// </summary>
    private void ToggleManualRecording()
    {
        if (!IsManualMode)
        {
            return;
        }

        IsManualRecordingEnabled = !IsManualRecordingEnabled;
    }

    /// <summary>
    /// 설정창에서 앱 테마를 직접 바꾸는 명령 처리임.
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
        AppendImportantLog($"테마가 {(nextTheme == AppThemeMode.Dark ? "어두운 테마" : "밝은 테마")}로 변경되었습니다.");
    }

    /// <summary>
    /// 설정창에서 주 탐지체 선택 시 현재 선택 상태를 갱신함.
    /// 위험 등급은 VLM 분석 결과가 들어올 때만 바뀌도록 유지함.
    /// </summary>
    private void SelectPrimaryTarget(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string target)
        {
            return;
        }

        SelectedPrimaryTarget = target;
        AppendImportantLog($"주 탐지체가 {SelectedPrimaryTarget}으로 변경되었습니다.");
    }

    /// <summary>
    /// EO/IR 메인 화면과 작은 인셋 화면을 서로 교체함.
    /// 사용자가 작은 화면을 눌러 원하는 영상을 크게 볼 수 있게 함.
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

        AppendImportantLog($"{LargeFeedTitle}이(가) 메인 화면으로 전환되었습니다.");
    }

    /// <summary>
    /// 수동 모드에서 모터 방향 버튼 클릭 시 좌우/상하 각도를 변경함.
    /// 현재는 UI 시뮬레이션 값이며, 이후 실제 모터 제어 명령과 연결 가능함.
    /// </summary>
    private void MoveMotor(object? parameter)
    {
        if (!CanUseMotorControls || parameter is not string direction)
        {
            return;
        }

        switch (direction)
        {
            case "Left":
                _motorPan = Math.Clamp(_motorPan - 5, -90, 90);
                break;
            case "Right":
                _motorPan = Math.Clamp(_motorPan + 5, -90, 90);
                break;
            case "Up":
                _motorTilt = Math.Clamp(_motorTilt + 5, -45, 45);
                break;
            case "Down":
                _motorTilt = Math.Clamp(_motorTilt - 5, -45, 45);
                break;
        }

        OnPropertyChanged(nameof(MotorPanText));
        OnPropertyChanged(nameof(MotorTiltText));
    }

    /// <summary>
    /// 현재 줌 이동 값이 허용 범위를 넘지 않도록 보정함.
    /// 화면 크기나 줌 배율 변경 후 필요한 정리임.
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
    /// 전자 줌 미니맵 사각형의 크기와 위치 재계산을 UI에 알림.
    /// </summary>
    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    /// <summary>
    /// 모드, 전원, 줌 가능 여부 변경 시 버튼 활성 상태를 다시 계산함.
    /// 관련 명령 객체에 CanExecuteChanged를 한 번에 전달함.
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

    /// <summary>
    /// 여러 곳에서 반복 사용할 고정 브러시 생성.
    /// Freeze 처리로 성능과 메모리 사용을 안정화함.
    /// </summary>
    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 실제 카메라 프레임 수신 전 보여 줄 플레이스홀더 이미지를 생성함.
    /// UI 테스트 단계에서 카메라 영역이 비어 보이지 않도록 함.
    /// </summary>
    private static ImageSource CreateCameraPlaceholderFrame(string label, Color accentColor)
    {
        // 실제 입력 수신 전에도 카메라 배치 의도를 알 수 있도록 안내 프레임 생성.
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

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// ViewModel 공통 속성 변경 도우미임.
    /// 값이 실제로 바뀐 경우에만 PropertyChanged를 발생시켜 불필요한 갱신을 줄임.
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
/// 상황 분석 창에 표시할 분석 문장임.
/// </summary>
public sealed record AnalysisItem(string Time, string Message);

/// <summary>
/// 시스템 로그에 표시할 중요 상태 변화 항목임.
/// </summary>
public sealed record SystemLogItem(string Time, string Message);
