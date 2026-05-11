using System.Diagnostics;
using System.Globalization;

namespace BroadcastControl.App.Services;

public sealed class JetsonBridgeSshService : IDisposable
{
    private bool _startedByThisApp;

    public event Action<string>? MessageReady;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!GetBoolEnvironment("JETSON_AUTO_BRIDGE", true))
        {
            MessageReady?.Invoke("Jetson bridge auto-start is disabled. Set JETSON_AUTO_BRIDGE=true to enable it.");
            return false;
        }

        var user = GetEnvironment("JETSON_SSH_USER", "lig");
        var host = GetEnvironment("JETSON_SSH_HOST", "192.168.3.143");
        var target = $"{user}@{host}";
        var projectDir = GetEnvironment("JETSON_BRIDGE_DIR", "~/LIG_DNA_GUI/JetsonThor.RosCameraBridge");
        var guiHost = GetEnvironment("JETSON_GUI_HOST", "192.168.1.94");
        var recordingDir = GetEnvironment("JETSON_RECORDING_DIR", "/home/lig/Desktop/video");
        var segmentSeconds = GetEnvironment("RECORDING_SEGMENT_SECONDS", "60");
        var httpPort = GetEnvironment("RECORDING_HTTP_PORT", "8090");
        var buildArg = GetBoolEnvironment("JETSON_BRIDGE_BUILD", false) ? " --build" : string.Empty;

        var remoteCommand =
            $"cd {ShellQuote(projectDir)} && " +
            $"GUI_HOST={ShellQuote(guiHost)} " +
            $"JETSON_RECORDING_DIR={ShellQuote(recordingDir)} " +
            $"RECORDING_SEGMENT_SECONDS={ShellQuote(segmentSeconds)} " +
            $"RECORDING_HTTP_PORT={ShellQuote(httpPort)} " +
            $"nohup bash ./run_camera_udp_bridge.sh{buildArg} > ~/lig_gui_camera_bridge.log 2>&1 < /dev/null &";

        var result = await RunSshAsync(target, remoteCommand, TimeSpan.FromSeconds(10), cancellationToken);
        if (result.ExitCode == 0)
        {
            _startedByThisApp = true;
            MessageReady?.Invoke($"Jetson camera bridge start requested: {target}, GUI_HOST={guiHost}");
            return true;
        }

        MessageReady?.Invoke(
            "Jetson camera bridge auto-start failed. " +
            "Check SSH key login, Jetson path, and docker permission. " +
            $"exit={result.ExitCode}, {result.Output.Trim()}");
        return false;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_startedByThisApp || !GetBoolEnvironment("JETSON_AUTO_BRIDGE", true))
        {
            return;
        }

        var user = GetEnvironment("JETSON_SSH_USER", "lig");
        var host = GetEnvironment("JETSON_SSH_HOST", "192.168.3.143");
        var target = $"{user}@{host}";
        var containerName = GetEnvironment("JETSON_BRIDGE_CONTAINER", "gui_camera_bridge");
        var remoteCommand = $"docker rm -f {ShellQuote(containerName)} >/dev/null 2>&1 || true";

        var result = await RunSshAsync(target, remoteCommand, TimeSpan.FromSeconds(8), cancellationToken);
        if (result.ExitCode == 0)
        {
            MessageReady?.Invoke("Jetson camera bridge stopped.");
        }
        else
        {
            MessageReady?.Invoke($"Jetson camera bridge stop failed. exit={result.ExitCode}, {result.Output.Trim()}");
        }

        _startedByThisApp = false;
    }

    public void Dispose()
    {
    }

    private static async Task<CommandResult> RunSshAsync(
        string target,
        string remoteCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=5");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(remoteCommand);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new CommandResult(-1, "ssh process did not start.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var output = await outputTask;
            var error = await errorTask;
            return new CommandResult(process.ExitCode, string.Concat(output, error));
        }
        catch (OperationCanceledException)
        {
            return new CommandResult(-1, "ssh command timed out.");
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, ex.Message);
        }
    }

    private static string GetEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static bool GetBoolEnvironment(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private readonly record struct CommandResult(int ExitCode, string Output);
}
