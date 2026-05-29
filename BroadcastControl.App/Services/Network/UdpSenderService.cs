using System.Net.Sockets;

namespace BroadcastControl.App.Services;

public sealed class UdpSenderService : IUdpSenderService
{
    private readonly UdpClient _udpClient = new();

    public bool TrySend(byte[] packet, string host, int port, out string? error)
    {
        try
        {
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

    public void Dispose()
    {
        _udpClient.Dispose();
    }
}
