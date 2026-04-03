using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 GUI의 상태와 버튼 동작을 관리한다.
/// 실제 장비가 연결되면 이 뷰모델이 장비 상태, 영상 상태, 분석 결과를 받아 화면에 반영한다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // 상단 왼쪽 큰 화면에 EO를 둘지, IR을 둘지 결정한다.
    private bool _isEoPrimary = true;

    // 버튼으로 바뀌는 주요 장비 상태값이다.
    private bool _isPoweredOn = true;
    private bool _isTrackingEnabled = true;
    private bool _isMotorEnabled = true;
    private bool _isElectronicZoomEnabled;

    // 모드와 탐지 프로필은 순환 방식으로 바뀌기 때문에 인덱스로 관리한다.
    private int _modeIndex = 2;
    private int _detectionProfileIndex;

    // 전자 줌은 실제 광학 줌이 아니라 큰 화면 안의 영상 배율만 키운다.
    private double _zoomLevel = 1.0;

    // 밝기와 대조비는 추후 실제 렌더러 파라미터와 연결할 수 있도록 보관한다.
    private double _brightness = 52;
    private double _contrast = 58;

    // EO 카메라 프레임은 코드 비하인드에서 주입받아 화면에 바인딩한다.
    private ImageSource? _eoFrame;

    private readonly string[] _modes = ["기체고정", "수동모드", "추적모드"];
    private readonly string[] _detectionProfiles = ["드론 탐지", "고정익 탐지", "사람 탐지", "복합 탐지"];

    public MainViewModel()
    {
        // VLM 결과창은 분석 결과 요약을 표시한다.
        VlmResults = new ObservableCollection<VlmResultItem>
        {
            new("10:05:00", "VLM", "북동측 500m 지점에서 소형 쿼드콥터 3기와 고정익 1기를 식별했습니다."),
            new("10:05:04", "TRACK", "FW-01 경로는 북동측에서 남서측으로 내려오는 방향입니다."),
            new("10:05:08", "ANALYSIS", "기만성 선회 패턴이 포함되어 있어 우선 추적 대상으로 유지합니다."),
        };

        // 시스템 로그창은 버튼 조작, 카메라 상태, 내부 동작 로그를 보여준다.
        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "SYSTEM", "GUI 초기화가 완료되었습니다."),
            new("10:05:01", "VIDEO", "EO 카메라 연결을 시도하는 중입니다."),
        };

        // 버튼 동작은 기능별로 분리해 두면 나중에 실제 장비 API를 붙이기 쉽다.
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        CycleModeCommand = new RelayCommand(_ => CycleMode());
        CycleDetectionCommand = new RelayCommand(_ => CycleDetection());
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        ToggleElectronicZoomCommand = new RelayCommand(_ => ToggleElectronicZoom());
        IncreaseZoomCommand = new RelayCommand(_ => IncreaseZoom(), _ => IsElectronicZoomEnabled);
        DecreaseZoomCommand = new RelayCommand(_ => DecreaseZoom(), _ => IsElectronicZoomEnabled);
        ToggleMotorCommand = new RelayCommand(_ => ToggleMotor());
        ToggleTrackingCommand = new RelayCommand(_ => ToggleTracking());
        EmergencyStopCommand = new RelayCommand(_ => EmergencyStop());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VlmResultItem> VlmResults { get; }

    public ObservableCollection<SystemLogItem> SystemLogs { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand CycleModeCommand { get; }

    public ICommand CycleDetectionCommand { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand ToggleElectronicZoomCommand { get; }

    public ICommand IncreaseZoomCommand { get; }

    public ICommand DecreaseZoomCommand { get; }

    public ICommand ToggleMotorCommand { get; }

    public ICommand ToggleTrackingCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public string LargeFeedTitle => _isEoPrimary ? "EO" : "IR";

    public string LargeFeedSubtitle => _isEoPrimary
        ? "주 화면: 노트북 카메라 테스트 영상"
        : "주 화면: 적외선 화면 자리";

    public string SmallFeedTitle => _isEoPrimary ? "IR" : "EO";

    public string SmallFeedSubtitle => _isEoPrimary
        ? "보조 화면: 적외선 입력 자리"
        : "보조 화면: EO 입력";

    public string CurrentMode => _modes[_modeIndex];

    public string CurrentModeStatus => $"현재 모드: {CurrentMode}";

    public string CurrentDetectionProfile => _detectionProfiles[_detectionProfileIndex];

    public string CurrentDetectionStatus => $"탐지 프로필: {CurrentDetectionProfile}";

    public string PowerStatus => _isPoweredOn ? "ON" : "OFF";

    public string PowerStatusText => $"전원 상태: {PowerStatus}";

    public string MotorStatus => _isMotorEnabled ? "ON" : "OFF";

    public string MotorStatusText => $"모터 상태: {MotorStatus}";

    public string TrackingStatus => _isTrackingEnabled ? "ON" : "OFF";

    public string TrackingStatusText => $"추적 상태: {TrackingStatus}";

    public string ElectronicZoomStatus => IsElectronicZoomEnabled ? $"전자 줌: ON / {ZoomLevelText}" : "전자 줌: OFF";

    public string ZoomLevelText => $"{_zoomLevel:0.0}x";

    // 전자 줌은 영상 안에서만 커지도록 이미지 자체에 적용한다.
    public double LargeFeedScale => _isElectronicZoomEnabled ? _zoomLevel : 1.0;

    // EO 프레임은 EO가 어느 창에 배치되든 같은 소스를 사용한다.
    public ImageSource? PrimaryFeedImage => _isEoPrimary ? _eoFrame : null;

    public ImageSource? SecondaryFeedImage => _isEoPrimary ? null : _eoFrame;

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

    public string BrightnessText => $"밝기: {Brightness:0}%";

    public string ContrastText => $"대조비: {Contrast:0}%";

    /// <summary>
    /// 코드 비하인드에서 새 카메라 프레임이 들어오면 이 메서드로 뷰모델에 반영한다.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(PrimaryFeedImage));
        OnPropertyChanged(nameof(SecondaryFeedImage));
    }

    /// <summary>
    /// 외부 서비스에서 발생한 로그를 시스템 로그창에 추가한다.
    /// </summary>
    public void AppendSystemLog(string category, string message)
    {
        SystemLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), category, message));
    }

    // 화면 스왑은 EO와 IR 중 어떤 화면을 크게 보여줄지만 바꾼다.
    private void SwapFeeds()
    {
        _isEoPrimary = !_isEoPrimary;
        OnPropertyChanged(nameof(LargeFeedTitle));
        OnPropertyChanged(nameof(LargeFeedSubtitle));
        OnPropertyChanged(nameof(SmallFeedTitle));
        OnPropertyChanged(nameof(SmallFeedSubtitle));
        OnPropertyChanged(nameof(PrimaryFeedImage));
        OnPropertyChanged(nameof(SecondaryFeedImage));

        AppendSystemLog("UI", $"{LargeFeedTitle} 화면이 큰 화면으로 전환되었습니다.");
    }

    // 모드 버튼은 운용 모드를 순환시킨다.
    private void CycleMode()
    {
        _modeIndex = (_modeIndex + 1) % _modes.Length;
        OnPropertyChanged(nameof(CurrentMode));
        OnPropertyChanged(nameof(CurrentModeStatus));
        AppendSystemLog("CTRL", $"모드가 {CurrentMode}로 변경되었습니다.");
    }

    // 탐지 버튼은 탐지 프로필을 순차적으로 바꾼다.
    private void CycleDetection()
    {
        _detectionProfileIndex = (_detectionProfileIndex + 1) % _detectionProfiles.Length;
        OnPropertyChanged(nameof(CurrentDetectionProfile));
        OnPropertyChanged(nameof(CurrentDetectionStatus));
        AppendSystemLog("CTRL", $"탐지 프로필이 {CurrentDetectionProfile}로 변경되었습니다.");
    }

    // On / Off 버튼은 전체 파이프라인의 운용 상태를 토글한다.
    private void TogglePower()
    {
        _isPoweredOn = !_isPoweredOn;
        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(PowerStatusText));
        AppendSystemLog("POWER", $"시스템 전원 상태가 {PowerStatus}로 변경되었습니다.");
    }

    // 전자 줌은 큰 화면 안의 영상 배율만 바꾸는 표시 기능이다.
    private void ToggleElectronicZoom()
    {
        IsElectronicZoomEnabled = !IsElectronicZoomEnabled;
        AppendSystemLog("DISPLAY", IsElectronicZoomEnabled
            ? $"전자 줌이 켜졌습니다. 현재 배율은 {ZoomLevelText}입니다."
            : "전자 줌이 꺼졌습니다.");
    }

    private void IncreaseZoom()
    {
        _zoomLevel = Math.Min(3.0, _zoomLevel + 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AppendSystemLog("DISPLAY", $"전자 줌 배율이 {ZoomLevelText}로 증가했습니다.");
    }

    private void DecreaseZoom()
    {
        _zoomLevel = Math.Max(1.0, _zoomLevel - 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AppendSystemLog("DISPLAY", $"전자 줌 배율이 {ZoomLevelText}로 감소했습니다.");
    }

    private void ToggleMotor()
    {
        _isMotorEnabled = !_isMotorEnabled;
        OnPropertyChanged(nameof(MotorStatus));
        OnPropertyChanged(nameof(MotorStatusText));
        AppendSystemLog("MOTOR", $"모터 상태가 {MotorStatus}로 변경되었습니다.");
    }

    private void ToggleTracking()
    {
        _isTrackingEnabled = !_isTrackingEnabled;
        OnPropertyChanged(nameof(TrackingStatus));
        OnPropertyChanged(nameof(TrackingStatusText));
        AppendSystemLog("TRACK", $"추적 상태가 {TrackingStatus}로 변경되었습니다.");
    }

    private void EmergencyStop()
    {
        _isPoweredOn = false;
        _isTrackingEnabled = false;
        _isMotorEnabled = false;
        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(PowerStatusText));
        OnPropertyChanged(nameof(TrackingStatus));
        OnPropertyChanged(nameof(TrackingStatusText));
        OnPropertyChanged(nameof(MotorStatus));
        OnPropertyChanged(nameof(MotorStatusText));

        AppendSystemLog("SAFE", "비상 정지가 실행되어 모든 핵심 기능이 정지되었습니다.");
        MessageBox.Show("비상 정지가 실행되었습니다.\n핵심 기능을 안전 모드로 전환합니다.", "Emergency Stop");
    }

    private void AddOrReplaceSystemMessage(string category, string message)
    {
        if (SystemLogs.Count > 0 && SystemLogs[0].Category == category)
        {
            SystemLogs[0] = new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), category, message);
            return;
        }

        AppendSystemLog(category, message);
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
/// 가운데 시스템 로그창에 쌓이는 운영 로그 데이터다.
/// </summary>
public sealed record SystemLogItem(string Time, string Category, string Message);
