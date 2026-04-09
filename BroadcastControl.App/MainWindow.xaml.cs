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
        UpdateRecordingViewportState();

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
            case nameof(MainViewModel.IsManualRecordingEnabled):
                HandleManualRecordingStateChanged();
                break;
            case nameof(MainViewModel.ZoomLevel):
            case nameof(MainViewModel.ZoomTransformX):
            case nameof(MainViewModel.ZoomTransformY):
                UpdateRecordingViewportState();
                break;
        }
    }

    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
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

    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 수동 모드에서만 마우스 휠로 전자 줌을 제어한다.
        if (!_viewModel.CanUseZoomControls)
        {
            return;
        }

        // 일반 마우스 휠 한 칸(120)을 기준으로 확대/축소 단계를 계산한다.
        _viewModel.AdjustZoomByWheel(e.Delta / 120.0);
        e.Handled = true;
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

    /// <summary>
    /// 화면에 표시되는 전자 줌 상태를 녹화 서비스에도 같이 전달한다.
    /// </summary>
    private void UpdateRecordingViewportState()
    {
        _webcamCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
    }

    /// <summary>
    /// 녹화 버튼이 켜지면 즉시 저장 경로를 만들고, 꺼지면 현재 영상을 파일로 마감한다.
    /// </summary>
    private void HandleManualRecordingStateChanged()
    {
        if (_viewModel.IsManualRecordingEnabled)
        {
            var filePath = _webcamCaptureService.StartRecordingToDesktop();
            _viewModel.AppendImportantLog($"수동 녹화를 시작했습니다: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        var savedPath = _webcamCaptureService.StopRecording();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            _viewModel.AppendImportantLog($"영상이 저장되었습니다: {System.IO.Path.GetFileName(savedPath)}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _webcamCaptureService.FrameReady -= OnFrameReady;
        _webcamCaptureService.Dispose();
    }
}
