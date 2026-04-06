using System.Windows;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 윈도우는 화면 초기화와 카메라 연결만 담당한다.
/// 실제 상태와 데이터 반영은 MainViewModel에 위임한다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WebcamCaptureService _webcamCaptureService;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _webcamCaptureService = new WebcamCaptureService();
        DataContext = _viewModel;

        // 창이 열릴 때 화면 크기를 맞추고 카메라 연결을 시작한다.
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveWindowSize();

        _webcamCaptureService.FrameReady += OnFrameReady;

        if (_webcamCaptureService.Start())
        {
            _viewModel.AppendSystemLog("VIDEO", "노트북 카메라가 EO 화면에 연결되었습니다.");
        }
        else
        {
            _viewModel.AppendSystemLog("VIDEO", "노트북 카메라 연결에 실패했습니다. 장치 점유 상태를 확인하세요.");
        }
    }

    /// <summary>
    /// 현재 사용 중인 모니터의 작업 영역을 기준으로 창 크기를 잡는다.
    /// 고정 해상도 대신 비율 기반으로 계산해서 노트북과 외부 모니터 모두에 대응한다.
    /// </summary>
    private void ApplyResponsiveWindowSize()
    {
        var workArea = SystemParameters.WorkArea;

        Width = Math.Max(MinWidth, workArea.Width * 0.94);
        Height = Math.Max(MinHeight, workArea.Height * 0.92);

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private void OnFrameReady(System.Windows.Media.Imaging.BitmapSource frame)
    {
        _viewModel.UpdateEoFrame(frame);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _webcamCaptureService.FrameReady -= OnFrameReady;
        _webcamCaptureService.Dispose();
    }
}
