// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
using System.Windows.Media.Imaging;

namespace BroadcastControl.App.Models.Camera;

public readonly record struct ReceivedVideoFrame(
    ulong StampNs,
    uint FrameIndex,
    ushort Width,
    ushort Height,
    BitmapSource Bitmap);

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string LabelText => $"{ClassName} object{ObjectId} ({Score:0.00})";
}

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
public enum DetectionStream
{
    Unknown = 0,
    Eo = 1,
    Ir = 2
}

public readonly record struct DetectionPacket(
    ulong StampNs,
    uint FrameId,
    int Width,
    int Height,
    IReadOnlyList<DetectionInfo> Detections,
    DetectionStream Stream = DetectionStream.Unknown,
    int ActiveTrackId = 0xFF);

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
public readonly record struct YoloStatusPacket(
    bool Enabled,
    bool ModelLoaded,
    float ConfThreshold,
    string LastError,
    string Source,
    ulong StampNs,
    uint FrameId);

// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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
