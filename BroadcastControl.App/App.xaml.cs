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
            SetBrushColor("WindowBackgroundBrush", "#FF0D1117");
            SetBrushColor("PanelBrush", "#FF141A23");
            SetBrushColor("PanelBorderBrush", "#FF2E3642");
            SetBrushColor("PrimaryTextBrush", "#FFFFFFFF");
            SetBrushColor("SecondaryTextBrush", "#FFD6E4EF");
            SetBrushColor("OverlayPanelBrush", "#CC101722");
            SetBrushColor("DrawerBrush", "#FF141A23");
            SetBrushColor("DrawerBorderBrush", "#FF2E3642");
            SetBrushColor("DrawerTextBrush", "#FFFFFFFF");
            SetBrushColor("DrawerItemBrush", "#FF101722");
            SetBrushColor("DrawerItemBorderBrush", "#FF2E3642");
            return;
        }

        SetBrushColor("WindowBackgroundBrush", "#FFF3F6FA");
        SetBrushColor("PanelBrush", "#FFFFFFFF");
        SetBrushColor("PanelBorderBrush", "#FFD5DEE7");
        SetBrushColor("PrimaryTextBrush", "#FF16202A");
        SetBrushColor("SecondaryTextBrush", "#FF4A5A6A");
        SetBrushColor("OverlayPanelBrush", "#E6FFFFFF");
        SetBrushColor("DrawerBrush", "#FFF8FBFF");
        SetBrushColor("DrawerBorderBrush", "#FFD5DEE7");
        SetBrushColor("DrawerTextBrush", "#FF16202A");
        SetBrushColor("DrawerItemBrush", "#FFF2F6FA");
        SetBrushColor("DrawerItemBorderBrush", "#FFC4D0DC");
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
