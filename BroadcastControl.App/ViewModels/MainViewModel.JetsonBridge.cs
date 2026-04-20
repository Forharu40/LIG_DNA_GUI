using System.Globalization;

namespace BroadcastControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int DefaultJetsonPort = 7001;
    private readonly string _jetsonHost = Environment.GetEnvironmentVariable("JETSON_BRIDGE_HOST") ?? "127.0.0.1";
    private readonly int _jetsonPort = ParseJetsonPort();
    private string _jetsonResponse = "Jetson VLM response is disabled in MEVA demo mode.";
    private string _jetsonStatusText = "Jetson VLM loop is disabled in MEVA demo mode.";
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
        JetsonStatusText = $"MEVA demo mode is using UDP video only. TCP bridge {_jetsonHost}:{_jetsonPort} is disabled.";
    }

    public void StartJetsonHelloLoop()
    {
        JetsonStatusText = $"MEVA demo mode is using UDP video only. TCP bridge {_jetsonHost}:{_jetsonPort} is disabled.";
    }

    public void StopJetsonHelloLoop()
    {
        JetsonStatusText = $"MEVA demo mode is using UDP video only. TCP bridge {_jetsonHost}:{_jetsonPort} is disabled.";
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
