using System.Buffers.Binary;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 8000;
    private const int MotorCommandPacketSize = 9;

    private readonly UdpClient _udpClient = new();

    public UdpMotorControlService(string? host = null, int? port = null)
    {
        Host = ResolveHost(host);
        Port = ResolvePort(port);
    }

    public string Host { get; }

    public int Port { get; }

    public bool TrySendMotorCommandPacket(
        byte mode,
        byte tracking,
        MotorButtonMask btnMask,
        ushort panPos,
        ushort tiltPos,
        byte scanStep,
        byte manualStep,
        out string? error)
    {
        var packet = new byte[MotorCommandPacketSize];
        packet[0] = mode;
        packet[1] = tracking;
        packet[2] = EncodeButtonMask(btnMask);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3, 2), panPos);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(5, 2), tiltPos);
        packet[7] = EncodeStepSize(scanStep);
        packet[8] = EncodeStepSize(manualStep);
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

    private static byte EncodeStepSize(int stepSize)
    {
        return (byte)Math.Clamp(stepSize, 1, 10);
    }

    private static byte EncodeButtonMask(MotorButtonMask buttons)
    {
        return (byte)((byte)buttons & 0x0F);
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
    Right = 0x01,
    Left = 0x02,
    Up = 0x04,
    Down = 0x08,
    Center = 0x10
}
