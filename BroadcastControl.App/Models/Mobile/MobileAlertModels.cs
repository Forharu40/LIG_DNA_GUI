namespace BroadcastControl.App.Models.Mobile;

// 모바일 경고 웹 페이지와 SSE 이벤트로 전달되는 위험 객체 알림 데이터입니다.
// VLM 분석 문장, YOLO 탐지 요약, 위험도, 증거 이미지 URL을 한 묶음으로 보냅니다.
public sealed record MobileAlertEvent(
    string Id,
    string CreatedAt,
    string Title,
    string VlmAnalysis,
    string DetectionSummary,
    string ThreatLevel,
    string EvidenceUrl);
