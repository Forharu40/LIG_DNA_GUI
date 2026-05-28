using BroadcastControl.App.Models.Motor;

namespace BroadcastControl.App.Services;

public sealed class MotorCommandService : IMotorCommandService
{
    private readonly UdpMotorControlService _udpMotorControlService;

    public MotorCommandService(UdpMotorControlService? udpMotorControlService = null)
    {
        _udpMotorControlService = udpMotorControlService ?? new UdpMotorControlService();
    }

    public string Host => _udpMotorControlService.Host;

    public int Port => _udpMotorControlService.Port;

    public void ConfigureEndpoint(string? host, int? port = null, int? trackingRecordingControlPort = null)
    {
        _udpMotorControlService.ConfigureEndpoint(host, port, trackingRecordingControlPort);
    }

    public bool TrySendMotorCommandPacket(
        byte mode,
        byte tracking,
        byte trackId,
        MotorButtonMask btnMask,
        ushort panPos,
        ushort tiltPos,
        byte scanStep,
        byte manualStep,
        bool isEoPrimary,
        out string? error)
    {
        return _udpMotorControlService.TrySendMotorCommandPacket(
            mode,
            tracking,
            trackId,
            btnMask,
            panPos,
            tiltPos,
            scanStep,
            manualStep,
            isEoPrimary,
            out error);
    }

    public void Dispose()
    {
        _udpMotorControlService.Dispose();
    }
}
