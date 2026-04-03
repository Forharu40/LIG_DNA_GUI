using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 GUI의 상태와 버튼 동작을 담당하는 뷰모델이다.
/// 실제 장비가 연결되면 이 뷰모델이 장비 상태와 분석 결과를 받아 화면에 반영한다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // 상단 좌측 큰 화면에 EO를 둘지, IR을 둘지 결정한다.
    private bool _isEoPrimary = true;

    // 주요 장비 상태 값이다. 버튼 클릭으로 즉시 바뀌며 상태창과 로그에 반영된다.
    private bool _isPoweredOn = true;
    private bool _isTrackingEnabled = true;
    private bool _isMotorEnabled = true;
    private bool _isElectronicZoomEnabled;

    // 모드와 탐지 프로필은 순환 방식으로 바꾸기 때문에 인덱스로 관리한다.
    private int _modeIndex = 2;
    private int _detectionProfileIndex;

    // 전자 줌은 실제 광학 줌이 아니라 좌측 큰 화면의 표시 배율만 확대한다.
    private double _zoomLevel = 1.0;

    // 밝기와 대조비는 추후 실제 영상 렌더러 파라미터와 연결할 수 있도록 보관한다.
    private double _brightness = 52;
    private double _contrast = 58;

    // EO 카메라 프레임은 코드 비하인드에서 업데이트되고, 화면 위치에 따라 크게 또는 작게 보인다.
    private ImageSource? _eoFrame;

    private readonly string[] _modes = ["기체고정", "수동모드", "추적모드"];
    private readonly string[] _detectionProfiles = ["드론 탐지", "고정익 탐지", "사람 탐지", "복합 탐지"];

    public MainViewModel()
    {
        // VLM 결과창은 분석 결과와 추적 요약을 보여준다.
        VlmResults = new ObservableCollection<VlmResultItem>
        {
            new("10:05:00", "VLM", "북동측 500m 지점에서 소형 쿼드콥터 3기와 고정익 1기를 식별했습니다."),
            new("10:05:04", "TRACK", "FW-01 경로는 북동측에서 남서측으로 내려오는 방향입니다."),
            new("10:05:08", "ANALYSIS", "기만성 선회 패턴이 포함되어 있어 우선 추적 대상으로 유지합니다."),
        };

        // 시스템 로그창은 버튼 조작, 카메라 상태, 분석 파이프라인 상태를 보여준다.
        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "SYSTEM", "GUI 초기화가 완료되었습니다."),
            new("10:05:01", "VIDEO", "EO 카메라 연결을 시도하는 중입니다."),
        };

        // 탐지 오버레이는 나중에 실제 detector/tracker 결과로 교체될 자리다.
        PrimaryOverlays = new ObservableCollection<OverlayItem>
        {
            new(610, 142, 144, 86, "Fixed-wing UAV"),
            new(402, 210, 118, 72, "Quadcopter"),
            new(272, 152, 128, 78, "Quadcopter"),
        };

        SecondaryOverlays = new ObservableCollection<OverlayItem>
        {
            new(118, 84, 86, 48, "IR target"),
        };

        // 우측 패널 하단 카드 영역은 각 기능이 어떤 상태인지 짧게 요약한다.
        FeatureSummaries = new ObservableCollection<FeatureSummaryItem>
        {
            new("모드", "현재 모드는 추적모드입니다."),
            new("탐지", "드론 탐지 프로필이 활성화되어 있습니다."),
            new("전자 줌", "좌측 큰 화면에서만 표시 배율을 키웁니다."),
            new("밝기/대조비", "현재 장면 대비에 맞춰 미세 조정 가능합니다."),
        };

        // 각 버튼은 기능별로 분리해서 연결해 두면 이후 실제 장비 API를 붙이기 쉽다.
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

    public ObservableCollection<OverlayItem> PrimaryOverlays { get; }

    public ObservableCollection<OverlayItem> SecondaryOverlays { get; }

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

    public ICommand EmergencyStopCommand { get; }

    public string LargeFeedTitle => _isEoPrimary ? "EO" : "IR";

    public string LargeFeedSubtitle => _isEoPrimary
        ? "주 화면: 노트북 카메라 테스트 영상"
        : "주 화면: 적외선 분석 화면 자리";

    public string SmallFeedTitle => _isEoPrimary ? "IR" : "EO";

    public string SmallFeedSubtitle => _isEoPrimary
        ? "보조 화면: 적외선 입력 자리"
        : "보조 화면: EO 입력";

    public string CurrentMode => _modes[_modeIndex];

    public string CurrentDetectionProfile => _detectionProfiles[_detectionProfileIndex];

    public string PowerStatus => _isPoweredOn ? "ON" : "OFF";

    public string MotorStatus => _isMotorEnabled ? "Motor ON" : "Motor OFF";

    public string TrackingStatus => _isTrackingEnabled ? "Tracking ON" : "Tracking OFF";

    public string ElectronicZoomStatus => IsElectronicZoomEnabled ? $"E-Zoom ON / {ZoomLevelText}" : "E-Zoom OFF";

    public string ZoomLevelText => $"{_zoomLevel:0.0}x";

    public double LargeFeedScale => _isElectronicZoomEnabled ? _zoomLevel : 1.0;

    // EO 프레임은 어느 창에 배치되든 같은 소스 데이터를 사용한다.
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

    public string BrightnessText => $"{Brightness:0}%";

    public string ContrastText => $"{Contrast:0}%";

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
        UpdateFeatureSummary("모드", $"현재 모드는 {CurrentMode}입니다.");
        AppendSystemLog("CTRL", $"모드가 {CurrentMode}로 변경되었습니다.");
    }

    // 탐지 버튼은 탐지 대상군을 순차적으로 바꾸는 샘플 동작이다.
    private void CycleDetection()
    {
        _detectionProfileIndex = (_detectionProfileIndex + 1) % _detectionProfiles.Length;
        OnPropertyChanged(nameof(CurrentDetectionProfile));
        UpdateFeatureSummary("탐지", $"{CurrentDetectionProfile} 프로필이 활성화되어 있습니다.");
        AppendSystemLog("CTRL", $"탐지 프로필이 {CurrentDetectionProfile}로 변경되었습니다.");
    }

    // On / Off 버튼은 전체 파이프라인의 운용 상태를 토글한다.
    private void TogglePower()
    {
        _isPoweredOn = !_isPoweredOn;
        OnPropertyChanged(nameof(PowerStatus));
        AppendSystemLog("POWER", $"시스템 전원 상태가 {PowerStatus}로 변경되었습니다.");
    }

    // 전자 줌은 좌측 큰 EO / IR 화면의 확대 배율만 바꾸는 표시 기능이다.
    private void ToggleElectronicZoom()
    {
        IsElectronicZoomEnabled = !IsElectronicZoomEnabled;
        UpdateFeatureSummary("전자 줌", IsElectronicZoomEnabled
            ? $"좌측 큰 화면 확대가 활성화되었습니다. 현재 배율은 {ZoomLevelText}입니다."
            : "좌측 큰 화면 확대가 비활성화되었습니다.");
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
        AppendSystemLog("MOTOR", $"모터 상태가 {MotorStatus}로 변경되었습니다.");
    }

    private void ToggleTracking()
    {
        _isTrackingEnabled = !_isTrackingEnabled;
        OnPropertyChanged(nameof(TrackingStatus));
        AppendSystemLog("TRACK", $"추적 상태가 {TrackingStatus}로 변경되었습니다.");
    }

    private void EmergencyStop()
    {
        _isPoweredOn = false;
        _isTrackingEnabled = false;
        _isMotorEnabled = false;
        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(TrackingStatus));
        OnPropertyChanged(nameof(MotorStatus));

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
/// 가운데 시스템 로그창에 쌓이는 운영 로그 데이터다.
/// </summary>
public sealed record SystemLogItem(string Time, string Category, string Message);

/// <summary>
/// 탐지된 객체를 나중에 박스와 라벨로 표현하기 위한 오버레이 모델이다.
/// </summary>
public sealed record OverlayItem(double X, double Y, double Width, double Height, string Label);

/// <summary>
/// 우측 기능 설명 영역에 보여줄 카드 데이터다.
/// </summary>
public sealed record FeatureSummaryItem(string Title, string Description);
