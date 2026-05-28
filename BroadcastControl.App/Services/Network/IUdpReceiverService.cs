namespace BroadcastControl.App.Services;

public interface IUdpReceiverService : IDisposable
{
    event EventHandler<byte[]>? PacketReceived;

    event EventHandler<string>? ReceiverError;

    int Port { get; }

    void Start();
}
