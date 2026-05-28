using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Models.Network;
using BroadcastControl.App.Models.Vlm;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;
        UpdateWindowModeButtonText();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ManualAnalysisSaveRequested += ViewModel_OnManualAnalysisSaveRequested;
        _viewModel.ManualSystemLogSaveRequested += ViewModel_OnManualSystemLogSaveRequested;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived += OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady += OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived += OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived += OnYoloStatusReceived;
        _detectionUdpReceiverService.DetectionsReceived += OnSharedDetectionsReceived;
        _detectionUdpReceiverService.StatusReceived += OnYoloStatusReceived;
        _motorStatusReceiverService.StatusReceived += OnMotorStatusReceived;
        _motorStatusReceiverService.ReceiverError += OnMotorStatusReceiverError;
        _vlmResultReceiverService.ResultReceived += OnVlmResultReceived;
        _vlmResultReceiverService.ReceiverError += OnVlmResultReceiverError;

        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
        _irUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _irUdpCaptureService.SetContrast(_viewModel.Contrast);
        _viewModel.InitializeMotorControlState();
        _motorStatusReceiverService.Start();
        _viewModel.AppendImportantLog($"紐⑦꽣 ?곹깭 ?섏떊 ?湲??ы듃: {_motorStatusReceiverService.Port}");
        _vlmResultReceiverService.Start();
        _viewModel.AppendImportantLog($"VLM 寃곌낵 ?섏떊 ?湲??ы듃: {_vlmResultReceiverService.Port}");

        _viewModel.UpdateViewportSize(CameraActiveView.CameraViewportElement.ActualWidth, CameraActiveView.CameraViewportElement.ActualHeight);
        UpdateRecordingViewportState();
        RenderDetectionOverlay();
        UpdateMotorAutomationState();
        _recordingMetadataWindowStart = DateTime.Now;
        _recordingMetadataTimer.Start();
        _jetsonConnectionTimer.Start();
        UpdateJetsonConnectionState();

        LoadNetworkSettingsEditor();
        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start(_networkSettings.EoUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the EO UDP stream receiver on port {_networkSettings.EoUdpPort}.");
        }

        if (_irUdpCaptureService.Start(_networkSettings.IrUdpPort))
        {
        }
        else
        {
            _viewModel.AppendImportantLog($"Failed to start the IR UDP stream receiver on port {_networkSettings.IrUdpPort}.");
        }

        if (_detectionUdpReceiverService.Start(_networkSettings.DetectionUdpPort))
        {
            _viewModel.AppendImportantLog($"EO/IR ?먯? 寃곌낵 ?섏떊 ?湲??ы듃: {_networkSettings.DetectionUdpPort}");
        }
        else
        {
            _viewModel.AppendImportantLog($"EO/IR ?먯? 寃곌낵 ?섏떊 ?ы듃 {_networkSettings.DetectionUdpPort}瑜??댁? 紐삵뻽?듬땲??");
        }

        if (_mobileAlertHubService.Start(_networkSettings.MobileAlertPort))
        {
            _viewModel.AppendImportantLog($"紐⑤컮???꾪뿕 ?뚮┝ ?깆씠 ?쒖옉?섏뿀?듬땲?? {_mobileAlertHubService.AccessHintUrls}");
        }
        else
        {
            _viewModel.AppendImportantLog($"紐⑤컮???꾪뿕 ?뚮┝ ???쒖옉???ㅽ뙣?덉뒿?덈떎. ?ы듃 {_networkSettings.MobileAlertPort}瑜??뺤씤?섏꽭??");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Brightness):
                _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
                _irUdpCaptureService.SetBrightness(_viewModel.Brightness);
                break;

            case nameof(MainViewModel.Contrast):
                _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
                _irUdpCaptureService.SetContrast(_viewModel.Contrast);
                break;

            case nameof(MainViewModel.IsRecordingActive):
                HandleRecordingActiveStateChanged();
                break;

            case nameof(MainViewModel.ZoomLevel):
            case nameof(MainViewModel.ZoomTransformX):
            case nameof(MainViewModel.ZoomTransformY):
            case nameof(MainViewModel.IsEoPrimary):
            case nameof(MainViewModel.SelectedPrimaryTarget):
                UpdateRecordingViewportState();
                RefreshPrimaryTrackingTarget();
                RenderDetectionOverlay(forceRefresh: true);
                break;

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: true);
                break;

            case nameof(MainViewModel.CurrentMode):
            case nameof(MainViewModel.IsSystemPoweredOn):
                UpdateMotorAutomationState();
                break;

            case nameof(MainViewModel.IsEnglishLanguage):
            case nameof(MainViewModel.IsKoreanLanguage):
                UpdateWindowModeButtonText();
                break;
        }
    }

    private void OnEoFrameReady(ReceivedVideoFrame frame)
    {
        MarkJetsonMessageReceived();
        _latestEoFrame = frame;
        CacheFrame(frame, _eoFrameCache);

        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
        }

        _viewModel.UpdateEoFrame(frame.Bitmap);
    }

    private void OnEoDetectionsReceived(DetectionPacket detectionPacket)
    {
        MarkJetsonMessageReceived();
        HandleDetectionsReceived(detectionPacket, _eoDetectionCache, _viewModel.IsEoPrimary);
    }

    private void OnIrDetectionsReceived(DetectionPacket detectionPacket)
    {
        MarkJetsonMessageReceived();
        HandleDetectionsReceived(detectionPacket, _irDetectionCache, !_viewModel.IsEoPrimary);
    }

    private void OnSharedDetectionsReceived(DetectionPacket detectionPacket)
    {
        MarkJetsonMessageReceived();
        switch (detectionPacket.Stream)
        {
            case DetectionStream.Eo:
                HandleDetectionsReceived(detectionPacket, _eoDetectionCache, _viewModel.IsEoPrimary);
                break;
            case DetectionStream.Ir:
                HandleDetectionsReceived(detectionPacket, _irDetectionCache, !_viewModel.IsEoPrimary);
                break;
        }
    }

    private void DetectionTargetList_OneSelctionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not DetectionTargetItem item)
        {
            return;
        }

        _viewModel.SelectYoloObject(item.ObjectId, item.ThreatLevel);
        listBox.SelectedItem = null;
    }
    private void HandleDetectionsReceived(
        DetectionPacket detectionPacket,
        Dictionary<uint, DetectionPacket> detectionCache,
        bool isPrimaryCamera)
    {
        CacheDetectionPacket(detectionPacket, detectionCache);

        if (!_hasReceivedDetectionPacket)
        {
            _hasReceivedDetectionPacket = true;
        }

        var displayDetections = EnsureDetectionObjectIds(
            ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections)));

        if (!_hasReceivedNonEmptyDetectionPacket && displayDetections.Count > 0)
        {
            _hasReceivedNonEmptyDetectionPacket = true;
        }

        if (detectionPacket.Detections.Count > 0 && displayDetections.Count == 0)
        {
            var filteredSignature = $"{_viewModel.SelectedPrimaryTarget}:{detectionPacket.FrameId}";
            if (!string.Equals(_lastFilteredOutTargetSignature, filteredSignature, StringComparison.Ordinal))
            {
                _lastFilteredOutTargetSignature = filteredSignature;
            }
        }
        else
        {
            _lastFilteredOutTargetSignature = null;
        }

        NotifyDetectionAlertIfNeeded(detectionPacket.FrameId, displayDetections);
        if (isPrimaryCamera)
        {
            _viewModel.UpdateDetectionSummary(displayDetections);
            _viewModel.UpdateDetectionTargets(BuildDetectionTargetItems(
                displayDetections,
                _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame,
                detectionPacket.Width,
                detectionPacket.Height));
        }

        RenderDetectionOverlay(forceRefresh: true);
        UpdateRiskAndMobileAlert(detectionPacket.FrameId, displayDetections);
    }

    private void RefreshPrimaryTrackingTarget()
    {
        if (!TryGetRenderableFrameAndDetection(out var frame, out var detectionPacket))
        {
            _viewModel.UpdateDetectionSummary(Array.Empty<DetectionInfo>());
            _viewModel.UpdateDetectionTargets(Array.Empty<DetectionTargetItem>());
            return;
        }

        var displayDetections = EnsureDetectionObjectIds(
            ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections)));
        _viewModel.UpdateDetectionSummary(displayDetections);
        _viewModel.UpdateDetectionTargets(BuildDetectionTargetItems(
            displayDetections,
            frame,
            detectionPacket.Width,
            detectionPacket.Height));
    }

    private void OnYoloStatusReceived(YoloStatusPacket statusPacket)
    {
        MarkJetsonMessageReceived();
        var signature = $"{statusPacket.Enabled}:{statusPacket.ModelLoaded}:{statusPacket.ConfThreshold}:{statusPacket.LastError}:{statusPacket.Source}";
        if (string.Equals(_lastStatusSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusSignature = signature;

        if (!statusPacket.ModelLoaded)
        {
            _viewModel.AppendImportantLog("YOLO 紐⑤뜽???꾩쭅 濡쒕뱶?섏? ?딆븯?듬땲??");
        }

        if (!string.IsNullOrWhiteSpace(statusPacket.LastError))
        {
            _viewModel.AppendImportantLog($"YOLO ?곹깭 ?ㅻ쪟: {statusPacket.LastError}");
        }
    }

    private void OnMotorStatusReceived(object? sender, MotorStatusSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            MarkJetsonMessageReceived();
            _viewModel.UpdateMotorStatus(snapshot);
        });
    }

    private void OnMotorStatusReceiverError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendImportantLog($"紐⑦꽣 ?곹깭 ?섏떊 ?ㅻ쪟: {message}"));
    }

    private void OnVlmResultReceived(object? sender, VlmResultPacket result)
    {
        Dispatcher.Invoke(() =>
        {
            MarkJetsonMessageReceived();
            if (!string.IsNullOrWhiteSpace(result.ThreatLevel))
            {
                _latestGlobalVlmThreatLevel = NormalizeThreatLevel(result.ThreatLevel);
            }

            foreach (var pair in result.ObjectThreatLevels)
            {
                _objectThreatLevels[pair.Key] = NormalizeThreatLevel(pair.Value);
            }

            var threatLevel = string.IsNullOrWhiteSpace(result.ThreatLevel)
                ? _viewModel.CurrentThreatLevel
                : result.ThreatLevel;
            var analysisMessage = string.IsNullOrWhiteSpace(result.DetectionSummary)
                ? result.AnalysisMessage
                : $"{result.AnalysisMessage} ?먯? ?댁슜: {result.DetectionSummary}";

            _viewModel.ApplyVlmAnalysisResult(threatLevel, analysisMessage);
            RefreshPrimaryTrackingTarget();
            RenderDetectionOverlay(forceRefresh: true);
        });
    }

    private void OnVlmResultReceiverError(object? sender, string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendImportantLog($"VLM 寃곌낵 ?섏떊 ?ㅻ쪟: {message}"));
    }

    private void OnIrFrameReady(ReceivedVideoFrame frame)
    {
        MarkJetsonMessageReceived();
        _latestIrFrame = frame;
        CacheFrame(frame, _irFrameCache);

        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
        }

        _viewModel.UpdateIrFrame(frame.Bitmap);
    }

    private void OnEoSegmentChanged(PlaybackSegmentInfo segmentInfo)
    {
    }

    private void OnEoSegmentLoopRestarted(PlaybackSegmentInfo segmentInfo)
    {
    }

    private void OnEoDiagnosticsMessageReady(string message)
    {
    }

    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraActiveView.CameraViewportElement.ActualWidth,
            CameraActiveView.CameraViewportElement.ActualHeight);
        _irUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraActiveView.CameraViewportElement.ActualWidth,
            CameraActiveView.CameraViewportElement.ActualHeight);
    }

    private void MarkJetsonMessageReceived()
    {
        _lastJetsonMessageAt = DateTime.Now;
        if (Dispatcher.CheckAccess())
        {
            UpdateJetsonConnectionState();
        }
        else
        {
            Dispatcher.BeginInvoke(UpdateJetsonConnectionState);
        }
    }

    private void JetsonConnectionTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateJetsonConnectionState();
    }

    private void UpdateJetsonConnectionState()
    {
        var isConnected =
            _lastJetsonMessageAt != DateTime.MinValue &&
            DateTime.Now - _lastJetsonMessageAt <= JetsonConnectionHoldTime;
        _viewModel.UpdateJetsonConnectionState(isConnected);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopManualMotorInput();
        _recordedVideoPositionTimer.Stop();
        _recordingMetadataTimer.Stop();
        _jetsonConnectionTimer.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ManualAnalysisSaveRequested -= ViewModel_OnManualAnalysisSaveRequested;
        _viewModel.ManualSystemLogSaveRequested -= ViewModel_OnManualSystemLogSaveRequested;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _eoUdpCaptureService.DetectionsReceived -= OnEoDetectionsReceived;
        _eoUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _irUdpCaptureService.FrameReady -= OnIrFrameReady;
        _irUdpCaptureService.DetectionsReceived -= OnIrDetectionsReceived;
        _irUdpCaptureService.StatusReceived -= OnYoloStatusReceived;
        _detectionUdpReceiverService.DetectionsReceived -= OnSharedDetectionsReceived;
        _detectionUdpReceiverService.StatusReceived -= OnYoloStatusReceived;
        _motorStatusReceiverService.StatusReceived -= OnMotorStatusReceived;
        _motorStatusReceiverService.ReceiverError -= OnMotorStatusReceiverError;
        _vlmResultReceiverService.ResultReceived -= OnVlmResultReceived;
        _vlmResultReceiverService.ReceiverError -= OnVlmResultReceiverError;
        _mobileAlertHubService.Dispose();
        _viewportRecordingService.Dispose();
        _eoUdpCaptureService.Dispose();
        _irUdpCaptureService.Dispose();
        _detectionUdpReceiverService.Dispose();
        _motorStatusReceiverService.Dispose();
        _vlmResultReceiverService.Dispose();
        _motorControlService.Dispose();
    }

}
