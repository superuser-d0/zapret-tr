using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Engel sayfasi tespitinin karar mantigi.
/// </summary>
/// <remarks>
/// Bu tespit iki yonde de yanilabilir ve yanlis yonler esit agirlikta degil:
///
///   Kacirmak  -> engel sayfasi "aciliyor" sayilir; hedef testte kullanilir ve
///                her strateji basarili gorunur.
///   Uydurmak  -> acilan bir site "DNS yonlendirmesi" sayilir; strateji
///                aramasindan cikarilir ve gercekten asilabilir bir engel hic
///                denenmez.
///
/// Ikincisi daha sinsi oldugu icin zayif isaretler tek basina yeterli sayilmiyor.
/// </remarks>
public sealed class BlockPageDetectionTests
{
    [Fact]
    public void GercekEngelSayfasi_Taniniyor()
    {
        // TTNET engel sayfasindan alinmis gercek parca.
        const string page = """
            <html><head><meta http-equiv="Content-Type" content="text/html; charset=utf8">
            <title>...</title><style type="text/css"> .erisime_engellenmis { font-family: Arial; }
            </style></head><body></body></html>
            """;

        Assert.NotNull(HttpProbeClient.FindBlockMarker(page));
    }

    [Theory]
    [InlineData("<html>bir sey btk.gov.tr baska sey</html>")]
    [InlineData("<html>tib.gov.tr</html>")]
    [InlineData("<div class=\"erisime_engellenmis\">")]
    public void GucluIsaret_TekBasina_Yeterli(string content)
    {
        Assert.NotNull(HttpProbeClient.FindBlockMarker(content));
    }

    [Theory]
    [InlineData("Mahkeme koruma tedbiri karari verdi diye haber yaptilar.")]
    [InlineData("5651 sayili kanun hakkinda uzun bir analiz yazisi.")]
    [InlineData("Bu internet sitesine erisim konusunda bir tartisma var.")]
    public void ZayifIsaret_TekBasina_Yetersiz(string content)
    {
        // Sansuru ANLATAN bir haber sayfasi engel sayfasi degildir. Tek zayif
        // eslesmeyi kanit saymak, acilan bir siteyi engelli gostermek olurdu.
        Assert.Null(HttpProbeClient.FindBlockMarker(content));
    }

    [Fact]
    public void IkiZayifIsaret_Birlikte_Yeterli()
    {
        const string page = "5651 sayili karar geregi bu internet sitesine erisim engellenmistir.";
        Assert.NotNull(HttpProbeClient.FindBlockMarker(page));
    }

    [Fact]
    public void SiradanSayfa_EngelSayilmaz()
    {
        const string page = "<html><head><title>YouTube</title></head><body>video 5651234 izlendi</body></html>";

        // "5651" rakam dizisi siradan iceriklerde gecebiliyor; isaret "5651 say"
        // olarak bosluklu tutuldugu icin bu eslesmemeli.
        Assert.Null(HttpProbeClient.FindBlockMarker(page));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BosIcerik_EngelSayilmaz(string content)
    {
        Assert.Null(HttpProbeClient.FindBlockMarker(content));
    }

    [Fact]
    public void BuyukKucukHarf_Onemsiz()
    {
        Assert.NotNull(HttpProbeClient.FindBlockMarker("<div class=\"ERISIME_ENGELLENMIS\">"));
    }
}
