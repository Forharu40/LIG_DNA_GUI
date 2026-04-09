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
            SetBrushColor("WindowBackgroundBrush", "#FF2B2E33");
            SetBrushColor("PanelBrush", "#FF3A3D43");
            SetBrushColor("PanelBorderBrush", "#FF565A61");
            SetBrushColor("AccentBrush", "#FF5F7BEA");
            SetBrushColor("PrimaryTextBrush", "#FFFFFFFF");
            SetBrushColor("SecondaryTextBrush", "#FFC5C9D1");
            SetBrushColor("SurfaceAltBrush", "#FFE6E0D6");
            SetBrushColor("OverlayPanelBrush", "#D932353A");
            SetBrushColor("DrawerBrush", "#FF34373D");
            SetBrushColor("DrawerBorderBrush", "#FF5A5E66");
            SetBrushColor("DrawerTextBrush", "#FFFFFFFF");
            SetBrushColor("DrawerItemBrush", "#FF3C4047");
            SetBrushColor("DrawerItemBorderBrush", "#FF61656D");
            SetBrushColor("TopBarButtonBrush", "#FF484C53");
            SetBrushColor("TopBarButtonHoverBrush", "#FF565B63");
            SetBrushColor("TopBarButtonPressedBrush", "#FF656A73");
            return;
        }

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
