using BroadcastControl.App.Models.Vlm;

namespace BroadcastControl.App.Services;

// VLM 분석 결과 수신 서비스의 계약입니다.
// VlmViewModel은 이 인터페이스로 결과 이벤트와 수신 포트를 확인합니다.
public interface IVlmService : IDisposable
{
    event EventHandler<VlmResultPacket>? ResultReceived;

    event EventHandler<string>? ReceiverError;

    int Port { get; }

    void Start();
}
