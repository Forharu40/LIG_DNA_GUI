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

/// 硫붿씤 ?붾㈃ ?곹깭, 紐⑤뱶, ?꾪뿕 ?깃툒, ?ㅼ젙 ?⑤꼸, 以???媛믪쓣 ??怨녹뿉??愿由ы븿.
/// ?ㅼ젣 ?λ퉬? VLM ?곌껐 ????ViewModel???ㅼ떆媛?媛믪쓣 ?ｌ뼱 媛숈? ?붾㈃ 援ъ“瑜??좎???
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    /// 紐⑤뱶, ?꾪뿕 ?깃툒, 諛앷린/?議곕퉬, ?꾩옄 以? 濡쒓렇, ?뚮쭏 踰꾪듉 ?곹깭瑜?愿由ы븯??ViewModel??
    // ?꾩옄 以?誘몃땲留듭? ?꾩옱 ?쒖빞瑜??쎄린 ?쎈룄濡??쎄컙 ?ш쾶 ?좎???
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
    // ?꾨줈洹몃옩 ?쒖옉 ??諛앷린 湲곕낯媛믪? 以묐┰媛?50%濡??쒖옉??
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

    // EO???몃? UDP ?곸긽, IR? ?꾩떆 ?명듃遺?移대찓???곸긽???ъ슜??
    // ?꾨젅???섏떊 ?꾩뿉???붾㈃??鍮꾩? ?딅룄濡?EO/IR 湲곕낯 ?덈궡 ?대?吏瑜?以鍮꾪븿.
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private readonly ImageSource _eoPlaceholderFrame = CreateCameraPlaceholderFrame("MEVA DEMO", Color.FromRgb(51, 94, 160));
    private readonly ImageSource _irPlaceholderFrame = CreateCameraPlaceholderFrame("IR TEMP", Color.FromRgb(192, 109, 40));

    public MainViewModel()
    {
        // ?깆쓽 ?꾩옱 ?뚮쭏瑜??쎌뼱 ?ㅼ젙李?踰꾪듉 ?곹깭? 留욎땄.
        if (Application.Current is App app)
        {
            _currentThemeMode = app.CurrentThemeMode;
        }

        AnalysisItems = new ObservableCollection<AnalysisItem>
        {
            new("10:05:00", "\uC2DC\uC2A4\uD15C \uCD08\uAE30\uD654 \uB2E8\uACC4\uC5D0\uC11C\uB294 \uAE30\uBCF8 \uC704\uD5D8 \uB4F1\uAE09\uC744 \uB0AE\uC74C\uC73C\uB85C \uC720\uC9C0\uD569\uB2C8\uB2E4."),
            new("10:05:03", "\uD604\uC7AC \uC8FC \uD0D0\uC9C0\uCCB4\uB294 \uBCF5\uD569\uC774\uBA70 \uC6B4\uC6A9\uC790\uAC00 \uD0D0\uC9C0 \uC870\uAC74\uC744 \uC870\uC815\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4."),
            new("10:05:05", "VLM \uACE0\uC704\uD5D8 \uBD84\uC11D \uACB0\uACFC\uAC00 \uB4E4\uC5B4\uC624\uAE30 \uC804\uAE4C\uC9C0 \uACBD\uBCF4 \uB2E8\uACC4\uB294 \uC0C1\uC2B9\uD558\uC9C0 \uC54A\uC2B5\uB2C8\uB2E4."),
        };

        SystemLogs = new ObservableCollection<SystemLogItem>
        {
            new("10:05:00", "\uC2DC\uC2A4\uD15C \uC804\uC6D0\uC744 \uCF1C\uC2B5\uB2C8\uB2E4."),
            new("10:05:02", "\uCE74\uBA54\uB77C \uC81C\uC5B4 \uBAA8\uB4DC\uAC00 \uC790\uB3D9\uC73C\uB85C \uC124\uC815\uB418\uC5C8\uC2B5\uB2C8\uB2E4."),
            new("10:05:04", "\uCD08\uAE30 \uC704\uD5D8 \uB4F1\uAE09\uC740 \uB0AE\uC74C\uC73C\uB85C \uC124\uC815\uB418\uC5C8\uC2B5\uB2C8\uB2E4."),
        };

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

        // ?붾㈃??紐⑤뱺 踰꾪듉? Command 諛붿씤?⑹쑝濡??곌껐?섎?濡??앹꽦?먯뿉????踰덉뿉 ?깅줉??
        TogglePowerCommand = new RelayCommand(_ => TogglePower());
        SetModeCommand = new RelayCommand(SetMode, _ => IsSystemPoweredOn);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        SelectPrimaryTargetCommand = new RelayCommand(SelectPrimaryTarget, _ => IsSystemPoweredOn);
        ResetBrightnessCommand = new RelayCommand(_ => Brightness = 50, _ => IsSystemPoweredOn);
        ResetContrastCommand = new RelayCommand(_ => Contrast = 50, _ => IsSystemPoweredOn);
        // ?꾩옄 以??쒕ぉ 踰꾪듉 ?대┃ ??湲곕낯 諛곗쑉 x1.0?쇰줈 蹂듦???
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

    // ?곷떒 ?꾩썝 踰꾪듉? ?꾨줈洹몃옩 醫낅즺 踰꾪듉?쇰줈 ?ъ슜??
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
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(ShowZoomMiniMap));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"\uCE74\uBA54\uB77C \uBAA8\uB4DC: {CurrentMode}";

    // ?꾩옱 紐⑤뱶 踰꾪듉留??좊챸?섍쾶 蹂댁뿬 蹂꾨룄 ?띿뒪???놁씠 ?곹깭瑜??쎄쾶 ??
    public double AutoModeOpacity => CurrentMode == "\uC790\uB3D9" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "\uC218\uB3D9" ? 1.0 : 0.35;

    // ?꾪뿕 ?깃툒 ?곸듅 ???ν썑 ?먮룞 ?뱁솕 ?쒖떆? ?곌껐???곹깭媛믪엫.
    // ?뱁솕 ?쒖떆?깆? ?꾪뿕 ?곹솴 ?먮룞 ?뱁솕? ?섎룞 ?뱁솕瑜?紐⑤몢 諛섏쁺??

    public bool IsRecordingActive =>
        IsManualRecordingEnabled ||
        (IsSystemPoweredOn && CurrentMode == "\uC790\uB3D9" && CurrentThreatLevel == "\uB192\uC74C");

    public Brush RecordingIndicatorBrush => IsRecordingActive ? RecordingOnBrush : RecordingOffBrush;

    public Brush RecordingTextBrush => IsRecordingActive ? RecordingOnBrush : RecordingTextOffBrush;

    public double RecordingIndicatorOpacity => IsRecordingActive ? 1.0 : 0.42;

    public bool IsManualMode => IsSystemPoweredOn && CurrentMode == "\uC218\uB3D9";

    public bool CanSelectAutoMode => IsSystemPoweredOn && CurrentMode != "\uC790\uB3D9";

    public bool CanSelectManualMode => IsSystemPoweredOn && CurrentMode != "\uC218\uB3D9";

    public bool CanUseMotorControls => IsManualMode;

    public bool CanUseZoomControls => IsManualMode;

    public double ManualRecordingButtonOpacity => IsManualMode ? 1.0 : 0.0;

    public bool IsDarkThemeActive => _currentThemeMode == AppThemeMode.Dark;

    public bool IsLightThemeActive => _currentThemeMode == AppThemeMode.Light;

    public double DarkThemeButtonOpacity => IsDarkThemeActive ? 1.0 : 0.55;

    public double LightThemeButtonOpacity => IsLightThemeActive ? 1.0 : 0.55;

    // ?섎룞 ?뱁솕???섎룞 紐⑤뱶?먯꽌留?耳쒓퀬 ?????덇쾶 ?쒗븳??
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

    public string ManualRecordingButtonText => IsManualRecordingEnabled ? "\uB179\uD654 \uC885\uB8CC" : "\uB179\uD654 \uC2DC\uC791";

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
            }
        }
    }

    public string PrimaryTargetText => $"\uC8FC \uD0D0\uC9C0\uCCB4: {SelectedPrimaryTarget}";

    // ?곸긽 ???쇰꺼? 吏㏐쾶 ?좎????ㅼ젣 ?붾㈃????媛由щ룄濡???
    public string EoTitle => "EO cam";

    public string IrTitle => "IR cam";

    public string EoSubtitle => "Jetson YOLO MEVA demo stream";

    public string IrSubtitle => "\uB178\uD2B8\uBD81 \uCE74\uBA54\uB77C \uC784\uC2DC \uC785\uB825";

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
            // ?щ씪?대뜑 媛?蹂寃????쒖떆 ?띿뒪?몃룄 ?④퍡 媛깆떊??
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
            // ?議곕퉬 ?レ옄 ?쒖떆? ?ㅼ젣 ?곸긽 蹂댁젙 媛믪쓣 ?숈씪?섍쾶 ?좎???
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
            // ?꾩옄 以뚯? 怨쇳솗?瑜?留됯린 ?꾪빐 1.0~4.0 踰붿쐞濡??쒗븳??
            var clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped))
            {
                // 湲곕낯 諛곗쑉 蹂듦? ???붾㈃ ?대룞媛믩룄 以묒떖?쇰줈 珥덇린?뷀븿.
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
            // ?ㅼ젣 ?붾㈃ ?대룞 諛⑺뼢怨?誘몃땲留??쒖떆 諛⑺뼢??留욎텛湲??꾪빐 醫뚰몴瑜?諛섏쟾??
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
            // ?ㅼ젣 ?붾㈃ ?대룞 諛⑺뼢怨?誘몃땲留??쒖떆 諛⑺뼢??留욎텛湲??꾪빐 醫뚰몴瑜?諛섏쟾??
            return (1.0 - normalized) * (MiniMapHeight - MiniMapViewportHeight);
        }
    }

    public string MotorPanText => $"모터 좌우: {_motorPan}도";

    public string MotorTiltText => $"모터 상하: {_motorTilt}도";

    /// <summary>
    /// EO ?꾨젅?꾩쓣 ?붾㈃??諛섏쁺??
    /// </summary>
    public void UpdateEoFrame(ImageSource? frame)
    {
        _eoFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    /// <summary>
    /// ?꾩떆 IR ?붾㈃?쇰줈 ?곕뒗 ?명듃遺?移대찓???꾨젅?꾩쓣 諛섏쁺??
    /// EO/IR ?ㅼ솑 ?곹깭???곕씪 ?묒? ?붾㈃ ?먮뒗 ???붾㈃??利됱떆 諛섏쁺??
    /// </summary>
    public void UpdateIrFrame(ImageSource? frame)
    {
        _irFrame = frame;
        OnPropertyChanged(nameof(LargeFeedImage));
        OnPropertyChanged(nameof(InsetFeedImage));
    }

    public void UpdateDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        // VLM 도입 전까지는 YOLO 탐지 개수만으로 위험 등급을 바꾸지 않는다.
        // 현재 위험 등급은 기본값(낮음)을 유지하고, 이후 VLM 판단이 들어오면 그때 반영한다.
    }

    /// <summary>
    /// 移대찓??酉고룷???ш린瑜?諛쏆븘 ?뺣? ?대룞 ?쒓퀎瑜??ㅼ떆 怨꾩궛??
    /// </summary>
    public void UpdateViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(width, 1);
        _viewportHeight = Math.Max(height, 1);
        ClampZoomPan();
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// ?뺣? ?곹깭?먯꽌 留덉슦???쒕옒洹몃줈 ?붾㈃ ?꾩튂瑜??대룞??
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
    /// 留덉슦?????낅젰?쇰줈 ?꾩옄 以?諛곗쑉??議곌툑??議곗젅??
    /// </summary>
    public void AdjustZoomByWheel(double wheelSteps)
    {
        if (!CanUseZoomControls || Math.Abs(wheelSteps) < double.Epsilon)
        {
            return;
        }

        // ????移몃쭏??0.1諛곗뵫 議곗젅???щ씪?대뜑? 鍮꾩듂??媛먮룄濡?留욎땄.
        ZoomLevel += wheelSteps * 0.1;
    }

    public void AppendImportantLog(string message)
    {
        // 媛??理쒓렐 濡쒓렇媛 ?꾩뿉 ?ㅻ룄濡?留??욎뿉 異붽???
        SystemLogs.Insert(0, new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        TrimCollection(SystemLogs, 8);
    }

    /// <summary>
    /// ?쒖뒪??濡쒓렇瑜?諛뷀깢?붾㈃???쒓컙 湲곗? ?뚯씪紐낆쑝濡???ν븿.
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
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8\uB97C \uC800\uC7A5\uD588\uC2B5\uB2C8\uB2E4: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8 \uC800\uC7A5\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4: {ex.Message}");
        }
    }

    /// <summary>
    /// ?곷떒 ?꾩썝 醫낅즺 踰꾪듉???ㅼ젣 ?숈옉??
    /// ?꾪뿕 ?깃툒???믪쓬?????ㅼ닔濡??꾨줈洹몃옩???レ? 紐삵븯寃?留됱쓬.
    /// </summary>
    private void TogglePower()
    {
        // ?꾪뿕 ?깃툒???믪쓬?대㈃ ?댁슜 以묒씤 ?꾨줈洹몃옩 醫낅즺瑜?李⑤떒??
        if (CurrentThreatLevel == "\uB192\uC74C")
        {
            AppendImportantLog("\uC704\uD5D8 \uB4F1\uAE09\uC774 \uB192\uC74C \uC0C1\uD0DC\uC5EC\uC11C \uD504\uB85C\uADF8\uB7A8\uC744 \uC885\uB8CC\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
            return;
        }

        Application.Current?.Shutdown();
    }

    /// <summary>
    /// ?먮룞/?섎룞 紐⑤뱶瑜??꾪솚??
    /// ?먮룞 紐⑤뱶 蹂듦? ???섎룞 ?뱁솕, 紐⑦꽣 媛? 以?諛곗쑉??珥덇린?뷀븿.
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
                // ?먮룞 紐⑤뱶 蹂듦? ???섎룞 ?뱁솕瑜?利됱떆 醫낅즺????ν븿.
                IsManualRecordingEnabled = false;
            }

            _motorPan = 0;
            _motorTilt = 0;
            ZoomLevel = 1.0;
            OnPropertyChanged(nameof(MotorPanText));
            OnPropertyChanged(nameof(MotorTiltText));
        }

        AppendImportantLog($"\uCE74\uBA54\uB77C \uC81C\uC5B4 \uBAA8\uB4DC\uAC00 {CurrentMode}(\uC73C)\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    /// <summary>
    /// ?섎룞 ?뱁솕 踰꾪듉 ?대┃ ???곹깭瑜?諛섏쟾??
    /// ?ㅼ젣 ?뚯씪 ????쒖옉/醫낅즺??MainWindow媛 ?쒕퉬?ㅼ? ?곌껐??泥섎━??
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
    /// ?ㅼ젙李쎌뿉?????뚮쭏瑜?吏곸젒 諛붽씀??紐낅졊 泥섎━??
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
    /// ?ㅼ젙李쎌뿉??二??먯?泥??좏깮 ???꾩옱 ?좏깮 ?곹깭瑜?媛깆떊??
    /// ?꾪뿕 ?깃툒? VLM 遺꾩꽍 寃곌낵媛 ?ㅼ뼱???뚮쭔 諛붾뚮룄濡??좎???
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
    /// EO/IR 硫붿씤 ?붾㈃怨??묒? ?몄뀑 ?붾㈃???쒕줈 援먯껜??
    /// ?ъ슜?먭? ?묒? ?붾㈃???뚮윭 ?먰븯???곸긽???ш쾶 蹂????덇쾶 ??
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

        AppendImportantLog($"{LargeFeedTitle}\uAC00 \uBA54\uC778 \uD654\uBA74\uC73C\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    /// <summary>
    /// ?섎룞 紐⑤뱶?먯꽌 紐⑦꽣 諛⑺뼢 踰꾪듉 ?대┃ ??醫뚯슦/?곹븯 媛곷룄瑜?蹂寃쏀븿.
    /// ?꾩옱??UI ?쒕??덉씠??媛믪씠硫? ?댄썑 ?ㅼ젣 紐⑦꽣 ?쒖뼱 紐낅졊怨??곌껐 媛?ν븿.
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
    /// ?꾩옱 以??대룞 媛믪씠 ?덉슜 踰붿쐞瑜??섏? ?딅룄濡?蹂댁젙??
    /// ?붾㈃ ?ш린??以?諛곗쑉 蹂寃????꾩슂???뺣━??
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
    /// ?꾩옄 以?誘몃땲留??ш컖?뺤쓽 ?ш린? ?꾩튂 ?ш퀎?곗쓣 UI???뚮┝.
    /// </summary>
    private void UpdateMiniMapViewport()
    {
        OnPropertyChanged(nameof(MiniMapViewportWidth));
        OnPropertyChanged(nameof(MiniMapViewportHeight));
        OnPropertyChanged(nameof(MiniMapViewportLeft));
        OnPropertyChanged(nameof(MiniMapViewportTop));
    }

    /// <summary>
    /// 紐⑤뱶, ?꾩썝, 以?媛???щ? 蹂寃???踰꾪듉 ?쒖꽦 ?곹깭瑜??ㅼ떆 怨꾩궛??
    /// 愿??紐낅졊 媛앹껜??CanExecuteChanged瑜???踰덉뿉 ?꾨떖??
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
    /// ?щ윭 怨녹뿉??諛섎났 ?ъ슜??怨좎젙 釉뚮윭???앹꽦.
    /// Freeze 泥섎━濡??깅뒫怨?硫붾え由??ъ슜???덉젙?뷀븿.
    /// </summary>
    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// ?ㅼ젣 移대찓???꾨젅???섏떊 ??蹂댁뿬 以??뚮젅?댁뒪????대?吏瑜??앹꽦??
    /// UI ?뚯뒪???④퀎?먯꽌 移대찓???곸뿭??鍮꾩뼱 蹂댁씠吏 ?딅룄濡???
    /// </summary>
    private static ImageSource CreateCameraPlaceholderFrame(string label, Color accentColor)
    {
        // ?ㅼ젣 ?낅젰 ?섏떊 ?꾩뿉??移대찓??諛곗튂 ?섎룄瑜??????덈룄濡??덈궡 ?꾨젅???앹꽦.
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
    /// ViewModel 怨듯넻 ?띿꽦 蹂寃??꾩슦誘몄엫.
    /// 媛믪씠 ?ㅼ젣濡?諛붾?寃쎌슦?먮쭔 PropertyChanged瑜?諛쒖깮?쒖폒 遺덊븘?뷀븳 媛깆떊??以꾩엫.
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
/// ?곹솴 遺꾩꽍 李쎌뿉 ?쒖떆??遺꾩꽍 臾몄옣??
/// </summary>
public sealed record AnalysisItem(string Time, string Message);

/// <summary>
/// ?쒖뒪??濡쒓렇???쒖떆??以묒슂 ?곹깭 蹂????ぉ??
/// </summary>
public sealed record SystemLogItem(string Time, string Message);
