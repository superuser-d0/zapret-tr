using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Engel sayfası tespitinin karar mantığı.
/// </summary>
/// <remarks>
/// Bu tespit iki yönde de yanılabilir ve yanlış yönler eşit ağırlıkta değil:
///
///   Kaçırmak  -> engel sayfası "açılıyor" sayılır; hedef testte kullanılır ve
///                her strateji başarılı görünür.
///   Uydurmak  -> açılan bir site "DNS yönlendirmesi" sayılır; strateji
///                aramasından çıkarılır ve gerçekten aşılabilir bir engel hiç
///                denenmez.
///
/// İkincisi daha sinsi olduğu için zayıf işaretler tek başına yeterli sayılmıyor.
/// </remarks>
public sealed class BlockPageDetectionTests
{
    [Fact]
    public void GercekEngelSayfasi_Taniniyor()
    {
        // TTNET engel sayfasından alınmış gerçek parça.
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
        // Sansürü ANLATAN bir haber sayfası engel sayfası değildir. Tek zayıf
        // eşleşmeyi kanıt saymak, açılan bir siteyi engelli göstermek olurdu.
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

        // "5651" rakam dizisi sıradan içeriklerde geçebiliyor; işaret "5651 say"
        // olarak boşluklu tutulduğu için bu eşleşmemeli.
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

/// <summary>
/// "Başarı" tanımı: gerçek sunucuya ulaştık mı.
/// </summary>
/// <remarks>
/// Bu testlerin var olma sebebi ölçülmüş bir yanlış negatif. İlk sürümde yalnızca
/// 2xx başarı sayılıyordu ve gerçek bir koşumda şu sonuçlar "başarısız" yazıldı:
///   xvideos.com        HTTP 301 -> https://www.xvideos.com/
///   pornhub.com        HTTP 301 -> https://www.pornhub.com/
///   gateway.discord.gg HTTP 404
/// Üçü de çalışıyordu. Strateji dördünü birden açmıştı; araç yalnızca birini
/// saydı ve boşuna aramaya devam etti.
/// </remarks>
public sealed class SuccessCriteriaTests
{
    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(200)]
    public void SunucudanGelenCevap_Basari_Sayilir(int statusCode)
    {
        // Hangi durum kodu gelirse gelsin, cevap geldiyse TLS el sıkışması
        // tamamlanmış ve DPI bağlantıyı kesmemiş demektir. Ölçtüğümüz şey bu.
        Assert.True(statusCode is >= 200 and < 600);
        Assert.NotEqual(400, statusCode);
    }

    [Fact]
    public void EngelSayfasinaYonlendirme_Basari_Sayilmaz()
    {
        // Yönlendirme hedefi engel sayfasıysa ulaştığımız yer gerçek sunucu değil.
        Assert.NotNull(HttpProbeClient.FindBlockMarker("http://195.175.254.2/btk.gov.tr/index.html"));
    }

    [Fact]
    public void SiteninKendiWwwYonlendirmesi_EngelSayilmaz()
    {
        // Tam olarak kaçırılan vaka.
        Assert.Null(HttpProbeClient.FindBlockMarker("https://www.xvideos.com/"));
        Assert.Null(HttpProbeClient.FindBlockMarker("https://www.pornhub.com/"));
    }
}
