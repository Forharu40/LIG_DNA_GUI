using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Operation;

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
