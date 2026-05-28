namespace BroadcastControl.App.Services;

public interface IMobileAlertService : IDisposable
{
    int Port { get; }

    bool Start(int port);

    Task PublishAlertAsync(
        string title,
        string vlmAnalysis,
        string detectionSummary,
        string threatLevel,
        byte[]? evidencePng);
}
