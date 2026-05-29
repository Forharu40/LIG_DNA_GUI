using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Mobile;

public sealed class MobileWebAppViewModel : ViewModelBase
{
    private bool _isAlertServerRunning;
    private int _alertServerPort = 8088;
    private string _latestEvidenceUrl = string.Empty;

    public bool IsAlertServerRunning
    {
        get => _isAlertServerRunning;
        set => SetProperty(ref _isAlertServerRunning, value);
    }

    public int AlertServerPort
    {
        get => _alertServerPort;
        set => SetProperty(ref _alertServerPort, value);
    }

    public string LatestEvidenceUrl
    {
        get => _latestEvidenceUrl;
        set => SetProperty(ref _latestEvidenceUrl, value);
    }
}
