using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Sürüm karşılaştırması.
/// </summary>
/// <remarks>
/// Buradaki tehlike sessiz olanı: metin karşılaştırması kullanılsaydı "0.1.9"
/// ile "0.1.14" karşılaştırıldığında metin sırası 0.1.9'u SONRAYA koyar ve
/// güncelleme bildirimi hiç görünmezdi. Hiçbir hata da vermezdi; özellik
/// çalışıyor gibi durur, yalnızca işe yaramazdı. Bu proje 0.1.9'u da 0.1.14'ü
/// de gördüğü için senaryo varsayımsal değil.
/// </remarks>
public sealed class UpdateCheckerTests
{
    [Fact]
    public void Sorgu_siniri_dolunca_ne_kadar_beklenecegi_soyleniyor()
    {
        var simdi = new DateTimeOffset(2026, 9, 14, 16, 0, 0, TimeSpan.FromHours(3));
        var sifirlanma = simdi.AddMinutes(37).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        var metin = UpdateChecker.DescribeHttpFailure(403, "0", sifirlanma, simdi);

        Assert.Contains("saatlik sorgu sınırı", metin, StringComparison.Ordinal);
        Assert.Contains("37 dakika", metin, StringComparison.Ordinal);
        Assert.Contains("16:37", metin, StringComparison.Ordinal);
    }

    [Theory]
    // Sınır dolmadan gelen 403 (örneğin başka bir erişim sorunu) sınır diye anlatılmamalı.
    [InlineData(403, "12")]
    [InlineData(500, null)]
    public void Sinir_disindaki_hatalar_sinir_diye_anlatilmiyor(int kod, string? kalan)
    {
        var metin = UpdateChecker.DescribeHttpFailure(kod, kalan, null, DateTimeOffset.Now);

        Assert.DoesNotContain("sınır", metin, StringComparison.Ordinal);
        Assert.Contains(kod.ToString(System.Globalization.CultureInfo.InvariantCulture), metin, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"tag_name\": \"v0.1.22\", \"draft\": false}", "0.1.22")]
    [InlineData("{\"tag_name\":\"0.1.9\"}", "0.1.9")]
    [InlineData("{\"message\":\"API rate limit exceeded\"}", null)]
    [InlineData("", null)]
    public void Yayin_cevabindan_surum_okunuyor(string json, string? beklenen)
    {
        Assert.Equal(beklenen, UpdateChecker.ParseTagName(json));
    }

    [Theory]
    [InlineData("0.1.14", "0.1.9")]    // iki haneli yama, metin sırasıyla TERS
    [InlineData("0.2.0", "0.1.14")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.1.15", "0.1.14")]
    public void Yeni_surum_tespit_ediliyor(string latest, string current)
    {
        Assert.True(UpdateChecker.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("0.1.14", "0.1.14")]
    [InlineData("0.1.13", "0.1.14")]
    [InlineData("0.1.9", "0.1.14")]
    public void Ayni_ya_da_eski_surum_bildirilmiyor(string latest, string current)
    {
        Assert.False(UpdateChecker.IsNewer(latest, current));
    }

    [Fact]
    public void Commit_damgali_surum_dogru_okunuyor()
    {
        // Kurulu sürüm "0.1.14+2bac773..." biçiminde geliyor; damga
        // ayıklanmazsa Version.TryParse başarısız olur ve bildirim HİÇ
        // görünmez.
        Assert.True(UpdateChecker.IsNewer("0.1.15", "0.1.14+2bac773be673ef7d"));
        Assert.False(UpdateChecker.IsNewer("0.1.14", "0.1.14+2bac773be673ef7d"));
    }

    [Theory]
    [InlineData(null, "0.1.14")]
    [InlineData("0.1.15", null)]
    [InlineData("", "0.1.14")]
    [InlineData("bilinmiyor", "0.1.14")]
    public void Eksik_ya_da_bozuk_deger_bildirim_uretmiyor(string? latest, string? current)
    {
        // Ağ hatasında null dönüyor. Yanlış bir bildirim göstermek, hiç
        // göstermemekten kötü: kullanıcı olmayan bir sürümü aramaya çıkar.
        Assert.False(UpdateChecker.IsNewer(latest, current));
    }
}
