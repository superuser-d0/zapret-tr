using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ZapretTr.App;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Acik ve koyu tema sozlukleri birbirinin karsiligi olmali; arayuzde sabit renk kalmamali.
/// </summary>
/// <remarks>
/// Koyu tema, renkleri uygulama kaynaklarindaki tema sozlugunu degistirerek boyuyor.
/// Iki sessiz hata yolu var ve ikisi de derlemeyi geciyor:
///
///   1. Bir anahtar yalnizca bir temada var. DynamicResource bulamazsa eleman eski
///      renginde kaliyor; koyu temada tek bir bembeyaz kutu.
///   2. XAML'a yeni bir sabit renk ("#FFFFFF") yaziliyor. O eleman tema degisince
///      hic degismiyor. Koyu tema eklenirken MainWindow.xaml'da sekiz tane vardi.
/// </remarks>
public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDir => IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App");

    [Fact]
    public void Iki_tema_ayni_anahtarlari_tanimliyor()
    {
        var acik = Anahtarlar("Light.xaml");
        var koyu = Anahtarlar("Dark.xaml");

        // Koyu temanin kendi sablon parcalari (Koyu... ile baslayanlar) acik temada
        // yok; acik tema Windows'un sablonlarini kullaniyor.
        var koyuOrtak = koyu.Where(k => !k.StartsWith("Koyu", StringComparison.Ordinal)).ToHashSet();

        Assert.Empty(acik.Except(koyuOrtak));
        Assert.Empty(koyuOrtak.Except(acik));
        Assert.Contains("ThemeName", acik);
    }

    [Theory]
    [InlineData("App.xaml")]
    [InlineData("MainWindow.xaml")]
    public void Arayuzde_sabit_renk_yok(string dosya)
    {
        var xaml = File.ReadAllText(IoPath.Combine(AppDir, dosya));

        // Yorumlar haric: gerekceyi anlatan metinde renk kodu gecebilir.
        var yorumsuz = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotMatch("\"#[0-9A-Fa-f]{3,8}\"", yorumsuz);
    }

    [Theory]
    [InlineData("dark", AppTheme.Dark)]
    [InlineData("DARK", AppTheme.Dark)]
    [InlineData(" light ", AppTheme.Light)]
    public void Kayitli_tercih_temaya_cevriliyor(string kayit, AppTheme beklenen)
    {
        Assert.Equal(beklenen, ThemeManager.Resolve(kayit));
    }

    [Fact]
    public void Tema_config_degeri_geri_okunabiliyor()
    {
        foreach (var tema in new[] { AppTheme.Light, AppTheme.Dark })
        {
            Assert.Equal(tema, ThemeManager.Resolve(ThemeManager.ToConfigValue(tema)));
        }
    }

    private static HashSet<string> Anahtarlar(string dosya)
    {
        var belge = XDocument.Load(IoPath.Combine(AppDir, "Themes", dosya));

        return belge.Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();
    }
}
