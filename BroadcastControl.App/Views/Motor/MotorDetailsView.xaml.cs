using System.Windows.Controls;
using System.Windows.Input;

namespace BroadcastControl.App.Views.Motor;

public partial class MotorDetailsView : UserControl
{
    public MotorDetailsView()
    {
        InitializeComponent();
    }

    public event MouseButtonEventHandler? BackdropMouseLeftButtonDownRequested;

    private void MotorDetailsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BackdropMouseLeftButtonDownRequested?.Invoke(sender, e);
    }
}
