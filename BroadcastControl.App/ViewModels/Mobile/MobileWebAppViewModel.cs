using BroadcastControl.App.ViewModels;

// 파일 역할:
// 모바일 위험 알림 웹 앱의 표시 상태를 보관합니다.
// 알림 서버 실행 여부, 포트, 최신 증거 이미지 URL을 ViewModel 계층에서 관리합니다.

namespace BroadcastControl.App.ViewModels.Mobile;

/// <summary>
/// 모바일 알림 서버와 모바일 웹 화면에 필요한 상태입니다.
/// 실제 HTTP/SSE 서버 동작은 Services/Alert 계층에서 수행합니다.
/// </summary>
public sealed class MobileWebAppViewModel : ViewModelBase
{
    private bool _isAlertServerRunning;
    private int _alertServerPort = 8088;
    private string _latestEvidenceUrl = string.Empty;

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
}
