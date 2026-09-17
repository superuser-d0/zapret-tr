using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using ZapretTr.Core.Engine;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Windows bir ikiliyi güvenlik gerekçesiyle çalıştırmadığında kullanıcıya ne
/// olduğunun ve ne yapılacağının söylenmesi.
/// </summary>
/// <remarks>
/// 2026-09: "Windows güncellemesinden sonra Defender ZapretTR'yi siliyor" duyumu.
/// Eskiden bu durumda günlükte yalnızca Windows'un cümlesi kalıyordu; hangi
/// korumanın engellediği yazmıyordu. Gerekçesi SecurityBlockAdvice'ta.
///
/// Gerçek bir 4551 üretmek için Akıllı Uygulama Denetimi'ni açmak gerekiyor, bu
/// yüzden testler hata kodundan gidiyor. Kodların anlamları geliştirici makinesinde
/// Win32Exception ile doğrulandı.
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
    [InlineData(2)]    // dosya yok: KURULUM DOSYALARI EKSİK'in işi
    [InlineData(5)]    // erişim engellendi: yönetici yetkisi, güvenlik engeli değil
    [InlineData(193)]  // geçersiz Win32 uygulaması
    [InlineData(1053)] // servis zamanında cevap vermedi
    [InlineData(4552)] // ilke GEÇERSİZ: dosya engellenmedi
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

        // Engel değilse ayrıntı eskisi gibi yalnızca sc çıktısı.
        Assert.Equal("[SC] StartService FAILED 1053:", ServiceManager.StartFailureDetail(ServiceManager.WinwsServiceName, "[SC] StartService FAILED 1053:\r\n"));
    }

    [Fact]
    public void Motor_ve_dns_baslatma_yollari_engeli_taniyor()
    {
        // Process.Start'ın etrafındaki sınıflandırma kaldırılırsa kullanıcı yine
        // yalnızca Windows'un cümlesini görür. İki yol da kaynakta sabitleniyor.
        var motor = IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.Core", "Engine");

        Assert.Contains("SecurityBlockAdvice.Describe(ex, \"winws.exe\")",
            File.ReadAllText(IoPath.Combine(motor, "WinwsRunner.cs")));
        Assert.Contains("SecurityBlockAdvice.Describe(ex, \"dnscrypt-proxy.exe\")",
            File.ReadAllText(IoPath.Combine(motor, "DnsCryptRunner.cs")));
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlNtStatusToDosError(int status);

    [Theory]
    [InlineData(unchecked((int)0xC0000428), 577, SecurityBlockKind.ApplicationControl)]
    [InlineData(unchecked((int)0xC0000906), 225, SecurityBlockKind.Antivirus)]
    [InlineData(unchecked((int)0xC0000907), 226, SecurityBlockKind.Antivirus)]
    [InlineData(unchecked((int)0xC0000361), 1260, SecurityBlockKind.GroupPolicy)]
    [InlineData(unchecked((int)0xC0000364), 1260, SecurityBlockKind.GroupPolicy)]
    public void Yukleyici_cikis_kodu_engeli_adiyla_soyluyor(int cikisKodu, int win32, SecurityBlockKind tur)
    {
        // Eşlemeyi Windows'un kendisine soruyoruz: bir kod yanlış ezberlenmişse test düşer.
        Assert.Equal(win32, RtlNtStatusToDosError(cikisKodu));

        var metin = SecurityBlockAdvice.DescribeExitCode(cikisKodu, "winws.exe");

        Assert.NotNull(metin);
        Assert.Contains("winws.exe", metin);
        Assert.Contains($"0x{unchecked((uint)cikisKodu):X8}", metin);
        if (tur != SecurityBlockKind.GroupPolicy)
        {
            // Grup ilkesi rehbere yollamıyor: çözüm BT yöneticisinde.
            Assert.Contains("Windows engelliyor", metin);
        }

        if (tur == SecurityBlockKind.ApplicationControl)
        {
            Assert.Contains("Akıllı Uygulama Denetimi", metin);
            Assert.Contains("Bu uygulamanın bir kısmı engellendi", metin);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]                               // winws zaten çalışıyor
    [InlineData(unchecked((int)0xC0000135))]      // DLL bulunamadı: dosya eksik, engel değil
    [InlineData(unchecked((int)0xC0000005))]      // erişim ihlali: çökme
    [InlineData(unchecked((int)0xC000013A))]      // Ctrl+C ile durduruldu
    public void Baska_cikis_kodlari_guvenlik_engeli_sayilmiyor(int cikisKodu)
        => Assert.Null(SecurityBlockAdvice.DescribeExitCode(cikisKodu, "winws.exe"));

    [Fact]
    public void Winws_erken_olumu_ve_dogrulama_engeli_taniyor()
    {
        // Doğrulama yolu engeli metin olarak dönerse test motoru her adayı "geçersiz
        // parametre" diye eler. İki çağrı da kaynakta sabitleniyor.
        var kaynak = File.ReadAllText(IoPath.Combine(
            XmlCommentTests.RepoRoot, "src", "ZapretTr.Core", "Engine", "WinwsRunner.cs"));

        Assert.Contains("SecurityBlockAdvice.DescribeExitCode(exitCode, \"winws.exe\")", kaynak);
        Assert.Contains("SecurityBlockAdvice.DescribeExitCode(process.ExitCode, \"winws.exe\")", kaynak);
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
        // "Defender bu paketi işaretlemiyor (ölçtük)" 0.1.x'te bir ölçümdü; bulut
        // kararları değiştiği için artık bir söz. Geri gelmesin.
        foreach (var dosya in new[] { "README.md", "README-DETAYLI.md", IoPath.Combine("docs", "SORUN-GIDERME.md") })
        {
            var metin = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, dosya));
            Assert.DoesNotContain("işaretlemiyor", metin);
        }
    }
}
