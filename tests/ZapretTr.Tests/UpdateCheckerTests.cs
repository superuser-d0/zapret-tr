using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Surum karsilastirmasi.
/// </summary>
/// <remarks>
/// Buradaki tehlike sessiz olani: metin karsilastirmasi kullanilsaydi "0.1.9"
/// ile "0.1.14" karsilastirildiginda metin sirasi 0.1.9'u SONRAYA koyar ve
/// guncelleme bildirimi hic gorunmezdi. Hicbir hata da vermezdi -- ozellik
/// calisiyor gibi durur, yalnizca ise yaramazdi. Bu proje 0.1.9'u da 0.1.14'u
/// de gordugu icin senaryo varsayimsal degil.
/// </remarks>
public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.1.14", "0.1.9")]    // iki haneli yama, metin sirasiyla TERS
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
        // Kurulu surum "0.1.14+2bac773..." biciminde geliyor; damga
        // ayiklanmazsa Version.TryParse basarisiz olur ve bildirim HIC
        // gorunmez.
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
        // Ag hatasinda null donuyor. Yanlis bir bildirim gostermek, hic
        // gostermemekten kotu: kullanici olmayan bir surumu aramaya cikar.
        Assert.False(UpdateChecker.IsNewer(latest, current));
    }
}
