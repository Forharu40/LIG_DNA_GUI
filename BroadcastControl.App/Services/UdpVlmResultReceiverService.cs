using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BroadcastControl.App.Services;

public sealed class UdpVlmResultReceiverService : IDisposable
{
    private const int DefaultPort = 6002;

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
            // The receive loop exits through cancellation or socket disposal during shutdown.
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
        var text = DecodeText(buffer);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new VlmResultPacket(string.Empty, string.Empty, string.Empty, null, DateTime.Now);
        }

        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var threatLevel = ReadString(root, "threatLevel", "riskLevel", "risk", "threat", "level") ?? string.Empty;
                var analysisMessage =
                    ReadString(root, "analysisMessage", "vlmAnalysis", "analysis", "message", "result") ?? text;
                var detectionSummary =
                    ReadString(root, "detectionSummary", "detections", "tracks", "objects") ?? string.Empty;
                var frameId = ReadUInt(root, "frameId", "frame_id");

                return new VlmResultPacket(threatLevel, analysisMessage, detectionSummary, frameId, DateTime.Now);
            }
            catch (JsonException)
            {
                return new VlmResultPacket(string.Empty, text, string.Empty, null, DateTime.Now);
            }
        }

        return new VlmResultPacket(string.Empty, text, string.Empty, null, DateTime.Now);
    }

    private static string DecodeText(byte[] buffer)
    {
        var offset = 0;
        if (buffer.Length >= 4 && Encoding.ASCII.GetString(buffer, 0, 4) == "VLMR")
        {
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

public readonly record struct VlmResultPacket(
    string ThreatLevel,
    string AnalysisMessage,
    string DetectionSummary,
    uint? FrameId,
    DateTime ReceivedAt);
