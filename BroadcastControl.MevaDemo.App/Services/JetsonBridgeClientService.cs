using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BroadcastControl.App.Services;

public sealed class JetsonBridgeClientService
{
    // Jetson C++ 브리지는 "한 줄에 JSON 1개" 형태의 UTF-8 텍스트를 주고받도록 맞춰져 있다.
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
        // 브리지는 줄바꿈 문자('\n')가 나올 때까지 읽기 때문에 요청 끝에 항상 줄바꿈을 붙인다.
        var payload = JsonSerializer.Serialize(request, SerializerOptions) + "\n";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        await stream.WriteAsync(payloadBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // 응답도 같은 방식으로 "JSON 1줄" 형태로 돌아온다.
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

        // 여기서는 HTTP처럼 무거운 프로토콜을 쓰지 않고,
        // C++ 브리지와 약속한 "JSON + 줄바꿈" 형식의 응답 한 줄만 읽는다.
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

// 아래 필드는 Jetson C++ 브리지가 돌려주는 JSON 구조와 1:1로 대응된다.
public sealed record JetsonBridgeResponse(
    string RequestId,
    string Status,
    string Response,
    string Error,
    long ElapsedMs);
