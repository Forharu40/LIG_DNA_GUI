using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BroadcastControl.App.Services;

public sealed class JetsonBridgeClientService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<JetsonBridgeResponse> SendPromptAsync(
        string host,
        int port,
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);

        await using var stream = client.GetStream();
        var request = new JetsonBridgeRequest(Guid.NewGuid().ToString("N"), model, prompt);
        var payload = JsonSerializer.Serialize(request, SerializerOptions) + "\n";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        await stream.WriteAsync(payloadBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var responseLine = await ReadLineAsync(stream, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new IOException("Jetson bridge returned an empty response.");
        }

        var response = JsonSerializer.Deserialize<JetsonBridgeResponse>(responseLine, SerializerOptions);
        if (response is null)
        {
            throw new InvalidOperationException("Jetson bridge returned invalid JSON.");
        }

        return response;
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(512);
        var singleByte = new byte[1];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(singleByte, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (singleByte[0] == '\n')
            {
                break;
            }

            if (singleByte[0] != '\r')
            {
                buffer.Add(singleByte[0]);
            }

            if (buffer.Count > 1024 * 1024)
            {
                throw new IOException("Jetson bridge response exceeded 1 MB.");
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

public sealed record JetsonBridgeRequest(string RequestId, string Model, string Prompt);

public sealed record JetsonBridgeResponse(
    string RequestId,
    string Status,
    string Response,
    string Error,
    long ElapsedMs);
