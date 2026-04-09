using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 창의 "화면과 장치 사이 연결"을 담당한다.
/// 
/// ViewModel은 상태와 명령을 관리하고,
/// MainWindow는 실제 WPF 컨트롤 이벤트와 카메라 서비스 같은 외부 객체를 연결한다.
/// 즉, 이 파일은
/// - 웹캠 시작/종료
/// - 줌 드래그/휠 입력
/// - 녹화 시작/종료 연동
/// - 설정창 열림/닫힘 애니메이션
/// 같은 UI-실행 계층의 접점을 맡는다.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 설정창이 닫혀 있을 때 화면 오른쪽 바깥으로 얼마나 밀려나 있을지 정의한다.
    /// 값이 클수록 더 멀리 숨어 있다가 들어온다.
    /// </summary>
    private const double SettingsDrawerClosedOffset = 320;

    /// <summary>
    /// 화면 상태, 버튼 명령, 표시용 텍스트를 관리하는 ViewModel이다.
    /// </summary>
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// 실제 노트북 카메라 프레임을 읽고, 밝기/대조비/녹화까지 처리하는 서비스이다.
    /// </summary>
    private readonly WebcamCaptureService _webcamCaptureService;

    /// <summary>
    /// 전자 줌 상태에서 마우스로 화면을 끌고 있는지 여부를 기억한다.
    /// </summary>
    private bool _isDraggingZoom;

    /// <summary>
    /// 마지막 드래그 좌표를 저장해서 다음 이동량(delta)을 계산한다.
    /// </summary>
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

    /// <summary>
    /// 창이 실제로 화면에 올라온 뒤 초기화가 끝나는 지점이다.
    /// 이 시점에 카메라 서비스와 화면 이벤트를 연결한다.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _webcamCaptureService.FrameReady += OnFrameReady;

        // ViewModel의 시작값을 그대로 카메라 서비스에도 반영해서
        // UI 숫자와 실제 영상 보정값이 일치하도록 만든다.
        _webcamCaptureService.SetBrightness(_viewModel.Brightness);
        _webcamCaptureService.SetContrast(_viewModel.Contrast);

        // 현재 EO 화면의 실제 표시 크기를 ViewModel과 녹화 서비스에 전달한다.
        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();

        // 설정창은 초기 로딩 순간에도 열림/닫힘 상태와 위치가 일치해야 한다.
        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_webcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("노트북 카메라가 EO 화면에 연결되었습니다.");
        }
        else
        {
            _viewModel.AppendImportantLog("노트북 카메라 연결에 실패했습니다.");
        }
    }

    /// <summary>
    /// ViewModel 속성이 바뀔 때 화면 밖 장치 서비스나 애니메이션과 연결해야 하는 항목을 처리한다.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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

            case nameof(MainViewModel.IsSettingsOpen):
                AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: true);
                break;
        }
    }

    /// <summary>
    /// EO 메인 화면 크기가 바뀌면 줌 이동 범위와 녹화 구도를 다시 계산해야 한다.
    /// 창 크기를 조절하거나 모니터가 바뀌었을 때 이 메서드가 호출된다.
    /// </summary>
    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
    }

    /// <summary>
    /// 확대 상태에서 마우스 왼쪽 버튼을 누르면 화면 이동(패닝) 시작으로 간주한다.
    /// 줌이 꺼진 상태에서는 드래그할 필요가 없으므로 무시한다.
    /// </summary>
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

    /// <summary>
    /// 확대 상태에서 마우스를 드래그하면 EO 화면 내부에서 확대된 위치를 이동한다.
    /// </summary>
    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        var currentPoint = e.GetPosition(CameraViewport);
        var delta = currentPoint - _lastZoomDragPoint;
        _lastZoomDragPoint = currentPoint;

        _viewModel.PanZoom(delta.X, delta.Y);
    }

    /// <summary>
    /// 수동 모드에서 EO 화면 위에 마우스가 있을 때 휠을 굴리면 전자 줌 배율을 조절한다.
    /// 슬라이더를 직접 움직이는 것과 같은 로직으로 동작한다.
    /// </summary>
    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.CanUseZoomControls)
        {
            return;
        }

        _viewModel.AdjustZoomByWheel(e.Delta / 120.0);
        e.Handled = true;
    }

    /// <summary>
    /// 드래그가 끝났음을 알리고 마우스 캡처를 해제한다.
    /// </summary>
    private void CameraViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        _isDraggingZoom = false;
        CameraViewport.ReleaseMouseCapture();
    }

    /// <summary>
    /// 카메라 서비스에서 새 프레임이 도착했을 때 ViewModel에 전달한다.
    /// 실제 EO 화면 바인딩은 ViewModel 속성을 통해 갱신된다.
    /// </summary>
    private void OnFrameReady(System.Windows.Media.Imaging.BitmapSource frame)
    {
        _viewModel.UpdateEoFrame(frame);
    }

    /// <summary>
    /// 녹화 서비스에도 현재 화면의 줌/이동 상태를 알려준다.
    /// 이렇게 해야 저장되는 영상이 원본 전체가 아니라,
    /// 사용자가 실제로 보고 있는 줌 화면과 같은 구도로 기록된다.
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
    /// ViewModel의 수동 녹화 상태가 바뀌면 실제 파일 녹화도 함께 시작/종료한다.
    /// 저장 위치는 현재 테스트 용도에 맞게 바탕화면으로 고정되어 있다.
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

    /// <summary>
    /// 설정창 바깥 어두운 배경을 클릭하면 설정창을 닫는다.
    /// 사용자가 "밖을 눌러 닫는" 자연스러운 상호작용을 기대할 수 있게 한다.
    /// </summary>
    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    /// <summary>
    /// 설정창을 직접 애니메이션해서 열고 닫는다.
    /// 기본 DrawerHost보다 더 가볍게 동작하도록
    /// 투명도와 X축 이동만 사용해서 부드럽게 처리한다.
    /// </summary>
    private void AnimateSettingsDrawer(bool isOpen, bool animate)
    {
        if (!animate)
        {
            SettingsBackdrop.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsBackdrop.IsHitTestVisible = isOpen;
            SettingsBackdrop.Opacity = isOpen ? 1.0 : 0.0;

            SettingsDrawer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsDrawer.Opacity = isOpen ? 1.0 : 0.0;
            SettingsDrawerTransform.X = isOpen ? 0 : SettingsDrawerClosedOffset;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 170);
        var easing = new CubicEase
        {
            EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn
        };

        if (isOpen)
        {
            SettingsBackdrop.Visibility = Visibility.Visible;
            SettingsBackdrop.IsHitTestVisible = true;
            SettingsDrawer.Visibility = Visibility.Visible;
        }

        var backdropAnimation = new DoubleAnimation
        {
            To = isOpen ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        var drawerOpacityAnimation = new DoubleAnimation
        {
            To = isOpen ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        var drawerSlideAnimation = new DoubleAnimation
        {
            To = isOpen ? 0 : SettingsDrawerClosedOffset,
            Duration = duration,
            EasingFunction = easing
        };

        if (!isOpen)
        {
            drawerSlideAnimation.Completed += (_, _) =>
            {
                SettingsBackdrop.Visibility = Visibility.Collapsed;
                SettingsBackdrop.IsHitTestVisible = false;
                SettingsDrawer.Visibility = Visibility.Collapsed;
            };
        }

        SettingsBackdrop.BeginAnimation(OpacityProperty, backdropAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsDrawer.BeginAnimation(OpacityProperty, drawerOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsDrawerTransform.BeginAnimation(TranslateTransform.XProperty, drawerSlideAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 창이 닫힐 때 이벤트 연결과 장치 자원을 정리한다.
    /// 카메라 장치를 해제하지 않으면 다음 실행에서 장치 점유 문제가 생길 수 있다.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _webcamCaptureService.FrameReady -= OnFrameReady;
        _webcamCaptureService.Dispose();
    }
}
