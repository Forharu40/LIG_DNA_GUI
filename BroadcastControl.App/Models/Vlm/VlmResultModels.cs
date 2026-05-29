// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
namespace BroadcastControl.App.Models.Vlm;

public readonly record struct VlmResultPacket(
    string ThreatLevel,
    string AnalysisMessage,
    string DetectionSummary,
    uint? FrameId,
    IReadOnlyDictionary<int, string> ObjectThreatLevels,
    DateTime ReceivedAt);
