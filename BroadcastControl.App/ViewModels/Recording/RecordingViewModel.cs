using System.Collections.ObjectModel;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Recording;

public sealed class RecordingViewModel : ViewModelBase
{
    private bool _isRecording;
    private string _recordingDirectory = string.Empty;

    public ObservableCollection<string> RecordedVideos { get; } = new();

    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    public string RecordingDirectory
    {
        get => _recordingDirectory;
        set => SetProperty(ref _recordingDirectory, value);
    }
}
