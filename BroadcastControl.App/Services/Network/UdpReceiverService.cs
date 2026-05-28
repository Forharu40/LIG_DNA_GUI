using System.Net;
using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpReceiverService : IUdpReceiverService
{
    private readonly UdpClient _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;

    public UdpReceiverService(int port)
    {
        Port = port;
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
    }

    public event EventHandler<byte[]>? PacketReceived;

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
                PacketReceived?.Invoke(this, result.Buffer);
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
}
