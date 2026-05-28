using BroadcastControl.App.Models.Motor;

namespace BroadcastControl.App.Services;

public interface IMotorCommandService : IDisposable
{
    string Host { get; }

    int Port { get; }

    void ConfigureEndpoint(string? host, int? port = null, int? trackingRecordingControlPort = null);

    bool TrySendMotorCommandPacket(
        byte mode,
        byte tracking,
        byte trackId,
        MotorButtonMask btnMask,
        ushort panPos,
        ushort tiltPos,
        byte scanStep,
        byte manualStep,
        bool isEoPrimary,
        out string? error);
}
