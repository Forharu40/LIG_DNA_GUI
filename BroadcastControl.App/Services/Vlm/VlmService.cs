using BroadcastControl.App.Models.Vlm;

namespace BroadcastControl.App.Services;

// VLM 결과 수신기를 ViewModel에서 쓰기 쉬운 서비스로 감싼 클래스입니다.
// UDP 수신기의 ResultReceived/ReceiverError 이벤트를 그대로 전달합니다.
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
