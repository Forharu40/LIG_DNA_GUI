using BroadcastControl.App.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BroadcastControl.App.Infrastructure;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels.Camera;
using BroadcastControl.App.ViewModels.Mobile;
using BroadcastControl.App.ViewModels.Monitoring;
using BroadcastControl.App.ViewModels.Motor;
using BroadcastControl.App.ViewModels.Operation;
using BroadcastControl.App.ViewModels.Recording;
using BroadcastControl.App.ViewModels.Vlm;

// 파일 역할:
// 하단 조작/설정 영역에서 사용하는 ViewModel과 MainViewModel의 시스템 운영 상태를 함께 둡니다.
// 전원, Scan/Manual 모드, Tracking, 테마, 언어, 주 탐지체, System 연결 표시를 담당합니다.

namespace BroadcastControl.App.ViewModels.Operation
{

/// <summary>
/// OperationControlView가 직접 참조할 수 있는 운영 상태입니다.
/// 기존 화면 바인딩과 명령은 아래 MainViewModel partial에서 유지합니다.
/// </summary>
public sealed class OperationControlViewModel : ViewModelBase
{
    private bool _isScanMode = true;
    private bool _isTrackingEnabled = true;
    private int _selectedTrackId = 0xFF;
    private bool _isEoPrimary = true;

    public bool IsScanMode
    {
        get => _isScanMode;
        set => SetProperty(ref _isScanMode, value);
    }

    public bool IsTrackingEnabled
    {
        get => _isTrackingEnabled;
        set => SetProperty(ref _isTrackingEnabled, value);
    }

    public int SelectedTrackId
    {
        get => _selectedTrackId;
        set => SetProperty(ref _selectedTrackId, Math.Clamp(value, 0, 0xFF));
    }

    public bool IsEoPrimary
    {
        get => _isEoPrimary;
        set => SetProperty(ref _isEoPrimary, value);
    }
}
}

