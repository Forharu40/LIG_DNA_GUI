namespace BroadcastControl.App.Services;

public sealed class MobileAlertService : IMobileAlertService
{
    private readonly MobileAlertHubService _hubService;

    public MobileAlertService(MobileAlertHubService? hubService = null)
    {
        _hubService = hubService ?? new MobileAlertHubService();
    }

    public int Port => _hubService.Port;

    public bool Start(int port)
    {
        return _hubService.Start(port);
    }

    public Task PublishAlertAsync(
        string title,
        string vlmAnalysis,
        string detectionSummary,
        string threatLevel,
        byte[]? evidencePng)
    {
        return _hubService.PublishAlertAsync(title, vlmAnalysis, detectionSummary, threatLevel, evidencePng);
    }

    public void Dispose()
    {
        _hubService.Dispose();
    }
}
