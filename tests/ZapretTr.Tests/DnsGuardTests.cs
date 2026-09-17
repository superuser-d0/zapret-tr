using System.Xml.Linq;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS bekçisinin kararları ve zamanlanmış görev tanımı.
/// </summary>
/// <remarks>
/// Bekçi SYSTEM olarak, kullanıcının haberi olmadan sistem DNS'ine dokunuyor. Yanlış
/// bir karar iki yöne de pahalı: gereksiz geri alma şifreli DNS'i sessizce söküp DNS
/// engellemesini geri getirir, gereksiz bekleme ise makineyi ad çözemez hâlde
/// bırakır. Karar bu yüzden ölçümden ayrılmış saf bir fonksiyon ve tablosu burada.
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
        // Kullanıcının kendi DNS ayarı. Bekçinin dokunabileceği tek şey kendi
        // yaptığı yönlendirme.
        Assert.Equal(DnsGuardAction.None, DnsGuard.Decide(Facts(hasBackup: false, owner: null, responding: false)));
    }

    [Fact]
    public void ServisAyakta_EksikKartlarTamamlanir()
    {
        // Sonradan takılan kartın yönlendirilmesi tam olarak bu yol.
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
        // Virüsten koruma dnscrypt-proxy.exe'yi karantinaya aldığında servis kaydı
        // duruyor ama dosya dönene kadar cevap veremez. Beklemek yalnızca makineyi
        // 90 sn daha ad çözemez bırakır; askıya almak, dosya geri geldiğinde
        // yönlendirmenin de geri gelmesini sağlıyor.
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
        // Çözümleyici o an cevap vermese bile: uygulama kendi dnscrypt'ini
        // yeniden başlatıyor olabilir ve çökmesini kendisi görüyor.
        Assert.Equal(DnsGuardAction.None,
            DnsGuard.Decide(Facts(owner: DnsBackupOwner.App, appRunning: true, responding: false)));
    }

    [Fact]
    public void UygulamaninYonlendirmesi_UygulamaKapali_CozumleyiciYok_GeriAlinir()
    {
        // Çökme ya da elektrik kesintisi sonrası açılış: bekçinin olmadığı
        // dönemde makine uygulama açılana kadar hiçbir adı çözemiyordu.
        Assert.Equal(DnsGuardAction.Restore,
            DnsGuard.Decide(Facts(owner: DnsBackupOwner.App, responding: false, serviceInstalled: false)));
    }

    [Fact]
    public void UygulamaninYonlendirmesi_UygulamaKapali_OksuzCozumleyiciCalisiyor_Dokunulmaz()
    {
        // Ad çözümü çalışıyor ve şifreli; sökmek çalışan bir durumu bozmak olurdu.
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

        // Servis silinmişse beklenecek bir şey yok: bir daha hiç cevap vermeyecek.
        var silinmis = Facts(serviceInstalled: false, responding: false);
        Assert.False(DnsGuard.NeedsResolverWait(silinmis, DnsGuard.Decide(silinmis)));

        var ayakta = Facts();
        Assert.False(DnsGuard.NeedsResolverWait(ayakta, DnsGuard.Decide(ayakta)));
    }

    // --- Zamanlanmış görev ----------------------------------------------------

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void GorevTanimi_GecerliXml_ve_KomutYoluKacisli()
    {
        // Program Files yolunda boşluk ve (başka bir kurulum dizininde) & olabilir.
        // Kaçışsız bir & schtasks'ın bütün tanımı reddetmesi demek, üstelik sessizce:
        // kurulum paketi --register-dns-guard'ın çıkış kodunu kullanıcıya göstermiyor.
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

        // SYSTEM: bekçi netsh ile DNS yazıyor ve kullanıcı oturumu açılmadan
        // (açılışta) çalışmalı.
        Assert.Equal("S-1-5-18", doc.Descendants(Ns + "UserId").Single().Value);

        var triggers = doc.Descendants(Ns + "Triggers").Single().Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("BootTrigger", triggers);
        Assert.Contains("EventTrigger", triggers);
        Assert.Contains("TimeTrigger", triggers);

        // Ağ olayı aboneliği: yeni kart ve uykudan uyanma bu olayla geliyor.
        var abonelik = doc.Descendants(Ns + "Subscription").Single().Value;
        Assert.Contains("Microsoft-Windows-NetworkProfile/Operational", abonelik);
        Assert.Contains("EventID=10000", abonelik);

        // Aynı anda iki bekçi aynı DNS'e dokunmasın.
        Assert.Equal("IgnoreNew", doc.Descendants(Ns + "MultipleInstancesPolicy").Single().Value);

        // Pille çalışan dizüstünde görev hiç başlamazsa bekçi yok demektir.
        Assert.Equal("false", doc.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
    }
}
