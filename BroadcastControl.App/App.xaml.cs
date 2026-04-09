using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace BroadcastControl.App;

/// <summary>
/// 애플리케이션 전체 테마와 공통 색상 리소스를 관리하는 진입점이다.
/// 
/// 이 클래스가 하는 일은 크게 세 가지다.
/// 1. 프로그램 시작 시 Windows 시스템 테마를 읽어 기본 다크/라이트 테마를 결정한다.
/// 2. Material Design 기본 테마와 우리가 직접 정의한 색상 브러시를 함께 갱신한다.
/// 3. 화면 뒤 배경, 패널, 버튼 같은 공통 리소스를 한 번에 바꿔서 모든 창이 같은 분위기를 유지하게 만든다.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 현재 앱이 어떤 테마 상태인지 기억한다.
    /// 설정창에서 테마 버튼을 눌렀을 때 ViewModel이 이 값을 참조한다.
    /// </summary>
    public AppThemeMode CurrentThemeMode { get; private set; } = AppThemeMode.Dark;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 프로그램이 뜨자마자 현재 Windows 테마를 읽어서 초기 분위기를 맞춘다.
        ApplyTheme(GetSystemThemeMode());
        base.OnStartup(e);

        // 메인 창은 StartupUri 대신 코드에서 직접 생성한다.
        // 이렇게 하면 시작 전에 테마 적용을 먼저 끝내고 화면을 띄울 수 있다.
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// 현재 앱 전체 테마를 다크 또는 라이트로 적용한다.
    /// Material Design 기본 테마와 사용자 정의 브러시를 함께 갱신해야
    /// 버튼, 슬라이더, 배경, 패널이 서로 어색하지 않게 맞물린다.
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
            // 다크 테마는 전술 콘솔 느낌을 살리기 위해 네이비-그래파이트 계열로 맞춘다.
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

        // 라이트 테마는 밝은 작업 캔버스처럼 보이도록 회백색 중심으로 맞춘다.
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
    /// Windows의 "앱 밝은 테마 사용" 설정을 읽어 현재 시스템 기본 테마를 판단한다.
    /// 레지스트리를 읽지 못하면 안전하게 다크 테마로 시작한다.
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
            // 시스템 테마를 읽지 못해도 프로그램은 정상 실행되어야 하므로 기본값만 유지한다.
            return AppThemeMode.Dark;
        }
    }

    /// <summary>
    /// SolidColorBrush 리소스를 새 색으로 교체한다.
    /// 기존 브러시를 직접 수정하지 않는 이유는,
    /// WPF 리소스 브러시가 공유되거나 Freeze 상태가 될 수 있기 때문이다.
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
    /// 화면 가장 뒤쪽에 깔리는 세로 그라데이션 배경을 갱신한다.
    /// 패널 색만 바뀌고 배경이 그대로면 라이트/다크 테마가 어색해지기 때문에
    /// 배경 브러시도 같이 교체한다.
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
/// 현재 앱이 지원하는 테마 종류이다.
/// 설정창에서는 이 값을 기준으로 버튼 상태를 표시한다.
/// </summary>
public enum AppThemeMode
{
    Light,
    Dark,
}
