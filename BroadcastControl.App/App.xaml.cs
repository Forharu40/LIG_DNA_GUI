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
            SetBrushColor("WindowBackgroundBrush", "#FF14100D");
            SetBrushColor("PanelBrush", "#FF1C1713");
            SetBrushColor("PanelBorderBrush", "#FF4C3C2F");
            SetBrushColor("AccentBrush", "#FFEB9B52");
            SetBrushColor("PrimaryTextBrush", "#FFFFFFFF");
            SetBrushColor("SecondaryTextBrush", "#FFD5C2AF");
            SetBrushColor("SurfaceAltBrush", "#FF15110E");
            SetBrushColor("OverlayPanelBrush", "#D9231B15");
            SetBrushColor("DrawerBrush", "#FF18130F");
            SetBrushColor("DrawerBorderBrush", "#FF4C3C2F");
            SetBrushColor("DrawerTextBrush", "#FFFFFFFF");
            SetBrushColor("DrawerItemBrush", "#FF241D16");
            SetBrushColor("DrawerItemBorderBrush", "#FF5D4937");
            SetBrushColor("TopBarButtonBrush", "#FF2A2018");
            SetBrushColor("TopBarButtonHoverBrush", "#FF36281E");
            SetBrushColor("TopBarButtonPressedBrush", "#FF443124");
            return;
        }

        SetBrushColor("WindowBackgroundBrush", "#FFF8F2EB");
        SetBrushColor("PanelBrush", "#FFFFFBF7");
        SetBrushColor("PanelBorderBrush", "#FFE1D2C3");
        SetBrushColor("AccentBrush", "#FFB56622");
        SetBrushColor("PrimaryTextBrush", "#FF2B2119");
        SetBrushColor("SecondaryTextBrush", "#FF7A6653");
        SetBrushColor("SurfaceAltBrush", "#FFF3E8DD");
        SetBrushColor("OverlayPanelBrush", "#F7FFF8F2");
        SetBrushColor("DrawerBrush", "#FFFFFBF7");
        SetBrushColor("DrawerBorderBrush", "#FFE1D2C3");
        SetBrushColor("DrawerTextBrush", "#FF2B2119");
        SetBrushColor("DrawerItemBrush", "#FFF5ECE3");
        SetBrushColor("DrawerItemBorderBrush", "#FFD9C6B2");
        SetBrushColor("TopBarButtonBrush", "#FFF6ECE1");
        SetBrushColor("TopBarButtonHoverBrush", "#FFF0E0D0");
        SetBrushColor("TopBarButtonPressedBrush", "#FFE9D3BE");
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
