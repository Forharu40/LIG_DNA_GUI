using BroadcastControl.App.Models.Motor;
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
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels.Camera;
using BroadcastControl.App.ViewModels.Mobile;
using BroadcastControl.App.ViewModels.Monitoring;
using BroadcastControl.App.ViewModels.Motor;
using BroadcastControl.App.ViewModels.Operation;
using BroadcastControl.App.ViewModels.Recording;
using BroadcastControl.App.ViewModels.Vlm;

namespace BroadcastControl.App.ViewModels.Motor
{

public sealed class MotorControlViewModel : ViewModelBase
{
    private double _panDegrees;
    private double _tiltDegrees;
    private ushort _targetPanRaw;
    private ushort _targetTiltRaw;
    private int _scanStep = 8;
    private int _manualStep = 8;
    private MotorButtonMask _activeButtons;

    public double PanDegrees
    {
        get => _panDegrees;
        set => SetProperty(ref _panDegrees, value);
    }

    public double TiltDegrees
    {
        get => _tiltDegrees;
        set => SetProperty(ref _tiltDegrees, value);
    }

    public ushort TargetPanRaw
    {
        get => _targetPanRaw;
        set => SetProperty(ref _targetPanRaw, value);
    }

    public ushort TargetTiltRaw
    {
        get => _targetTiltRaw;
        set => SetProperty(ref _targetTiltRaw, value);
    }

    public int ScanStep
    {
        get => _scanStep;
        set => SetProperty(ref _scanStep, Math.Clamp(value, 1, 10));
    }

    public int ManualStep
    {
        get => _manualStep;
        set => SetProperty(ref _manualStep, Math.Clamp(value, 1, 10));
    }

    public MotorButtonMask ActiveButtons
    {
        get => _activeButtons;
        set => SetProperty(ref _activeButtons, value);
    }
}
}

