// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace BroadcastControl.App.Models.Network;

public sealed class AppNetworkSettings
{
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    private const string SettingsFileName = "LigDnaGui.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string JetsonHost { get; set; } = "192.168.3.143";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string PcGuiHost { get; set; } = "192.168.1.94";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public string JetsonRecordingDir { get; set; } = "/home/lig/Desktop/video";

    // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    public int EoUdpPort { get; set; } = 6000;

    public int IrUdpPort { get; set; } = 6001;

    public int DetectionUdpPort { get; set; } = 6002;

    public int VlmResultPort { get; set; } = 6003;

    public int MotorControlPort { get; set; } = 8000;

    public int TrackingRecordingControlPort { get; set; } = 8010;

    public int MotorStatusPort { get; set; } = 8001;

    public int MobileAlertPort { get; set; } = 8088;

    public int RecordingHttpPort { get; set; } = 8090;

    public int RecordingSegmentSeconds { get; set; } = 60;

    public string RecordedVideoUrl { get; set; } = "http://192.168.3.143:8090/";

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    public static AppNetworkSettings Load()
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        AppNetworkSettings settings;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppNetworkSettings>(json, JsonOptions) ?? new AppNetworkSettings();
            }
            else
            {
                settings = new AppNetworkSettings();
            }
        }
        catch
        {
            settings = new AppNetworkSettings();
        }

        settings.ApplyEnvironmentOverrides();
        settings.Normalize();
        settings.SaveIfMissing();
        return settings;
    }

    public void Save()
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        Normalize();
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToList();
    }

    private void ApplyEnvironmentOverrides()
    {
        JetsonHost = GetEnvironment("JETSON_HOST", JetsonHost);
        PcGuiHost = GetEnvironment("JETSON_GUI_HOST", PcGuiHost);
        JetsonRecordingDir = GetEnvironment("JETSON_RECORDING_DIR", JetsonRecordingDir);
        RecordedVideoUrl = GetEnvironment("JETSON_VIDEO_URL", RecordedVideoUrl);
        EoUdpPort = GetIntEnvironment("EO_GUI_PORT", EoUdpPort);
        IrUdpPort = GetIntEnvironment("IR_GUI_PORT", IrUdpPort);
        DetectionUdpPort = GetIntEnvironment("DETECTION_GUI_PORT", DetectionUdpPort);
        VlmResultPort = GetIntEnvironment("VLM_RESULT_PORT", VlmResultPort);
        MotorControlPort = GetIntEnvironment("MOTOR_CONTROL_PORT", MotorControlPort);
        TrackingRecordingControlPort = GetIntEnvironment("TRACKING_RECORDING_CONTROL_PORT", TrackingRecordingControlPort);
        MotorStatusPort = GetIntEnvironment("MOTOR_STATUS_PORT", MotorStatusPort);
        MobileAlertPort = GetIntEnvironment("MOBILE_ALERT_PORT", MobileAlertPort);
        RecordingHttpPort = GetIntEnvironment("RECORDING_HTTP_PORT", RecordingHttpPort);
        RecordingSegmentSeconds = GetIntEnvironment("RECORDING_SEGMENT_SECONDS", RecordingSegmentSeconds);
    }

    private void Normalize()
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        JetsonHost = Clean(JetsonHost, "192.168.3.143");
        PcGuiHost = Clean(PcGuiHost, "192.168.1.94");
        JetsonRecordingDir = Clean(JetsonRecordingDir, "/home/lig/Desktop/video");
        EoUdpPort = ClampPort(EoUdpPort, 6000);
        IrUdpPort = ClampPort(IrUdpPort, 6001);
        DetectionUdpPort = ClampPort(DetectionUdpPort, 6002);
        VlmResultPort = ClampPort(VlmResultPort, 6003);
        if (VlmResultPort == DetectionUdpPort)
        {
            VlmResultPort = 6003;
        }
        MotorControlPort = ClampPort(MotorControlPort, 8000);
        TrackingRecordingControlPort = ClampPort(TrackingRecordingControlPort, 8010);
        MotorStatusPort = ClampPort(MotorStatusPort, 8001);
        MobileAlertPort = ClampPort(MobileAlertPort, 8088);
        RecordingHttpPort = ClampPort(RecordingHttpPort, 8090);
        RecordingSegmentSeconds = Math.Clamp(RecordingSegmentSeconds, 10, 3600);
        RecordedVideoUrl = Clean(RecordedVideoUrl, $"http://{JetsonHost}:{RecordingHttpPort}/");
        if (!RecordedVideoUrl.EndsWith("/", StringComparison.Ordinal))
        {
            RecordedVideoUrl += "/";
        }
    }

    private void SaveIfMissing()
    {
        if (!File.Exists(SettingsPath))
        {
            Save();
        }
    }

    private static string Clean(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ClampPort(int port, int fallback)
    {
        return port is > 0 and <= 65535 ? port : fallback;
    }

    private static string GetEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetIntEnvironment(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;
    }

}
