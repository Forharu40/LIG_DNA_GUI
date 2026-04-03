using System.Windows;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 윈도우는 화면 초기화와 외부 장치 연결만 담당한다.
/// 카메라 프레임은 서비스에서 받고, 상태 반영은 뷰모델에 위임한다.
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

        // 창이 열릴 때 카메라 연결을 시도하고, 닫힐 때 자원을 정리한다.
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
