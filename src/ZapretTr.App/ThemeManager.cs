using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ZapretTr.App;

/// <summary>Arayuz temasi.</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Acik ve koyu tema arasinda gecis yapar.
/// </summary>
/// <remarks>
/// Renkler Themes/Light.xaml ve Themes/Dark.xaml'da. Gecis, uygulama kaynaklarindaki
/// tema sozlugunu digeriyle degistirerek yapiliyor; stiller renklere DynamicResource
/// ile baktigi icin acik pencere yeniden kurulmadan boyaniyor.
///
/// Tema sozlugu kaynak adresiyle degil, icindeki "ThemeName" anahtariyla bulunuyor:
/// App.xaml'daki goreli adres ile koddaki pack adresi ayni sozlugu gostermelerine
/// ragmen metin olarak eslesmiyor.
/// </remarks>
public static class ThemeManager
{
    private const string ThemeNameKey = "ThemeName";

    /// <summary>Su an uygulanmis tema.</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>Kayitli tercihi temaya cevirir; tercih yoksa Windows'un ayarina uyar.</summary>
    /// <param name="saved">config.json'daki deger: "dark", "light" ya da null.</param>
    public static AppTheme Resolve(string? saved) => saved?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => IsWindowsDark() ? AppTheme.Dark : AppTheme.Light,
    };

    /// <summary>Temanin config.json'a yazilacak adi.</summary>
    public static string ToConfigValue(AppTheme theme) => theme == AppTheme.Dark ? "dark" : "light";

    /// <summary>Temayi uygular: kaynak sozlugunu degistirir ve acik pencerelerin basligini boyar.</summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var yeni = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/ZapretTR;component/Themes/{(theme == AppTheme.Dark ? "Dark" : "Light")}.xaml",
                UriKind.Absolute),
        };

        var sozlukler = app.Resources.MergedDictionaries;
        var eski = sozlukler.FirstOrDefault(d => d.Contains(ThemeNameKey));

        if (eski is null)
        {
            sozlukler.Insert(0, yeni);
        }
        else
        {
            sozlukler[sozlukler.IndexOf(eski)] = yeni;
        }

        Current = theme;

        foreach (Window pencere in app.Windows)
        {
            ApplyTitleBar(pencere);
        }
    }

    /// <summary>Pencerenin Windows basligini temaya uygun boyar.</summary>
    /// <remarks>
    /// Baslik cubugu WPF'in degil Windows'un. Boyanmazsa koyu pencerenin ustunde
    /// bembeyaz bir serit kaliyor. Pencere tutamaci henuz yoksa (SourceInitialized
    /// oncesi) bir sey yapilmiyor; MainWindow bunu SourceInitialized'da yeniden cagiriyor.
    /// </remarks>
    public static void ApplyTitleBar(Window window)
    {
        var tutamac = new WindowInteropHelper(window).Handle;
        if (tutamac == IntPtr.Zero)
        {
            return;
        }

        var deger = Current == AppTheme.Dark ? 1 : 0;

        // 20: Windows 11 ve Windows 10 20H1 sonrasi. 19: daha eski Windows 10 surumleri.
        // Ikisi de desteklenmiyorsa hata kodu donuyor ve baslik acik kaliyor; zararsiz.
        if (DwmSetWindowAttribute(tutamac, 20, ref deger, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(tutamac, 19, ref deger, sizeof(int));
        }
    }

    /// <summary>Windows "uygulama modu" koyu mu.</summary>
    private static bool IsWindowsDark()
    {
        try
        {
            using var anahtar = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return anahtar?.GetValue("AppsUseLightTheme") is int acik && acik == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
