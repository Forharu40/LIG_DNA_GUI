using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

// 파일 역할:
// MotorDetailsView의 코드비하인드입니다.
// 모터 상세 상태 오버레이의 배경 클릭 이벤트를 외부로 전달합니다.

namespace BroadcastControl.App.Views.Motor
{
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
}

namespace BroadcastControl.App
{
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
}
