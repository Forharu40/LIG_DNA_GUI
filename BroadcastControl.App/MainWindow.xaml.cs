using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 윈도우는 전체화면 초기화, 웹캠 연결, 확대 드래그 입력, 화면 보정값 전달을 담당한다.
/// 실제 화면 상태와 표시 데이터는 MainViewModel에서 관리한다.
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
        WindowState = WindowState.Maximized;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _webcamCaptureService.FrameReady += OnFrameReady;

        // 초기 슬라이더 값도 바로 카메라 서비스에 반영한다.
        _webcamCaptureService.SetBrightness(_viewModel.Brightness);
        _webcamCaptureService.SetContrast(_viewModel.Contrast);

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 밝기 또는 대조비 슬라이더가 바뀌면 카메라 프레임 보정값도 즉시 갱신한다.
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Brightness):
                _webcamCaptureService.SetBrightness(_viewModel.Brightness);
                break;
            case nameof(MainViewModel.Contrast):
                _webcamCaptureService.SetContrast(_viewModel.Contrast);
                break;
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

        // 드래그 방향으로 확대 화면이 이동하도록 ViewModel에 오프셋 갱신을 요청한다.
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _webcamCaptureService.FrameReady -= OnFrameReady;
        _webcamCaptureService.Dispose();
    }
}