namespace BroadcastControl.App.ViewModels
{
public sealed partial class MainViewModel
{
    public int AutoMotorAngleSize
    {
        get => _autoMotorAngleSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _autoMotorAngleSize, normalized))
            {
                OnPropertyChanged(nameof(AutoMotorAngleSizeText));
            }
        }
    }

    public int ManualMotorAngleSize
    {
        get => _manualMotorAngleSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _manualMotorAngleSize, normalized))
            {
                OnPropertyChanged(nameof(ManualMotorAngleSizeText));
            }
        }
    }

    public string AutoMotorAngleSizeText => AutoMotorAngleSize.ToString(CultureInfo.InvariantCulture);

    public string ManualMotorAngleSizeText => ManualMotorAngleSize.ToString(CultureInfo.InvariantCulture);

    public bool IsTrackingModeEnabled
    {
        get => _isTrackingModeEnabled;
        private set
        {
            if (SetProperty(ref _isTrackingModeEnabled, value))
            {
                OnPropertyChanged(nameof(TrackingModeText));
                OnPropertyChanged(nameof(TrackingModeOpacity));
            }
        }
    }

    public string TrackingModeText => IsTrackingModeEnabled ? Text["TrackingOn"] : Text["TrackingOff"];

    public double TrackingModeOpacity => IsSystemPoweredOn
        ? (IsTrackingModeEnabled ? 1.0 : 0.42)
        : 0.32;

    public string PanMotorPositionText => (_panMotorPositionDegrees - 180).ToString("0.0", CultureInfo.InvariantCulture);

    public string TiltMotorPositionText => (_tiltMotorPositionDegrees - 90).ToString("0.0", CultureInfo.InvariantCulture);

    public bool IsMotorDetailsOpen
    {
        get => _isMotorDetailsOpen;
        set => SetProperty(ref _isMotorDetailsOpen, value);
    }

    public string MotorPanText => $"모터 좌우: {_motorPan:0.0}°";

    public string MotorTiltText => $"모터 상하: {_motorTilt:0.0}°";

    public string MotorTargetPanText
    {
        get => _motorTargetPanText;
        set => SetProperty(ref _motorTargetPanText, value);
    }

    public string MotorTargetTiltText
    {
        get => _motorTargetTiltText;
        set => SetProperty(ref _motorTargetTiltText, value);
    }

    public void InitializeMotorControlState()
    {
        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"초기 모터 제어 패킷 전송에 실패했습니다: {modeError}");
            return;
        }
    }

    public void MoveMotorStep(string direction)
    {
        if (!TryMapDirectionToButton(direction, out var buttons))
        {
            return;
        }

        UpdateManualButtonState(buttons);
    }

    public void SetMotorPosition(double panDegrees, double tiltDegrees)
    {
        _motorPan = NormalizeMotorDegrees(panDegrees, MotorPanLimitDegrees);
        _motorTilt = NormalizeMotorDegrees(tiltDegrees, MotorTiltLimitDegrees);
        _motorPanRaw = DegreesToDynamixelPosition(_motorPan);
        _motorTiltRaw = DegreesToDynamixelPosition(_motorTilt);
        _panMotorPositionDegrees = _motorPan;
        _tiltMotorPositionDegrees = _motorTilt;

        OnPropertyChanged(nameof(MotorPanText));
        OnPropertyChanged(nameof(MotorTiltText));
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(TiltMotorPositionText));
    }

    public void UpdateMotorStatus(MotorStatusSnapshot snapshot)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        UpdateMotorStatusItems(PanMotorStatusItems, snapshot.Pan);
        _panMotorFeedbackRaw = ClampMotorRaw((int)Math.Min(snapshot.Pan.PresentPosition, (uint)MotorRawMaximum));
        _panMotorPositionDegrees = DynamixelPositionToDegrees(snapshot.Pan.PresentPosition);
        _motorPanRaw = _panMotorFeedbackRaw.Value;
        _motorPan = NormalizeMotorDegrees(_panMotorPositionDegrees, MotorPanLimitDegrees);
        OnPropertyChanged(nameof(PanMotorPositionText));
        OnPropertyChanged(nameof(MotorPanText));
        if (snapshot.Tilt is { } tilt)
        {
            UpdateMotorStatusItems(TiltMotorStatusItems, tilt);
            _tiltMotorFeedbackRaw = ClampMotorRaw((int)Math.Min(tilt.PresentPosition, (uint)MotorRawMaximum));
            _tiltMotorPositionDegrees = DynamixelPositionToDegrees(tilt.PresentPosition);
            _motorTiltRaw = _tiltMotorFeedbackRaw.Value;
            _motorTilt = NormalizeMotorDegrees(_tiltMotorPositionDegrees, MotorTiltLimitDegrees);
            OnPropertyChanged(nameof(TiltMotorPositionText));
            OnPropertyChanged(nameof(MotorTiltText));
        }
    }

    private static void UpdateMotorStatusItems(ObservableCollection<MotorStatusItem> items, MotorStatusPacket packet)
    {
        SetMotorStatusValue(items, "Motor Value", packet.PresentPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Value", $"{DynamixelPositionToDegrees(packet.PresentPosition):0.0} deg");
        SetMotorStatusValue(items, "Motor Change Value", packet.GoalPosition.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Actual Change Value", $"{DynamixelPositionToDegrees(packet.GoalPosition):0.0} deg");
        SetMotorStatusValue(items, "Velocity", packet.PresentVelocity.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Current", packet.PresentCurrentRaw.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "PWM", packet.PresentPwm.ToString(CultureInfo.InvariantCulture));
        SetMotorStatusValue(items, "Temperature", $"{packet.PresentTemperature} C");
        SetMotorStatusValue(items, "Voltage", $"{packet.PresentInputVoltage:0.0} V");
        SetMotorStatusValue(items, "Moving", packet.Moving == 0 ? "Stop" : "Moving");
        SetMotorStatusValue(items, "Error Status", $"0x{packet.HardwareErrorStatus:X2}");
        SetMotorStatusValue(items, "Moving Status", $"0x{packet.MovingStatus:X2}");
        SetMotorStatusValue(items, "Last Update", packet.ReceivedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    public void UpdateManualButtonState(MotorButtonMask buttons)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        if (!IsManualMode)
        {
            return;
        }

        ApplyMotorButtonStateToCommandTarget(buttons);

        if (!TrySendMotorCommandPacket(out var modeError, buttons, syncFromFeedback: false))
        {
            AppendImportantLog($"모터 수동 제어 패킷 전송에 실패했습니다: {modeError}");
            return;
        }
    }

    private void MoveMotor(object? parameter)
    {
        if (!CanUseMotorControls || parameter is not string direction)
        {
            return;
        }

        if (!TryMapDirectionToButton(direction, out var buttons))
        {
            return;
        }

        UpdateManualButtonState(buttons);
    }

    private void SendMotorTargetAngles()
    {
        if (!CanUseMotorTargetControls)
        {
            return;
        }

        if (!double.TryParse(MotorTargetPanText, NumberStyles.Float, CultureInfo.InvariantCulture, out var panDegrees) ||
            !double.TryParse(MotorTargetTiltText, NumberStyles.Float, CultureInfo.InvariantCulture, out var tiltDegrees))
        {
            AppendImportantLog("모터 각도 입력값을 확인하세요. 예: 0, 45.5, 360");
            return;
        }

        panDegrees = NormalizeMotorDegrees(panDegrees, MotorPanLimitDegrees);
        tiltDegrees = NormalizeMotorDegrees(tiltDegrees, MotorTiltLimitDegrees);

        _motorPanRaw = DegreesToDynamixelPosition(panDegrees);
        _motorTiltRaw = DegreesToDynamixelPosition(tiltDegrees);

        if (!TrySendMotorCommandPacket(out var error, syncFromFeedback: false, forcedMode: 1))
        {
            AppendImportantLog($"모터 각도 전송에 실패했습니다: {error}");
            return;
        }

        MotorTargetPanText = string.Empty;
        MotorTargetTiltText = string.Empty;
        AppendImportantLog($"모터 각도 전송: pan {panDegrees:0.0}°, tilt {tiltDegrees:0.0}°");
    }

    private void AdjustMotorStep(object? parameter)
    {
        if (!TryParseMotorAngleParameter(parameter, out var mode, out var delta, out var resetToDefault))
        {
            return;
        }

        if (mode == MotorStepMode.Auto)
        {
            AutoMotorAngleSize = resetToDefault ? DefaultMotorAngleSize : AutoMotorAngleSize + delta;
        }
        else
        {
            ManualMotorAngleSize = resetToDefault ? DefaultMotorAngleSize : ManualMotorAngleSize + delta;
        }

        if (!TrySendMotorCommandPacket(out var error))
        {
            AppendImportantLog($"모터 속도 전송에 실패했습니다: {error}");
            return;
        }

    }

    private static bool TryParseMotorAngleParameter(
        object? parameter,
        out MotorStepMode mode,
        out int delta,
        out bool resetToDefault)
    {
        mode = MotorStepMode.Auto;
        delta = 0;
        resetToDefault = false;

        if (parameter is string text)
        {
            var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                mode = string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase)
                    ? MotorStepMode.Manual
                    : MotorStepMode.Auto;
                if (string.Equals(parts[2], "Reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetToDefault = true;
                    return true;
                }

                return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
            }

            if (parts.Length == 2)
            {
                if (string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[0], "Auto", StringComparison.OrdinalIgnoreCase))
                {
                    mode = string.Equals(parts[0], "Manual", StringComparison.OrdinalIgnoreCase)
                        ? MotorStepMode.Manual
                        : MotorStepMode.Auto;
                    if (string.Equals(parts[1], "Reset", StringComparison.OrdinalIgnoreCase))
                    {
                        resetToDefault = true;
                        return true;
                    }

                    return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
                }

                if (string.Equals(parts[1], "Reset", StringComparison.OrdinalIgnoreCase))
                {
                    resetToDefault = true;
                    return true;
                }

                return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) && delta != 0;
        }

        delta = parameter switch
        {
            int intValue => intValue,
            _ => 0
        };

        return delta != 0;
    }

    private static void SetMotorStatusValue(ObservableCollection<MotorStatusItem> items, string name, string value)
    {
        var item = items.FirstOrDefault(status => status.Name == name);
        if (item is not null)
        {
            item.Value = value;
        }
    }

    private static IEnumerable<MotorStatusItem> CreateDefaultMotorStatusItems()
    {
        var names = new[]
        {
            "Motor Value",
            "Actual Value",
            "Motor Change Value",
            "Actual Change Value",
            "Velocity",
            "Current",
            "PWM",
            "Temperature",
            "Voltage",
            "Moving",
            "Error Status",
            "Moving Status",
            "Last Update"
        };

        return names.Select(name => new MotorStatusItem(name, "-")).ToArray();
    }

    private static double NormalizeMotorDegrees(double degrees, double limit)
    {
        var rounded = Math.Round(degrees, 1, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, 0, limit);
    }

    private static double DynamixelPositionToDegrees(uint position)
    {
        return Math.Min(position, (uint)MotorRawMaximum) / MotorRawResolution * 360.0;
    }

    private static ushort DegreesToDynamixelPosition(double degrees)
    {
        var position = (int)Math.Round(Math.Clamp(degrees, 0, 360) / 360.0 * MotorRawResolution, MidpointRounding.AwayFromZero);
        return ClampMotorRaw(position);
    }

    private static ushort ClampMotorRaw(int position)
    {
        return (ushort)Math.Clamp(position, MotorRawMinimum, MotorRawMaximum);
    }

    private bool TrySendMotorCommandPacket(
        out string? error,
        MotorButtonMask buttons = MotorButtonMask.None,
        bool syncFromFeedback = true,
        byte? forcedMode = null)
    {
        if (syncFromFeedback)
        {
            SyncMotorRawFromFeedback();
        }

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        return _motorControlService.TrySendMotorCommandPacket(
            mode: forcedMode ?? (IsManualMode ? (byte)1 : (byte)0),
            tracking: IsTrackingModeEnabled ? (byte)1 : (byte)0,
            trackId: EncodeTrackId(),
            btnMask: buttons,
            panPos: _motorPanRaw,
            tiltPos: _motorTiltRaw,
            scanStep: (byte)MotorSpeedToStepDelta(AutoMotorAngleSize),
            manualStep: (byte)MotorSpeedToStepDelta(ManualMotorAngleSize),
            isEoPrimary: IsEoPrimary,
            out error);
    }

    private byte EncodeTrackId()
    {
        if (!ShouldSendTrackingToZybo)
        {
            return 0xFF;
        }

        if (_isUserSelectedTrackId && _yoloObjectId is >= 0 and <= 254)
        {
            return 0xFF;
            //return (byte)_yoloObjectId;
        }

        return 0xFF;
    }

    private bool ShouldSendTrackingToZybo =>
        IsTrackingModeEnabled &&
        _hasTrackedTarget &&
        _yoloObjectId >= 0;

    private readonly record struct TrackingCandidate(int ObjectId, int ThreatWeight, int Order);

    private bool SyncMotorRawFromFeedback()
    {
        if (_panMotorFeedbackRaw is not { } panRaw)
        {
            return false;
        }

        _motorPanRaw = panRaw;
        _motorPan = NormalizeMotorDegrees(DynamixelPositionToDegrees(panRaw), MotorPanLimitDegrees);
        if (_tiltMotorFeedbackRaw is { } tiltRaw)
        {
            _motorTiltRaw = tiltRaw;
            _motorTilt = NormalizeMotorDegrees(DynamixelPositionToDegrees(tiltRaw), MotorTiltLimitDegrees);
        }

        return true;
    }

    private static int GetThreatWeight(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "\uB192\uC74C" => 3,
            "\uC911\uAC04" => 2,
            _ => 1
        };
    }

    private static bool IsHighThreatLevel(string threatLevel)
    {
        return GetThreatWeight(threatLevel) >= 3;
    }

    private static int MotorSpeedToStepDelta(int motorSpeed)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        return Math.Clamp(motorSpeed, 1, 10);
    }

    private void ApplyMotorButtonStateToCommandTarget(MotorButtonMask buttons)
    {
        if ((buttons & MotorButtonMask.Center) == MotorButtonMask.Center)
        {
            _motorPanRaw = 0;
            _motorTiltRaw = 0;
        }
        else
        {
            var panRaw = _panMotorFeedbackRaw ?? _motorPanRaw;
            var tiltRaw = _tiltMotorFeedbackRaw ?? _motorTiltRaw;

            if ((buttons & MotorButtonMask.Left) == MotorButtonMask.Left)
            {
                panRaw = ClampMotorRaw(panRaw - MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Right) == MotorButtonMask.Right)
            {
                panRaw = ClampMotorRaw(panRaw + MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Up) == MotorButtonMask.Up)
            {
                tiltRaw = ClampMotorRaw(tiltRaw + MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            if ((buttons & MotorButtonMask.Down) == MotorButtonMask.Down)
            {
                tiltRaw = ClampMotorRaw(tiltRaw - MotorSpeedToStepDelta(ManualMotorAngleSize));
            }

            _motorPanRaw = panRaw;
            _motorTiltRaw = tiltRaw;
        }

    }

    private static bool TryMapDirectionToButton(string direction, out MotorButtonMask buttons)
    {
        buttons = direction switch
        {
            "Left" => MotorButtonMask.Left,
            "Right" => MotorButtonMask.Right,
            "Up" => MotorButtonMask.Up,
            "Down" => MotorButtonMask.Down,
            "Center" => MotorButtonMask.Center,
            _ => MotorButtonMask.None
        };

        return buttons != MotorButtonMask.None;
    }
}
}
