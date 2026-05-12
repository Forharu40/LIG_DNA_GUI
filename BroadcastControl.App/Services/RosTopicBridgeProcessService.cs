using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BroadcastControl.App.Services;

public sealed class RosTopicBridgeProcessService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Process? _process;

    public event Action<string>? MessageReady;

    public event Action<string, byte[]>? PacketReceived;

    public bool IsRunning => _process is { HasExited: false };

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Ros", "ros_topic_gui_bridge.py");
        if (!File.Exists(scriptPath))
        {
            MessageReady?.Invoke($"ROS2 topic bridge script was not found: {scriptPath}");
            return false;
        }

        var startInfo = CreateStartInfo(scriptPath);
        if (startInfo is null)
        {
            return false;
        }

        SetDefaultEnvironment(startInfo, "EO_IMAGE_TOPIC", "/video/eo/preprocessed");
        SetDefaultEnvironment(startInfo, "IR_IMAGE_TOPIC", "/camera/ir");
        SetDefaultEnvironment(startInfo, "EO_DETECTION_TOPIC", "/detections/eo");
        SetDefaultEnvironment(startInfo, "IR_DETECTION_TOPIC", "/detections/ir");

        try
        {
            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
            {
                MessageReady?.Invoke("Failed to start the ROS2 topic bridge process.");
                return false;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            MessageReady?.Invoke("ROS2 topic bridge started. GUI video UDP sockets are disabled.");
            return true;
        }
        catch (Exception ex)
        {
            MessageReady?.Invoke($"ROS2 topic bridge start failed: {ex.Message}");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch
        {
        }
        finally
        {
            _process.OutputDataReceived -= OnOutputDataReceived;
            _process.ErrorDataReceived -= OnErrorDataReceived;
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static void SetDefaultEnvironment(ProcessStartInfo startInfo, string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        startInfo.Environment[name] = string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private ProcessStartInfo? CreateStartInfo(string scriptPath)
    {
        var ros2SetupBat = Environment.GetEnvironmentVariable("ROS2_SETUP_BAT");
        var pythonExecutable = Environment.GetEnvironmentVariable("ROS2_PYTHON");

        if (!string.IsNullOrWhiteSpace(ros2SetupBat))
        {
            if (!File.Exists(ros2SetupBat))
            {
                MessageReady?.Invoke($"ROS2_SETUP_BAT file was not found: {ros2SetupBat}");
                return null;
            }

            return CreateBaseStartInfo(
                "cmd.exe",
                $"/d /s /c \"\"call \"{ros2SetupBat}\" && python \"{scriptPath}\"\"\"");
        }

        if (string.IsNullOrWhiteSpace(pythonExecutable))
        {
            pythonExecutable = "python";
            MessageReady?.Invoke("ROS2_PYTHON/ROS2_SETUP_BAT is not set. Trying the default python executable.");
        }

        return CreateBaseStartInfo(pythonExecutable, $"\"{scriptPath}\"");
    }

    private static ProcessStartInfo CreateBaseStartInfo(string fileName, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        if (!TryHandleBridgePacket(e.Data))
        {
            MessageReady?.Invoke($"ROS2 topic bridge: {e.Data}");
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            MessageReady?.Invoke($"ROS2 topic bridge: {e.Data}");
        }
    }

    private bool TryHandleBridgePacket(string line)
    {
        try
        {
            var packet = JsonSerializer.Deserialize<RosBridgePacket>(line, JsonOptions);
            if (packet is null ||
                string.IsNullOrWhiteSpace(packet.Stream) ||
                string.IsNullOrWhiteSpace(packet.Packet))
            {
                return false;
            }

            var bytes = Convert.FromBase64String(packet.Packet);
            PacketReceived?.Invoke(packet.Stream, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process is { } process)
        {
            MessageReady?.Invoke($"ROS2 topic bridge exited. exit={process.ExitCode}");
        }
    }

    private sealed record RosBridgePacket
    {
        public string Stream { get; init; } = string.Empty;

        public string Packet { get; init; } = string.Empty;
    }
}
