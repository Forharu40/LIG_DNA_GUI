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
// 녹화 화면에서 사용하는 ViewModel과 MainViewModel의 녹화/로그 관련 상태를 함께 둡니다.
// 녹화 상태, 수동 녹화, 분석 로그 저장, 시스템 로그 저장, 녹화 목록 표시를 담당합니다.

namespace BroadcastControl.App.ViewModels.Recording
{

/// <summary>
/// RecordedVideosView와 녹화 패널이 직접 참조할 수 있는 녹화 전용 상태입니다.
/// 실제 화면 공통 바인딩과 로그 저장 로직은 아래 MainViewModel partial에 있습니다.
/// </summary>
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
}

namespace BroadcastControl.App.ViewModels
{
// RecordingViewModel.cs 안에 둔 MainViewModel partial 영역입니다.
// 녹화와 로그 기능 코드를 Recording 폴더에 모아 기능 단위로 관리합니다.
public sealed partial class MainViewModel
{
    public bool IsRecordingActive =>
        IsSystemPoweredOn &&
        !_isRecordingSuppressed &&
        (IsManualRecordingEnabled || _isAutoRecordingLatched);

    public Brush RecordingIndicatorBrush => IsSystemPoweredOn && IsJetsonConnected ? RecordingOnBrush : RecordingOffBrush;

    public Brush RecordingTextBrush => IsSystemPoweredOn && IsJetsonConnected ? RecordingOnBrush : RecordingTextOffBrush;

    public double RecordingIndicatorOpacity => IsSystemPoweredOn && IsJetsonConnected ? 1.0 : 0.36;

    public bool IsManualRecordingEnabled
    {
        get => _isManualRecordingEnabled;
        private set
        {
            if (SetProperty(ref _isManualRecordingEnabled, value))
            {
                OnPropertyChanged(nameof(ManualRecordingButtonText));
                OnPropertyChanged(nameof(IsRecordingActive));
                OnPropertyChanged(nameof(RecordingIndicatorBrush));
                OnPropertyChanged(nameof(RecordingTextBrush));
                OnPropertyChanged(nameof(RecordingIndicatorOpacity));
            }
        }
    }

    public string ManualRecordingButtonText => IsRecordingActive ? Text["StopRecording"] : Text["StartRecording"];

    public void AppendImportantLog(string message)
    {
        AddSystemLogItem(new SystemLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
    }

    public void AppendAnalysisLog(string message)
    {
        AddAnalysisItem(new AnalysisItem(DateTime.Now.ToString("HH:mm:ss"), message));
    }

    public string BuildAnalysisLogSnapshot(DateTime startInclusive, DateTime endExclusive, bool includeAll)
    {
        var items = includeAll
            ? _analysisHistory.OrderBy(item => item.CreatedAt).ToArray()
            : _analysisHistory
                .Where(item => item.CreatedAt >= startInclusive && item.CreatedAt < endExclusive)
                .OrderBy(item => item.CreatedAt)
                .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("LIG DNA GUI VLM Analysis Result");
        builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!includeAll)
        {
            builder.AppendLine($"Window: {startInclusive:yyyy-MM-dd HH:mm:ss} - {endExclusive:yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();

        if (items.Length == 0)
        {
            builder.AppendLine("No VLM analysis result in this period.");
        }
        else
        {
            foreach (var item in items)
            {
                builder.AppendLine($"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.Message}");
            }
        }

        return builder.ToString();
    }

    public string BuildSystemLogSnapshot(DateTime startInclusive, DateTime endExclusive, bool includeAll)
    {
        var items = includeAll
            ? _systemLogHistory.OrderBy(item => item.CreatedAt).ToArray()
            : _systemLogHistory
                .Where(item => item.CreatedAt >= startInclusive && item.CreatedAt < endExclusive)
                .OrderBy(item => item.CreatedAt)
                .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("LIG DNA GUI System Log");
        builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!includeAll)
        {
            builder.AppendLine($"Window: {startInclusive:yyyy-MM-dd HH:mm:ss} - {endExclusive:yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();

        if (items.Length == 0)
        {
            builder.AppendLine("No system log in this period.");
        }
        else
        {
            foreach (var item in items)
            {
                builder.AppendLine($"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.Message}");
            }
        }

        return builder.ToString();
    }

    private void SaveAnalysisLogsToDesktop()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(desktopPath, $"analysis_log_{timestamp}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("LIG DNA GUI Situation Analysis Log");
            builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            foreach (var item in _analysisHistory)
            {
                builder.AppendLine($"[{item.Time}] {item.Message}");
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
            AppendImportantLog($"\uC0C1\uD669 \uBD84\uC11D \uAE30\uB85D\uC744 \uC800\uC7A5\uD588\uC2B5\uB2C8\uB2E4: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"\uC0C1\uD669 \uBD84\uC11D \uAE30\uB85D \uC800\uC7A5\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4: {ex.Message}");
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SaveSystemLogsToDesktop()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(desktopPath, $"system_log_{timestamp}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("LIG DNA GUI System Log");
            builder.AppendLine($"Saved At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();

            foreach (var log in _systemLogHistory)
            {
                builder.AppendLine($"[{log.Time}] {log.Message}");
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8\uB97C \uC800\uC7A5\uD588\uC2B5\uB2C8\uB2E4: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            AppendImportantLog($"\uC2DC\uC2A4\uD15C \uB85C\uADF8 \uC800\uC7A5\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4: {ex.Message}");
        }
    }

    private void ToggleManualRecording()
    {
        if (!IsSystemPoweredOn)
        {
            return;
        }

        if (IsRecordingActive)
        {
            _isRecordingSuppressed = true;
            _isAutoRecordingLatched = false;
            IsManualRecordingEnabled = false;
        }
        else
        {
            _isRecordingSuppressed = false;
            IsManualRecordingEnabled = true;
        }

        OnRecordingStateChanged();
    }

    private void OnRecordingStateChanged()
    {
        OnPropertyChanged(nameof(ManualRecordingButtonText));
        OnPropertyChanged(nameof(IsRecordingActive));
        OnPropertyChanged(nameof(RecordingIndicatorBrush));
        OnPropertyChanged(nameof(RecordingTextBrush));
        OnPropertyChanged(nameof(RecordingIndicatorOpacity));
    }

    private static void TrimCollection<T>(ObservableCollection<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static void TrimList<T>(List<T> items, int maxCount)
    {
        while (items.Count > maxCount)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private void AddAnalysisItem(AnalysisItem item)
    {
        _analysisHistory.Insert(0, item);
        TrimList(_analysisHistory, StoredLogItemLimit);

        AnalysisItems.Insert(0, item);
        TrimCollection(AnalysisItems, VisibleLogItemLimit);
    }

    private void AddSystemLogItem(SystemLogItem item)
    {
        _systemLogHistory.Insert(0, item);
        TrimList(_systemLogHistory, StoredLogItemLimit);

        SystemLogs.Insert(0, item);
        TrimCollection(SystemLogs, VisibleLogItemLimit);
    }
}
}
