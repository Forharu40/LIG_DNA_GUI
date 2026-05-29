// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
namespace BroadcastControl.App.Models.Mobile;

public sealed record MobileAlertEvent(
    string Id,
    string CreatedAt,
    string Title,
    string VlmAnalysis,
    string DetectionSummary,
    string ThreatLevel,
    string EvidenceUrl);
