using System.Globalization;
using BroadcastControl.App.Services;

namespace BroadcastControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string OllamaExampleModel = "gemma3";
    private const string OllamaExamplePrompt = "Why is the sky blue?";
    private const string DefaultJetsonHost = "127.0.0.1";
    private const int DefaultJetsonPort = 7001;

    private readonly JetsonBridgeClientService _jetsonBridgeClientService = new();

    private readonly string _jetsonHost = Environment.GetEnvironmentVariable("JETSON_BRIDGE_HOST") ?? DefaultJetsonHost;
    private readonly int _jetsonPort = ParseJetsonPort();
    private string _jetsonModel = OllamaExampleModel;
    private string _jetsonPrompt = OllamaExamplePrompt;
    private string _jetsonResponse = "Jetson/Ollama response will appear here.";
    private string _jetsonStatusText = "Test mode: sends the official Ollama example prompt.";
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

    public async Task StartJetsonExampleProbeAsync()
    {
        if (!IsSystemPoweredOn || IsJetsonBusy)
        {
            return;
        }

        _jetsonModel = OllamaExampleModel;
        _jetsonPrompt = OllamaExamplePrompt;

        IsJetsonBusy = true;
        JetsonStatusText = $"Sending fixed Ollama example to {_jetsonHost}:{_jetsonPort}...";
        AppendImportantLog($"Jetson relay request started: {_jetsonModel} @ {_jetsonHost}:{_jetsonPort}");

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
                JetsonStatusText = $"Response received in {response.ElapsedMs} ms.";
                AnalysisItems.Insert(
                    0,
                    new AnalysisItem(
                        DateTime.Now.ToString("HH:mm:ss"),
                        $"Jetson/Ollama 응답: {response.Response}"));
                TrimCollection(AnalysisItems, 8);
                AppendImportantLog("Jetson relay response received successfully.");
            }
            else
            {
                JetsonResponse = response.Error;
                JetsonStatusText = "Jetson bridge reported an error.";
                AnalysisItems.Insert(
                    0,
                    new AnalysisItem(
                        DateTime.Now.ToString("HH:mm:ss"),
                        $"Jetson/Ollama 오류: {response.Error}"));
                TrimCollection(AnalysisItems, 8);
                AppendImportantLog($"Jetson relay error: {response.Error}");
            }
        }
        catch (Exception ex)
        {
            JetsonStatusText = $"Request failed: {ex.Message}";
            JetsonResponse = ex.Message;
            AnalysisItems.Insert(
                0,
                new AnalysisItem(
                    DateTime.Now.ToString("HH:mm:ss"),
                    $"Jetson/Ollama 연결 실패: {ex.Message}"));
            TrimCollection(AnalysisItems, 8);
            AppendImportantLog($"Jetson relay request failed: {ex.Message}");
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
