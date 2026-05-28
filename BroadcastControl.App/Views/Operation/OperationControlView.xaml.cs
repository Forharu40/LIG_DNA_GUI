using System.Windows;
using System.Windows.Controls;

namespace BroadcastControl.App.Views.Operation;

public partial class OperationControlView : UserControl
{
    public OperationControlView()
    {
        InitializeComponent();
    }

    public TextBox JetsonHostTextBoxElement => JetsonHostTextBox;

    public ComboBox PcGuiHostComboBoxElement => PcGuiHostComboBox;

    public event RoutedEventHandler? ManualModeClicked;
    public event RoutedEventHandler? SaveNetworkSettingsClicked;

    private void Button_Click(object sender, RoutedEventArgs e) => ManualModeClicked?.Invoke(sender, e);

    private void SaveNetworkSettingsButton_OnClick(object sender, RoutedEventArgs e) => SaveNetworkSettingsClicked?.Invoke(sender, e);
}
