using System.Collections.ObjectModel;
using BroadcastControl.App.ViewModels;

// 파일 역할:
// MonitoringView의 System Status와 YOLO Targets 리스트에 표시할 요약 데이터를 보관합니다.
// 시스템 로그, 객체 ID/분류/정확도/위험도 목록, System 연결 상태 문구를 화면 바인딩으로 전달합니다.

namespace BroadcastControl.App.ViewModels.Monitoring;

/// <summary>
/// System Status 패널과 YOLO Targets 리스트가 읽는 상태입니다.
/// MainViewModel에서 갱신한 위험도, 주 탐지체, 연결 상태, 로그 목록을 모니터링 화면 형식으로 보관합니다.
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
