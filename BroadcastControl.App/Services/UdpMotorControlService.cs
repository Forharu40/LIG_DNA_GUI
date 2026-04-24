using System.Net.Sockets;

namespace BroadcastControl.App.Services;

/// <summary>
/// 운용통제 GUI에서 미션 PC로 모터 제어용 UDP 패킷을 보내는 서비스다.
/// 현재 프로토콜은 2바이트 mode packet과 2바이트 button packet을 사용한다.
/// </summary>
public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 3000;

    private readonly UdpClient _udpClient = new();

    public UdpMotorControlService(string? host = null, int? port = null)
    {
        Host = ResolveHost(host);
        Port = ResolvePort(port);
    }

    public string Host { get; }

    public int Port { get; }

    public bool TrySendModePacket(MotorPacketMode mode, out string? error)
    {
        return TrySendPacket([0x01, (byte)mode], out error);
    }

    public bool TrySendButtonPacket(MotorButtonMask buttons, out string? error)
    {
        return TrySendPacket([0x02, (byte)buttons], out error);
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

public enum MotorPacketMode : byte
{
    Scan = 0,
    Tracking = 1,
    Manual = 2
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
