using System.Xml.Linq;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS bekcisinin kararlari ve zamanlanmis gorev tanimi.
/// </summary>
/// <remarks>
/// Bekci SYSTEM olarak, kullanicinin haberi olmadan sistem DNS'ine dokunuyor. Yanlis
/// bir karar iki yone de pahali: gereksiz geri alma sifreli DNS'i sessizce sokup DNS
/// engellemesini geri getirir, gereksiz bekleme ise makineyi ad cozemez halde
/// birakir. Karar bu yuzden olcumden ayrilmis saf bir fonksiyon ve tablosu burada.
/// </remarks>
public sealed class DnsGuardTests
{
    private static DnsGuardFacts Facts(
        bool hasBackup = true,
        string? owner = DnsBackupOwner.Service,
        bool suspended = false,
        bool serviceInstalled = true,
        bool binaryExists = true,
        bool appRunning = false,
        bool responding = true)
        => new(hasBackup, owner, suspended, serviceInstalled, binaryExists, appRunning, responding);

    [Fact]
    public void YedekYok_AskidaDegil_HicbirSeyYapilmaz()
    {
        // Kullanicinin kendi DNS ayari. Bekcinin dokunabilecegi tek sey kendi
        // yaptigi yonlendirme.
        Assert.Equal(DnsGuardAction.None, DnsGuard.Decide(Facts(hasBackup: false, owner: null, responding: false)));
    }

    [Fact]
    public void ServisAyakta_EksikKartlarTamamlanir()
    {
        // Sonradan takilan kartin yonlendirilmesi tam olarak bu yol.
        Assert.Equal(DnsGuardAction.Reapply, DnsGuard.Decide(Facts()));
    }

    [Fact]
    public void ServisKaldirilmis_YonlendirmeGeriAlinir()
    {
        Assert.Equal(DnsGuardAction.Restore, DnsGuard.Decide(Facts(serviceInstalled: false, responding: false)));
    }

    [Fact]
    public void ServisinIkilisiYok_AskiyaAlinir_Beklenmeden()
    {
        // Virusten koruma dnscrypt-proxy.exe'yi karantinaya aldiginda servis kaydi
        // duruyor ama dosya donene kadar cevap veremez. Beklemek yalnizca makineyi
        // 90 sn daha ad cozemez birakir; askiya almak, dosya geri geldiginde
        // yonlendirmenin de geri gelmesini sagliyor.
        var facts = Facts(binaryExists: false, responding: false);
        var action = DnsGuard.Decide(facts);

        Assert.Equal(DnsGuardAction.RestoreAndSuspend, action);
        Assert.False(DnsGuard.NeedsResolverWait(facts, action));
    }

    [Fact]
    public void ServisKurulu_CevapYok_AskiyaAlinir()
    {
        Assert.Equal(DnsGuardAction.RestoreAndSuspend, DnsGuard.Decide(Facts(responding: false)));
    }

    [Fact]
    public void Askida_ServisDondu_YenidenYonlendirilir()
    {
        Assert.Equal(DnsGuardAction.Resume,
            DnsGuard.Decide(Facts(hasBackup: false, owner: null, suspended: true)));
    }

    [Fact]
    public void Askida_ServisHalaCevapsiz_Beklenir()
    {
        Assert.Equal(DnsGuardAction.None,
            DnsGuard.Decide(Facts(hasBackup: false, owner: null, suspended: true, responding: false)));
    }

    [Fact]
    public void Askida_ServisSilinmis_IsaretTemizlenir()
    {
        Assert.Equal(DnsGuardAction.ClearSuspension,
            DnsGuard.Decide(Facts(hasBackup: false, owner: null, suspended: true, serviceInstalled: false)));
    }

    [Fact]
    public void UygulamaninYonlendirmesi_UygulamaAcikken_Dokunulmaz()
    {
        // Cozumleyici o an cevap vermese bile: uygulama kendi dnscrypt'ini
        // yeniden baslatiyor olabilir ve cokmesini kendisi goruyor.
        Assert.Equal(DnsGuardAction.None,
            DnsGuard.Decide(Facts(owner: DnsBackupOwner.App, appRunning: true, responding: false)));
    }

