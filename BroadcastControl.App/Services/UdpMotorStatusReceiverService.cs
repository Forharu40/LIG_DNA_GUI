using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpMotorStatusReceiverService : IDisposable
{
    private const int DefaultPort = 3001;
    private const int PacketSize = 32;

    private readonly UdpClient _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;

    public UdpMotorStatusReceiverService(int? port = null)
    {
        Port = ResolvePort(port);
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
    }

    public event EventHandler<MotorStatusPacket>? StatusReceived;

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
        _cancellationTokenSource?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                if (TryParsePacket(result.Buffer, out var packet))
                {
                    StatusReceived?.Invoke(this, packet);
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

    private static bool TryParsePacket(byte[] buffer, out MotorStatusPacket packet)
    {
        packet = default;
        if (buffer.Length < PacketSize)
        {
            return false;
        }

        packet = new MotorStatusPacket(
            HardwareErrorStatus: buffer[0],
            PresentTemperature: buffer[1],
            PresentInputVoltageRaw: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2, 2)),
            PresentPosition: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4, 2)),
            PresentVelocity: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6, 2)),
            PresentLoad: BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(8, 2)),
            PresentPwm: BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(10, 2)),
            GoalPosition: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(12, 2)),
            GoalVelocity: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14, 2)),
            Moving: buffer[16],
            MovingStatus: buffer[17],
            ReceivedAt: DateTime.Now);
        return true;
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

public readonly record struct MotorStatusPacket(
    byte HardwareErrorStatus,
    byte PresentTemperature,
    ushort PresentInputVoltageRaw,
    ushort PresentPosition,
    ushort PresentVelocity,
    short PresentLoad,
    short PresentPwm,
    ushort GoalPosition,
    ushort GoalVelocity,
    byte Moving,
    byte MovingStatus,
    DateTime ReceivedAt)
{
    public double PresentInputVoltage => PresentInputVoltageRaw / 10.0;
}
