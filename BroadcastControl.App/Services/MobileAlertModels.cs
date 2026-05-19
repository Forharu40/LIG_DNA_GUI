namespace BroadcastControl.App.Services;

public sealed record MobileAlertEvent(
    string Id,
    string CreatedAt,
    string Title,
    string VlmAnalysis,
    string DetectionSummary,
    string ThreatLevel,
    string EvidenceUrl);