    [Fact]
    public void UygulamaninYonlendirmesi_UygulamaKapali_CozumleyiciYok_GeriAlinir()
    {
        // Cokme ya da elektrik kesintisi sonrasi acilis: bekcinin olmadigi
        // donemde makine uygulama acilana kadar hicbir adi cozemiyordu.
        Assert.Equal(DnsGuardAction.Restore,
            DnsGuard.Decide(Facts(owner: DnsBackupOwner.App, responding: false, serviceInstalled: false)));
    }

    [Fact]
    public void UygulamaninYonlendirmesi_UygulamaKapali_OksuzCozumleyiciCalisiyor_Dokunulmaz()
    {
        // Ad cozumu calisiyor ve sifreli; sokmek calisan bir durumu bozmak olurdu.
        Assert.Equal(DnsGuardAction.None,
            DnsGuard.Decide(Facts(owner: DnsBackupOwner.App, responding: true, serviceInstalled: false)));
    }

    [Fact]
    public void Bekleme_YalnizcaCevapsizlikYuzundenGeriAlmadaYapilir()
    {
        var olu = Facts(responding: false);
        Assert.True(DnsGuard.NeedsResolverWait(olu, DnsGuard.Decide(olu)));

        var uygulamaCokmus = Facts(owner: DnsBackupOwner.App, responding: false);
        Assert.True(DnsGuard.NeedsResolverWait(uygulamaCokmus, DnsGuard.Decide(uygulamaCokmus)));

        // Servis silinmisse beklenecek bir sey yok: bir daha hic cevap vermeyecek.
        var silinmis = Facts(serviceInstalled: false, responding: false);
        Assert.False(DnsGuard.NeedsResolverWait(silinmis, DnsGuard.Decide(silinmis)));

        var ayakta = Facts();
        Assert.False(DnsGuard.NeedsResolverWait(ayakta, DnsGuard.Decide(ayakta)));
    }

    // --- Zamanlanmis gorev ----------------------------------------------------

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void GorevTanimi_GecerliXml_ve_KomutYoluKacisli()
    {
        // Program Files yolunda bosluk ve (baska bir kurulum dizininde) & olabilir.
        // Kacissiz bir & schtasks'in butun tanimi reddetmesi demek -- sessizce:
        // kurulum paketi --register-dns-guard'in cikis kodunu kullaniciya gostermiyor.
        var yol = @"C:\Program Files\R&D Araclari\ZapretTR\ZapretTR.exe";
        var doc = XDocument.Parse(DnsGuardTask.BuildTaskXml(yol));

        var exec = doc.Descendants(Ns + "Exec").Single();
        Assert.Equal(yol, exec.Element(Ns + "Command")!.Value);
        Assert.Equal(DnsGuardTask.GuardFlag, exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void GorevTanimi_SystemOlarak_ve_UcTetikleyiciyle()
    {
        var doc = XDocument.Parse(DnsGuardTask.BuildTaskXml(@"C:\x\ZapretTR.exe"));

        // SYSTEM: bekci netsh ile DNS yaziyor ve kullanici oturumu acilmadan
        // (acilista) calismali.
        Assert.Equal("S-1-5-18", doc.Descendants(Ns + "UserId").Single().Value);

        var triggers = doc.Descendants(Ns + "Triggers").Single().Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("BootTrigger", triggers);
        Assert.Contains("EventTrigger", triggers);
        Assert.Contains("TimeTrigger", triggers);

        // Ag olayi aboneligi: yeni kart ve uykudan uyanma bu olayla geliyor.
        var abonelik = doc.Descendants(Ns + "Subscription").Single().Value;
        Assert.Contains("Microsoft-Windows-NetworkProfile/Operational", abonelik);
        Assert.Contains("EventID=10000", abonelik);

        // Ayni anda iki bekci ayni DNS'e dokunmasin.
        Assert.Equal("IgnoreNew", doc.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);

        // Pille calisan dizustunde gorev hic baslamazsa bekci yok demektir.
        Assert.Equal("false", doc.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
    }
}
