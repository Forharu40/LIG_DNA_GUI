using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpMotorStatusReceiverService : IDisposable
{
    private const int DefaultPort = 8001;
    private const int CurrentPacketSize = 18;
    private const int CurrentSnapshotSize = CurrentPacketSize * 2;
    private const int LegacyPacketSize = 32;
    private const int LegacySnapshotSize = LegacyPacketSize * 2;

    private readonly UdpClient _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;

    public UdpMotorStatusReceiverService(int? port = null)
    {
        Port = ResolvePort(port);
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
    }

    public event EventHandler<MotorStatusSnapshot>? StatusReceived;

    public event EventHandler<string>? ReceiverError;

    public int Port { get; }

    public void Start()
    {
        if (_receiveTask is { IsCompleted: false })
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _udpClient.Dispose();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The receive loop exits through cancellation or socket disposal during shutdown.
        }
        _cancellationTokenSource?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                if (TryParseSnapshot(result.Buffer, out var snapshot))
                {
                    StatusReceived?.Invoke(this, snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                ReceiverError?.Invoke(this, ex.Message);
            }
        }
    }

    private static bool TryParseSnapshot(byte[] buffer, out MotorStatusSnapshot snapshot)
    {
        snapshot = default;
        if (buffer.Length < CurrentSnapshotSize && buffer.Length < LegacyPacketSize)
        {
            return false;
        }

        var receivedAt = DateTime.Now;
        var isLegacyPacket = buffer.Length >= LegacySnapshotSize || buffer.Length == LegacyPacketSize;
        var packetSize = isLegacyPacket ? LegacyPacketSize : CurrentPacketSize;
        var pan = isLegacyPacket
            ? ParseLegacyPacket(buffer.AsSpan(0, packetSize), receivedAt)
            : ParseCurrentPacket(buffer.AsSpan(0, packetSize), receivedAt);
        MotorStatusPacket? tilt = null;
        if (buffer.Length >= packetSize * 2)
        {
            tilt = isLegacyPacket
                ? ParseLegacyPacket(buffer.AsSpan(packetSize, packetSize), receivedAt)
                : ParseCurrentPacket(buffer.AsSpan(packetSize, packetSize), receivedAt);
        }

        snapshot = new MotorStatusSnapshot(pan, tilt);
        return true;
    }

    private static MotorStatusPacket ParseCurrentPacket(ReadOnlySpan<byte> buffer, DateTime receivedAt)
    {
        var presentPosition = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(10, 4));
        var presentVelocity = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(6, 4));
        return new MotorStatusPacket(
            HardwareErrorStatus: buffer[17],
            PresentTemperature: buffer[16],
            PresentInputVoltageRaw: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(14, 2)),
            PresentPosition: presentPosition,
            PresentVelocity: presentVelocity,
            PresentCurrentRaw: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2)),
            PresentPwm: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2)),
            GoalPosition: presentPosition,
            GoalVelocity: 0,
            Moving: buffer[0],
            MovingStatus: buffer[1],
            ReceivedAt: receivedAt);
    }

    private static MotorStatusPacket ParseLegacyPacket(ReadOnlySpan<byte> buffer, DateTime receivedAt)
    {
        return new MotorStatusPacket(
            HardwareErrorStatus: buffer[0],
            PresentTemperature: buffer[1],
            PresentInputVoltageRaw: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2)),
            PresentPosition: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2)),
            PresentVelocity: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2)),
            PresentCurrentRaw: (ushort)Math.Max(0, (int)BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(8, 2))),
            PresentPwm: (ushort)Math.Max(0, (int)BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(10, 2))),
            GoalPosition: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(12, 2)),
            GoalVelocity: BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(14, 2)),
            Moving: buffer[16],
            MovingStatus: buffer[17],
            ReceivedAt: receivedAt);
    }

    private static int ResolvePort(int? port)
    {
        if (port is > 0 and <= 65535)
        {
            return port.Value;
        }

        var envPort = Environment.GetEnvironmentVariable("MOTOR_STATUS_PORT");
        return int.TryParse(envPort, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535
            ? parsedPort
            : DefaultPort;
    }
}

public readonly record struct MotorStatusSnapshot(
    MotorStatusPacket Pan,
    MotorStatusPacket? Tilt);

public readonly record struct MotorStatusPacket(
    byte HardwareErrorStatus,
    byte PresentTemperature,
    ushort PresentInputVoltageRaw,
    uint PresentPosition,
    uint PresentVelocity,
    ushort PresentCurrentRaw,
    ushort PresentPwm,
    uint GoalPosition,
    uint GoalVelocity,
    byte Moving,
    byte MovingStatus,
    DateTime ReceivedAt)
{
    public double PresentInputVoltage => PresentInputVoltageRaw / 10.0;
}
