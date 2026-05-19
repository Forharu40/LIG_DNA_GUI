using System.Windows.Media.Imaging;

namespace BroadcastControl.App.Services;

public readonly record struct ReceivedVideoFrame(
    ulong StampNs,
    uint FrameIndex,
    ushort Width,
    ushort Height,
    BitmapSource Bitmap);

public readonly record struct DetectionInfo(
    string ClassName,
    float Score,
    float X1,
    float Y1,
    float X2,
    float Y2,
    int ObjectId,
    string ThreatLevel = "")
{
    public string LabelText => $"{ClassName} object{ObjectId} ({Score:0.00})";
}

public readonly record struct DetectionPacket(
    ulong StampNs,
    uint FrameId,
    int Width,
    int Height,
    IReadOnlyList<DetectionInfo> Detections);

public readonly record struct YoloStatusPacket(
    bool Enabled,
    bool ModelLoaded,
    float ConfThreshold,
    string LastError,
    string Source,
    ulong StampNs,
    uint FrameId);

public readonly record struct PlaybackSegmentInfo(
    uint ClipIndex,
    uint ClipCount,
    uint SegmentStartSeconds,
    uint SegmentEndSeconds,
    uint CurrentPlaybackSeconds,
    uint CycleIndex)
{
    public string ToLogMessage()
    {
        return $"MEVA video segment changed: clip {ClipIndex}/{ClipCount} now playing {FormatTime(SegmentStartSeconds)} ~ {FormatTime(SegmentEndSeconds)}";
    }

    public string ToLoopRestartLogMessage()
    {
        return $"MEVA video segment replay restarted: clip {ClipIndex}/{ClipCount} now replaying {FormatTime(SegmentStartSeconds)} ~ {FormatTime(SegmentEndSeconds)}";
    }

    public string GetSignature()
    {
        return $"{ClipIndex}:{ClipCount}:{SegmentStartSeconds}:{SegmentEndSeconds}";
    }

    private static string FormatTime(uint totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }
}
