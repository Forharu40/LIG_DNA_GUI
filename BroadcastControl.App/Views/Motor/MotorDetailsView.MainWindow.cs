using System.Windows;
using System.Windows.Input;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private void MotorDetailsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsMotorDetailsOpen)
        {
            _viewModel.IsMotorDetailsOpen = false;
            e.Handled = true;
        }
    }
}