namespace BroadcastControl.App.ViewModels
{
// OperationControlViewModel.cs 안에 둔 MainViewModel partial 영역입니다.
// 시스템 운영과 설정 관련 코드를 Operation 폴더로 모아 기능별 책임을 분리합니다.
public sealed partial class MainViewModel
{
    public bool IsEoPrimary => _isEoPrimary;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string PowerButtonText => Text["PowerExit"];

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
                OnPropertyChanged(nameof(ManualRecordingButtonText));
                OnPropertyChanged(nameof(CanSelectAutoMode));
                OnPropertyChanged(nameof(CanSelectManualMode));
                OnPropertyChanged(nameof(CanUseMotorControls));
                OnPropertyChanged(nameof(CanUseMotorTargetControls));
                OnPropertyChanged(nameof(MotorControlsOpacity));
                OnPropertyChanged(nameof(CanUseZoomControls));
                OnPropertyChanged(nameof(ShowZoomMiniMap));
                OnPropertyChanged(nameof(TrackingModeOpacity));
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                RaiseAllCommandStates();
            }
        }
    }

    public string CurrentModeText => $"{Text["CameraMode"]}: {TranslateMode(CurrentMode)}";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public double AutoModeOpacity => CurrentMode == "\uC790\uB3D9" ? 1.0 : 0.35;

    public double ManualModeOpacity => CurrentMode == "\uC218\uB3D9" ? 1.0 : 0.35;

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.


    public bool IsJetsonConnected
    {
        get => _isJetsonConnected;
        private set
        {
            if (SetProperty(ref _isJetsonConnected, value))
            {
                OnPropertyChanged(nameof(JetsonConnectionText));
                OnPropertyChanged(nameof(JetsonConnectionBrush));
                OnPropertyChanged(nameof(JetsonConnectionOpacity));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
            }
        }
    }

    public string JetsonConnectionText => IsJetsonConnected ? Text["Connected"] : Text["Connecting"];

    public Brush JetsonConnectionBrush => IsJetsonConnected ? RecordingOnBrush : RecordingTextBrush;

    public double JetsonConnectionOpacity => RecordingIndicatorOpacity;

    public bool IsManualMode => IsSystemPoweredOn && CurrentMode == "\uC218\uB3D9";

    public bool IsAutoMode => IsSystemPoweredOn && CurrentMode == "\uC790\uB3D9";

    public bool CanSelectAutoMode => IsSystemPoweredOn && CurrentMode != "\uC790\uB3D9";

    public bool CanSelectManualMode => IsSystemPoweredOn && CurrentMode != "\uC218\uB3D9";

    public bool CanUseMotorControls => IsManualMode;

    public bool CanUseMotorTargetControls => IsManualMode;

    public double MotorControlsOpacity => CanUseMotorControls ? 1.0 : 0.38;

    public bool CanUseZoomControls => IsSystemPoweredOn;

    public double ManualRecordingButtonOpacity => IsSystemPoweredOn ? 1.0 : 0.45;

    public bool IsDarkThemeActive => _currentThemeMode == AppThemeMode.Dark;

    public bool IsLightThemeActive => _currentThemeMode == AppThemeMode.Light;

    public double DarkThemeButtonOpacity => IsDarkThemeActive ? 1.0 : 0.55;

    public double LightThemeButtonOpacity => IsLightThemeActive ? 1.0 : 0.55;

    public bool IsEnglishLanguage => _uiLanguage == UiLanguage.English;

    public bool IsKoreanLanguage => _uiLanguage == UiLanguage.Korean;

    public double EnglishLanguageButtonOpacity => IsEnglishLanguage ? 1.0 : 0.55;

    public double KoreanLanguageButtonOpacity => IsKoreanLanguage ? 1.0 : 0.55;

    private void TogglePower()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        void ShutdownApplication()
        {
            app.MainWindow?.Close();
            app.Shutdown();
        }

        if (app.Dispatcher.CheckAccess())
        {
            ShutdownApplication();
            return;
        }

        app.Dispatcher.BeginInvoke(ShutdownApplication);
    }

    /// <summary>
     /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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

        if (IsAutoMode && CurrentThreatLevel == "\uB192\uC74C" && !_isRecordingSuppressed)
        {
            _isAutoRecordingLatched = true;
        }

        if (!IsManualMode)
        {
            if (IsManualRecordingEnabled)
            {
                // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
                IsManualRecordingEnabled = false;
            }
        }

        OnRecordingStateChanged();

        if (IsAutoMode)
        {
            SyncMotorRawFromFeedback();
        }

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"모터 모드 전송에 실패했습니다: {modeError}");
        }
        else if (IsAutoMode)
        {
            _lastAutomaticTrackingPacketSentAt = DateTime.Now;
        }

        AppendImportantLog($"\uCE74\uBA54\uB77C \uC81C\uC5B4 \uBAA8\uB4DC\uAC00 {CurrentMode}(\uC73C)\uB85C \uC804\uD658\uB418\uC5C8\uC2B5\uB2C8\uB2E4.");
    }

    private void ToggleTrackingMode()
    {
        if (!IsSystemPoweredOn)
        {
            return;
        }

        IsTrackingModeEnabled = !IsTrackingModeEnabled;

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"추적 모드 전송에 실패했습니다: {modeError}");
        }
    }

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
    }

    private void SetLanguage(object? parameter)
    {
        if (parameter is not string languageName)
        {
            return;
        }

        var nextLanguage = string.Equals(languageName, "Korean", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Korean
            : UiLanguage.English;

        if (_uiLanguage == nextLanguage)
        {
            return;
        }

        _uiLanguage = nextLanguage;
        Text.Refresh();
        RefreshPrimaryTargetLabels();
        RaiseLocalizedTextProperties();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SelectPrimaryTarget(object? parameter)
    {
        if (!IsSystemPoweredOn || parameter is not string target)
        {
            return;
        }

        SelectedPrimaryTarget = target;
    }

    private string GetShortPrimaryTargetName(string target)
    {
        if (_uiLanguage == UiLanguage.English)
        {
            return target switch
            {
                "\uBB34\uAE30\uCCB4\uACC4" => "Weapon",
                "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4" => "Air",
                "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "Ground",
                "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "Sea",
                "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
                "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
                "\uC0AC\uB78C" => "Person",
                "\uBCF5\uD569" => "Composite",
                _ => target,
            };
        }

        return target switch
        {
            "\uBB34\uAE30\uCCB4\uACC4" => "\uBB34\uAE30\uCCB4\uACC4",
            "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4" => "\uACF5\uC911",
            "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uC721\uC0C1",
            "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4" => "\uD574\uC0C1",
            "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
            "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
            _ => target,
        };
    }

    private string TranslateMode(string mode)
    {
        return mode switch
        {
            "\uC790\uB3D9" => Text["AutoMode"],
            "\uC218\uB3D9" => Text["ManualMode"],
            _ => mode,
        };
    }

    private string TranslateThreatLevel(string threatLevel)
    {
        return threatLevel switch
        {
            "\uB192\uC74C" => Text["ThreatHigh"],
            "\uC911\uAC04" => Text["ThreatMedium"],
            _ => Text["ThreatLow"],
        };
    }

    private string TranslatePrimaryTarget(string target)
    {
        return target switch
        {
            "\uBCF5\uD569" => Text["TargetComposite"],
            "\uC0AC\uB78C" => Text["TargetPerson"],
            "\uBB34\uAE30\uCCB4\uACC4" => Text["TargetWeapon"],
            "\uD1B5\uC2E0 \uC7A5\uBE44" => Text["TargetComm"],
            "\uBE44\uAD70\uC0AC \uD45C\uC801" => Text["TargetCivil"],
            _ => target,
        };
    }

    private IEnumerable<PrimaryTargetOption> CreatePrimaryTargetOptions()
    {
        var targets = new[]
        {
            "\uBCF5\uD569",
            "\uC0AC\uB78C",
            "\uBB34\uAE30\uCCB4\uACC4",
            "\uD1B5\uC2E0 \uC7A5\uBE44",
            "\uBE44\uAD70\uC0AC \uD45C\uC801",
        };

        return targets.Select(target => new PrimaryTargetOption(target, TranslatePrimaryTarget(target))).ToArray();
    }

    private void RefreshPrimaryTargetLabels()
    {
        foreach (var option in PrimaryTargets)
        {
            option.DisplayName = TranslatePrimaryTarget(option.Value);
        }
    }

    private void RaiseLocalizedTextProperties()
    {
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(IsKoreanLanguage));
        OnPropertyChanged(nameof(EnglishLanguageButtonOpacity));
        OnPropertyChanged(nameof(KoreanLanguageButtonOpacity));
        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(CurrentModeText));
        OnPropertyChanged(nameof(ManualRecordingButtonText));
        OnPropertyChanged(nameof(TrackingModeText));
        OnPropertyChanged(nameof(JetsonConnectionText));
        OnPropertyChanged(nameof(CurrentThreatText));
        OnPropertyChanged(nameof(PrimaryTargetText));
        OnPropertyChanged(nameof(PrimaryTargetShortText));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ContrastText));
    }

    private void RaiseAllCommandStates()
    {
        RaiseCommand(SetModeCommand);
        RaiseCommand(SelectPrimaryTargetCommand);
        RaiseCommand(ResetBrightnessCommand);
        RaiseCommand(ResetContrastCommand);
        RaiseCommand(ResetZoomCommand);
        RaiseCommand(ToggleManualRecordingCommand);
        RaiseCommand(MoveMotorCommand);
        RaiseCommand(SendMotorTargetCommand);
        RaiseCommand(AdjustMotorStepCommand);
        RaiseCommand(ToggleTrackingModeCommand);
    }

    private static void RaiseCommand(ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string NormalizeThreatLevel(string threatLevel)
    {
        return threatLevel.Trim().ToLowerInvariant() switch
        {
            "high" or "높음" => "\uB192\uC74C",
            "medium" or "mid" or "중간" => "\uC911\uAC04",
            _ => "\uB0AE\uC74C"
        };
    }
}
}
