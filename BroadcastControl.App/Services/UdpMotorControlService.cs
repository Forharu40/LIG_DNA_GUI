using System.Buffers.Binary;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

/// <summary>
/// GUI에서 Thor로 모터 제어 명령을 보내는 UDP 송신 서비스다.
/// MainViewModel이 만든 모드/추적/방향/목표 각도/회전 각도 크기/YOLO 객체 ID 값을
/// Thor가 해석할 수 있는 13B little-endian 패킷으로 직렬화한다.
/// </summary>
public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 8000;
    private const int MotorCommandPacketSize = 13;

    private readonly UdpClient _udpClient = new();
    private readonly object _endpointLock = new();
    private string _host;
    private int _port;

    public UdpMotorControlService(string? host = null, int? port = null)
    {
        _host = ResolveHost(host);
        _port = ResolvePort(port);
    }

    public string Host
    {
        get
        {
            lock (_endpointLock)
            {
                return _host;
            }
        }
    }

    public int Port
    {
        get
        {
            lock (_endpointLock)
            {
                return _port;
            }
        }
    }

    public void ConfigureEndpoint(string? host, int? port = null)
    {
        lock (_endpointLock)
        {
            _host = ResolveHost(host);
            _port = ResolvePort(port);
        }
    }

    public bool TrySendMotorCommandPacket(
        byte mode,
        byte tracking,
        MotorButtonMask btnMask,
        ushort panPos,
        ushort tiltPos,
        byte scanStep,
        byte manualStep,
        int yoloObjectId,
        bool publishAngleCommand,
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
            string host;
            int port;
            lock (_endpointLock)
            {
                host = _host;
                port = _port;
            }

            _udpClient.Send(packet, packet.Length, host, port);
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
        return (byte)Math.Clamp(stepSize, 0, byte.MaxValue);
    }

    private static byte EncodeButtonMask(MotorButtonMask buttons)
    {
        return (byte)((byte)buttons & 0x1F);
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
