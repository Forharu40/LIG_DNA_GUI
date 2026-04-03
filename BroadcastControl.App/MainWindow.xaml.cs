using System.Windows;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

/// <summary>
/// 메인 윈도우는 화면만 담당하고,
/// 실제 상태와 버튼 동작은 MainViewModel에 위임한다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 화면이 시작될 때 뷰모델을 연결해 모든 바인딩이 동작하도록 한다.
        DataContext = new MainViewModel();
    }
}
