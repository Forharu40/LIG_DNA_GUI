using System.Collections.ObjectModel;
using System.Windows.Media;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.ViewModels;

// 파일 역할:
// MonitoringView의 System Status와 YOLO Targets 리스트에 표시할 요약 데이터를 보관합니다.
// 시스템 로그, 객체 ID/분류/정확도/위험도 목록, System 연결 상태 문구를 화면 바인딩으로 전달합니다.

namespace BroadcastControl.App.ViewModels.Monitoring
{
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
}

namespace BroadcastControl.App.ViewModels
{
// MonitoringViewModel.cs 안에 둔 MainViewModel partial 영역입니다.
// YOLO 탐지 결과를 기반으로 System Status의 위험도, YOLO Targets 목록, 모터 추적 대상 track_id를 갱신합니다.
public sealed partial class MainViewModel
{
    public string CurrentThreatLevel
    {
        get => _currentThreatLevel;
        private set
        {
            if (SetProperty(ref _currentThreatLevel, value))
            {
                if (value == "\uB192\uC74C" && IsAutoMode)
                {
                    _isAutoRecordingLatched = true;
                }

                OnPropertyChanged(nameof(CurrentThreatText));
                OnPropertyChanged(nameof(CurrentThreatBrush));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
                OnPropertyChanged(nameof(ManualRecordingButtonText));
            }
        }
    }

    public string CurrentThreatText => $"{Text["ThreatLevel"]}: {TranslateThreatLevel(CurrentThreatLevel)}";

    public Brush CurrentThreatBrush => CurrentThreatLevel switch
    {
        "\uB0AE\uC74C" => LowThreatBrush,
        "\uC911\uAC04" => MediumThreatBrush,
        _ => HighThreatBrush,
    };

    public string SelectedPrimaryTarget
    {
        get => _selectedPrimaryTarget;
        private set
        {
            if (SetProperty(ref _selectedPrimaryTarget, value))
            {
                OnPropertyChanged(nameof(PrimaryTargetText));
                OnPropertyChanged(nameof(PrimaryTargetShortText));
            }
        }
    }

    public string PrimaryTargetText => $"{Text["PrimaryTarget"]}: {TranslatePrimaryTarget(SelectedPrimaryTarget)}";

    public string PrimaryTargetShortText => $"{Text["PrimaryTarget"]}: {GetShortPrimaryTargetName(SelectedPrimaryTarget)}";

    public void UpdateDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        // 큰 화면의 YOLO 탐지 결과 중 위험도가 높은 객체를 자동 추적 후보로 사용합니다.
        CurrentThreatLevel = detections
            .OrderByDescending(detection => GetThreatWeight(detection.ThreatLevel))
            .Select(detection => NormalizeThreatLevel(detection.ThreatLevel))
            .FirstOrDefault("\uB0AE\uC74C");

        var highThreatCandidates = detections
            .Select((detection, index) => new TrackingCandidate(
                detection.ObjectId,
                GetThreatWeight(detection.ThreatLevel),
                index))
            .Where(candidate => candidate.ThreatWeight >= 3 && candidate.ObjectId >= 0)
            .OrderByDescending(candidate => candidate.ThreatWeight)
            .ThenBy(candidate => candidate.Order)
            .ToArray();

        var hasTrackedTarget = highThreatCandidates.Length > 0;
        var yoloObjectId = -1;
        if (hasTrackedTarget)
        {
            var highestThreatWeight = highThreatCandidates[0].ThreatWeight;
            var currentTarget = highThreatCandidates
                .Where(candidate =>
                    candidate.ObjectId == _yoloObjectId &&
                    candidate.ThreatWeight == highestThreatWeight)
                .Select(candidate => (TrackingCandidate?)candidate)
                .FirstOrDefault();
            yoloObjectId = currentTarget?.ObjectId ?? highThreatCandidates[0].ObjectId;
        }

        var targetChanged = _hasTrackedTarget != hasTrackedTarget || _yoloObjectId != yoloObjectId;
        var shouldRefreshAutomaticTracking =
            IsAutoMode &&
            DateTime.Now - _lastAutomaticTrackingPacketSentAt >= TimeSpan.FromMilliseconds(AutomaticTrackingResendMilliseconds);
        if (!targetChanged && !shouldRefreshAutomaticTracking)
        {
            return;
        }

        _hasTrackedTarget = hasTrackedTarget;
        _yoloObjectId = yoloObjectId;
        _isUserSelectedTrackId = false;

        if (targetChanged && hasTrackedTarget)
        {
            AppendImportantLog($"자동 추적 후보 ID 선택: object {yoloObjectId}");
        }

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"자동 추적 상태 전송에 실패했습니다: {modeError}");
            return;
        }

        if (IsAutoMode)
        {
            _lastAutomaticTrackingPacketSentAt = DateTime.Now;
        }
    }

    public void SelectYoloObject(int objectId, string threatLevel)
    {
        // 사용자가 큰 화면이나 YOLO Targets 리스트에서 객체를 선택하면 위험도와 상관없이 해당 객체를 추적 대상으로 선택합니다.
        if (!IsSystemPoweredOn || objectId < 0)
        {
            return;
        }

        var normalizedThreatLevel = NormalizeThreatLevel(threatLevel);
        _hasTrackedTarget = true;
        _yoloObjectId = objectId;
        _isUserSelectedTrackId = true;

        if (!TrySendMotorCommandPacket(out var error))
        {
            AppendImportantLog($"YOLO 객체 추적 ID 전송에 실패했습니다: {error}");
            return;
        }

        var trackingState = IsTrackingModeEnabled ? "tracking=1" : "tracking=0";
        var trackingNote = IsTrackingModeEnabled
            ? "추적 모드가 켜져 있어 선택 객체를 추적 대상으로 전송했습니다."
            : "추적 모드가 꺼져 있어 객체 ID는 저장했지만 추적 요청은 비활성 상태로 전송했습니다.";
        AppendImportantLog(
            $"YOLO 객체 선택: object {objectId}, 위험도 {TranslateThreatLevel(normalizedThreatLevel)}, {trackingState}. {trackingNote}");
    }
}
}
