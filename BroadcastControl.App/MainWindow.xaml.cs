using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BroadcastControl.App.Services;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private const double SettingsDrawerClosedOffset = 320;

    private readonly MainViewModel _viewModel;
    private readonly UdpEncodedVideoReceiverService _eoUdpCaptureService;
    private readonly WebcamCaptureService _irWebcamCaptureService;

    private bool _isDraggingZoom;
    private Point _lastZoomDragPoint;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Maximized;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady += OnEoFrameReady;
        _irWebcamCaptureService.FrameReady += OnIrFrameReady;

        _eoUdpCaptureService.SetBrightness(_viewModel.Brightness);
        _eoUdpCaptureService.SetContrast(_viewModel.Contrast);

        _viewModel.UpdateViewportSize(CameraViewport.ActualWidth, CameraViewport.ActualHeight);
        UpdateRecordingViewportState();

        AnimateSettingsDrawer(_viewModel.IsSettingsOpen, animate: false);

        if (_eoUdpCaptureService.Start())
        {
            _viewModel.AppendImportantLog($"EO UDP receiver is waiting on port {_eoUdpCaptureService.ListeningPort}.");
        }
        else
        {
            _viewModel.AppendImportantLog("Failed to start the EO UDP receiver.");
        }

        if (_irWebcamCaptureService.Start())
        {
            _viewModel.AppendImportantLog("Connected the laptop camera to the temporary IR panel.");
        }
        else
        {
            _viewModel.AppendImportantLog("Could not connect the laptop camera to the temporary IR panel.");
        }

        _ = _viewModel.StartJetsonExampleProbeAsync();
    }

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

        _viewModel.PanZoom(delta.X, delta.Y);
    }

    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.CanUseZoomControls)
        {
            return;
        }

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

    private void OnEoFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedEoFrame)
        {
            _hasReceivedEoFrame = true;
            _viewModel.AppendImportantLog("EO UDP camera first frame received.");
        }

        _viewModel.UpdateEoFrame(frame);
    }

    private void OnIrFrameReady(BitmapSource frame)
    {
        if (!_hasReceivedIrFrame)
        {
            _hasReceivedIrFrame = true;
            _viewModel.AppendImportantLog("IR temporary camera first frame received.");
        }

        _viewModel.UpdateIrFrame(frame);
    }

    private void UpdateRecordingViewportState()
    {
        _eoUdpCaptureService.UpdateViewportTransform(
            _viewModel.ZoomLevel,
            _viewModel.ZoomTransformX,
            _viewModel.ZoomTransformY,
            CameraViewport.ActualWidth,
            CameraViewport.ActualHeight);
    }

    private void HandleManualRecordingStateChanged()
    {
        if (_viewModel.IsManualRecordingEnabled)
        {
            var filePath = _eoUdpCaptureService.StartRecordingToDesktop();
            _viewModel.AppendImportantLog($"Manual recording started: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        var savedPath = _eoUdpCaptureService.StopRecording();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            _viewModel.AppendImportantLog($"Video saved: {System.IO.Path.GetFileName(savedPath)}");
        }
    }

    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

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

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eoUdpCaptureService.FrameReady -= OnEoFrameReady;
        _irWebcamCaptureService.FrameReady -= OnIrFrameReady;
        _eoUdpCaptureService.Dispose();
        _irWebcamCaptureService.Dispose();
    }
}
