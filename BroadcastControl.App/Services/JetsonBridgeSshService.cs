using System.Diagnostics;
using System.Globalization;

namespace BroadcastControl.App.Services;

/// <summary>
/// GUI 실행 시 Jetson에 SSH로 접속해 gui_camera_bridge 컨테이너 실행을 요청하는 서비스다.
/// SSH 키 로그인과 Docker 권한이 준비되어 있으면 사용자가 Jetson 터미널을 따로 열지 않아도 bridge를 시작할 수 있다.
/// </summary>
public sealed class JetsonBridgeSshService : IDisposable
{
    private readonly AppNetworkSettings _settings;
    private bool _startedByThisApp;

    public JetsonBridgeSshService(AppNetworkSettings settings)
    {
        _settings = settings;
    }

    public event Action<string>? MessageReady;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.AutoStartBridge)
        {
            MessageReady?.Invoke("Jetson bridge auto-start is disabled in LigDnaGui.config.json.");
            return false;
        }

        var user = _settings.JetsonSshUser;
        var host = _settings.JetsonHost;
        var target = $"{user}@{host}";
        var buildArg = _settings.BuildBridgeOnStart ? " --build" : string.Empty;

        // Jetson 쪽 run_camera_udp_bridge.sh에 필요한 환경변수를 넘기고 nohup으로 백그라운드 실행한다.
        // 로그는 ~/lig_gui_camera_bridge.log에 남겨 GUI에서 자동 실행 실패 시 Jetson 터미널로 확인할 수 있게 한다.
        var remoteCommand =
            $"cd {ShellQuote(_settings.JetsonBridgeDir)} && " +
            $"GUI_HOST={ShellQuote(_settings.PcGuiHost)} " +
            $"EO_GUI_PORT={ShellQuote(_settings.EoUdpPort.ToString(CultureInfo.InvariantCulture))} " +
            $"IR_GUI_PORT={ShellQuote(_settings.IrUdpPort.ToString(CultureInfo.InvariantCulture))} " +
            $"JETSON_RECORDING_DIR={ShellQuote(_settings.JetsonRecordingDir)} " +
            $"RECORDING_SEGMENT_SECONDS={ShellQuote(_settings.RecordingSegmentSeconds.ToString(CultureInfo.InvariantCulture))} " +
            $"RECORDING_HTTP_PORT={ShellQuote(_settings.RecordingHttpPort.ToString(CultureInfo.InvariantCulture))} " +
            $"nohup bash ./run_camera_udp_bridge.sh{buildArg} > ~/lig_gui_camera_bridge.log 2>&1 < /dev/null &";

        var result = await RunSshAsync(target, remoteCommand, TimeSpan.FromSeconds(10), cancellationToken);
        if (result.ExitCode == 0)
        {
            _startedByThisApp = true;
            MessageReady?.Invoke($"Jetson camera bridge start requested: {target}, GUI_HOST={_settings.PcGuiHost}");
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
        if (!_startedByThisApp || !_settings.AutoStartBridge)
        {
            return;
        }

        var target = $"{_settings.JetsonSshUser}@{_settings.JetsonHost}";
        var remoteCommand = "docker rm -f gui_camera_bridge >/dev/null 2>&1 || true";

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
        // BatchMode=yes는 비밀번호 프롬프트를 띄우지 않기 위한 설정이다.
        // 즉, 자동 실행을 쓰려면 Windows PC의 SSH 공개키가 Jetson authorized_keys에 등록되어 있어야 한다.
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

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private readonly record struct CommandResult(int ExitCode, string Output);
}
