namespace BroadcastControl.App.Services;

public readonly record struct VlmResultPacket(
    string ThreatLevel,
    string AnalysisMessage,
    string DetectionSummary,
    uint? FrameId,
    IReadOnlyDictionary<int, string> ObjectThreatLevels,
    DateTime ReceivedAt);
