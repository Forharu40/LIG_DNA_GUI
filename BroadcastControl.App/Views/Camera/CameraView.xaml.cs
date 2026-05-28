using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace BroadcastControl.App.Views.Camera;

public partial class CameraView : UserControl
{
    public CameraView()
    {
        InitializeComponent();
    }

    public Grid CameraViewportElement => CameraViewport;

    public Border CameraPanelElement => CameraPanel;

    public Canvas DetectionOverlayCanvasElement => DetectionOverlayCanvas;

    public event SizeChangedEventHandler? CameraViewportSizeChanged;
    public event MouseButtonEventHandler? CameraViewportMouseLeftButtonDown;
    public event MouseWheelEventHandler? CameraViewportMouseWheel;
    public event MouseEventHandler? CameraViewportMouseMove;
    public event MouseButtonEventHandler? CameraViewportMouseLeftButtonUp;
    public event RoutedEventHandler? RotateLargeFeedRequested;
    public event RoutedEventHandler? RotateInsetFeedRequested;
    public event MouseButtonEventHandler? MotorButtonPreviewMouseLeftButtonDownRequested;
    public event MouseButtonEventHandler? MotorButtonPreviewMouseLeftButtonUpRequested;
    public event MouseEventHandler? MotorButtonMouseLeaveRequested;

    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e) => CameraViewportSizeChanged?.Invoke(sender, e);

    private void CameraViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CameraViewportMouseLeftButtonDown?.Invoke(sender, e);

    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e) => CameraViewportMouseWheel?.Invoke(sender, e);

    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e) => CameraViewportMouseMove?.Invoke(sender, e);

    private void CameraViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CameraViewportMouseLeftButtonUp?.Invoke(sender, e);

    private void RotateLargeFeedButton_OnClick(object sender, RoutedEventArgs e) => RotateLargeFeedRequested?.Invoke(sender, e);

    private void RotateInsetFeedButton_OnClick(object sender, RoutedEventArgs e) => RotateInsetFeedRequested?.Invoke(sender, e);

    private void MotorButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => MotorButtonPreviewMouseLeftButtonDownRequested?.Invoke(sender, e);

    private void MotorButton_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => MotorButtonPreviewMouseLeftButtonUpRequested?.Invoke(sender, e);

    private void MotorButton_OnMouseLeave(object sender, MouseEventArgs e) => MotorButtonMouseLeaveRequested?.Invoke(sender, e);
}
