using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 화면의 표시 상태와 버튼 활성 조건을 관리한다.
/// 실제 장비가 연결되면 이 ViewModel이 장비 상태와 분석 결과를 받아 화면에 반영한다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // EO 화면이 큰 화면인지, IR 화면이 큰 화면인지 관리한다.
    private bool _isEoPrimary = true;

    // 시스템 전원과 전자 줌 사용 여부를 저장한다.
    private bool _isSystemPoweredOn = true;
    private bool _isElectronicZoomEnabled;

    // 현재 제어 모드와 탐지 분류를 보관한다.
    private string _currentMode = "수동";
    private string _currentDetectionCategory = "복합";

    // 모터 좌우/상하 위치를 각도 단위로 기록한다.
    private int _motorPan;
    private int _motorTilt;

    // 자동 모드에서 표시할 추적 대상을 저장한다.
    private string _trackingTarget = "FW-01";

    // 전자 줌은 큰 화면 내부 영상 배율만 변경한다.
    private double _zoomLevel = 1.0;

    // 밝기와 대조비는 0~100 범위 값으로 관리한다.
    private double _brightness = 52;
    private double _contrast = 58;

    // 코드 비하인드에서 전달받은 EO 카메라 프레임을 저장한다.
    private ImageSource? _eoFrame;

    public MainViewModel()
    {
        // 상단 통합 패널에 표시할 VLM 결과 예시 데이터다.
        VlmResults = new ObservableCollection<VlmResultItem>
        {
            new("10:05:00", "VLM", "북동측 500m 지점에서 소형 비행체 3개체를 식별했습니다."),
            new("10:05:03", "TRACK", "주요 관측 대상 FW-01이 동쪽에서 남동쪽 방향으로 이동 중입니다."),
            new("10:05:08", "ANALYSIS", "현재 우선 탐지 분류는 복합이며 드론과 회전익 개체가 혼재합니다."),
            new("10:05:10", "SYS", "EO 입력과 VLM 분석 파이프라인이 정상 대기 상태입니다."),
        };

        // 시스템 변경 로그는 최신 로그가 위로 오도록 관리한다.
        SystemChangeLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "SYSTEM", "GUI 초기화가 완료되었습니다."),
            new("10:05:01", "VIDEO", "EO 카메라 연결을 준비 중입니다."),
        };

        // 화면과 장비 제어 기능을 목적별 명령으로 분리한다.
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SelectModeCommand = new RelayCommand(SelectMode);
        SelectDetectionCategoryCommand = new RelayCommand(SelectDetectionCategory, _ => CanUseDetectionControls);
        ToggleElectronicZoomCommand = new RelayCommand(_ => ToggleElectronicZoom(), _ => CanUseDisplayControls);
        IncreaseZoomCommand = new RelayCommand(_ => ChangeZoom(+0.2), _ => CanUseZoomStepControls);
        DecreaseZoomCommand = new RelayCommand(_ => ChangeZoom(-0.2), _ => CanUseZoomStepControls);
        MoveMotorLeftCommand = new RelayCommand(_ => MoveMotor(-5, 0), _ => CanUseMotorControls);
        MoveMotorRightCommand = new RelayCommand(_ => MoveMotor(+5, 0), _ => CanUseMotorControls);
        MoveMotorUpCommand = new RelayCommand(_ => MoveMotor(0, +5), _ => CanUseMotorControls);
        MoveMotorDownCommand = new RelayCommand(_ => MoveMotor(0, -5), _ => CanUseMotorControls);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VlmResultItem> VlmResults { get; }

    public ObservableCollection<SystemLogItem> SystemChangeLogs { get; }

    public ICommand SwapFeedsCommand { get; }

    public ICommand TogglePowerCommand { get; }

    public ICommand SelectModeCommand { get; }

    public ICommand SelectDetectionCategoryCommand { get; }

    public ICommand ToggleElectronicZoomCommand { get; }

    public ICommand IncreaseZoomCommand { get; }

    public ICommand DecreaseZoomCommand { get; }

    public ICommand MoveMotorLeftCommand { get; }

    public ICommand MoveMotorRightCommand { get; }

    public ICommand MoveMotorUpCommand { get; }

    public ICommand MoveMotorDownCommand { get; }

    public string EoTitle => "EO 화면";

    public string IrTitle => "IR 화면";

    public string EoSubtitle => "노트북 카메라 입력";

    public string IrSubtitle => "IR 카메라 연결 예정";

    public ImageSource? LargeFeedImage => _isEoPrimary ? _eoFrame : null;

    public ImageSource? InsetFeedImage => _isEoPrimary ? null : _eoFrame;

    public string LargeFeedTitle => _isEoPrimary ? EoTitle : IrTitle;

    public string InsetFeedTitle => _isEoPrimary ? IrTitle : EoTitle;

    public string LargeFeedSubtitle => _isEoPrimary ? EoSubtitle : IrSubtitle;

    public string InsetFeedSubtitle => _isEoPrimary ? IrSubtitle : EoSubtitle;

    public string PowerStatus => _isSystemPoweredOn ? "ON" : "OFF";

    public string PowerStatusText => $"시스템 전원: {PowerStatus}";

    public string CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(CurrentModeStatus));
                OnPropertyChanged(nameof(TrackingTargetText));
                OnPropertyChanged(nameof(CanUseModeButtons));
                OnPropertyChanged(nameof(CanUseDetectionControls));
                OnPropertyChanged(nameof(CanUseDisplayControls));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomStepControls));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeStatus => $"시스템 모드: {CurrentMode}";

    public string CurrentDetectionCategory
    {
        get => _currentDetectionCategory;
        private set
        {
            if (SetProperty(ref _currentDetectionCategory, value))
            {
                OnPropertyChanged(nameof(CurrentDetectionStatus));
            }
        }
    }

    public string CurrentDetectionStatus => $"탐지 분류: {CurrentDetectionCategory}";

    public string ZoomStatusText => $"Zoom 배율: {ZoomLevelText}";

    public string ZoomLevelText => $"x{_zoomLevel:0.0}";

    public string MotorPanStatusText => $"모터 좌/우: {_motorPan}도";

    public string MotorTiltStatusText => $"모터 상/하: {_motorTilt}도";

    public string TrackingTargetText => $"관측 대상: {GetTrackingTargetStatus()}";

    public string BrightnessText => $"화면 밝기: {Brightness:0}%";

    public string ContrastText => $"화면 대조비: {Contrast:0}%";

    public bool CanUseModeButtons => _isSystemPoweredOn;

    public bool CanUseDetectionControls => _isSystemPoweredOn && CurrentMode != "자동";

    public bool CanUseDisplayControls => _isSystemPoweredOn && CurrentMode != "자동";

    public bool CanUseMotorControls => _isSystemPoweredOn && CurrentMode == "수동";

    public bool CanUseZoomStepControls => CanUseDisplayControls && IsElectronicZoomEnabled;

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
                OnPropertyChanged(nameof(CanUseZoomStepControls));
                RaiseAllCommandStates();
            }
        }
    }

    public string ElectronicZoomStatus => IsElectronicZoomEnabled
        ? $"전자 Zoom: ON / {ZoomLevelText}"
        : "전자 Zoom: OFF";

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

    /// <summary>
    /// 카메라 서비스에서 EO 프레임을 전달받아 화면에 반영한다.
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    /// <summary>
    /// UI 또는 장비 이벤트에서 발생한 상태 메시지를 로그 패널에 추가한다.
    /// </summary>
    public void AppendSystemLog(string category, string message)
    {
        SystemChangeLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), category, message));
    }

    private void SwapFeeds()
    {
        _isEoPrimary = !_isEoPrimary;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
        OnPropertyChanged(nameof(LargeFeedTitle));
        OnPropertyChanged(nameof(InsetFeedTitle));
        OnPropertyChanged(nameof(LargeFeedSubtitle));
        OnPropertyChanged(nameof(InsetFeedSubtitle));

        AppendSystemLog("VIDEO", $"{LargeFeedTitle}이(가) 메인 화면으로 전환되었습니다.");
    }

    private void TogglePower()
    {
        _isSystemPoweredOn = !_isSystemPoweredOn;

        if (!_isSystemPoweredOn)
        {
            // 시스템이 꺼지면 모든 가변 기능을 기본 상태로 정리한다.
            IsElectronicZoomEnabled = false;
            _zoomLevel = 1.0;
            _motorPan = 0;
            _motorTilt = 0;
            OnPropertyChanged(nameof(ZoomLevelText));
            OnPropertyChanged(nameof(ZoomStatusText));
            OnPropertyChanged(nameof(LargeFeedScale));
            OnPropertyChanged(nameof(MotorPanStatusText));
            OnPropertyChanged(nameof(MotorTiltStatusText));
            OnPropertyChanged(nameof(TrackingTargetText));
        }

        OnPropertyChanged(nameof(PowerStatus));
        OnPropertyChanged(nameof(PowerStatusText));
        OnPropertyChanged(nameof(CanUseModeButtons));
        OnPropertyChanged(nameof(CanUseDetectionControls));
        OnPropertyChanged(nameof(CanUseDisplayControls));
        OnPropertyChanged(nameof(CanUseMotorControls));
        OnPropertyChanged(nameof(CanUseZoomStepControls));
        RaiseAllCommandStates();

        AppendSystemLog("POWER", $"시스템 전원 상태가 {PowerStatus}로 변경되었습니다.");
    }

    private void SelectMode(object? parameter)
    {
        if (!_isSystemPoweredOn || parameter is not string mode)
        {
            return;
        }

        CurrentMode = mode;

        // 자동 모드에서는 조작 버튼을 잠그므로 전자 줌도 함께 해제한다.
        if (CurrentMode == "자동")
        {
            IsElectronicZoomEnabled = false;
            _zoomLevel = 1.0;
            OnPropertyChanged(nameof(ZoomLevelText));
            OnPropertyChanged(nameof(ZoomStatusText));
            OnPropertyChanged(nameof(LargeFeedScale));
        }

        AppendSystemLog("MODE", $"시스템 모드가 {CurrentMode}로 변경되었습니다.");
    }

    private void SelectDetectionCategory(object? parameter)
    {
        if (!CanUseDetectionControls || parameter is not string category)
        {
            return;
        }

        CurrentDetectionCategory = category;
        AppendSystemLog("DETECT", $"탐지 분류가 {CurrentDetectionCategory}로 변경되었습니다.");
    }

    private void ToggleElectronicZoom()
    {
        if (!CanUseDisplayControls)
        {
            return;
        }

        IsElectronicZoomEnabled = !IsElectronicZoomEnabled;

        if (!IsElectronicZoomEnabled)
        {
            _zoomLevel = 1.0;
            OnPropertyChanged(nameof(ZoomLevelText));
            OnPropertyChanged(nameof(ZoomStatusText));
            OnPropertyChanged(nameof(LargeFeedScale));
        }

        AppendSystemLog("ZOOM", IsElectronicZoomEnabled
            ? $"전자 Zoom이 활성화되었습니다. 현재 배율은 {ZoomLevelText}입니다."
            : "전자 Zoom이 비활성화되었습니다.");
    }

    private void ChangeZoom(double delta)
    {
        if (!CanUseZoomStepControls)
        {
            return;
        }

        _zoomLevel = Math.Clamp(_zoomLevel + delta, 1.0, 3.0);
        OnPropertyChanged(nameof(ZoomLevelText));
        OnPropertyChanged(nameof(ZoomStatusText));
        OnPropertyChanged(nameof(ElectronicZoomStatus));
        OnPropertyChanged(nameof(LargeFeedScale));
        AppendSystemLog("ZOOM", $"전자 Zoom 배율이 {ZoomLevelText}로 변경되었습니다.");
    }

    private void MoveMotor(int panDelta, int tiltDelta)
    {
        if (!CanUseMotorControls)
        {
            return;
        }

        _motorPan = Math.Clamp(_motorPan + panDelta, -90, 90);
        _motorTilt = Math.Clamp(_motorTilt + tiltDelta, -45, 45);

        OnPropertyChanged(nameof(MotorPanStatusText));
        OnPropertyChanged(nameof(MotorTiltStatusText));

        if (panDelta != 0)
        {
            AppendSystemLog("MOTOR", $"모터 좌/우 위치가 {_motorPan}도로 변경되었습니다.");
        }

        if (tiltDelta != 0)
        {
            AppendSystemLog("MOTOR", $"모터 상/하 위치가 {_motorTilt}도로 변경되었습니다.");
        }
    }

    private string GetTrackingTargetStatus()
    {
        if (!_isSystemPoweredOn)
        {
            return "시스템 OFF";
        }

        return CurrentMode switch
        {
            "자동" => _trackingTarget,
            "수동" => "수동 관측 중",
            _ => "고정 대기"
        };
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

    private void RaiseAllCommandStates()
    {
        RaiseCommand(SelectDetectionCategoryCommand);
        RaiseCommand(ToggleElectronicZoomCommand);
        RaiseCommand(IncreaseZoomCommand);
        RaiseCommand(DecreaseZoomCommand);
        RaiseCommand(MoveMotorLeftCommand);
        RaiseCommand(MoveMotorRightCommand);
        RaiseCommand(MoveMotorUpCommand);
        RaiseCommand(MoveMotorDownCommand);
    }

    private static void RaiseCommand(ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
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
/// VLM 출력값 패널에 표시할 결과 데이터다.
/// </summary>
public sealed record VlmResultItem(string Time, string Category, string Message);

/// <summary>
/// 시스템 변경 로그 패널에 표시할 로그 데이터다.
/// </summary>
public sealed record SystemLogItem(string Time, string Category, string Message);
