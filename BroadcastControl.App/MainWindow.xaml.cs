using System.Windows;
using System.Windows.Input;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 윈도우는 전체화면 초기화, 웹캠 연결, 확대 화면 드래그 입력만 담당한다.
/// 실제 상태와 화면 데이터는 MainViewModel에서 관리한다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WebcamCaptureService _webcamCaptureService;

    private bool _isDraggingZoom;
    private Point _lastZoomDragPoint;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _webcamCaptureService = new WebcamCaptureService();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 전체화면 콘솔 형태로 시작한다.
        WindowState = WindowState.Maximized;

        _webcamCaptureService.FrameReady += OnFrameReady;
        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);

        if (_webcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("노트북 카메라가 EO 화면에 연결되었습니다.");
        }
        else
        {
            _viewModel.AppendImportantLog("노트북 카메라 연결에 실패했습니다.");
        }
    }

    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
    }

    private void CameraViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.ShowZoomMiniMap)
        {
            return;
        }

        _isDraggingZoom = true;
        _lastZoomDragPoint = e.GetPosition(CameraViewport);
        CameraViewport.CaptureMouse();
    }

    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        var currentPoint = e.GetPosition(CameraViewport);
        var delta = currentPoint - _lastZoomDragPoint;
        _lastZoomDragPoint = currentPoint;

        // 드래그 방향으로 확대 화면이 움직이도록 오프셋을 갱신한다.
        _viewModel.PanZoom(delta.X, delta.Y);
    }

    private void CameraViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        _isDraggingZoom = false;
        CameraViewport.ReleaseMouseCapture();
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
