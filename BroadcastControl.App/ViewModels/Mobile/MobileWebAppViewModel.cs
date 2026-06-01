using BroadcastControl.App.Models.Network;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

// 파일 역할:
// VLM이 위험 객체를 판단했을 때 모바일 브라우저로 보낼 경고 화면 상태를 관리합니다.
// 알림 HTTP/SSE 서버 실행 여부, 접속 포트, 최신 위험 객체 캡처 이미지 URL을 화면과 서비스 계층에 전달합니다.

namespace BroadcastControl.App.ViewModels.Mobile;

/// <summary>
/// 모바일 알림 웹 페이지를 띄우기 위해 필요한 서버 상태입니다.
/// 실제 HTTP/SSE 송수신은 Services/Alert 계층에서 수행하고, 이 클래스는 UI에 보여줄 실행 상태와 URL만 보관합니다.
/// </summary>
public sealed class MobileWebAppViewModel : ViewModelBase
{
    private bool _isAlertServerRunning;
    private int _alertServerPort = 8088;
    private string _latestEvidenceUrl = string.Empty;

    public MobileAlertHubService AlertHubService { get; } = new();

    public bool IsAlertServerRunning
    {
        get => _isAlertServerRunning;
        set => SetProperty(ref _isAlertServerRunning, value);
    }

    public int AlertServerPort
    {
        get => _alertServerPort;
        set => SetProperty(ref _alertServerPort, value);
    }

    public string LatestEvidenceUrl
    {
        get => _latestEvidenceUrl;
        set => SetProperty(ref _latestEvidenceUrl, value);
    }

    public bool StartAlertServer(AppNetworkSettings settings, Action<string> appendLog)
    {
        AlertServerPort = settings.MobileAlertPort;
        IsAlertServerRunning = AlertHubService.Start(settings.MobileAlertPort);
        if (IsAlertServerRunning)
        {
            appendLog($"모바일 위험 알림 앱이 시작되었습니다: {AlertHubService.AccessHintUrls}");
        }
        else
        {
            appendLog($"모바일 위험 알림 앱 시작에 실패했습니다. 포트 {settings.MobileAlertPort}를 확인하세요.");
        }

        return IsAlertServerRunning;
    }

    public void DisposeServices()
    {
        AlertHubService.Dispose();
    }
}
