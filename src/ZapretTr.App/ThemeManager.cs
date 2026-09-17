using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ZapretTr.App;

/// <summary>Arayüz teması.</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Açık ve koyu tema arasında geçiş yapar.
/// </summary>
/// <remarks>
/// Renkler Themes/Light.xaml ve Themes/Dark.xaml'da. Geçiş, uygulama kaynaklarındaki
/// tema sözlüğünü diğeriyle değiştirerek yapılıyor; stiller renklere DynamicResource
/// ile baktığı için açık pencere yeniden kurulmadan boyanıyor.
///
/// Tema sözlüğü kaynak adresiyle değil, içindeki "ThemeName" anahtarıyla bulunuyor:
/// App.xaml'daki göreli adres ile koddaki pack adresi aynı sözlüğü göstermelerine
/// rağmen metin olarak eşleşmiyor.
/// </remarks>
public static class ThemeManager
{
    private const string ThemeNameKey = "ThemeName";

    /// <summary>Şu an uygulanmış tema.</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>Kayıtlı tercihi temaya çevirir; tercih yoksa Windows'un ayarına uyar.</summary>
    /// <param name="saved">config.json'daki değer: "dark", "light" ya da null.</param>
    public static AppTheme Resolve(string? saved) => saved?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => IsWindowsDark() ? AppTheme.Dark : AppTheme.Light,
    };

    /// <summary>Temanın config.json'a yazılacak adı.</summary>
    public static string ToConfigValue(AppTheme theme) => theme == AppTheme.Dark ? "dark" : "light";

    /// <summary>Temayı uygular: kaynak sözlüğünü değiştirir ve açık pencerelerin başlığını boyar.</summary>
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

    /// <summary>Pencerenin Windows başlığını temaya uygun boyar.</summary>
    /// <remarks>
    /// Başlık çubuğu WPF'in değil Windows'un. Boyanmazsa koyu pencerenin üstünde
    /// bembeyaz bir şerit kalıyor. Pencere tutamacı henüz yoksa (SourceInitialized
    /// öncesi) bir şey yapılmıyor; MainWindow bunu SourceInitialized'da yeniden çağırıyor.
    /// </remarks>
    public static void ApplyTitleBar(Window window)
    {
        var tutamac = new WindowInteropHelper(window).Handle;
        if (tutamac == IntPtr.Zero)
        {
            return;
        }

        var deger = Current == AppTheme.Dark ? 1 : 0;

        // 20: Windows 11 ve Windows 10 20H1 sonrası. 19: daha eski Windows 10 sürümleri.
        // İkisi de desteklenmiyorsa hata kodu dönüyor ve başlık açık kalıyor; zararsız.
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
