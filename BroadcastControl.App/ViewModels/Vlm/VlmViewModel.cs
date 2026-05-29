using System.Collections.ObjectModel;
using BroadcastControl.App.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BroadcastControl.App.Infrastructure;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels.Camera;
using BroadcastControl.App.ViewModels.Mobile;
using BroadcastControl.App.ViewModels.Monitoring;
using BroadcastControl.App.ViewModels.Motor;
using BroadcastControl.App.ViewModels.Operation;
using BroadcastControl.App.ViewModels.Recording;
using BroadcastControl.App.ViewModels.Vlm;

// 파일 역할:
// VLM/YOLO 분석 화면에서 사용하는 ViewModel과 MainViewModel의 탐지/위험도 상태를 함께 둡니다.
// 객체 ID, 객체 분류, 정확도, 위험도, VLM 분석 결과, 추적 대상 선택 로직을 담당합니다.

namespace BroadcastControl.App.ViewModels.Vlm
{

/// <summary>
/// VlmPanelView가 직접 참조할 수 있는 VLM 분석 상태입니다.
/// 화면 전체에 연결된 탐지/위험도 바인딩은 아래 MainViewModel partial에서 유지합니다.
/// </summary>
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
}

namespace BroadcastControl.App.ViewModels
{
// VlmViewModel.cs 안에 둔 MainViewModel partial 영역입니다.
// YOLO 탐지 결과와 VLM 위험도 처리 코드를 Vlm 폴더에 모아 관리합니다.
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
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
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
            AppendImportantLog($"큰 화면 위험 객체 추적 ID 선택: object {yoloObjectId}");
        }

        if (!TrySendMotorCommandPacket(out var modeError))
        {
            AppendImportantLog($"자동 모드 상태 전송에 실패했습니다: {modeError}");
            return;
        }

        if (IsAutoMode)
        {
            _lastAutomaticTrackingPacketSentAt = DateTime.Now;
        }
    }

    public void SelectYoloObject(int objectId, string threatLevel)
    {
        // 사용자가 큰 화면에서 특정 바운딩 박스를 클릭했을 때 호출합니다.
        // 현재는 선택한 객체 ID를 추적 대상으로 Jetson에 전송합니다.
        if (!IsSystemPoweredOn || objectId < 0)
        {
            return;
        }

        var isHighThreat = IsHighThreatLevel(threatLevel);
        //_hasTrackedTarget = isHighThreat;
        //_yoloObjectId = isHighThreat ?
        //_isUserSelectedTrackId = isHighThreat;
        //IsTrackingModeEnabled = true;
        _hasTrackedTarget = true;
        _yoloObjectId = objectId;
        _isUserSelectedTrackId = true;

        if (!TrySendMotorCommandPacket(out var error))
        {
            AppendImportantLog($"YOLO 객체 ID 전송에 실패했습니다: {error}");
            return;
        }

        AppendImportantLog(isHighThreat
            ? $"위험 객체 추적 ID 전송: object {objectId}"
            : $"선택한 객체가 위험 등급 높음이 아니므로 tracking=0으로 전송했습니다: object {objectId}");
    }

    public void ApplyVlmAnalysisResult(string threatLevel, string analysisMessage)
    {
        // VLM 결과를 상황 분석 창과 시스템 위험도에 반영합니다.
        // 위험도가 높음으로 올라가면 자동 모드 녹화 latch가 켜져 녹화 주기가 끝날 때까지 유지됩니다.
        var normalizedThreatLevel = NormalizeThreatLevel(threatLevel);
        var threatChanged = !string.Equals(CurrentThreatLevel, normalizedThreatLevel, StringComparison.Ordinal);
        CurrentThreatLevel = normalizedThreatLevel;

        if (!string.IsNullOrWhiteSpace(analysisMessage) &&
            !string.Equals(_lastAnalysisMessage, analysisMessage, StringComparison.Ordinal))
        {
            _lastAnalysisMessage = analysisMessage;
            AppendAnalysisLog(analysisMessage);
        }

        if (threatChanged)
        {
            AppendImportantLog($"위험 등급이 {CurrentThreatLevel}(으)로 변경되었습니다.");
        }
    }
}
}
