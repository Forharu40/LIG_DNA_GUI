using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 화면의 상태, 모드, 위험 등급, 설정 패널, 줌/팬 값을 한 곳에서 관리한다.
/// 실제 장비와 VLM이 연결되면 이 ViewModel에 실시간 값을 넣어 같은 화면 구조를 유지할 수 있다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// 메인 화면의 상태를 한 곳에서 관리하는 ViewModel이다.
    /// 모드, 위험 등급, 밝기/대조비, 전자 줌, 로그, 테마 버튼 상태까지 모두 이 클래스에 모여 있다.
    /// </summary>
    // 전자 줌 미니맵은 이전보다 아주 조금만 키워서 현재 시야를 읽기 쉽게 만든다.
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
    // 프로그램 시작 시 밝기 기본값은 항상 중립값인 50%로 시작한다.
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

    private ImageSource? _eoFrame;
    private readonly ImageSource _irPlaceholderFrame = CreateIrPlaceholderFrame();

    public MainViewModel()
    {
        // 앱이 이미 어떤 테마로 실행 중인지 확인해 설정창 버튼 상태를 맞춘다.
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

        // 화면의 모든 버튼은 Command 바인딩으로 연결되므로, 생성자에서 한 번에 등록한다.
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
        ResetBrightnessCommand = new RelayCommand(_ => Brightness = 50, _ => IsSystemPoweredOn);
        ResetContrastCommand = new RelayCommand(_ => Contrast = 50, _ => IsSystemPoweredOn);
        // 전자 줌 제목 버튼을 누르면 항상 기본 배율인 x1.0으로 복귀한다.
        ResetZoomCommand = new RelayCommand(_ => ZoomLevel = 1.0, _ => CanUseZoomControls);
        ToggleManualRecordingCommand = new RelayCommand(_ => ToggleManualRecording(), _ => IsManualMode);
        SetThemeCommand = new RelayCommand(SetTheme);
        SaveSystemLogsCommand = new RelayCommand(_ => SaveSystemLogsToDesktop());
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        MoveMotorCommand = new RelayCommand(MoveMotor, _ => CanUseMotorControls);
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

    // 상단 전원 버튼은 프로그램 종료 버튼으로 사용한다.
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

    // 현재 모드 버튼만 선명하게 보여서 별도 텍스트 없이도 상태를 바로 읽을 수 있게 한다.
    public double AutoModeOpacity => CurrentMode == "자동" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "수동" ? 1.0 : 0.35;

    // 위험 등급이 올라가면 이후 VLM 연동 시 자동 녹화가 시작될 수 있도록 상태 표시를 준비한다.
    // 녹화 표시등은 위험 상황 자동 녹화와 수동 녹화를 모두 반영한다.
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

    // 수동 녹화는 수동 모드일 때만 켜고 끌 수 있게 제한한다.
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

    // 영상 위 라벨은 짧게 유지해서 실제 화면을 덜 가리도록 한다.
    public string EoTitle => "EO cam";

    public string IrTitle => "IR cam";

    public string EoSubtitle => "노트북 카메라 입력";

    public string IrSubtitle => "IR 입력 연결 예정";

    public ImageSource? LargeFeedImage => _isEoPrimary ? _eoFrame : _irPlaceholderFrame;

    public ImageSource? InsetFeedImage => _isEoPrimary ? _irPlaceholderFrame : _eoFrame;

    public string LargeFeedTitle => _isEoPrimary ? EoTitle : IrTitle;

    public string InsetFeedTitle => _isEoPrimary ? IrTitle : EoTitle;

    public string LargeFeedSubtitle => _isEoPrimary ? EoSubtitle : IrSubtitle;

    public string InsetFeedSubtitle => _isEoPrimary ? IrSubtitle : EoSubtitle;

    public double Brightness
    {
        get => _brightness;
        set
        {
            // 슬라이더 값이 바뀌면 화면에 표시되는 텍스트도 바로 함께 갱신한다.
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
            // 대조비 역시 숫자 표시와 실제 영상 보정이 같은 값을 바라보게 유지한다.
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
            // 전자 줌은 과도하게 확대되지 않도록 1.0~4.0 범위로 제한한다.
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 기본 배율로 돌아오면 화면 이동값도 의미가 없어지므로 중심으로 복귀시킨다.
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
            // 실제 화면을 왼쪽/위쪽으로 끌어 이동할 때 미니맵 표시도 같은 방향으로 움직이도록 반전한다.
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
            // 실제 화면을 왼쪽/위쪽으로 끌어 이동할 때 미니맵 표시도 같은 방향으로 움직이도록 반전한다.
            return (1.0 - normalized) * (MiniMapHeight - MiniMapViewportHeight);
        }
    }

    public string MotorPanText => $"모터 좌/우: {_motorPan}도";

    public string MotorTiltText => $"모터 상/하: {_motorTilt}도";

    /// <summary>
    /// 웹캠 프레임을 EO 화면에 반영한다.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    /// <summary>
    /// 카메라 뷰포트 크기를 받아 확대 이동 한계를 다시 계산한다.
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
    /// 마우스 휠 입력으로 전자 줌 배율을 조금씩 조절한다.
    /// </summary>
    public void AdjustZoomByWheel(double wheelSteps)
    {
        if (!CanUseZoomControls || Math.Abs(wheelSteps) < double.Epsilon)
        {
            return;
        }

        // 휠 한 칸마다 0.1배씩 조절해서 슬라이더와 비슷한 감도로 맞춘다.
        ZoomLevel += wheelSteps * 0.1;
    }

    public void AppendImportantLog(string message)
    {
        // 가장 최근 로그가 위에 오도록 맨 앞에 넣는다.
        SystemLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        TrimCollection(SystemLogs, 8);
    }

    /// <summary>
    /// 시스템 로그를 바탕화면에 시간 기준 파일명으로 저장해 테스트 결과를 바로 확인할 수 있게 한다.
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
    /// 상단 전원 종료 버튼의 실제 동작이다.
    /// 위험 등급이 높음인 상태에서는 실수로 프로그램을 닫지 못하게 막는다.
    /// </summary>
    private void TogglePower()
    {
        // 위험 등급이 높음이면 운용 중인 프로그램을 종료하지 못하게 막는다.
        if (CurrentThreatLevel == "높음")
        {
            AppendImportantLog("위험 등급이 높음 상태여서 프로그램을 종료할 수 없습니다.");
            return;
        }

        Application.Current?.Shutdown();
    }

    /// <summary>
    /// 자동/수동 모드를 전환한다.
    /// 자동 모드로 돌아가면 수동 녹화, 모터 값, 줌 배율 같은 수동 전용 상태를 초기화한다.
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
                // 자동 모드로 돌아갈 때 수동 녹화는 즉시 종료되어 저장되도록 한다.
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
    /// 수동 녹화 버튼을 눌렀을 때 상태를 반전시킨다.
    /// 실제 파일 저장 시작/종료는 MainWindow가 서비스와 연결해서 처리한다.
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
    /// 설정창에서 앱 테마를 직접 바꿀 수 있게 한다.
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
    /// 설정창에서 주 탐지체를 선택했을 때 현재 선택 상태를 갱신한다.
    /// 위험 등급은 여기서 바꾸지 않고, 이후 VLM 분석 결과가 들어올 때만 바뀌게 둔다.
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
    /// EO/IR 메인 화면과 작은 인셋 화면을 서로 교체한다.
    /// 사용자는 작은 화면을 눌러 원하는 영상을 크게 볼 수 있다.
    /// </summary>
    private void SwapFeeds()
    {
        _isEoPrimary = !_isEoPrimary;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
        OnPropertyChanged(nameof(LargeFeedTitle));
        OnPropertyChanged(nameof(InsetFeedTitle));
        OnPropertyChanged(nameof(LargeFeedSubtitle));
        OnPropertyChanged(nameof(InsetFeedSubtitle));

        AppendImportantLog($"{LargeFeedTitle}이(가) 메인 화면으로 전환되었습니다.");
    }

    /// <summary>
    /// 수동 모드에서 모터 방향 버튼을 누를 때 좌우/상하 각도를 조금씩 변경한다.
    /// 현재는 UI 시뮬레이션 값이지만, 이후 실제 모터 제어 명령으로 연결할 수 있다.
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
    /// 현재 줌 이동 값이 허용 범위를 넘지 않도록 보정한다.
    /// 화면 크기나 줌 배율이 바뀐 뒤에는 항상 이 정리가 필요하다.
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
    /// 전자 줌 미니맵 사각형의 크기와 위치를 다시 계산하도록 UI에 알린다.
    /// </summary>
    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    /// <summary>
    /// 모드, 전원, 줌 가능 여부 등이 바뀌면 버튼 활성 상태도 다시 계산해야 한다.
    /// 그래서 관련 명령 객체 모두에게 CanExecuteChanged를 한 번에 전달한다.
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
    /// 여러 곳에서 반복 사용할 고정 브러시를 생성한다.
    /// Freeze해서 성능과 메모리 사용을 조금 더 안정적으로 만든다.
    /// </summary>
    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 실제 IR 장치가 아직 연결되지 않았을 때 대신 보여 줄 플레이스홀더 이미지를 생성한다.
    /// 덕분에 UI 테스트 단계에서도 IR 인셋이 비어 보이지 않는다.
    /// </summary>
    private static ImageSource CreateIrPlaceholderFrame()
    {
        // 실제 IR 카메라 입력 전에도 화면이 비어 보이지 않도록 임시 열화상 프레임을 만든다.
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(23, 28, 36),
                Color.FromRgb(73, 25, 24),
                new Point(0, 0),
                new Point(1, 1));

            dc.DrawRectangle(background, null, new Rect(0, 0, 320, 240));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 255, 134, 52)), null, new Point(220, 92), 46, 32);
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
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// ViewModel 공통 속성 변경 도우미이다.
    /// 값이 정말 바뀐 경우에만 PropertyChanged를 발생시켜 불필요한 화면 갱신을 줄인다.
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
/// 상황 분석 창에 표시할 분석 문장이다.
/// </summary>
public sealed record AnalysisItem(string Time, string Message);

/// <summary>
/// 시스템 로그에는 중요한 상태 변화만 기록한다.
/// </summary>
public sealed record SystemLogItem(string Time, string Message);
