using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace BroadcastControl.App;

/// <summary>
/// 앱 전체 테마와 공통 색상을 관리한다.
/// 시스템 테마를 기본값으로 읽고, 설정창에서 밝은/어두운 테마를 수동으로 바꿀 수도 있다.
/// </summary>
public partial class App : Application
{
    public AppThemeMode CurrentThemeMode { get; private set; } = AppThemeMode.Dark;

    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyTheme(GetSystemThemeMode());
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public void ApplyTheme(AppThemeMode themeMode)
    {
        CurrentThemeMode = themeMode;

        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(themeMode == AppThemeMode.Dark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);

        if (themeMode == AppThemeMode.Dark)
        {
            SetBrushColor("WindowBackgroundBrush", "#FF07111D");
            SetBrushColor("PanelBrush", "#FF0E1827");
            SetBrushColor("PanelBorderBrush", "#FF223249");
            SetBrushColor("AccentBrush", "#FF41D6FF");
            SetBrushColor("PrimaryTextBrush", "#FFFFFFFF");
            SetBrushColor("SecondaryTextBrush", "#FFAFC1D6");
            SetBrushColor("SurfaceAltBrush", "#FF0A1421");
            SetBrushColor("OverlayPanelBrush", "#CC0B1420");
            SetBrushColor("DrawerBrush", "#FF0C1523");
            SetBrushColor("DrawerBorderBrush", "#FF223249");
            SetBrushColor("DrawerTextBrush", "#FFFFFFFF");
            SetBrushColor("DrawerItemBrush", "#FF111D2D");
            SetBrushColor("DrawerItemBorderBrush", "#FF29415C");
            SetBrushColor("TopBarButtonBrush", "#FF102032");
            SetBrushColor("TopBarButtonHoverBrush", "#FF173149");
            SetBrushColor("TopBarButtonPressedBrush", "#FF1D3D59");
            return;
        }

        SetBrushColor("WindowBackgroundBrush", "#FFF4F7FB");
        SetBrushColor("PanelBrush", "#FFFFFFFF");
        SetBrushColor("PanelBorderBrush", "#FFD8E2EC");
        SetBrushColor("AccentBrush", "#FF007EA7");
        SetBrushColor("PrimaryTextBrush", "#FF132130");
        SetBrushColor("SecondaryTextBrush", "#FF607487");
        SetBrushColor("SurfaceAltBrush", "#FFEAF0F7");
        SetBrushColor("OverlayPanelBrush", "#F2FFFFFF");
        SetBrushColor("DrawerBrush", "#FFF8FBFF");
        SetBrushColor("DrawerBorderBrush", "#FFD8E2EC");
        SetBrushColor("DrawerTextBrush", "#FF132130");
        SetBrushColor("DrawerItemBrush", "#FFF3F7FB");
        SetBrushColor("DrawerItemBorderBrush", "#FFC9D6E4");
        SetBrushColor("TopBarButtonBrush", "#FFF1F6FB");
        SetBrushColor("TopBarButtonHoverBrush", "#FFE4EEF7");
        SetBrushColor("TopBarButtonPressedBrush", "#FFD8E7F4");
    }

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
            // 시스템 설정을 읽지 못하면 기존 다크 콘솔 테마를 기본값으로 유지한다.
            return AppThemeMode.Dark;
        }
    }

    private void SetBrushColor(string resourceKey, string colorCode)
    {
        if (ColorConverter.ConvertFromString(colorCode) is not Color color)
        {
            return;
        }

        // XAML 리소스 브러시는 공유 과정에서 동결될 수 있으므로 기존 인스턴스를 직접 수정하지 않는다.
        Resources[resourceKey] = new SolidColorBrush(color);
    }
}

public enum AppThemeMode
{
    Light,
    Dark,
}
