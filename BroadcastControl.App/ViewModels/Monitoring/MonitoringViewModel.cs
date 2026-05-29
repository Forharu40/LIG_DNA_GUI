using System.Collections.ObjectModel;
using BroadcastControl.App.ViewModels;

// 파일 역할:
// 모니터링 화면에서 독립적으로 표시할 상태를 보관합니다.
// 시스템 로그, YOLO 타겟 요약, 위험도와 System 연결 상태를 View에 전달합니다.

namespace BroadcastControl.App.ViewModels.Monitoring;

/// <summary>
/// MonitoringView 전용 표시 상태입니다.
/// MainViewModel에서 갱신한 시스템 상태를 모니터링 화면이 읽기 좋은 형태로 보관합니다.
/// </summary>
public sealed class MonitoringViewModel : ViewModelBase
{
    private string _threatLevel = "Low";
    private string _targetSummary = "Composite";
    private bool _isJetsonConnected;

    public ObservableCollection<string> SystemLogs { get; } = new();

    public ObservableCollection<string> YoloTargets { get; } = new();

    public string ThreatLevel
    {
        get => _threatLevel;
        set => SetProperty(ref _threatLevel, value);
    }

    public string TargetSummary
    {
        get => _targetSummary;
        set => SetProperty(ref _targetSummary, value);
    }

    public bool IsJetsonConnected
    {
        get => _isJetsonConnected;
        set => SetProperty(ref _isJetsonConnected, value);
    }
}
