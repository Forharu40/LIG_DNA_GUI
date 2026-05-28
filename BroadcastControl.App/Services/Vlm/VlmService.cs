using BroadcastControl.App.Models.Vlm;

namespace BroadcastControl.App.Services;

public sealed class VlmService : IVlmService
{
    private readonly UdpVlmResultReceiverService _receiverService;

    public VlmService(UdpVlmResultReceiverService? receiverService = null)
    {
        _receiverService = receiverService ?? new UdpVlmResultReceiverService();
        _receiverService.ResultReceived += (sender, packet) => ResultReceived?.Invoke(sender, packet);
        _receiverService.ReceiverError += (sender, message) => ReceiverError?.Invoke(sender, message);
    }

    public event EventHandler<VlmResultPacket>? ResultReceived;

    public event EventHandler<string>? ReceiverError;

    public int Port => _receiverService.Port;

    public void Start()
    {
        _receiverService.Start();
    }

    public void Dispose()
    {
        _receiverService.Dispose();
    }
}
