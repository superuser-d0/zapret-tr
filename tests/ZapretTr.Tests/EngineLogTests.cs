using System.IO;
using System.Text;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Motorun ve dnscrypt-proxy'nin çıktısından okuduğumuz şeyler.
/// </summary>
/// <remarks>
/// Buradaki bütün örnek satırlar GERÇEK bir kullanıcının gönderdiği rapordan
/// birebir alındı (2026-09-12, TTNET). Uydurma satırla test yazmak bu iki yolda
/// özellikle tehlikeli olurdu: ikisi de dışarıdan gelen metni ayrıştırıyor ve
/// biçimi biz belirlemiyoruz.
/// </remarks>
public sealed class EngineLogTests
{
    [Fact]
    public void Winws_kendi_surumunu_bildiriyor()
    {
        // Uygulama "winws v72.13" yazıyordu; motor bu satırı yazıyordu. İkisi
        // AYRIŞMIŞTI ve fark ancak bir kullanıcının günlüğünden görüldü:
        // winws.exe zapret-win-bundle deposundan bir COMMIT ile sabitleniyor,
        // v72.13 ise yalnızca sahte yük dosyalarının geldiği zapret etiketi.
        var satir = "github version v72.12 (5cc46a9815b00e97401b1459984dff44abfec411)";

        Assert.Equal("v72.12", WinwsRunner.ParseVersion(satir));
    }

    [Fact]
    public void Winws_surumu_calistirmadan_ikiliden_okunuyor()
    {
        // Dizge sırası gerçek v72.12 ikilisinden birebir: commit, NUL, sürüm, NUL,
        // biçim metni. Alt bilgi bu olmadan motor çalışana kadar sürüm gösteremiyordu.
        var ikili = Encoding.ASCII.GetBytes(
            "desync\05cc46a9815b00e97401b1459984dff44abfec411\0v72.12\0github version %s (%s)\n\n\0desync");

        Assert.Equal("v72.12", WinwsRunner.FindEmbeddedVersion(ikili));
    }

    [Theory]
    // Biçim metni yok.
    [InlineData("v72.12\0baska bir metin\0")]
    // Biçim metninden önce sürüm biçimine uymayan bir dizge.
    [InlineData("abc\05cc46a98\0github version %s (%s)")]
    // Aradaki NUL yok: bitişik metin sürüm sayılmamalı.
    [InlineData("xv72.12github version %s (%s)")]
    public void Ikilide_surum_bulunamazsa_tahmin_edilmiyor(string icerik)
    {
        Assert.Null(WinwsRunner.FindEmbeddedVersion(Encoding.ASCII.GetBytes(icerik)));
    }

    [Fact]
    public void Depodaki_winws_ikilisinden_surum_okunuyor()
    {
        // CI tools/fetch-upstream.ps1 ile vendor/ klasörünü dolduruyor; arayüz duman
        // testleri de aynı dosyaya dayanıyor. Sürüm numarası burada sabitlenmiyor:
        // zapret-win-bundle commit'i değişince bu test değil, ikili değişir.
        var exe = Path.Combine(XmlCommentTests.RepoRoot, "vendor", "zapret-winws", "winws.exe");

        var surum = WinwsRunner.ReadEmbeddedVersion(exe);

        Assert.NotNull(surum);
        Assert.Matches(@"^v\d+(\.\d+)+$", surum);
    }

    [Fact]
    public void Surum_satiri_degilse_tahmin_edilmiyor()
    {
        // Bilmemek, yanlış bilmekten iyi: bu alanın tek işi teşhis ve yanlış
        // bir sürüm numarası teşhisi doğrudan yanlış yöne gönderir.
        Assert.Null(WinwsRunner.ParseVersion("windivert initialized. capture is started."));
        Assert.Null(WinwsRunner.ParseVersion(null));
        Assert.Null(WinwsRunner.ParseVersion("github version "));
    }

    [Theory]
    // Çözümleyici başına tekrar eden satırlar: 340 sunucu x 3 satır.
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] OK (DNSCrypt) - rtt: 194ms")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] OK (DNSCrypt) - rtt: 194ms - additional certificate")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [dnscry.pt-vancouver-ipv4] using the post-quantum X-Wing key exchange")]
    [InlineData("[2026-09-12 23:53:55] [NOTICE] [quad9-doh-ip4-port443-nofilter-ecs-pri] OK (DoH) - rtt: 73ms")]
    // Gecikme tablosu: tek başına ~340 satır.
    [InlineData("[2026-09-12 23:54:00] [NOTICE] Sorted latencies:")]
    [InlineData("[2026-09-12 23:54:00] [NOTICE] -    19ms dnscry.pt-doh-istanbul-ipv4")]
    [InlineData("[2026-09-12 23:54:00] [NOTICE] -   430ms dnscry.pt-doh-perth-ipv4")]
    [InlineData("")]
    public void Cozumleyici_dokumu_gunluge_girmiyor(string satir)
        => Assert.True(DnsCryptRunner.IsNoise(satir), "Bu satir gurultu sayilmaliydi: " + satir);

    [Theory]
    // Tek satırlık özet: hangi sunucunun seçildiği teşhis için değerli.
    [InlineData("[2026-09-12 23:54:00] [NOTICE] Server with the lowest initial latency: dnscry.pt-doh-istanbul-ipv4 (rtt: 19ms), live servers: 343")]
    // Hata ve uyarılar HER ZAMAN geçer: dnscrypt'in sorunu, kullanıcının ad
    // çözümünün tehlikede olması demek. İkinci satır bilerek gecikme tablosu
    // biçimini TAKLİT ediyor; süzgeç biçime değil seviyeye bakmalı.
    [InlineData("[2026-09-12 23:54:00] [ERROR] no servers configured")]
    [InlineData("[2026-09-12 23:54:00] [WARNING] -    19ms sunucuya ulasilamiyor")]
    [InlineData("[2026-09-12 23:53:50] [NOTICE] dnscrypt-proxy 2.1.18")]
    public void Bilgi_tasiyan_satirlar_geciyor(string satir)
        => Assert.False(DnsCryptRunner.IsNoise(satir), "Bu satir gunluge girmeliydi: " + satir);
}
