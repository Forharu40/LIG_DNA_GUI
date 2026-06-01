namespace BroadcastControl.App.Services;

// 모바일 위험 알림 서비스의 계약입니다.
// VLM 위험 객체 감지 시 제목, 분석 문장, 탐지 요약, 증거 이미지를 모바일 웹으로 보냅니다.
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
