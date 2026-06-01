using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace BroadcastControl.App.Models.Network;

// GUI와 Jetson 사이의 IP/포트 설정을 JSON 파일과 환경 변수에서 읽고 저장합니다.
// SettingsDrawerView의 Network 영역에서 수정한 GUI IP와 Jetson IP가 이 모델을 통해 각 UDP 서비스에 반영됩니다.
public sealed class AppNetworkSettings
{
    // 실행 파일 폴더에 저장되는 사용자 네트워크 설정 파일 이름입니다.
    private const string SettingsFileName = "LigDnaGui.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // GUI가 UDP 명령을 보낼 Jetson 주소입니다.
    public string JetsonHost { get; set; } = "192.168.3.143";

    // Jetson 브릿지가 EO/IR 영상과 탐지 결과를 송출해야 하는 GUI PC 주소입니다.
    public string PcGuiHost { get; set; } = "192.168.1.94";

    // Jetson 녹화 HTTP 서버가 파일 목록을 읽는 기본 저장 위치입니다.
    public string JetsonRecordingDir { get; set; } = "/home/lig/Desktop/video";

    // Jetson에서 GUI로 들어오는 EO 영상 UDP 포트입니다.
    public int EoUdpPort { get; set; } = 6000;

    // Jetson에서 GUI로 들어오는 IR 영상 UDP 포트입니다.
    public int IrUdpPort { get; set; } = 6001;

    // Jetson에서 GUI로 들어오는 EO/IR 탐지 결과 UDP 포트입니다.
    public int DetectionUdpPort { get; set; } = 6002;

    // VLM 분석 결과 JSON을 수신하는 UDP 포트입니다.
    public int VlmResultPort { get; set; } = 6003;

    // GUI가 Jetson gui_bridge로 11바이트 모터 커맨드 패킷을 보내는 포트입니다.
    public int MotorControlPort { get; set; } = 8000;

    // 위험 객체 추적 녹화 시작/중지 신호를 보내는 보조 제어 포트입니다.
    public int TrackingRecordingControlPort { get; set; } = 8010;

    // Jetson에서 GUI로 모터 상태 패킷을 송신하는 포트입니다.
    public int MotorStatusPort { get; set; } = 8001;

    // 모바일 위험 알림 HTTP/SSE 서버 포트입니다.
    public int MobileAlertPort { get; set; } = 8088;

    // Jetson 녹화 영상 목록과 파일을 제공하는 HTTP 서버 포트입니다.
    public int RecordingHttpPort { get; set; } = 8090;

    // Jetson 자동 녹화 파일이 몇 초 단위로 분할되는지 표시하기 위한 설정입니다.
    public int RecordingSegmentSeconds { get; set; } = 60;

    public string RecordedVideoUrl { get; set; } = "http://192.168.3.143:8090/";

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    public static AppNetworkSettings Load()
    {
        // 설정 파일이 있으면 읽고, 없거나 손상된 경우 기본값으로 시작합니다.
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
        // 저장 전 IP 문자열과 포트 범위를 정리해 다음 실행 때도 유효한 값만 사용합니다.
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
        // 네트워크 설정 드롭다운에 보여줄 실제 사용 가능한 GUI PC IPv4 주소 목록을 찾습니다.
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
        // 비어 있는 IP/경로는 기본값으로 되돌리고, 포트는 1~65535 범위 안으로 보정합니다.
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
