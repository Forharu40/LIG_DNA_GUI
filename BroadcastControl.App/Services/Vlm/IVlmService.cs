using BroadcastControl.App.Models.Vlm;

namespace BroadcastControl.App.Services;

public interface IVlmService : IDisposable
{
    event EventHandler<VlmResultPacket>? ResultReceived;

    event EventHandler<string>? ReceiverError;

    int Port { get; }

    void Start();
}
