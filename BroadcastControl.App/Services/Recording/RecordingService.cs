using System.Windows;

namespace BroadcastControl.App.Services;

public sealed class RecordingService : IRecordingService
{
    private readonly ViewportRecordingService _viewportRecordingService;

    public RecordingService(ViewportRecordingService? viewportRecordingService = null)
    {
        _viewportRecordingService = viewportRecordingService ?? new ViewportRecordingService();
    }

    public string? LastRecordingErrorMessage => _viewportRecordingService.LastRecordingErrorMessage;

    public int RecordedFrameCount => _viewportRecordingService.RecordedFrameCount;

    public string StartRecordingToDesktop(FrameworkElement target)
    {
        return _viewportRecordingService.StartRecordingToDesktop(target);
    }

    public string? StopRecording()
    {
        return _viewportRecordingService.StopRecording();
    }

    public void Dispose()
    {
        _viewportRecordingService.Dispose();
    }
}
