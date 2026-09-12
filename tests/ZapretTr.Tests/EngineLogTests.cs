using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Motorun ve dnscrypt-proxy'nin ciktisindan okudugumuz seyler.
/// </summary>
/// <remarks>
/// Buradaki butun ornek satirlar GERCEK bir kullanicinin gonderdigi rapordan
/// birebir alindi (2026-09-12, TTNET). Uydurma satirla test yazmak bu iki yolda
/// ozellikle tehlikeli olurdu: ikisi de disaridan gelen metni ayristiriyor ve
/// bicimi biz belirlemiyoruz.
/// </remarks>
public sealed class EngineLogTests
{
    [Fact]
    public void Winws_kendi_surumunu_bildiriyor()
    {
        // Uygulama "winws v72.13" yaziyordu; motor bu satiri yaziyordu. Ikisi
        // AYRISMISTI ve fark ancak bir kullanicinin gunlugunden goruldu:
        // winws.exe zapret-win-bundle deposundan bir COMMIT ile sabitleniyor,
        // v72.13 ise yalnizca sahte yuk dosyalarinin geldigi zapret tag'i.
        var satir = "github version v72.12 (5cc46a9815b00e97401b1459984dff44abfec411)";

        Assert.Equal("v72.12", WinwsRunner.ParseVersion(satir));
    }

    [Fact]
    public void Surum_satiri_degilse_tahmin_edilmiyor()
    {
        // Bilmemek, yanlis bilmekten iyi: bu alanin tek isi teshis ve yanlis
        // bir surum numarasi teshisi dogrudan yanlis yone gonderir.
        Assert.Null(WinwsRunner.ParseVersion("windivert initialized. capture is started."));
        Assert.Null(WinwsRunner.ParseVersion(null));
        Assert.Null(WinwsRunner.ParseVersion("github version "));
    }

    [Theory]
    // Cozumleyici basina tekrar eden satirlar: 340 sunucu x 3 satir.
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] OK (DNSCrypt) - rtt: 194ms")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] OK (DNSCrypt) - rtt: 194ms - additional certificate")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] using the post-quantum X-Wing key exchange")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [quad9-doh-ip4-port443-nofilter-ecs-pri] OK (DoH) - rtt: 73ms")]
    // Gecikme tablosu: tek basina ~340 satir.
    [InlineData("[2026-09-12 23:54:00] [NOTICE] Sorted latencies:")]
    [InlineData("[2026-09-12 23:54:00] [NOTICE] -    19ms dnscry.pt-doh-istanbul-ipv4")]
    [InlineData("[2026-09-12 23:54:00] [NOTICE] -   430ms dnscry.pt-doh-perth-ipv4")]
    [InlineData("")]
    public void Cozumleyici_dokumu_gunluge_girmiyor(string satir)
        => Assert.True(DnsCryptRunner.IsNoise(satir), "Bu satir gurultu sayilmaliydi: " + satir);

    [Theory]
    // Tek satirlik ozet: hangi sunucunun secildigi teshis icin degerli.
    [InlineData("[2026-09-12 23:54:00] [NOTICE] Server with the lowest initial latency: dnscry.pt-doh-istanbul-ipv4 (rtt: 19ms), live servers: 343")]
    // Hata ve uyarilar HER ZAMAN gecer: dnscrypt'in sorunu, kullanicinin ad
    // cozumunun tehlikede olmasi demek. Ikinci satir bilerek gecikme tablosu
    // bicimini TAKLIT ediyor -- suzgec bicime degil seviyeye bakmali.
    [InlineData("[2026-09-12 23:54:00] [ERROR] no servers configured")]
    [InlineData("[2026-09-12 23:54:00] [WARNING] -    19ms sunucuya ulasilamiyor")]
    [InlineData("[2026-09-12 23:53:50] [NOTICE] dnscrypt-proxy 2.1.18")]
    public void Bilgi_tasiyan_satirlar_geciyor(string satir)
        => Assert.False(DnsCryptRunner.IsNoise(satir), "Bu satir gunluge girmeliydi: " + satir);
}
