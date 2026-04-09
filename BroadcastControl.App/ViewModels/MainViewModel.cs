using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 화면의 표시 상태, 모드 상태, 위험 등급, 설정 패널 상태를 한 곳에서 관리한다.
/// 실제 장비와 VLM이 연결되면 같은 ViewModel에 실시간 값을 주입해 그대로 확장할 수 있다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const double MiniMapWidth = 120;
    private const double MiniMapHeight = 68;

    private static readonly SolidColorBrush LowThreatBrush = CreateBrush(0x7B, 0xD8, 0x8F);
    private static readonly SolidColorBrush MediumThreatBrush = CreateBrush(0xFF, 0xC1, 0x45);
    private static readonly SolidColorBrush HighThreatBrush = CreateBrush(0xFF, 0x6B, 0x6B);

    // EO 화면이 메인 화면인지 여부를 저장한다.
    private bool _isEoPrimary = true;

    // 우측 설정 패널 열림 상태를 저장한다.
    private bool _isSettingsOpen;

    // 현재 카메라 운용 상태를 저장한다.
    private bool _isSystemPoweredOn = true;
    private string _currentMode = "수동";
    private string _selectedPrimaryTarget = "비행체";
    private string _currentThreatLevel = "높음";

    // 상단바와 카메라 패널에서 사용할 표시값이다.
    private double _brightness = 58;
    private double _contrast = 52;
    private double _zoomLevel = 1.0;

    // 확대된 EO 화면을 드래그할 때 사용할 팬 오프셋과 뷰포트 크기다.
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;

    // 수동 모드에서 모터 방향 값을 상태창에 표시하기 위해 유지한다.
    private int _motorPan;
    private int _motorTilt;

    private ImageSource? _eoFrame;
    private readonly ImageSource _irPlaceholderFrame = CreateIrPlaceholderFrame();

    public MainViewModel()
    {
        AnalysisItems = new ObservableCollection<AnalysisItem>
        {
            new("10:05:00", "북동측 500m 구간에서 복합 비행체 패턴이 감지되었습니다."),
            new("10:05:03", "현재 주 탐색체는 비행체이며 우선 감시 상태를 유지합니다."),
            new("10:05:05", "EO 입력 기준 이동 밀도가 상승해 위험 등급을 높음으로 유지합니다."),
        };

        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "시스템 전원이 켜졌습니다."),
            new("10:05:02", "카메라 제어 모드가 수동으로 설정되었습니다."),
            new("10:05:04", "현재 위험 등급이 높음으로 평가되었습니다."),
        };

        PrimaryTargets = new ReadOnlyCollection<string>(new[]
        {
            "비행체",
            "비군사 표적(오탐 유발 요소)",
            "통신 장비",
            "자주포 및 견인포",
        });

        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
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
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(IsManualMode));
                RaiseAllCommandStates();
            }
        }
    }

    // 상단 전원 버튼은 시스템 종료 버튼으로 사용한다.
    public string PowerButtonText => "전원 종료";

    public string CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(CurrentModeText));
                OnPropertyChanged(nameof(IsManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomControls));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"카메라 모드: {CurrentMode}";

    public bool IsManualMode => CurrentMode == "수동";

    public bool CanUseMotorControls => IsSystemPoweredOn && IsManualMode;

    public bool CanUseZoomControls => IsSystemPoweredOn && IsManualMode;

    public string CurrentThreatLevel
    {
        get => _currentThreatLevel;
        private set
        {
            if (SetProperty(ref _currentThreatLevel, value))
            {
                OnPropertyChanged(nameof(CurrentThreatText));
                OnPropertyChanged(nameof(CurrentThreatBrush));
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

    public string EoTitle => "EO 카메라";

    public string IrTitle => "IR 카메라";

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
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
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
            return normalized * (MiniMapWidth - MiniMapViewportWidth);
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
            return normalized * (MiniMapHeight - MiniMapViewportHeight);
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
    /// 확대 상태에서 마우스 드래그로 화면 위치를 옮긴다.
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

    public void AppendImportantLog(string message)
    {
        SystemLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        TrimCollection(SystemLogs, 8);
    }

    private void TogglePower()
    {
        // 위험 등급이 높음이면 운용 중인 프로그램을 종료할 수 없게 막는다.
        if (CurrentThreatLevel == "높음")
        {
            AppendImportantLog("위험 등급이 높음 상태여서 프로그램을 종료할 수 없습니다.");
            return;
        }

        Application.Current?.Shutdown();
    }

    private void SetMode(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string mode)
        {
            return;
        }

        CurrentMode = mode;

        if (!IsManualMode)
        {
            _motorPan = 0;
            _motorTilt = 0;
            ZoomLevel = 1.0;
            OnPropertyChanged(nameof(MotorPanText));
            OnPropertyChanged(nameof(MotorTiltText));
        }

        AppendImportantLog($"카메라 제어 모드가 {CurrentMode}으로 전환되었습니다.");
    }

    private void SelectPrimaryTarget(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string target)
        {
            return;
        }

        var previousThreat = CurrentThreatLevel;
        SelectedPrimaryTarget = target;
        CurrentThreatLevel = MapThreatLevel(target);

        AnalysisItems.Insert(0, new AnalysisItem(DateTime.Now.ToString("HH:mm:ss"), $"주 탐지체가 {SelectedPrimaryTarget}으로 변경되었습니다."));
        TrimCollection(AnalysisItems, 10);

        AppendImportantLog($"주 탐지체가 {SelectedPrimaryTarget}으로 변경되었습니다.");
        if (previousThreat != CurrentThreatLevel)
        {
            AppendImportantLog($"위험 등급이 {CurrentThreatLevel}으로 변경되었습니다.");
        }
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

        AppendImportantLog($"{LargeFeedTitle}이(가) 메인 화면으로 전환되었습니다.");
    }

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

    private void ClampZoomPan()
    {
        _zoomPanX = Math.Clamp(_zoomPanX, -GetMaxPanX(), GetMaxPanX());
        _zoomPanY = Math.Clamp(_zoomPanY, -GetMaxPanY(), GetMaxPanY());
        OnPropertyChanged(nameof(ZoomTransformX));
        OnPropertyChanged(nameof(ZoomTransformY));
    }

    private double GetMaxPanX() => (_viewportWidth * (ZoomLevel - 1)) / 2;

    private double GetMaxPanY() => (_viewportHeight * (ZoomLevel - 1)) / 2;

    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    private void RaiseAllCommandStates()
    {
        RaiseCommand(SetModeCommand);
        RaiseCommand(SelectPrimaryTargetCommand);
        RaiseCommand(MoveMotorCommand);
    }

    private static void RaiseCommand(ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    private static string MapThreatLevel(string target) => target switch
    {
        "비군사 표적(오탐 유발 요소)" => "낮음",
        "통신 장비" => "중간",
        _ => "높음",
    };

    private static void TrimCollection<T>(ObservableCollection<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

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
