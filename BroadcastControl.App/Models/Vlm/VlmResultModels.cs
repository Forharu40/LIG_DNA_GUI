namespace BroadcastControl.App.Models.Vlm;

// VLM 결과 UDP 패킷을 GUI가 쓰기 쉬운 형태로 파싱한 데이터입니다.
// 전체 위험도, 분석 문장, 객체별 위험도 맵을 VlmViewModel과 모바일 알림 서비스가 함께 사용합니다.
public readonly record struct VlmResultPacket(
    string ThreatLevel,
    string AnalysisMessage,
    string DetectionSummary,
    uint? FrameId,
    IReadOnlyDictionary<int, string> ObjectThreatLevels,
    DateTime ReceivedAt);
