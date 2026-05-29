namespace BroadcastControl.App.Services;

public interface IUdpSenderService : IDisposable
{
    bool TrySend(byte[] packet, string host, int port, out string? error);
}
