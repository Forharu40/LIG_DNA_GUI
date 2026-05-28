using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BroadcastControl.App.Views.Settings;

public partial class SettingsDrawerView : UserControl
{
    public SettingsDrawerView()
    {
        InitializeComponent();
    }

    public Rectangle SettingsBackdropElement => SettingsBackdrop;

    public Border SettingsDrawerElement => SettingsDrawer;

    public TranslateTransform SettingsDrawerTransformElement => SettingsDrawerTransform;

    public Button WindowModeToggleButtonElement => WindowModeToggleButton;

    public event MouseButtonEventHandler? BackdropMouseLeftButtonDownRequested;
    public event RoutedEventHandler? WindowModeToggleClicked;

    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BackdropMouseLeftButtonDownRequested?.Invoke(sender, e);

    private void WindowModeToggleButton_OnClick(object sender, RoutedEventArgs e) => WindowModeToggleClicked?.Invoke(sender, e);
}
