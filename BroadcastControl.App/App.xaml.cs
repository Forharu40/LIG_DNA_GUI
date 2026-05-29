// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace BroadcastControl.App;

/// <summary>
/// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
/// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    public AppThemeMode CurrentThemeMode { get; private set; } = AppThemeMode.Dark;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        ApplyTheme(GetSystemThemeMode());
        base.OnStartup(e);

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    public void ApplyTheme(AppThemeMode themeMode)
    {
        CurrentThemeMode = themeMode;

        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(themeMode == AppThemeMode.Dark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);

        if (themeMode == AppThemeMode.Dark)
        {
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            SetBrushColor("WindowBackgroundBrush", "#FF161B24");
            SetBrushColor("PanelBrush", "#FF242A35");
            SetBrushColor("PanelBorderBrush", "#FF464E5D");
            SetBrushColor("AccentBrush", "#FFE09A36");
            SetBrushColor("PrimaryTextBrush", "#FFF0F3F8");
            SetBrushColor("SecondaryTextBrush", "#FFC7CDD8");
            SetBrushColor("SurfaceAltBrush", "#FF0C1018");
            SetBrushColor("OverlayPanelBrush", "#DD111722");
            SetBrushColor("DrawerBrush", "#FF202631");
            SetBrushColor("DrawerBorderBrush", "#FF465061");
            SetBrushColor("DrawerTextBrush", "#FFF0F3F8");
            SetBrushColor("DrawerItemBrush", "#FF2C3442");
            SetBrushColor("DrawerItemBorderBrush", "#FF4A5567");
            SetBrushColor("TopBarButtonBrush", "#FF2E3643");
            SetBrushColor("TopBarButtonHoverBrush", "#FF394353");
            SetBrushColor("TopBarButtonPressedBrush", "#FF465164");
            SetBackdropBrush("#FF1C2230", "#FF1A2742", "#FF141A25", "#FF161B24");
            return;
        }

        // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
        SetBrushColor("WindowBackgroundBrush", "#FFE7E8EB");
        SetBrushColor("PanelBrush", "#FFF8F8F9");
        SetBrushColor("PanelBorderBrush", "#FFCBCDD2");
        SetBrushColor("AccentBrush", "#FF4E68D1");
        SetBrushColor("PrimaryTextBrush", "#FF1E2329");
        SetBrushColor("SecondaryTextBrush", "#FF616873");
        SetBrushColor("SurfaceAltBrush", "#FFEBE4D8");
        SetBrushColor("OverlayPanelBrush", "#F3FFFFFF");
        SetBrushColor("DrawerBrush", "#FFF2F3F5");
        SetBrushColor("DrawerBorderBrush", "#FFC8CBD0");
        SetBrushColor("DrawerTextBrush", "#FF1E2329");
        SetBrushColor("DrawerItemBrush", "#FFE7E9EC");
        SetBrushColor("DrawerItemBorderBrush", "#FFBEC2C8");
        SetBrushColor("TopBarButtonBrush", "#FFE3E6EA");
        SetBrushColor("TopBarButtonHoverBrush", "#FFD8DCE2");
        SetBrushColor("TopBarButtonPressedBrush", "#FFCDD2D9");
        SetBackdropBrush("#FFF7F8FB", "#FFEAEFF8", "#FFE4E9F2", "#FFE7E8EB");
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private AppThemeMode GetSystemThemeMode()
    {
        try
        {
            const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
            var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");

            return appsUseLightTheme is int lightThemeFlag && lightThemeFlag > 0
                ? AppThemeMode.Light
                : AppThemeMode.Dark;
        }
        catch
        {
            // 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
            return AppThemeMode.Dark;
        }
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SetBrushColor(string resourceKey, string colorCode)
    {
        if (ColorConverter.ConvertFromString(colorCode) is not Color color)
        {
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
    }

    /// <summary>
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
    /// </summary>
    private void SetBackdropBrush(string startColor, string accentColor, string midColor, string endColor)
    {
        if (ColorConverter.ConvertFromString(startColor) is not Color start ||
            ColorConverter.ConvertFromString(accentColor) is not Color accent ||
            ColorConverter.ConvertFromString(midColor) is not Color mid ||
            ColorConverter.ConvertFromString(endColor) is not Color end)
        {
            return;
        }

        Resources["WindowBackdropBrush"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(start, 0.0),
                new(accent, 0.08),
                new(mid, 0.16),
                new(end, 1.0),
            },
            new Point(0, 0),
            new Point(0, 1));
    }
}

/// <summary>
/// 화면 상태와 사용자 동작 처리 흐름을 설명하는 주석입니다.
/// </summary>
public enum AppThemeMode
{
    Light,
    Dark,
}
