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
/// MainWindow와 입력/출력 서비스를 연결하는 조정자임.
/// XAML은 화면 배치를 담당하고, 이 파일은 EO UDP 수신, IR 임시 웹캠, 줌 입력, 녹화, 설정창 애니메이션을 연결함.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 설정창 닫힘 상태에서 오른쪽 바깥으로 밀어둘 거리임.
    /// </summary>
    private const double SettingsDrawerClosedOffset = 320;

    /// <summary>
    /// 화면 상태, 명령, 텍스트, 줌 값을 관리하는 ViewModel임.
    /// </summary>
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// 외부 EO 카메라 UDP 패킷을 프레임으로 복원하는 서비스임.
    /// </summary>
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;

    /// <summary>
    /// 임시 IR 화면으로 사용할 노트북 카메라 서비스임.
    /// </summary>
    private readonly WebcamCaptureService _irWebcamCaptureService;

    /// <summary>
    /// 전자 줌 상태에서 마우스 드래그 중인지 저장함.
    /// </summary>
    private bool _isDraggingZoom;

    /// <summary>
    /// 이전 드래그 좌표임. 다음 이동량 계산에 사용함.
    /// </summary>
    private Point _lastZoomDragPoint;

    /// <summary>
    /// 첫 프레임 수신 로그를 한 번만 남기기 위한 플래그임.
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
    /// 창 표시 시 입력 서비스 연결과 초기 상태 동기화를 수행함.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _irWebcamCaptureService.FrameReady += OnIrFrameReady;

        // 밝기/대조비 슬라이더 값은 EO 외부 영상 보정에 적용함.
        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);

        // 현재 EO 표시 영역 크기를 ViewModel과 EO 녹화 서비스에 전달함.
        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();

        // 초기 설정창 위치를 ViewModel 상태와 맞춤.
        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start())
        {
            _viewModel.AppendImportantLog($"EO UDP 수신 대기 중임. 포트: {_eoUdpCaptureService.ListeningPort}");
        }
        else
        {
            _viewModel.AppendImportantLog("EO UDP 수신 소켓 생성 실패.");
        }

        if (_irWebcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("노트북 카메라를 IR 임시 화면에 연결함.");
        }
        else
        {
            _viewModel.AppendImportantLog("노트북 카메라를 IR 임시 화면에 연결하지 못함.");
        }
    }

    /// <summary>
    /// ViewModel 변경 사항 중 실제 서비스와 애니메이션에 반영해야 할 항목을 처리함.
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
    /// EO 메인 화면 크기 변경 시 전자 줌 이동 범위와 녹화 구도를 재계산함.
    /// </summary>
    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
    }

    /// <summary>
    /// 전자 줌 상태에서만 드래그 패닝을 시작함.
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
    /// 드래그 중 확대된 EO 시야를 이동함.
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
    /// 수동 모드에서 EO 화면 위 마우스 휠로 전자 줌 배율을 조절함.
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
    /// 드래그 종료 시 마우스 캡처를 해제함.
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
    /// EO UDP 프레임을 메인 EO 화면에 반영함.
    /// 첫 프레임 수신 시 연결 확인 로그를 한 번만 남김.
    /// </summary>
    private void OnEoFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
            _viewModel.AppendImportantLog("EO UDP 카메라 첫 프레임을 수신함.");
        }

        _viewModel.UpdateEoFrame(frame);
    }

    /// <summary>
    /// 노트북 카메라 프레임을 임시 IR 화면에 반영함.
    /// </summary>
    private void OnIrFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
            _viewModel.AppendImportantLog("IR 임시 카메라 첫 프레임을 수신함.");
        }

        _viewModel.UpdateIrFrame(frame);
    }

    /// <summary>
    /// EO 화면의 전자 줌/패닝 상태를 녹화 서비스에 전달함.
    /// 저장 영상 구도를 현재 운용자가 보는 화면과 맞추기 위한 처리임.
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
    /// 수동 녹화 상태 변경 시 EO 서비스의 실제 녹화 시작/종료를 처리함.
    /// </summary>
    private void HandleManualRecordingStateChanged()
    {
        if (_viewModel.IsManualRecordingEnabled)
        {
            var filePath = _eoUdpCaptureService.StartRecordingToDesktop();
            _viewModel.AppendImportantLog($"수동 녹화를 시작함: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        var savedPath = _eoUdpCaptureService.StopRecording();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            _viewModel.AppendImportantLog($"영상 저장 완료: {System.IO.Path.GetFileName(savedPath)}");
        }
    }

    /// <summary>
    /// 설정창 바깥 배경 클릭 시 설정창을 닫음.
    /// </summary>
    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    /// <summary>
    /// 설정창을 페이드와 슬라이드 애니메이션으로 열고 닫음.
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
    /// 창 종료 시 이벤트 구독과 입력 장치 리소스를 정리함.
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
