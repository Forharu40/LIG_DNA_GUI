using System.Globalization;
using BroadcastControl.App.Services;

namespace BroadcastControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string JetsonDummyModel = "heartbeat";
    private const string JetsonDummyPrompt = "Hello";
    private const string DefaultJetsonHost = "127.0.0.1";
    private const int DefaultJetsonPort = 7001;
    private static readonly TimeSpan JetsonDummyInterval = TimeSpan.FromSeconds(20);

    private readonly JetsonBridgeClientService _jetsonBridgeClientService = new();

    private readonly string _jetsonHost = Environment.GetEnvironmentVariable("JETSON_BRIDGE_HOST") ?? DefaultJetsonHost;
    private readonly int _jetsonPort = ParseJetsonPort();
    private CancellationTokenSource? _jetsonLoopCancellationTokenSource;
    private Task? _jetsonLoopTask;
    private string _jetsonModel = JetsonDummyModel;
    private string _jetsonPrompt = JetsonDummyPrompt;
    private string _jetsonResponse = "Jetson VLM response will appear here.";
    private string _jetsonStatusText = "Test mode: sends dummy 'Hello' to Jetson every 20 seconds.";
    private string _jetsonLastRequestId = "-";
    private string _jetsonElapsedText = "-";
    private bool _isJetsonBusy;

    public string JetsonResponse
    {
        get => _jetsonResponse;
        private set => SetProperty(ref _jetsonResponse, value);
    }

    public string JetsonStatusText
    {
        get => _jetsonStatusText;
        private set => SetProperty(ref _jetsonStatusText, value);
    }

    public string JetsonLastRequestId
    {
        get => _jetsonLastRequestId;
        private set => SetProperty(ref _jetsonLastRequestId, value);
    }

    public string JetsonElapsedText
    {
        get => _jetsonElapsedText;
        private set => SetProperty(ref _jetsonElapsedText, value);
    }

    public bool IsJetsonBusy
    {
        get => _isJetsonBusy;
        private set => SetProperty(ref _isJetsonBusy, value);
    }

    private void InitializeJetsonBridge()
    {
        AnalysisItems.Clear();
        SystemLogs.Clear();
    }

    public void StartJetsonHelloLoop()
    {
        if (_jetsonLoopTask is not null)
        {
            return;
        }

        _jetsonLoopCancellationTokenSource = new CancellationTokenSource();
        _jetsonLoopTask = RunJetsonHelloLoopAsync(_jetsonLoopCancellationTokenSource.Token);
        JetsonStatusText = $"Jetson VLM hello loop started for {_jetsonHost}:{_jetsonPort}.";
        AppendImportantLog("Jetson VLM hello loop started.");
    }

    public void StopJetsonHelloLoop()
    {
        _jetsonLoopCancellationTokenSource?.Cancel();
        _jetsonLoopCancellationTokenSource?.Dispose();
        _jetsonLoopCancellationTokenSource = null;
        _jetsonLoopTask = null;
        JetsonStatusText = "Jetson VLM hello loop stopped.";
    }

    private async Task RunJetsonHelloLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendJetsonDummyHelloAsync(cancellationToken);

            using var timer = new PeriodicTimer(JetsonDummyInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SendJetsonDummyHelloAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Window close or manual shutdown.
        }
    }

    private async Task SendJetsonDummyHelloAsync(CancellationToken cancellationToken)
    {
        if (!IsSystemPoweredOn || IsJetsonBusy)
        {
            return;
        }

        _jetsonModel = JetsonDummyModel;
        _jetsonPrompt = JetsonDummyPrompt;

        IsJetsonBusy = true;
        JetsonStatusText = $"Sending '{_jetsonPrompt}' heartbeat to {_jetsonHost}:{_jetsonPort}...";
        AppendImportantLog($"Jetson VLM heartbeat started: {_jetsonPrompt} @ {_jetsonHost}:{_jetsonPort}");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            var response = await _jetsonBridgeClientService.SendPromptAsync(
                _jetsonHost,
                _jetsonPort,
                _jetsonModel,
                _jetsonPrompt,
                timeoutCts.Token);

            JetsonLastRequestId = response.RequestId;
            JetsonElapsedText = $"{response.ElapsedMs} ms";

            if (string.Equals(response.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                JetsonResponse = response.Response;
                JetsonStatusText = $"Jetson VLM response received in {response.ElapsedMs} ms.";
                AnalysisItems.Insert(
                    0,
                    new AnalysisItem(
                        DateTime.Now.ToString("HH:mm:ss"),
                        response.Response));
                TrimCollection(AnalysisItems, 8);
                AppendImportantLog($"Jetson VLM response received: {response.Response}");
            }
            else
            {
                JetsonResponse = response.Error;
                JetsonStatusText = "Jetson VLM bridge reported an error.";
                AnalysisItems.Insert(
                    0,
                    new AnalysisItem(
                        DateTime.Now.ToString("HH:mm:ss"),
                        $"Jetson VLM 오류: {response.Error}"));
                TrimCollection(AnalysisItems, 8);
                AppendImportantLog($"Jetson VLM error: {response.Error}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            JetsonStatusText = "Jetson VLM hello loop cancelled.";
        }
        catch (Exception ex)
        {
            JetsonStatusText = $"Request failed: {ex.Message}";
            JetsonResponse = ex.Message;
            AnalysisItems.Insert(
                0,
                new AnalysisItem(
                    DateTime.Now.ToString("HH:mm:ss"),
                    $"Jetson VLM 연결 실패: {ex.Message}"));
            TrimCollection(AnalysisItems, 8);
            AppendImportantLog($"Jetson VLM request failed: {ex.Message}");
        }
        finally
        {
            IsJetsonBusy = false;
        }
    }

    private static int ParseJetsonPort()
    {
        var raw = Environment.GetEnvironmentVariable("JETSON_BRIDGE_PORT");
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535
            ? port
            : DefaultJetsonPort;
    }
}
