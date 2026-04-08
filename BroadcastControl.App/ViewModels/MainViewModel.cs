using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

/// <summary>
/// 메인 화면에서 필요한 표시 상태와 제어 상태를 한 곳에서 관리한다.
/// 실제 장비와 VLM이 연결되면 이 ViewModel에 실시간 값을 주입해 같은 화면 구조를 그대로 사용할 수 있다.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const double MiniMapWidth = 120;
    private const double MiniMapHeight = 68;

    private static readonly SolidColorBrush LowThreatBrush = CreateBrush(0x7B, 0xD8, 0x8F);
    private static readonly SolidColorBrush MediumThreatBrush = CreateBrush(0xFF, 0xC1, 0x45);
    private static readonly SolidColorBrush HighThreatBrush = CreateBrush(0xFF, 0x6B, 0x6B);

    // EO가 큰 화면인지, IR이 큰 화면인지 기억한다.
    private bool _isEoPrimary = true;

    // 설정 패널은 우측에서 슬라이드되는 형태로 열고 닫는다.
    private bool _isSettingsOpen;

    // 전원과 모드는 상단바 전체와 하단 조작 기능의 기준이 된다.
    private bool _isSystemPoweredOn = true;
    private string _currentMode = "수동";
    private string _selectedPrimaryTarget = "복합";
    private string _currentThreatLevel = "높음";

    // 상단바와 카메라 패널에서 함께 사용할 밝기/대조비/줌 값이다.
    private double _brightness = 58;
    private double _contrast = 52;
    private double _zoomLevel = 1.0;

    // 확대된 화면을 드래그로 움직일 수 있도록 현재 팬 오프셋을 저장한다.
    private double _zoomPanX;
    private double _zoomPanY;
    private double _viewportWidth = 1;
    private double _viewportHeight = 1;

    // 수동 모드에서 모터 방향 조정을 눌렀을 때 상태창에 반영할 값이다.
    private int _motorPan;
    private int _motorTilt;

    // EO 입력은 실제 웹캠 프레임, IR은 임시 열화상 느낌 프레임을 사용한다.
    private ImageSource? _eoFrame;
    private readonly ImageSource _irPlaceholderFrame = CreateIrPlaceholderFrame();

    public MainViewModel()
    {
        VlmResults = new ObservableCollection<VlmInsightItem>
        {
            new("10:05:00", "북동측 500m 구간에 복합 비행체 패턴이 감지되었습니다."),
            new("10:05:03", "현재 주 탐색체는 복합이며 다중 객체 관측 모드가 유지되고 있습니다."),
            new("10:05:05", "EO 입력 기준 객체 밀집도가 상승해 위험 등급을 높음으로 유지합니다."),
        };

        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "시스템 전원이 켜졌습니다."),
            new("10:05:02", "카메라 제어 모드가 수동으로 설정되었습니다."),
            new("10:05:04", "현재 위험 등급이 높음으로 평가되었습니다."),
        };

        PrimaryTargets = new ReadOnlyCollection<string>(new[]
        {
            "복합",
            "고정익",
            "회전익",
            "드론",
            "사람",
        });

        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => CanUseOperationalControls);
        SwapFeedsCommand = new RelayCommand(_ => SwapFeeds());
        MoveMotorCommand = new RelayCommand(MoveMotor, _ => CanUseMotorControls);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VlmInsightItem> VlmResults { get; }

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
                OnPropertyChanged(nameof(PowerButtonText));
                OnPropertyChanged(nameof(SystemPowerText));
                OnPropertyChanged(nameof(CanUseOperationalControls));
                OnPropertyChanged(nameof(CanUseMotorControls));
                RaiseAllCommandStates();
            }
        }
    }

    // 상단 전원 버튼은 상태 표시가 아니라 프로그램 종료 버튼으로 사용한다.
    public string PowerButtonText => "전원 종료";

    public string SystemPowerText => $"시스템 전원: {(IsSystemPoweredOn ? "ON" : "OFF")}";

    public string CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(CurrentModeText));
                OnPropertyChanged(nameof(CanUseMotorControls));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"카메라 모드: {CurrentMode}";

    public bool CanUseOperationalControls => IsSystemPoweredOn;

    public bool CanUseMotorControls => IsSystemPoweredOn && CurrentMode == "수동";

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

    public string PrimaryTargetText => $"주 탐색체: {SelectedPrimaryTarget}";

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

    public string BrightnessText => $"카메라 밝기 {Brightness:0}%";

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

    public string ContrastText => $"화면 대조비 {Contrast:0}%";

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

    public string ZoomLevelText => $"전자 ZOOM x{ZoomLevel:0.0}";

    public double LargeFeedScale => ZoomLevel;

    public double ZoomTransformX => _zoomPanX;

    public double ZoomTransformY => _zoomPanY;

    public bool ShowZoomMiniMap => ZoomLevel > 1.0;

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
    /// 웹캠 프레임을 받아 EO 화면에 갱신한다.
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
    /// 확대된 상태에서 마우스 드래그로 화면 위치를 옮긴다.
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
        // 전원 버튼을 누르면 운용 콘솔을 즉시 종료한다.
        Application.Current?.Shutdown();
    }

    private void SetMode(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string mode)
        {
            return;
        }

        CurrentMode = mode;

        if (CurrentMode == "자동")
        {
            _motorPan = 0;
            _motorTilt = 0;
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

        VlmResults.Insert(0, new VlmInsightItem(DateTime.Now.ToString("HH:mm:ss"), $"주 탐색체가 {SelectedPrimaryTarget}으로 변경되었습니다."));
        TrimCollection(VlmResults, 10);

        AppendImportantLog($"주 탐색체가 {SelectedPrimaryTarget}으로 변경되었습니다.");
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
        "사람" => "낮음",
        "드론" or "회전익" => "중간",
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
        // 실제 IR 입력 전에도 우측 상단 작은 화면이 비지 않도록 열화상 느낌의 임시 프레임을 만든다.
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
/// VLM 결과창에 표시할 짧은 분석 문장이다.
/// </summary>
public sealed record VlmInsightItem(string Time, string Message);

/// <summary>
/// 시스템 로그에는 중요한 상태 변화만 남긴다.
/// </summary>
public sealed record SystemLogItem(string Time, string Message);
