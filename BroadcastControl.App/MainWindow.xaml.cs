using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// MainWindow는 ViewModel과 실제 입력/출력 서비스 사이를 연결하는 조정자 역할을 한다.
/// 화면 배치 자체는 XAML이 담당하고, 여기서는 다음 흐름을 연결한다.
/// - EO UDP 영상 수신 시작/종료
/// - IR 임시 노트북 카메라 시작/종료
/// - 전자 줌 드래그/휠 입력
/// - 수동 녹화 시작/종료
/// - 설정창 열림/닫힘 애니메이션
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 설정창이 닫혀 있을 때 화면 오른쪽 바깥으로 얼마나 밀려나 있을지 정의한다.
    /// </summary>
    private const double SettingsDrawerClosedOffset = 320;

    /// <summary>
    /// 화면 상태, 버튼 명령, 텍스트, 줌 값을 관리하는 ViewModel이다.
    /// </summary>
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// 외부 EO 카메라가 보내는 UDP 패킷을 받아 프레임으로 복원하는 서비스이다.
    /// </summary>
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;

    /// <summary>
    /// 임시 IR 화면으로 쓸 노트북 카메라 프레임을 읽는 서비스이다.
    /// </summary>
    private readonly WebcamCaptureService _irWebcamCaptureService;

    /// <summary>
    /// 전자 줌 상태에서 마우스로 화면을 끌고 있는지 여부를 기억한다.
    /// </summary>
    private bool _isDraggingZoom;

    /// <summary>
    /// 마지막 드래그 좌표를 기억해 다음 이동량(delta)을 계산한다.
    /// </summary>
    private Point _lastZoomDragPoint;

    /// <summary>
    /// 첫 프레임 수신 로그를 한 번만 남기기 위한 플래그이다.
    /// </summary>
    private bool _hasReceivedEoFrame;
    private bool _hasReceivedIrFrame;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _eoUdpCaptureService = new UdpEncodedVideoReceiverService();
        _irWebcamCaptureService = new WebcamCaptureService();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// 창이 실제로 화면에 표시될 때 입력 서비스 연결과 초기 상태 동기화를 수행한다.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _irWebcamCaptureService.FrameReady += OnIrFrameReady;

        // 상단 밝기/대조비 슬라이더는 EO 외부 영상에 적용된다.
        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);

        // 현재 EO 화면의 실제 표시 크기를 ViewModel과 EO 녹화 서비스에 전달한다.
        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();

        // 설정창은 초기 로딩 순간에도 위치와 열림 상태가 맞아야 한다.
        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start())
        {
            _viewModel.AppendImportantLog($"EO UDP 수신 대기 중입니다. 포트: {_eoUdpCaptureService.ListeningPort}");
        }
        else
        {
            _viewModel.AppendImportantLog("EO UDP 수신 소켓 생성에 실패했습니다.");
        }

        if (_irWebcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("노트북 카메라가 IR 화면에 임시 연결되었습니다.");
        }
        else
        {
            _viewModel.AppendImportantLog("노트북 카메라를 IR 화면에 연결하지 못했습니다.");
        }
    }

    /// <summary>
    /// ViewModel 값이 바뀔 때 실제 서비스와 애니메이션이 따라가야 하는 항목을 처리한다.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Brightness):
                _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
                break;

            case nameof(MainViewModel.Contrast):
                _eoUdpCaptureService.SetContrast(_viewModel.Contrast);
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
    /// EO 메인 화면 크기가 바뀌면 전자 줌 이동 범위와 녹화 구도를 다시 계산한다.
    /// </summary>
    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
    }

    /// <summary>
    /// 전자 줌이 켜져 있을 때만 드래그 패닝을 시작한다.
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
    /// 드래그 중에는 EO 화면 안에서 확대된 시야를 이동시킨다.
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
    /// 수동 모드일 때 EO 화면 위에서 마우스 휠로 전자 줌 배율을 조절한다.
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
    /// 드래그를 끝내고 마우스 캡처를 해제한다.
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
    /// EO UDP 프레임이 도착하면 메인 EO 화면에 반영한다.
    /// 첫 프레임 수신 시 로그를 한 번 남겨 연결 여부를 쉽게 확인할 수 있게 한다.
    /// </summary>
    private void OnEoFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
            _viewModel.AppendImportantLog("EO UDP 카메라 첫 프레임을 수신했습니다.");
        }

        _viewModel.UpdateEoFrame(frame);
    }

    /// <summary>
    /// 노트북 카메라 프레임을 임시 IR 화면에 반영한다.
    /// </summary>
    private void OnIrFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
            _viewModel.AppendImportantLog("IR 임시 카메라 첫 프레임을 수신했습니다.");
        }

        _viewModel.UpdateIrFrame(frame);
    }

    /// <summary>
    /// EO 화면에 보이는 전자 줌/패닝 상태를 녹화 서비스에 전달한다.
    /// 그래야 저장되는 영상도 현재 운용자가 보는 구도와 같아진다.
    /// </summary>
    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
    }

    /// <summary>
    /// ViewModel의 수동 녹화 상태가 바뀌면 EO 서비스의 실제 녹화 시작/종료를 처리한다.
    /// </summary>
    private void HandleManualRecordingStateChanged()
    {
        if (_viewModel.IsManualRecordingEnabled)
        {
            var filePath = _eoUdpCaptureService.StartRecordingToDesktop();
            _viewModel.AppendImportantLog($"수동 녹화를 시작했습니다: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        var savedPath = _eoUdpCaptureService.StopRecording();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            _viewModel.AppendImportantLog($"영상이 저장되었습니다: {System.IO.Path.GetFileName(savedPath)}");
        }
    }

    /// <summary>
    /// 설정창 바깥 어두운 배경을 클릭하면 설정창을 닫는다.
    /// </summary>
    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    /// <summary>
    /// 설정창을 직접 페이드+슬라이드 애니메이션으로 열고 닫는다.
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
    /// 창 종료 시 이벤트와 입력 장치를 모두 정리한다.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _irWebcamCaptureService.FrameReady -= OnIrFrameReady;
        _eoUdpCaptureService.Dispose();
        _irWebcamCaptureService.Dispose();
    }
}
