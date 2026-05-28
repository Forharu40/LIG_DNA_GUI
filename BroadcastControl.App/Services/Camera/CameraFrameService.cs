using BroadcastControl.App.Models.Camera;

namespace BroadcastControl.App.Services;

public sealed class CameraFrameService : ICameraFrameService
{
    private readonly UdpEncodedVideoReceiverService _receiver;

    public CameraFrameService(UdpEncodedVideoReceiverService receiver)
    {
        _receiver = receiver;
        _receiver.FrameReady += frame => FrameReady?.Invoke(frame);
        _receiver.DetectionsReceived += packet => DetectionsReceived?.Invoke(packet);
        _receiver.StatusReceived += packet => StatusReceived?.Invoke(packet);
    }

    public event Action<ReceivedVideoFrame>? FrameReady;

    public event Action<DetectionPacket>? DetectionsReceived;

    public event Action<YoloStatusPacket>? StatusReceived;

    public int ListeningPort => _receiver.ListeningPort;

    public bool Start(int port)
    {
        return _receiver.Start(port);
    }

    public void Stop()
    {
        _receiver.Stop();
    }

    public void Dispose()
    {
        _receiver.Dispose();
    }
}
