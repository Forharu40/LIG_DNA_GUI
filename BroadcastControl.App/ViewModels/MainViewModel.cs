using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using BroadcastControl.App.Infrastructure;

namespace BroadcastControl.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _pipelineStatus = "LIVE";
    private string _trackingStatus = "Tracking Active";
    private string _modeName = "AUTO";
    private bool _isInferencePaused;

    public MainViewModel()
    {
        OtherFunctions = new ObservableCollection<FunctionCard>
        {
            new("Detection Summary", "3 quadcopters and 2 fixed-wing targets recognized in the active zone."),
            new("Priority Target", "FW-01 remains the highest-priority inbound object."),
            new("Route Analysis", "North-east sector movement is converging toward the inner line."),
            new("System Health", "Detector, tracker, and LLM summarizer are synchronized."),
        };

        ControlLogs = new ObservableCollection<LogItem>
        {
            new("10:05:00", "EO and IR feeds locked."),
            new("10:05:02", "Tracker initialized for FW-01."),
            new("10:05:05", "LLM summary refreshed from latest event bundle."),
            new("10:05:10", "Operator mode remains AUTO."),
        };

        StartPipelineCommand = new RelayCommand(_ => TogglePower());
        StopPipelineCommand = new RelayCommand(_ => SetPowerState(false));
        ToggleInferenceCommand = new RelayCommand(_ => ToggleTracking());
        RefreshSummaryCommand = new RelayCommand(_ => ChangeMode());
        SaveSnapshotCommand = new RelayCommand(_ => AddLog("Snapshot saved from active EO view."));
        EmergencyStopCommand = new RelayCommand(_ => EmergencyStop());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FunctionCard> OtherFunctions { get; }

    public ObservableCollection<LogItem> ControlLogs { get; }

    public ICommand StartPipelineCommand { get; }

    public ICommand StopPipelineCommand { get; }

    public ICommand ToggleInferenceCommand { get; }

    public ICommand RefreshSummaryCommand { get; }

    public ICommand SaveSnapshotCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public string MissionTitle => "LIG DNA GUI";

    public string HeaderSummary => "EO / IR dual monitoring, control panel, tracking actions, and side utilities";

    public string PipelineStatus
    {
        get => _pipelineStatus;
        private set => SetProperty(ref _pipelineStatus, value);
    }

    public string LiveSource => "NODE Z-9A / LOCAL SIMULATION";

    public string EOFeedLabel => "EO";

    public string IRFeedLabel => "IR";

    public string EOFeedSummary => "Electro-optical stream ready for daytime target review.";

    public string IRFeedSummary => "Infrared stream ready for thermal verification and night use.";

    public string ControlPanelTitle => "제어창";

    public string ControlPanelSummary => "Stream state, target status, and operator notes";

    public string CurrentTarget => "Current target: FW-01 / inbound from north-east";

    public string TrackingStatus
    {
        get => _trackingStatus;
        private set => SetProperty(ref _trackingStatus, value);
    }

    public string ModeName
    {
        get => _modeName;
        private set => SetProperty(ref _modeName, value);
    }

    public string PowerButtonLabel => "on/off";

    public string TrackingButtonLabel => _isInferencePaused ? "추적 재개" : "추적";

    public string ModeButtonLabel => "모드 변경";

    private void TogglePower()
    {
        SetPowerState(PipelineStatus != "LIVE");
    }

    private void SetPowerState(bool isLive)
    {
        PipelineStatus = isLive ? "LIVE" : "STANDBY";
        OnPropertyChanged(nameof(PowerButtonLabel));
        AddLog(isLive ? "System power changed to LIVE." : "System power changed to STANDBY.");
    }

    private void ToggleTracking()
    {
        _isInferencePaused = !_isInferencePaused;
        TrackingStatus = _isInferencePaused ? "Tracking Paused" : "Tracking Active";
        OnPropertyChanged(nameof(TrackingButtonLabel));
        AddLog(_isInferencePaused ? "Tracking paused by operator." : "Tracking resumed by operator.");
    }

    private void ChangeMode()
    {
        ModeName = ModeName == "AUTO" ? "MANUAL" : "AUTO";
        OnPropertyChanged(nameof(ModeButtonLabel));
        AddLog($"Control mode changed to {ModeName}.");
    }

    private void EmergencyStop()
    {
        PipelineStatus = "SAFE MODE";
        TrackingStatus = "Tracking Halted";
        OnPropertyChanged(nameof(PowerButtonLabel));
        OnPropertyChanged(nameof(TrackingButtonLabel));
        AddLog("Emergency stop executed.");
        MessageBox.Show("Emergency stop executed.\nSystem moved to safe mode.", "Emergency Stop");
    }

    private void AddLog(string message)
    {
        ControlLogs.Insert(0, new LogItem(DateTime.Now.ToString("HH:mm:ss"), message));
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

public sealed record FunctionCard(string Title, string Description);

public sealed record LogItem(string Time, string Message);
