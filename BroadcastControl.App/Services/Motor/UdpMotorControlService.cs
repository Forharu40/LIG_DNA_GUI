using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using BroadcastControl.App.Models.Motor;

namespace BroadcastControl.App.Services;

/// <summary>
/// GUI에서 Jetson으로 모터 제어 UDP 패킷을 보내는 서비스입니다.
/// 8000/udp 모터 명령은 10바이트 고정 길이이며, 추적 녹화 제어는 8010/udp로 별도 전송합니다.
/// </summary>
public sealed class UdpMotorControlService : IDisposable
{
    private const string DefaultHost = "192.168.3.143";
    private const int DefaultPort = 8000;
    private const int DefaultTrackingRecordingControlPort = 8010;
    private const int TrackingRecordingPacketSize = 11;
    private static readonly byte[] TrackingRecordingPacketMagic = "TRCK"u8.ToArray();

    private readonly UdpClient _udpClient = new();
    private readonly object _endpointLock = new();
    private string _host;
    private int _port;
    private int _trackingRecordingControlPort;

    public UdpMotorControlService(string? host = null, int? port = null, int? trackingRecordingControlPort = null)
    {
        _host = ResolveHost(host);
        _port = ResolvePort(port);
        _trackingRecordingControlPort = ResolveTrackingRecordingControlPort(trackingRecordingControlPort);
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

    public void ConfigureEndpoint(string? host, int? port = null, int? trackingRecordingControlPort = null)
    {
        lock (_endpointLock)
        {
            _host = ResolveHost(host);
            _port = ResolvePort(port);
            _trackingRecordingControlPort = ResolveTrackingRecordingControlPort(trackingRecordingControlPort);
        }
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
        var packet = MotorPacketSerializer.CreateCommandPacket(
            mode,
            tracking,
            trackId,
            btnMask,
            panPos,
            tiltPos,
            scanStep,
            manualStep);

        if (!TrySendPacket(packet, out error))
        {
            return false;
        }

        TrySendTrackingRecordingPacket(tracking != 0, isEoPrimary, trackId == 0xFF ? -1 : trackId);
        return true;
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

    private void TrySendTrackingRecordingPacket(bool tracking, bool isEoPrimary, int yoloObjectId)
    {
        try
        {
            string host;
            int port;
            lock (_endpointLock)
            {
                host = _host;
                port = _trackingRecordingControlPort;
            }

            var packet = new byte[TrackingRecordingPacketSize];
            TrackingRecordingPacketMagic.CopyTo(packet, 0);
            packet[4] = tracking && yoloObjectId >= 0 ? (byte)1 : (byte)0;
            packet[5] = isEoPrimary ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(6, 4), yoloObjectId);
            _udpClient.Send(packet, packet.Length, host, port);
        }
        catch
        {
            // Tracking recording is auxiliary. Motor command success should not depend on this packet.
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

    private static int ResolveTrackingRecordingControlPort(int? port)
    {
        if (port is > 0 and <= 65535)
        {
            return port.Value;
        }

        var envPort = Environment.GetEnvironmentVariable("TRACKING_RECORDING_CONTROL_PORT");
        return int.TryParse(envPort, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535
            ? parsedPort
            : DefaultTrackingRecordingControlPort;
    }
}
