using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _pipelineStatus = "LIVE / ANALYSIS ACTIVE";
    private bool _isInferencePaused;

    public MainViewModel()
    {
        DetectionMetrics = new ObservableCollection<MetricItem>
        {
            new("Threat Level", "Critical", "#FFFFC145"),
            new("Tracked Objects", "5 active", "#FF18C6B0"),
            new("LLM Cycle", "0.8 sec", "#FF7BD88F"),
            new("Tracking Zone", "North-East / 500m", "#FF8AB4FF"),
        };

        ActiveDetections = new ObservableCollection<DetectionItem>
        {
            new("FW-01", "Fixed-wing UAV", "High threat", 0.94, 620, 152, 110, 62, "North-East to South-West"),
            new("QC-02", "Quadcopter", "Medium threat", 0.91, 485, 138, 92, 56, "Holding near ridge line"),
            new("QC-03", "Quadcopter", "Medium threat", 0.88, 340, 188, 84, 54, "Moving west at low speed"),
            new("QC-04", "Quadcopter", "Low threat", 0.83, 248, 228, 80, 50, "Crossing main frame center"),
            new("QC-05", "Quadcopter", "Medium threat", 0.86, 422, 262, 88, 56, "Drifting south"),
        };

        Trajectories = new ObservableCollection<TrajectoryItem>
        {
            new("FW-01 path", 675, 182, 792, 244, "#FFFF6B6B"),
            new("QC-02 path", 533, 165, 612, 136, "#FFFFC145"),
            new("QC-03 path", 382, 216, 312, 238, "#FF18C6B0"),
        };

        CameraTiles = new ObservableCollection<CameraTile>
        {
            new("CCTV 1", "EO / perimeter lane", false),
            new("CCTV 2", "Thermal / right flank", false),
            new("CCTV 4", "Wide desert sector", false),
            new("CCTV 5", "Tracked target cluster", true),
            new("CCTV 7", "Fallback optics", false),
            new("CCTV 8", "Long zoom reserve", false),
        };

        RouteSummaries = new ObservableCollection<RouteSummary>
        {
            new("FW-01", "North-East", "Inner defense line", "Fast descending diagonal route"),
            new("QC-02", "Upper ridge", "Hold position", "Intermittent hover with decoy pattern"),
            new("QC-03", "West corridor", "South-West", "Slow lateral transit"),
        };

        AgentLogs = new ObservableCollection<LogItem>
        {
            new("10:05:00", "AI brief: 3 quadcopters and 2 fixed-wing objects in the north-east sector."),
            new("10:05:02", "Tracker lock refreshed on FW-01 with stable confidence 0.94."),
            new("10:05:05", "LLM summary updated: possible coordinated decoy pattern detected."),
            new("10:05:10", "Flight path estimate recalculated from recent track history."),
        };

        StartPipelineCommand = new RelayCommand(_ => AddLog("Streaming and analysis pipeline start requested."));
        StopPipelineCommand = new RelayCommand(_ => AddLog("Streaming and analysis pipeline stop requested."));
        ToggleInferenceCommand = new RelayCommand(_ => ToggleInference());
        RefreshSummaryCommand = new RelayCommand(_ => AddLog("LLM summary refresh requested from latest track bundle."));
        SaveSnapshotCommand = new RelayCommand(_ => AddLog("Annotated frame snapshot stored to review queue."));
        EmergencyStopCommand = new RelayCommand(_ => EmergencyStop());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MetricItem> DetectionMetrics { get; }

    public ObservableCollection<DetectionItem> ActiveDetections { get; }

    public ObservableCollection<TrajectoryItem> Trajectories { get; }

    public ObservableCollection<CameraTile> CameraTiles { get; }

    public ObservableCollection<RouteSummary> RouteSummaries { get; }

    public ObservableCollection<LogItem> AgentLogs { get; }

    public ICommand StartPipelineCommand { get; }

    public ICommand StopPipelineCommand { get; }

    public ICommand ToggleInferenceCommand { get; }

    public ICommand RefreshSummaryCommand { get; }

    public ICommand SaveSnapshotCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public string MissionTitle => "AI Aerial Tracking Console";

    public string HeaderSummary => "Realtime video stream, object detection overlay, route tracking, and LLM summary sidecar";

    public string PipelineStatus
    {
        get => _pipelineStatus;
        private set => SetProperty(ref _pipelineStatus, value);
    }

    public string LiveSource => "EO SIMULATION / NODE Z-9A";

    public string MissionClock => "2026-03-31 10:05:00";

    public string SensorReadout => "Target position: North-East / Distance: 500m / Zoom: 21x";

    public string LlmHeadline => "10:05 update: 3 quadcopters and 2 fixed-wing UAVs identified in the north-east sector. One fixed-wing object remains highest threat due to rapid inbound movement.";

    public string LlmNarrative => "The inference agent should not detect from raw pixels alone. A detector and tracker produce objects, confidence, and coordinates first. The LLM agent then converts those events into operator language, highlights threat priority, and explains route change or group behavior.";

    public string AnalystNote => "Recommended pipeline: camera ingest -> detector/tracker -> event buffer -> LLM summary -> WPF dashboard.";

    public string CurrentFocus => "Priority target FW-01 is descending toward the inner defense line.";

    public string InferenceButtonText => _isInferencePaused ? "Resume Inference" : "Pause Inference";

    private void ToggleInference()
    {
        _isInferencePaused = !_isInferencePaused;
        PipelineStatus = _isInferencePaused ? "LIVE / INFERENCE PAUSED" : "LIVE / ANALYSIS ACTIVE";
        OnPropertyChanged(nameof(InferenceButtonText));
        AddLog(_isInferencePaused
            ? "Inference paused. Stream remains live with last known tracks frozen."
            : "Inference resumed. Track and summary generation active.");
    }

    private void EmergencyStop()
    {
        PipelineStatus = "STREAM HALTED / SAFE MODE";
        AddLog("Emergency stop executed. Stream and analysis outputs moved into safe mode.");
        MessageBox.Show("Emergency stop executed.\nVideo stream and analysis outputs are now in safe mode.", "Emergency Stop");
    }

    private void AddLog(string message)
    {
        AgentLogs.Insert(0, new LogItem(DateTime.Now.ToString("HH:mm:ss"), message));
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record MetricItem(string Label, string Value, string AccentColor);

public sealed record DetectionItem(
    string Tag,
    string Classification,
    string Threat,
    double Confidence,
    double X,
    double Y,
    double Width,
    double Height,
    string MovementSummary);

public sealed record TrajectoryItem(
    string Label,
    double X1,
    double Y1,
    double X2,
    double Y2,
    string StrokeColor);

public sealed record CameraTile(string Title, string Subtitle, bool IsHighlighted);

public sealed record RouteSummary(string TargetId, string From, string To, string Note);

public sealed record LogItem(string Time, string Message);
