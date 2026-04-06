using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 대시보드의 화면 상태와 버튼 동작을 관리한다.
/// 실제 장비가 연결되면 이 뷰모델이 장비 상태와 분석 결과를 받아 화면에 반영한다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private bool _isPoweredOn = true;
    private bool _isMotorEnabled = true;
    private bool _isTrackingEnabled = true;
    private bool _isElectronicZoomEnabled;

    private int _modeIndex = 2;
    private int _detectionCategoryIndex = 0;
    private double _zoomLevel = 1.0;
    private double _brightness = 52;
    private double _contrast = 58;

    private ImageSource? _eoFrame;

    private readonly string[] _modes = ["기체 고정", "수동", "추적"];
    private readonly string[] _detectionCategories = ["복합", "고정익", "회전익", "드론", "사람"];

    public MainViewModel()
    {
        // VLM 결과 패널에 표시할 더미 결과다.
        VlmResults = new ObservableCollection<VlmResultItem>
        {
            new("10:05:00", "VLM", "북동측 500m 지점에서 소형 비행체 복수 개체가 식별되었습니다."),
            new("10:05:04", "TRACK", "주요 표적은 북동측에서 남서측 방향으로 접근 중입니다."),
            new("10:05:08", "ANALYSIS", "현재 추적 우선순위는 FW-01로 유지됩니다."),
        };

        // 실시간 시스템 변경 로그 창에 표시할 로그다.
        SystemChangeLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "SYSTEM", "GUI 초기화가 완료되었습니다."),
            new("10:05:01", "VIDEO", "EO 카메라 연결을 시도하는 중입니다."),
        };

        // 버튼 동작은 기능별로 분리해 두면 나중에 실제 장비 API 연결이 쉽다.
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        ToggleMotorCommand = new RelayCommand(_ => ToggleMotor());
        ToggleTrackingCommand = new RelayCommand(_ => ToggleTracking());
        CycleModeCommand = new RelayCommand(_ => CycleMode());
        SelectModeCommand = new RelayCommand(SelectMode);
        CycleDetectionCommand = new RelayCommand(_ => CycleDetectionCategory());
        SelectDetectionCategoryCommand = new RelayCommand(SelectDetectionCategory);
        ToggleElectronicZoomCommand = new RelayCommand(_ => ToggleElectronicZoom());
        IncreaseZoomCommand = new RelayCommand(_ => IncreaseZoom(), _ => IsElectronicZoomEnabled);
        DecreaseZoomCommand = new RelayCommand(_ => DecreaseZoom(), _ => IsElectronicZoomEnabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VlmResultItem> VlmResults { get; }

    public ObservableCollection<SystemLogItem> SystemChangeLogs { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand ToggleMotorCommand { get; }

    public ICommand ToggleTrackingCommand { get; }

    public ICommand CycleModeCommand { get; }

    public ICommand SelectModeCommand { get; }

    public ICommand CycleDetectionCommand { get; }

    public ICommand SelectDetectionCategoryCommand { get; }

    public ICommand ToggleElectronicZoomCommand { get; }

    public ICommand IncreaseZoomCommand { get; }

    public ICommand DecreaseZoomCommand { get; }

    public string EoTitle => "EO 화면";

    public string IrTitle => "IR 화면";

    public string EoSubtitle => "실험용 노트북 카메라 입력";

    public string IrSubtitle => "IR 입력 또는 향후 열상 카메라 자리";

    public ImageSource? EoFeedImage => _eoFrame;

    public string CurrentMode => _modes[_modeIndex];

    public string CurrentModeStatus => $"현재 모드: {CurrentMode}";

    public string CurrentDetectionCategory => _detectionCategories[_detectionCategoryIndex];

    public string CurrentDetectionStatus => $"현재 탐지 대상: {CurrentDetectionCategory}";

    public string PowerStatus => _isPoweredOn ? "ON" : "OFF";

    public string PowerStatusText => $"시스템 전원: {PowerStatus}";

    public string MotorStatus => _isMotorEnabled ? "ON" : "OFF";

    public string MotorStatusText => $"모터 상태: {MotorStatus}";

    public string TrackingStatus => _isTrackingEnabled ? "ON" : "OFF";

    public string TrackingStatusText => $"트래킹 상태: {TrackingStatus}";

    public string ZoomLevelText => $"{_zoomLevel:0.0}x";

    public string ElectronicZoomStatus => IsElectronicZoomEnabled ? $"전자 Zoom 활성 / {ZoomLevelText}" : "전자 Zoom 비활성";

    // 전자 줌은 EO 창 안의 영상에만 적용한다.
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
                AddOrReplaceSystemMessage("DISPLAY", $"카메라 밝기 값이 {value:0}%로 변경되었습니다.");
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
                AddOrReplaceSystemMessage("DISPLAY", $"화면 대조비 값이 {value:0}%로 변경되었습니다.");
            }
        }
    }

    public string BrightnessText => $"카메라 밝기: {Brightness:0}%";

    public string ContrastText => $"화면 대조비: {Contrast:0}%";

    /// <summary>
    /// 카메라 서비스에서 새 EO 프레임이 들어오면 화면에 반영한다.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(EoFeedImage));
    }

    /// <summary>
    /// 외부 서비스 또는 버튼 동작에서 발생한 로그를 시스템 변경 로그에 추가한다.
    /// </summary>
    public void AppendSystemLog(string category, string message)
    {
        SystemChangeLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), category, message));
    }

    private void TogglePower()
    {
        _isPoweredOn = !_isPoweredOn;
        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(PowerStatusText));
        AppendSystemLog("POWER", $"시스템 전원 상태가 {PowerStatus}로 변경되었습니다.");
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
        AppendSystemLog("TRACK", $"트래킹 상태가 {TrackingStatus}로 변경되었습니다.");
    }

    private void CycleMode()
    {
        _modeIndex = (_modeIndex + 1) % _modes.Length;
        OnPropertyChanged(nameof(CurrentMode));
        OnPropertyChanged(nameof(CurrentModeStatus));
        AppendSystemLog("MODE", $"시스템 모드가 {CurrentMode}로 변경되었습니다.");
    }

    private void SelectMode(object? parameter)
    {
        if (parameter is not string mode)
        {
            return;
        }

        var index = Array.IndexOf(_modes, mode);
        if (index < 0)
        {
            return;
        }

        _modeIndex = index;
        OnPropertyChanged(nameof(CurrentMode));
        OnPropertyChanged(nameof(CurrentModeStatus));
        AppendSystemLog("MODE", $"시스템 모드가 {CurrentMode}로 직접 선택되었습니다.");
    }

    private void CycleDetectionCategory()
    {
        _detectionCategoryIndex = (_detectionCategoryIndex + 1) % _detectionCategories.Length;
        OnPropertyChanged(nameof(CurrentDetectionCategory));
        OnPropertyChanged(nameof(CurrentDetectionStatus));
        AppendSystemLog("DETECT", $"탐지 대상이 {CurrentDetectionCategory}로 변경되었습니다.");
    }

    private void SelectDetectionCategory(object? parameter)
    {
        if (parameter is not string category)
        {
            return;
        }

        var index = Array.IndexOf(_detectionCategories, category);
        if (index < 0)
        {
            return;
        }

        _detectionCategoryIndex = index;
        OnPropertyChanged(nameof(CurrentDetectionCategory));
        OnPropertyChanged(nameof(CurrentDetectionStatus));
        AppendSystemLog("DETECT", $"탐지 대상이 {CurrentDetectionCategory}로 직접 선택되었습니다.");
    }

    private void ToggleElectronicZoom()
    {
        IsElectronicZoomEnabled = !IsElectronicZoomEnabled;
        AppendSystemLog("ZOOM", IsElectronicZoomEnabled
            ? $"전자 Zoom 이 활성화되었습니다. 현재 배율은 {ZoomLevelText}입니다."
            : "전자 Zoom 이 비활성화되었습니다.");
    }

    private void IncreaseZoom()
    {
        _zoomLevel = Math.Min(3.0, _zoomLevel + 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AppendSystemLog("ZOOM", $"전자 Zoom 배율이 {ZoomLevelText}로 증가했습니다.");
    }

    private void DecreaseZoom()
    {
        _zoomLevel = Math.Max(1.0, _zoomLevel - 0.2);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AppendSystemLog("ZOOM", $"전자 Zoom 배율이 {ZoomLevelText}로 감소했습니다.");
    }

    private void AddOrReplaceSystemMessage(string category, string message)
    {
        if (SystemChangeLogs.Count > 0 && SystemChangeLogs[0].Category == category)
        {
            SystemChangeLogs[0] = new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), category, message);
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
/// VLM 결과 패널에 한 줄씩 표시할 결과 데이터다.
/// </summary>
public sealed record VlmResultItem(string Time, string Category, string Message);

/// <summary>
/// 실시간 시스템 변경 로그 패널에 표시할 로그 데이터다.
/// </summary>
public sealed record SystemLogItem(string Time, string Category, string Message);
