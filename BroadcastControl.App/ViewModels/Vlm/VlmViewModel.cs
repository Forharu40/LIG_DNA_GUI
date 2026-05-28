using System.Collections.ObjectModel;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Vlm;

public sealed class VlmViewModel : ViewModelBase
{
    private string _latestAnalysis = string.Empty;
    private string _latestThreatLevel = "Low";

    public ObservableCollection<string> AnalysisHistory { get; } = new();

    public string LatestAnalysis
    {
        get => _latestAnalysis;
        set => SetProperty(ref _latestAnalysis, value);
    }

    public string LatestThreatLevel
    {
        get => _latestThreatLevel;
        set => SetProperty(ref _latestThreatLevel, value);
    }
}
