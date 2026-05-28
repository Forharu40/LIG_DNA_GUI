using BroadcastControl.App.Models.Camera;

namespace BroadcastControl.App.Services;

public interface ICameraFrameService : IDisposable
{
    event Action<ReceivedVideoFrame>? FrameReady;

    event Action<DetectionPacket>? DetectionsReceived;

    event Action<YoloStatusPacket>? StatusReceived;

    int ListeningPort { get; }

    bool Start(int port);

    void Stop();
}
