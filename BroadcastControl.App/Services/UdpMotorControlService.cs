using System.Buffers.Binary;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

/// <summary>
/// GUI에서 Thor로 모터 제어 명령을 보내는 UDP 송신 서비스다.
/// MainViewModel이 만든 모드/추적/방향/각도/step size/YOLO 객체 ID 값을
/// Thor가 해석할 수 있는 13B little-endian 패킷으로 직렬화한다.
/// </summary>
public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 8000;
    private const int MotorCommandPacketSize = 13;

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
        int yoloObjectId,
        out string? error)
    {
        // GUI -> Thor 모터 제어 패킷:
        // 기존 9B 명령 뒤에 YOLO가 발행한 객체 ID(int32)를 붙여 모터가 어떤 객체를 따라갈지 알 수 있게 한다.
        var packet = new byte[MotorCommandPacketSize];
        packet[0] = mode;
        packet[1] = tracking;
        packet[2] = EncodeButtonMask(btnMask);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3, 2), panPos);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(5, 2), tiltPos);
        packet[7] = EncodeStepSize(scanStep);
        packet[8] = EncodeStepSize(manualStep);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(9, 4), yoloObjectId);
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
