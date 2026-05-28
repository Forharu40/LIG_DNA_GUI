using System.Windows;
using System.Windows.Controls;

namespace BroadcastControl.App.Views.Monitoring;

public partial class MonitoringView : UserControl
{
    public MonitoringView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? OpenRecordedVideosClicked;
    public event RoutedEventHandler? SettingsClicked;
    public event SelectionChangedEventHandler? DetectionTargetSelectionChanged;

    private void OpenRecordedVideosButton_OnClick(object sender, RoutedEventArgs e) => OpenRecordedVideosClicked?.Invoke(sender, e);

    private void Button_Click_1(object sender, RoutedEventArgs e) => SettingsClicked?.Invoke(sender, e);

    private void DetectionTargetList_OneSelctionChanged(object sender, SelectionChangedEventArgs e) => DetectionTargetSelectionChanged?.Invoke(sender, e);
}
