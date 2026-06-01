using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BroadcastControl.App.Models.Vlm;

namespace BroadcastControl.App.Services;

// VLM 분석 결과 UDP 포트(기본 6003)를 열고 JSON 또는 일반 텍스트 결과를 VlmResultPacket으로 변환합니다.
// 객체별 위험도 맵은 위험 객체 우선순위 계산과 모바일 알림 생성에 사용됩니다.
public sealed class UdpVlmResultReceiverService : IDisposable
{
    private const int DefaultPort = 6003;

    private readonly UdpClient _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;

    public UdpVlmResultReceiverService(int? port = null)
    {
        Port = ResolvePort(port);
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
    }

    public event EventHandler<VlmResultPacket>? ResultReceived;

    public event EventHandler<string>? ReceiverError;

    public int Port { get; }

    public void Start()
    {
        if (_receiveTask is { IsCompleted: false })
        {
            return;
        }

        // 백그라운드 수신 루프를 시작해 VLM 결과가 도착할 때마다 ResultReceived 이벤트를 발생시킵니다.
        _cancellationTokenSource = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _udpClient.Dispose();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // 앱 종료 중 수신 루프가 소켓 해제로 끝나는 경우입니다.
        }

        _cancellationTokenSource?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                var packet = ParsePacket(result.Buffer);
                if (!string.IsNullOrWhiteSpace(packet.AnalysisMessage))
                {
                    ResultReceived?.Invoke(this, packet);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                ReceiverError?.Invoke(this, ex.Message);
            }
        }
    }

    private static VlmResultPacket ParsePacket(byte[] buffer)
    {
        // Jetson이 보내는 VLMR 헤더 또는 JSON 본문을 해석해 위험도와 분석 문장을 추출합니다.
        var text = DecodeText(buffer);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new VlmResultPacket(string.Empty, string.Empty, string.Empty, null, EmptyThreatMap(), DateTime.Now);
        }

        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                // 여러 버전의 필드명을 허용해 Jetson VLM 메시지 포맷 변경에 대응합니다.
                var threatLevel = ReadString(root, "threatLevel", "riskLevel", "risk", "threat", "level") ?? string.Empty;
                var analysisMessage =
                    ReadString(root, "analysisMessage", "vlmAnalysis", "analysis", "message", "result") ?? text;
                var detectionSummary =
                    ReadString(root, "detectionSummary", "detections", "tracks", "objects") ?? string.Empty;
                var frameId = ReadUInt(root, "frameId", "frame_id");
                var objectThreatLevels = ReadObjectThreatLevels(root);

                return new VlmResultPacket(threatLevel, analysisMessage, detectionSummary, frameId, objectThreatLevels, DateTime.Now);
            }
            catch (JsonException)
            {
                return new VlmResultPacket(string.Empty, text, string.Empty, null, EmptyThreatMap(), DateTime.Now);
            }
        }

        return new VlmResultPacket(string.Empty, text, string.Empty, null, EmptyThreatMap(), DateTime.Now);
    }

    private static string DecodeText(byte[] buffer)
    {
        var offset = 0;
        if (buffer.Length >= 4 && Encoding.ASCII.GetString(buffer, 0, 4) == "VLMR")
        {
            // "VLMR" magic이 붙은 패킷은 헤더 4바이트를 건너뛰고 UTF-8 본문만 읽습니다.
            offset = 4;
        }

        return Encoding.UTF8.GetString(buffer, offset, buffer.Length - offset).Trim('\0', ' ', '\r', '\n', '\t');
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
                    JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }

    private static uint? ReadUInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                uint.TryParse(value.GetString(), out var parsedNumber))
            {
                return parsedNumber;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<int, string> ReadObjectThreatLevels(JsonElement root)
    {
        // detections/tracks/objects 배열에서 object_id 또는 track_id별 위험도 문자열을 모읍니다.
        foreach (var name in new[] { "objectThreats", "object_threats", "trackThreats", "track_threats", "detections", "tracks", "objects" })
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var threats = new Dictionary<int, string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var objectId = ReadInt(item, "objectId", "object_id", "trackId", "track_id", "id");
                var threatLevel = ReadString(item, "threatLevel", "riskLevel", "risk", "threat", "level");
                if (objectId is null || string.IsNullOrWhiteSpace(threatLevel))
                {
                    continue;
                }

                threats[objectId.Value] = threatLevel;
            }

            if (threats.Count > 0)
            {
                return threats;
            }
        }

        return EmptyThreatMap();
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), out var parsedNumber))
            {
                return parsedNumber;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<int, string> EmptyThreatMap() => new Dictionary<int, string>();

    private static int ResolvePort(int? port)
    {
        if (port is > 0 and <= 65535)
        {
            return port.Value;
        }

        var envPort = Environment.GetEnvironmentVariable("VLM_RESULT_PORT");
        return int.TryParse(envPort, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535
            ? parsedPort
            : DefaultPort;
    }
}
