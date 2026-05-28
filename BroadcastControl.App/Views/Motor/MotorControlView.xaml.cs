using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BroadcastControl.App.Views.Motor;

public partial class MotorControlView : UserControl
{
    public MotorControlView()
    {
        InitializeComponent();
    }

    public Button MotorPadUpButtonElement => MotorPadUpButton;

    public Button MotorPadLeftButtonElement => MotorPadLeftButton;

    public Button MotorPadCenterButtonElement => MotorPadCenterButton;

    public Button MotorPadRightButtonElement => MotorPadRightButton;

    public Button MotorPadDownButtonElement => MotorPadDownButton;

    public event RoutedEventHandler? RotateAuxCameraRequested;
    public event RoutedEventHandler? MotorTargetEnterClicked;
    public event MouseButtonEventHandler? MotorButtonPreviewMouseLeftButtonDownRequested;
    public event MouseButtonEventHandler? MotorButtonPreviewMouseLeftButtonUpRequested;
    public event MouseEventHandler? MotorButtonMouseLeaveRequested;

    private void RotateAuxCameraButton_OnClick(object sender, RoutedEventArgs e) => RotateAuxCameraRequested?.Invoke(sender, e);

    private void Button_Click_2(object sender, RoutedEventArgs e) => MotorTargetEnterClicked?.Invoke(sender, e);

    private void MotorButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => MotorButtonPreviewMouseLeftButtonDownRequested?.Invoke(sender, e);

    private void MotorButton_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => MotorButtonPreviewMouseLeftButtonUpRequested?.Invoke(sender, e);

    private void MotorButton_OnMouseLeave(object sender, MouseEventArgs e) => MotorButtonMouseLeaveRequested?.Invoke(sender, e);
}
