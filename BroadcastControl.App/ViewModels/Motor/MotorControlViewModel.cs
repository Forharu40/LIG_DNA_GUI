using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Motor;

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
