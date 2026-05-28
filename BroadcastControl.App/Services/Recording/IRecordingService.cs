using System.Windows;

namespace BroadcastControl.App.Services;

public interface IRecordingService : IDisposable
{
    string? LastRecordingErrorMessage { get; }

    int RecordedFrameCount { get; }

    string StartRecordingToDesktop(FrameworkElement target);

    string? StopRecording();
}
