using System.ComponentModel;
using System.IO;
using ZapretTr.Core.Engine;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Windows bir ikiliyi guvenlik gerekcesiyle calistirmadiginda kullaniciya ne
/// oldugunun ve ne yapilacaginin soylenmesi.
/// </summary>
/// <remarks>
/// 2026-09: "Windows guncellemesinden sonra Defender ZapretTR'i siliyor" duyumu.
/// Eskiden bu durumda gunlukte yalnizca Windows'un cumlesi kaliyordu; hangi
/// korumanin engelledigi yazmiyordu. Gerekcesi SecurityBlockAdvice'ta.
///
/// Gercek bir 4551 uretmek icin Akilli Uygulama Denetimi'ni acmak gerekiyor, bu
/// yuzden testler hata kodundan gidiyor. Kodlarin anlamlari gelistirici makinesinde
/// Win32Exception ile dogrulandi.
/// </remarks>
public sealed class SecurityBlockAdviceTests
{
    [Theory]
    [InlineData(225, SecurityBlockKind.Antivirus)]
    [InlineData(226, SecurityBlockKind.Antivirus)]
    [InlineData(4551, SecurityBlockKind.ApplicationControl)]
    [InlineData(4556, SecurityBlockKind.ApplicationControl)]
    [InlineData(4580, SecurityBlockKind.ApplicationControl)]
    [InlineData(4581, SecurityBlockKind.ApplicationControl)]
    [InlineData(4582, SecurityBlockKind.ApplicationControl)]
    [InlineData(1260, SecurityBlockKind.GroupPolicy)]
    public void Guvenlik_engeli_kodlari_taniniyor(int kod, SecurityBlockKind beklenen)
        => Assert.Equal(beklenen, SecurityBlockAdvice.Classify(kod));

    [Theory]
    [InlineData(2)]    // dosya yok: KURULUM DOSYALARI EKSIK'in isi
    [InlineData(5)]    // erisim engellendi: yonetici yetkisi, guvenlik engeli degil
    [InlineData(193)]  // gecersiz Win32 uygulamasi
    [InlineData(1053)] // servis zamaninda cevap vermedi
    [InlineData(4552)] // ilke GECERSIZ: dosya engellenmedi
    public void Baska_hatalar_guvenlik_engeli_sayilmiyor(int kod)
    {
        Assert.Null(SecurityBlockAdvice.Classify(kod));
        Assert.Null(SecurityBlockAdvice.Describe(new Win32Exception(kod), "winws.exe"));
    }

    [Fact]
    public void Akilli_Uygulama_Denetimi_adiyla_ve_cozumuyle_soyleniyor()
    {
        var metin = SecurityBlockAdvice.Describe(new Win32Exception(4551), "winws.exe");

        Assert.NotNull(metin);
        Assert.Contains("winws.exe", metin);
        Assert.Contains("Akıllı Uygulama Denetimi", metin);
        Assert.Contains("4551", metin);
        Assert.Contains("Windows engelliyor", metin);
    }

    [Fact]
    public void Antivirus_engeli_koruma_gecmisini_gosteriyor()
    {
        var metin = SecurityBlockAdvice.Describe(new Win32Exception(225), "dnscrypt-proxy.exe");

        Assert.NotNull(metin);
        Assert.Contains("dnscrypt-proxy.exe", metin);
        Assert.Contains("Koruma geçmişi", metin);
        Assert.Contains("Windows engelliyor", metin);
    }

    [Fact]
    public void Sc_ciktisindaki_hata_kodu_okunuyor()
    {
        const string cikti = "[SC] StartService FAILED 225:\r\n\r\n"
                             + "Operation did not complete successfully because the file contains a virus or potentially unwanted software.\r\n";

        var metin = SecurityBlockAdvice.DescribeScOutput(cikti, "winws.exe");

        Assert.NotNull(metin);
        Assert.Contains("antivirüs", metin);
        Assert.Contains("225", metin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[SC] StartService FAILED 1053:\r\n\r\nThe service did not respond to the start or control request in a timely fashion.")]
    [InlineData("SERVICE_NAME: ZapretTR\r\n        STATE              : 2  START_PENDING")]
    public void Sc_ciktisinda_guvenlik_engeli_yoksa_ekleme_yapilmiyor(string? cikti)
        => Assert.Null(SecurityBlockAdvice.DescribeScOutput(cikti, "winws.exe"));

    [Fact]
    public void Servis_hatasi_ayrintisi_dogru_dosyayi_adliyor()
    {
        const string cikti = "[SC] StartService FAILED 4551:\r\n\r\nAn Application Control policy has blocked this file.";

        var dns = ServiceManager.StartFailureDetail(ServiceManager.DnsServiceName, cikti);
        Assert.StartsWith("[SC] StartService FAILED 4551", dns);
        Assert.Contains("dnscrypt-proxy.exe", dns);

        var winws = ServiceManager.StartFailureDetail(ServiceManager.WinwsServiceName, cikti);
        Assert.Contains("winws.exe", winws);

        // Engel degilse ayrinti eskisi gibi yalnizca sc ciktisi.
        Assert.Equal("[SC] StartService FAILED 1053:", ServiceManager.StartFailureDetail(ServiceManager.WinwsServiceName, "[SC] StartService FAILED 1053:\r\n"));
    }

    [Fact]
    public void Motor_ve_dns_baslatma_yollari_engeli_taniyor()
    {
        // Process.Start'in etrafindaki siniflandirma kaldirilirsa kullanici yine
        // yalnizca Windows'un cumlesini gorur. Iki yol da kaynakta sabitleniyor.
        var motor = IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.Core", "Engine");

        Assert.Contains("SecurityBlockAdvice.Describe(ex, \"winws.exe\")",
            File.ReadAllText(IoPath.Combine(motor, "WinwsRunner.cs")));
        Assert.Contains("SecurityBlockAdvice.Describe(ex, \"dnscrypt-proxy.exe\")",
            File.ReadAllText(IoPath.Combine(motor, "DnsCryptRunner.cs")));
    }

    [Fact]
    public void Mesajin_gosterdigi_rehber_bolumu_var()
    {
        var rehber = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "docs", "SORUN-GIDERME.md"));

        Assert.Contains("<a id=\"windows-engelliyor\"></a>", rehber);
        Assert.Contains("### Windows engelliyor", rehber);
    }

    [Fact]
    public void Belgelerde_olculmemis_Defender_guvencesi_kalmadi()
    {
        // "Defender bu paketi isaretlemiyor (olctuk)" 0.1.x'te bir olcumdu; bulut
        // kararlari degistigi icin artik bir soz. Geri gelmesin.
        foreach (var dosya in new[] { "README.md", "README-DETAYLI.md", IoPath.Combine("docs", "SORUN-GIDERME.md") })
        {
            var metin = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, dosya));
            Assert.DoesNotContain("işaretlemiyor", metin);
        }
    }
}
