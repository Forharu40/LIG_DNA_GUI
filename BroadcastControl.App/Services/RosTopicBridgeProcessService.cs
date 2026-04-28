using System.Diagnostics;
using System.IO;

namespace BroadcastControl.App.Services;

public sealed class RosTopicBridgeProcessService : IDisposable
{
    private Process? _process;

    public event Action<string>? MessageReady;

    public bool IsRunning => _process is { HasExited: false };

    public bool Start(int eoPort, int irPort)
    {
        if (IsRunning)
        {
            return true;
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Ros", "ros_topic_gui_bridge.py");
        if (!File.Exists(scriptPath))
        {
            MessageReady?.Invoke($"ROS2 토픽 브릿지 스크립트를 찾을 수 없습니다: {scriptPath}");
            return false;
        }

        var startInfo = CreateStartInfo(scriptPath);
        if (startInfo is null)
        {
            return false;
        }

        startInfo.Environment["GUI_HOST"] = "127.0.0.1";
        startInfo.Environment["EO_GUI_PORT"] = eoPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["IR_GUI_PORT"] = irPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetDefaultEnvironment(startInfo, "EO_IMAGE_TOPIC", "/video/eo/preprocessed");
        SetDefaultEnvironment(startInfo, "IR_IMAGE_TOPIC", "/video/ir/preprocessed");
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
            _process.ErrorDataReceived += OnOutputDataReceived;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
            {
                MessageReady?.Invoke("ROS2 토픽 브릿지를 시작하지 못했습니다.");
                return false;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            MessageReady?.Invoke("ROS2 토픽 브릿지를 시작했습니다. /video/eo, /video/ir, /detections/eo, /detections/ir 토픽을 구독합니다.");
            return true;
        }
        catch (Exception ex)
        {
            MessageReady?.Invoke($"ROS2 토픽 브릿지 시작 실패: {ex.Message}");
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
            _process.ErrorDataReceived -= OnOutputDataReceived;
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
        }
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
                MessageReady?.Invoke($"ROS2_SETUP_BAT 파일을 찾을 수 없습니다: {ros2SetupBat}");
                return null;
            }

            return CreateBaseStartInfo(
                "cmd.exe",
                $"/c \"call \"{ros2SetupBat}\" && python \"{scriptPath}\"\"");
        }

        if (string.IsNullOrWhiteSpace(pythonExecutable))
        {
            pythonExecutable = "python";
            MessageReady?.Invoke("ROS2_PYTHON/ROS2_SETUP_BAT이 설정되지 않았습니다. 기본 python으로 ROS2 토픽 브릿지를 실행합니다.");
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
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            MessageReady?.Invoke($"ROS2 토픽 브릿지: {e.Data}");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process is { } process)
        {
            MessageReady?.Invoke($"ROS2 토픽 브릿지가 종료되었습니다. exit={process.ExitCode}");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
