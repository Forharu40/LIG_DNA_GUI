namespace BroadcastControl.App.Services;

// 모바일 위험 알림 기능을 ViewModel에 제공하는 래퍼 서비스입니다.
// HTTP/SSE 서버 구현은 MobileAlertHubService에 위임합니다.
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
