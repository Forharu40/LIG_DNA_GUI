using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 화면에서 사용하는 모든 상태와 명령을 관리한다.
/// 실제 카메라, 추적기, VLM이 연결되면 이 뷰모델의 더미 데이터 자리에
/// 실시간 데이터 바인딩이 들어가면 된다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // 상단 화면에서 어떤 피드가 크게 보이는지 관리한다.
    private bool _isEoPrimary = true;

    // 전원/모터/추적과 같이 버튼으로 즉시 바뀌는 운용 상태를 보관한다.
    private bool _isPoweredOn = true;
    private bool _isTrackingEnabled = true;
    private bool _isMotorEnabled = true;
    private bool _isElectronicZoomEnabled;

    // 모드와 탐지 프로필은 순환 방식으로 바꾸기 위해 인덱스로 관리한다.
    private int _modeIndex = 2;
    private int _detectionProfileIndex = 0;

    // 전자 줌은 광학 줌이 아니라 좌측 큰 화면의 표시 배율만 키운다.
    private double _zoomLevel = 1.0;

    // 화면 조정 값은 추후 실제 영상 렌더러 파라미터와 연결하기 쉽도록 double로 둔다.
    private double _brightness = 52;
    private double _contrast = 58;

    // 명령 입력창과 실행 결과를 화면 하단 중앙 패널에 표시한다.
    private string _commandInput = "FW-01 target summary 요청";
    private string _lastCommandResult = "대기 중";

    private readonly string[] _modes = ["기체고정", "수동모드", "추적모드"];
    private readonly string[] _detectionProfiles = ["드론 탐지", "고정익 탐지", "사람 탐지", "복합 탐지"];

    public MainViewModel()
    {
        // 좌측 하단 VLM 결과창에 표시할 요약 결과 목록이다.
        VlmResults = new ObservableCollection<VlmResultItem>
        {
            new("10:05:00", "VLM", "북동측 500m 지점에서 소형 쿼드콥터 3기와 고정익 1기를 식별했습니다."),
            new("10:05:04", "TRACK", "FW-01 경로는 북동측에서 남서측으로 내려오는 방향입니다."),
            new("10:05:08", "ANALYSIS", "기만성 선회 패턴이 포함되어 있어 우선 추적 대상으로 유지합니다."),
            new("10:05:12", "SYS", "EO/IR 입력과 객체 추적 상태가 동기화되어 있습니다."),
        };

        // 우측 기능 패널의 카드 요약이다.
        FeatureSummaries = new ObservableCollection<FeatureSummaryItem>
        {
            new("모드", "현재 모드는 추적모드입니다."),
            new("탐지", "드론 탐지 프로필이 활성화되어 있습니다."),
            new("전자 줌", "좌측 큰 화면에서만 표시 배율을 키웁니다."),
            new("밝기/대조비", "현재 장면 대비에 맞춰 미세 조정 가능합니다."),
        };

        // 명령은 각 버튼이 무엇을 바꾸는지 처음 보는 사람도 이해할 수 있게 분리했다.
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        CycleModeCommand = new RelayCommand(_ => CycleMode());
        CycleDetectionCommand = new RelayCommand(_ => CycleDetection());
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        ToggleElectronicZoomCommand = new RelayCommand(_ => ToggleElectronicZoom());
        IncreaseZoomCommand = new RelayCommand(_ => IncreaseZoom(), _ => IsElectronicZoomEnabled);
        DecreaseZoomCommand = new RelayCommand(_ => DecreaseZoom(), _ => IsElectronicZoomEnabled);
        ToggleMotorCommand = new RelayCommand(_ => ToggleMotor());
        ToggleTrackingCommand = new RelayCommand(_ => ToggleTracking());
        SendCommandInputCommand = new RelayCommand(_ => SendCommandInput());
        ClearCommandInputCommand = new RelayCommand(_ => ClearCommandInput());
        EmergencyStopCommand = new RelayCommand(_ => EmergencyStop());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VlmResultItem> VlmResults { get; }

    public ObservableCollection<FeatureSummaryItem> FeatureSummaries { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand CycleModeCommand { get; }

    public ICommand CycleDetectionCommand { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand ToggleElectronicZoomCommand { get; }

    public ICommand IncreaseZoomCommand { get; }

    public ICommand DecreaseZoomCommand { get; }

    public ICommand ToggleMotorCommand { get; }

    public ICommand ToggleTrackingCommand { get; }

    public ICommand SendCommandInputCommand { get; }

    public ICommand ClearCommandInputCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public string MissionTitle => "LIG DNA GUI";

    public string HeaderSummary => "상단 EO / IR 화면, 하단 VLM 결과, 명령 입력, 기능 제어로 구성된 운용 콘솔";

    public string LargeFeedTitle => _isEoPrimary ? "EO" : "IR";

    public string LargeFeedSubtitle => _isEoPrimary
        ? "주 화면: 주간 전자광학 영상"
        : "주 화면: 열상 적외선 영상";

    public string SmallFeedTitle => _isEoPrimary ? "IR" : "EO";

    public string SmallFeedSubtitle => _isEoPrimary
        ? "보조 화면: 적외선 검증 영상"
        : "보조 화면: 전자광학 검증 영상";

    public string SwapButtonLabel => "화면 스왑";

    public string CurrentMode => _modes[_modeIndex];

    public string CurrentDetectionProfile => _detectionProfiles[_detectionProfileIndex];

    public string PowerStatus => _isPoweredOn ? "ON" : "OFF";

    public string MotorStatus => _isMotorEnabled ? "Motor ON" : "Motor OFF";

    public string TrackingStatus => _isTrackingEnabled ? "Tracking ON" : "Tracking OFF";

    public string ElectronicZoomStatus => IsElectronicZoomEnabled ? $"E-Zoom ON / {ZoomLevelText}" : "E-Zoom OFF";

    public string ZoomLevelText => $"{_zoomLevel:0.0}x";

    public double LargeFeedScale => _isElectronicZoomEnabled ? _zoomLevel : 1.0;

    public bool IsElectronicZoomEnabled
    {
        get => _isElectronicZoomEnabled;
        private set
        {
            if (SetProperty(ref _isElectronicZoomEnabled, value))
            {
                OnPropertyChanged(nameof(ElectronicZoomStatus));
                OnPropertyChanged(nameof(LargeFeedScale));
                RaiseZoomCommands();
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value))
            {
                OnPropertyChanged(nameof(BrightnessText));
                AddOrReplaceSystemMessage("DISPLAY", $"밝기 값이 {value:0}%로 조정되었습니다.");
            }
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            if (SetProperty(ref _contrast, value))
            {
                OnPropertyChanged(nameof(ContrastText));
                AddOrReplaceSystemMessage("DISPLAY", $"대조비 값이 {value:0}%로 조정되었습니다.");
            }
        }
    }

    public string BrightnessText => $"{Brightness:0}%";

    public string ContrastText => $"{Contrast:0}%";

    public string CommandInput
    {
        get => _commandInput;
        set => SetProperty(ref _commandInput, value);
    }

    public string LastCommandResult
    {
        get => _lastCommandResult;
        private set => SetProperty(ref _lastCommandResult, value);
    }

    // 화면 스왑은 좌우 비율을 바꾸지 않고 어느 화면을 크게 보여줄지만 교체한다.
    private void SwapFeeds()
    {
        _isEoPrimary = !_isEoPrimary;
        OnPropertyChanged(nameof(LargeFeedTitle));
        OnPropertyChanged(nameof(LargeFeedSubtitle));
        OnPropertyChanged(nameof(SmallFeedTitle));
        OnPropertyChanged(nameof(SmallFeedSubtitle));

        AddResult("UI", $"{LargeFeedTitle} 화면이 확대 영역으로 전환되었습니다.");
    }

    // 모드 버튼은 세 가지 운용 모드를 순환한다.
    private void CycleMode()
    {
        _modeIndex = (_modeIndex + 1) % _modes.Length;
        OnPropertyChanged(nameof(CurrentMode));
        UpdateFeatureSummary("모드", $"현재 모드는 {CurrentMode}입니다.");
        AddResult("CTRL", $"모드가 {CurrentMode}로 변경되었습니다.");
    }

    // 탐지 버튼은 탐지 대상군을 순차적으로 바꾸는 샘플 동작이다.
    private void CycleDetection()
    {
        _detectionProfileIndex = (_detectionProfileIndex + 1) % _detectionProfiles.Length;
        OnPropertyChanged(nameof(CurrentDetectionProfile));
        UpdateFeatureSummary("탐지", $"{CurrentDetectionProfile} 프로필이 활성화되어 있습니다.");
        AddResult("CTRL", $"탐지 프로필이 {CurrentDetectionProfile}로 변경되었습니다.");
    }

    // On / Off 버튼은 전체 파이프라인의 운용 상태를 토글한다.
    private void TogglePower()
    {
        _isPoweredOn = !_isPoweredOn;
        OnPropertyChanged(nameof(PowerStatus));
        AddResult("POWER", $"시스템 전원 상태가 {PowerStatus}로 변경되었습니다.");
    }

    // 전자 줌은 실제 카메라 제어가 아니라 좌측 큰 화면의 렌더 배율만 바꾼다.
    private void ToggleElectronicZoom()
    {
        IsElectronicZoomEnabled = !IsElectronicZoomEnabled;
        AddResult("DISPLAY", IsElectronicZoomEnabled
            ? $"전자 줌이 켜졌습니다. 현재 배율은 {ZoomLevelText}입니다."
            : "전자 줌이 꺼졌습니다.");
    }

    private void IncreaseZoom()
    {
        _zoomLevel = Math.Min(3.0, _zoomLevel + 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AddResult("DISPLAY", $"전자 줌 배율이 {ZoomLevelText}로 증가했습니다.");
    }

    private void DecreaseZoom()
    {
        _zoomLevel = Math.Max(1.0, _zoomLevel - 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AddResult("DISPLAY", $"전자 줌 배율이 {ZoomLevelText}로 감소했습니다.");
    }

    private void ToggleMotor()
    {
        _isMotorEnabled = !_isMotorEnabled;
        OnPropertyChanged(nameof(MotorStatus));
        AddResult("MOTOR", $"모터 상태가 {MotorStatus}로 변경되었습니다.");
    }

    private void ToggleTracking()
    {
        _isTrackingEnabled = !_isTrackingEnabled;
        OnPropertyChanged(nameof(TrackingStatus));
        AddResult("TRACK", $"추적 상태가 {TrackingStatus}로 변경되었습니다.");
    }

    // 명령 입력창은 추후 LLM Agent 또는 제어 서버로 보낼 프롬프트/명령의 자리다.
    private void SendCommandInput()
    {
        if (string.IsNullOrWhiteSpace(CommandInput))
        {
            LastCommandResult = "입력된 명령이 없습니다.";
            return;
        }

        LastCommandResult = $"전송 완료: {CommandInput}";
        AddResult("CMD", CommandInput);
    }

    private void ClearCommandInput()
    {
        CommandInput = string.Empty;
        LastCommandResult = "입력창이 비워졌습니다.";
    }

    private void EmergencyStop()
    {
        _isPoweredOn = false;
        _isTrackingEnabled = false;
        _isMotorEnabled = false;
        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(TrackingStatus));
        OnPropertyChanged(nameof(MotorStatus));

        AddResult("SAFE", "비상 정지가 실행되어 모든 핵심 기능이 정지되었습니다.");
        MessageBox.Show("비상 정지가 실행되었습니다.\n핵심 기능을 안전 모드로 전환합니다.", "Emergency Stop");
    }

    private void AddResult(string category, string message)
    {
        VlmResults.Insert(0, new VlmResultItem(DateTime.Now.ToString("HH:mm:ss"), category, message));
    }

    private void AddOrReplaceSystemMessage(string category, string message)
    {
        if (VlmResults.Count > 0 && VlmResults[0].Category == category)
        {
            VlmResults[0] = new VlmResultItem(DateTime.Now.ToString("HH:mm:ss"), category, message);
            return;
        }

        AddResult(category, message);
    }

    private void UpdateFeatureSummary(string title, string description)
    {
        for (var index = 0; index < FeatureSummaries.Count; index++)
        {
            if (FeatureSummaries[index].Title == title)
            {
                FeatureSummaries[index] = new FeatureSummaryItem(title, description);
                return;
            }
        }
    }

    private void RaiseZoomCommands()
    {
        if (IncreaseZoomCommand is RelayCommand increaseCommand)
        {
            increaseCommand.RaiseCanExecuteChanged();
        }

        if (DecreaseZoomCommand is RelayCommand decreaseCommand)
        {
            decreaseCommand.RaiseCanExecuteChanged();
        }
    }

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
/// 좌측 하단 VLM 결과 출력창에 한 줄씩 보여줄 데이터다.
/// </summary>
public sealed record VlmResultItem(string Time, string Category, string Message);

/// <summary>
/// 우측 기능 설명 영역에 보여줄 카드 데이터다.
/// </summary>
public sealed record FeatureSummaryItem(string Title, string Description);
