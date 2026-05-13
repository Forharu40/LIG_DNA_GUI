using System.Buffers.Binary;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 3000;
    private const int MotorStatePacketSize = 12;

    private readonly UdpClient _udpClient = new();

    public UdpMotorControlService(string? host = null, int? port = null)
    {
        Host = ResolveHost(host);
        Port = ResolvePort(port);
    }

    public string Host { get; }

    public int Port { get; }

    public bool TrySendMotorStatePacket(
        bool isManualMode,
        bool isTrackingEnabled,
        double panDegrees,
        double tiltDegrees,
        int autoStepSize,
        int manualStepSize,
        int objectId,
        out string? error)
    {
        var packet = new byte[MotorStatePacketSize];
        packet[0] = isManualMode ? (byte)1 : (byte)0;
        packet[1] = isTrackingEnabled ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), EncodeDegrees(panDegrees));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), EncodeDegrees(tiltDegrees));
        packet[6] = EncodeStepSize(autoStepSize);
        packet[7] = EncodeStepSize(manualStepSize);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), objectId);
        return TrySendPacket(packet, out error);
    }

    public void Dispose()
    {
        _udpClient.Dispose();
    }

    private bool TrySendPacket(byte[] packet, out string? error)
    {
        try
        {
            _udpClient.Send(packet, packet.Length, Host, Port);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ushort EncodeDegrees(double degrees)
    {
        var tenths = (int)Math.Round(Math.Clamp(degrees, 0, 360) * 10, MidpointRounding.AwayFromZero);
        return (ushort)Math.Clamp(tenths, 0, 3600);
    }

    private static byte EncodeStepSize(int stepSize)
    {
        return (byte)Math.Clamp(stepSize, 1, 10);
    }

    private static string ResolveHost(string? host)
    {
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host.Trim();
        }

        var envHost = Environment.GetEnvironmentVariable("MOTOR_CONTROL_HOST");
        return string.IsNullOrWhiteSpace(envHost)
            ? DefaultHost
            : envHost.Trim();
    }

    private static int ResolvePort(int? port)
    {
        if (port is > 0 and <= 65535)
        {
            return port.Value;
        }

        var envPort = Environment.GetEnvironmentVariable("MOTOR_CONTROL_PORT");
        return int.TryParse(envPort, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535
            ? parsedPort
            : DefaultPort;
    }
}

[Flags]
public enum MotorButtonMask : byte
{
    None = 0,
    Left = 0x01,
    Right = 0x02,
    Up = 0x04,
    Down = 0x08,
    Center = 0x10
}
