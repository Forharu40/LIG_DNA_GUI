using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace BroadcastControl.App.Services;

public sealed class AppNetworkSettings
{
    private const string SettingsFileName = "LigDnaGui.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string JetsonHost { get; set; } = "192.168.3.143";

    public string PcGuiHost { get; set; } = "192.168.1.94";

    public string JetsonSshUser { get; set; } = "lig";

    public string JetsonBridgeDir { get; set; } = "~/LIG_DNA_GUI/JetsonThor.RosCameraBridge";

    public string JetsonRecordingDir { get; set; } = "/home/lig/Desktop/video";

    public bool AutoStartBridge { get; set; } = true;

    public bool BuildBridgeOnStart { get; set; }

    public int EoUdpPort { get; set; } = 6000;

    public int IrUdpPort { get; set; } = 6001;

    public int VlmResultPort { get; set; } = 6002;

    public int MotorControlPort { get; set; } = 8000;

    public int MotorStatusPort { get; set; } = 8001;

    public int MobileAlertPort { get; set; } = 8088;

    public int RecordingHttpPort { get; set; } = 8090;

    public int RecordingSegmentSeconds { get; set; } = 60;

    public string RecordedVideoUrl { get; set; } = "http://192.168.3.143:8090/";

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    public static AppNetworkSettings Load()
    {
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
        JetsonHost = GetEnvironment("JETSON_SSH_HOST", JetsonHost);
        PcGuiHost = GetEnvironment("JETSON_GUI_HOST", PcGuiHost);
        JetsonSshUser = GetEnvironment("JETSON_SSH_USER", JetsonSshUser);
        JetsonBridgeDir = GetEnvironment("JETSON_BRIDGE_DIR", JetsonBridgeDir);
        JetsonRecordingDir = GetEnvironment("JETSON_RECORDING_DIR", JetsonRecordingDir);
        RecordedVideoUrl = GetEnvironment("JETSON_VIDEO_URL", RecordedVideoUrl);
        AutoStartBridge = GetBoolEnvironment("JETSON_AUTO_BRIDGE", AutoStartBridge);
        BuildBridgeOnStart = GetBoolEnvironment("JETSON_BRIDGE_BUILD", BuildBridgeOnStart);
        EoUdpPort = GetIntEnvironment("EO_GUI_PORT", EoUdpPort);
        IrUdpPort = GetIntEnvironment("IR_GUI_PORT", IrUdpPort);
        VlmResultPort = GetIntEnvironment("VLM_RESULT_PORT", VlmResultPort);
        MotorControlPort = GetIntEnvironment("MOTOR_CONTROL_PORT", MotorControlPort);
        MotorStatusPort = GetIntEnvironment("MOTOR_STATUS_PORT", MotorStatusPort);
        MobileAlertPort = GetIntEnvironment("MOBILE_ALERT_PORT", MobileAlertPort);
        RecordingHttpPort = GetIntEnvironment("RECORDING_HTTP_PORT", RecordingHttpPort);
        RecordingSegmentSeconds = GetIntEnvironment("RECORDING_SEGMENT_SECONDS", RecordingSegmentSeconds);
    }

    private void Normalize()
    {
        JetsonHost = Clean(JetsonHost, "192.168.3.143");
        PcGuiHost = Clean(PcGuiHost, "192.168.1.94");
        JetsonSshUser = Clean(JetsonSshUser, "lig");
        JetsonBridgeDir = Clean(JetsonBridgeDir, "~/LIG_DNA_GUI/JetsonThor.RosCameraBridge");
        JetsonRecordingDir = Clean(JetsonRecordingDir, "/home/lig/Desktop/video");
        EoUdpPort = ClampPort(EoUdpPort, 6000);
        IrUdpPort = ClampPort(IrUdpPort, 6001);
        VlmResultPort = ClampPort(VlmResultPort, 6002);
        MotorControlPort = ClampPort(MotorControlPort, 8000);
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

    private static bool GetBoolEnvironment(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }
}
