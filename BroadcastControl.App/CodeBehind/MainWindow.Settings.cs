using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Models.Network;
using BroadcastControl.App.Models.Vlm;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsSettingsOpen)
        {
            _viewModel.IsSettingsOpen = false;
        }
    }

    private void MotorDetailsBackdrop_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsMotorDetailsOpen)
        {
            _viewModel.IsMotorDetailsOpen = false;
            e.Handled = true;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
    }

    private void LoadNetworkSettingsEditor()
    {
        OperationActiveView.JetsonHostTextBoxElement.Text = _networkSettings.JetsonHost;

        var localAddresses = AppNetworkSettings.GetLocalIpv4Addresses();
        OperationActiveView.PcGuiHostComboBoxElement.ItemsSource = localAddresses;
        OperationActiveView.PcGuiHostComboBoxElement.Text = _networkSettings.PcGuiHost;
        if (localAddresses.Count > 0 && !localAddresses.Contains(_networkSettings.PcGuiHost, StringComparer.Ordinal))
        {
            _viewModel.AppendImportantLog($"?꾩옱 GUI IP ?꾨낫: {string.Join(", ", localAddresses)}");
        }
    }

    private void SaveNetworkSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        _networkSettings.JetsonHost = OperationActiveView.JetsonHostTextBoxElement.Text;
        _networkSettings.PcGuiHost = OperationActiveView.PcGuiHostComboBoxElement.Text;
        _networkSettings.RecordedVideoUrl = $"http://{_networkSettings.JetsonHost.Trim()}:{_networkSettings.RecordingHttpPort.ToString(CultureInfo.InvariantCulture)}/";
        _networkSettings.Save();
        _motorControlService.ConfigureEndpoint(
            _networkSettings.JetsonHost,
            _networkSettings.MotorControlPort,
            _networkSettings.TrackingRecordingControlPort);

        _viewModel.AppendImportantLog($"?ㅽ듃?뚰겕 ?ㅼ젙????ν븯怨?利됱떆 ?곸슜?덉뒿?덈떎: Jetson {_networkSettings.JetsonHost}, GUI {_networkSettings.PcGuiHost}");
        MessageBox.Show(
            "?ㅽ듃?뚰겕 ?ㅼ젙????ν뻽?듬땲??\n\n紐⑦꽣 紐낅졊怨??뱁솕 ?곸긽 二쇱냼??利됱떆 ??Jetson IP瑜??ъ슜?⑸땲??\nGUI IP??Jetson 釉뚮┸吏???≪텧 ????ㅼ젙?먮룄 諛섏쁺?섏뼱???곸긽 ?섏떊 ??곸씠 諛붾앸땲??",
            "Network",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void WindowModeToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowMode();
    }

    private void ToggleWindowMode()
    {
        if (_isFullscreenMode)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width = WindowedWidth;
            Height = WindowedHeight;
            Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
            Top = Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
            _isFullscreenMode = false;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreenMode = true;
        }

        UpdateWindowModeButtonText();
    }

    private void UpdateWindowModeButtonText()
    {
        if (SettingsActiveView.WindowModeToggleButtonElement is null)
        {
            return;
        }

        SettingsActiveView.WindowModeToggleButtonElement.Content = _isFullscreenMode
            ? _viewModel.Text["WindowMode"]
            : _viewModel.Text["FullscreenMode"];
    }
    private void AnimateSettingsDrawer(bool isOpen, bool animate)
    {
        if (!animate)
        {
            SettingsActiveView.SettingsBackdropElement.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = isOpen;
            SettingsActiveView.SettingsBackdropElement.Opacity = isOpen ? 1.0 : 0.0;

            SettingsActiveView.SettingsDrawerElement.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsActiveView.SettingsDrawerElement.Opacity = isOpen ? 1.0 : 0.0;
            SettingsActiveView.SettingsDrawerTransformElement.X = isOpen ? 0 : SettingsDrawerClosedOffset;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 170);
        var easing = new CubicEase
        {
            EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn
        };

        if (isOpen)
        {
            SettingsActiveView.SettingsBackdropElement.Visibility = Visibility.Visible;
            SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = true;
            SettingsActiveView.SettingsDrawerElement.Visibility = Visibility.Visible;
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
                SettingsActiveView.SettingsBackdropElement.Visibility = Visibility.Collapsed;
                SettingsActiveView.SettingsBackdropElement.IsHitTestVisible = false;
                SettingsActiveView.SettingsDrawerElement.Visibility = Visibility.Collapsed;
            };
        }

        SettingsActiveView.SettingsBackdropElement.BeginAnimation(OpacityProperty, backdropAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsActiveView.SettingsDrawerElement.BeginAnimation(OpacityProperty, drawerOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
        SettingsActiveView.SettingsDrawerTransformElement.BeginAnimation(TranslateTransform.XProperty, drawerSlideAnimation, HandoffBehavior.SnapshotAndReplace);
    }
}
