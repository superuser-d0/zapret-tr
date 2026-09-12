using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Makinenin O ANKI durumunu satir satir yazar: "Raporu Kaydet"in gunlukten
/// sonraki yarisi.
/// </summary>
/// <remarks>
/// Neden ayri bir sinif ve neden bu kadar cok alan: raporun tek isi, uzaktan
/// gelen "olmadi" cumlesini teshis edilebilir bir seye cevirmek. Bugune kadar
/// rapor yalnizca gorunum modelinin BILDIGI seyleri tasiyordu -- secili profil,
/// secili strateji, gunluk. Oysa "olmadi" bildirimlerinin cogunda gorunum
/// modelinin bildigi hicbir sey yanlis degil; yanlis olan, gorunum modelinin
/// BAKMADIGI seyler:
///
///   * uygulama yonetici olarak acilmamis (winws hicbir sey yapamaz),
///   * upstream ikilileri eksik ya da bozuk kurulmus,
///   * winws/dnscrypt surecleri hic ayakta degil,
///   * otomatik baslatma servisi kurulu ama DURMUS,
///   * sistem DNS'i hala bizde ama dinleyen kimse yok (ad cozumu tamamen olu),
///   * GoodbyeDPI gibi baska bir arac WinDivert'i tutuyor.
///
/// Bunlarin hicbiri gunlukte gorunmuyor -- ozellikle de kullanici bilgisayari
/// yeniden baslatip uygulamayi YENI actiysa: o durumda gunluk neredeyse bos ve
/// eski rapor bicimi hicbir sey anlatmiyordu. Bu bolum kullanicidan tek tek
/// soru sormadan o alti soruyu birden cevapliyor.
///
/// Hicbir adim disari istek yapmaz ve genel IP adresi yazilmaz.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class EnvironmentReport
{
    /// <summary>Ortam ozetini toplar. Hicbir kosulda firlatmaz.</summary>
    public static async Task<IReadOnlyList<string>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();

        await AddAsync(lines, "Yonetici yetkisi", YetkiAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Kurulum dosyalari", IkililerAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Calisan surecler", SureclerAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Servisler", ServislerAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Sistem DNS'i", DnsAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Cakisan araclar", CakismaAsync, cancellationToken).ConfigureAwait(false);
        await AddAsync(lines, "Kullanici verisi", VeriAsync, cancellationToken).ConfigureAwait(false);

        return lines;
    }

    /// <summary>
    /// Tek bir bolumu ekler ve o bolumun hatasini kendi icinde tutar.
    /// </summary>
    /// <remarks>
    /// Bolumlerden biri patlarsa rapor yine de yazilmali. Rapor bir teshis araci;
    /// teshis araclarinin en kotu ozelligi, tam ihtiyac duyuldugu anda hicbir sey
    /// uretmemeleridir.
    /// </remarks>
    private static async Task AddAsync(
        List<string> lines,
        string baslik,
        Func<CancellationToken, Task<IReadOnlyList<string>>> uret,
        CancellationToken cancellationToken)
    {
        lines.Add(baslik);
        lines.Add(new string('-', baslik.Length));

        try
        {
            lines.AddRange(await uret(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            lines.Add("  (okunamadi: " + ex.Message + ")");
        }

        lines.Add(string.Empty);
    }

    private static Task<IReadOnlyList<string>> YetkiAsync(CancellationToken _)
    {
        var lines = new List<string>();

        if (ElevationGuard.IsElevated())
        {
            lines.Add("  evet");
        }
        else
        {
            lines.Add("  HAYIR -- ASIL SEBEP BU OLABILIR.");
            lines.Add("  winws cekirdek surucusu kullaniyor ve yonetici yetkisi olmadan hicbir sey");
            lines.Add("  yapamaz. Uygulamayi kisayola sag tiklayip \"Yonetici olarak calistir\" ile acin.");
        }

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    private static Task<IReadOnlyList<string>> IkililerAsync(CancellationToken _)
    {
        var lines = new List<string>();
        var vendor = VendorPaths.TryLocate();

        if (vendor is null)
        {
            lines.Add("  winws.exe BULUNAMADI -- kurulum eksik ya da bozuk.");
            lines.Add("  " + VendorPaths.MissingFilesAdvice("winws.exe"));
            return Task.FromResult<IReadOnlyList<string>>(lines);
        }

        lines.Add("  Dizin: " + vendor.Root);

        var missing = vendor.FindMissingFiles();
        if (missing.Count == 0)
        {
            lines.Add("  Butun dosyalar yerinde.");
        }
        else
        {
            lines.Add($"  {missing.Count} DOSYA EKSIK: " + string.Join(", ", missing.Select(Path.GetFileName)));
            lines.Add("  Bu haliyle hicbir strateji calisamaz.");
            lines.Add("  " + VendorPaths.MissingFilesAdvice(
                string.Join(", ", missing.Select(Path.GetFileName))));
        }

        lines.Add("  dnscrypt-proxy.exe: " + (File.Exists(vendor.DnsCryptExe) ? "var" : "YOK"));

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    private static Task<IReadOnlyList<string>> SureclerAsync(CancellationToken _)
    {
        var lines = new List<string>
        {
            "  winws.exe          : " + SurecSayisi("winws"),
            "  dnscrypt-proxy.exe : " + SurecSayisi("dnscrypt-proxy"),
            "  ZapretTR.exe       : " + SurecSayisi("ZapretTR"),
        };

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    /// <summary>Ayni addaki surecten kac tane var.</summary>
    /// <remarks>
    /// ZapretTR icin sayinin BIRDEN buyuk olmasi tek basina bir teshis: ikinci
    /// bir ornek winws'i ayni filtreyle acamaz ve kullanici "Baslat calismiyor"
    /// diye bildirir.
    /// </remarks>
    private static string SurecSayisi(string ad)
    {
        try
        {
            var count = Process.GetProcessesByName(ad).Length;
            return count == 0 ? "calismiyor" : $"{count} ornek calisiyor";
        }
        catch (Exception ex)
        {
            return "okunamadi (" + ex.Message + ")";
        }
    }

    private static async Task<IReadOnlyList<string>> ServislerAsync(CancellationToken cancellationToken)
    {
        var status = await ServiceManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        string winws;
        if (!status.WinwsInstalled)
        {
            winws = "kurulu degil";
        }
        else
        {
            winws = status.WinwsRunning ? "kurulu ve calisiyor" : "KURULU AMA DURMUS";
        }

        var lines = new List<string>
        {
            $"  {ServiceManager.WinwsServiceName,-14}: " + winws,
            $"  {ServiceManager.DnsServiceName,-14}: " + (status.DnsInstalled ? "kurulu" : "kurulu degil"),
        };

        if (status.InstalledButStopped)
        {
            lines.Add("  Servis var ama durmus: koruma su anda KAPALI.");
        }

        if (!status.WinwsInstalled)
        {
            lines.Add("  Otomatik baslatma kurulu degil: bilgisayar yeniden baslatildiginda");
            lines.Add("  koruma KENDILIGINDEN acilmaz. Kalici olmasi icin uygulamadaki");
            lines.Add("  \"Servis Olarak Yukle (Otomatik Baslat)\" dugmesi kullanilmali.");
        }

        return lines;
    }

    private static async Task<IReadOnlyList<string>> DnsAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var responding = await DnsCryptRunner
            .IsLocalResolverRespondingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        lines.Add("  127.0.0.1:53 cevap veriyor mu : " + (responding ? "evet" : "hayir"));
        lines.Add("  Yedek (yani DNS bizde mi)     : "
                  + (SystemDnsManager.HasBackup
                      ? "var, sahibi: " + (SystemDnsManager.BackupOwner ?? "bilinmiyor")
                      : "yok"));

        // EN TEHLIKELI BILESIM ve raporda tek satirda gorunmesi gereken sey:
        // sistem DNS'i bize cevrilmis ama dinleyen kimse yok. O makine hicbir adi
        // cozemez ve kullanicinin bildirdigi sey "internetim gitti" olur -- ki
        // disaridan "program calismadi" ile ayni cumleyle anlatilir.
        if (SystemDnsManager.HasBackup && !responding)
        {
            lines.Add("  DIKKAT: sistem DNS'i bize cevrilmis ama cozumleyici cevap vermiyor.");
            lines.Add("  Bu makine su anda hicbir adres cozemiyor olabilir. Uygulamayi acmak");
            lines.Add("  bunu kendiliginden geri alir; almazsa \"Tum Ayarlari Sifirla\".");
        }

        foreach (var nic in AktifArayuzler())
        {
            var props = nic.GetIPProperties();
            var sunucular = props.DnsAddresses.Select(a => a.ToString()).ToList();

            lines.Add($"  [{nic.Name}] {nic.NetworkInterfaceType}, {nic.OperationalStatus}"
                      + " · DNS: " + (sunucular.Count == 0 ? "(yok)" : string.Join(", ", sunucular)));
        }

        return lines;
    }

    /// <summary>
    /// Raporda gosterilecek arayuzler: yonlendirmenin dokundugu kume.
    /// </summary>
    /// <remarks>
    /// Kasitli olarak <see cref="SystemDnsManager.IsRedirectTarget"/> ile ayni
    /// soruyu soruyor. Raporun degeri, "hangi kartlarin DNS'i degistirilecekti"
    /// ile "hangi kartlarin DNS'i gercekten degismis" arasindaki farki
    /// gosterebilmesinde: ikisi ayrisiyorsa sifreli DNS yarim kalmis demektir ve
    /// belirtisi yok -- arayuz yine "KORUMA AKTIF" der.
    /// </remarks>
    private static IEnumerable<NetworkInterface> AktifArayuzler()
    {
        NetworkInterface[] hepsi;
        try
        {
            hepsi = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return hepsi.Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && SystemDnsManager.IsRedirectTarget(n));
    }

    private static Task<IReadOnlyList<string>> CakismaAsync(CancellationToken _)
    {
        var conflicts = WinDivertCleanup.DetectConflictingTools();
        var lines = new List<string>();

        if (conflicts.Count == 0)
        {
            lines.Add("  yok");
        }
        else
        {
            lines.Add("  CALISIYOR: " + string.Join(", ", conflicts));
            lines.Add("  WinDivert surucusunu ayni anda iki arac kullanamaz. Bu acikken winws");
            lines.Add("  paketleri hic goremez ve butun stratejiler ayni sekilde basarisiz olur.");
        }

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    private static Task<IReadOnlyList<string>> VeriAsync(CancellationToken _)
    {
        var lines = new List<string>
        {
            "  Dizin      : " + WinDivertCleanup.ConfigDirectory,
            "  config.json: " + (File.Exists(ConfigStore.ConfigPath) ? "var" : "yok"),
        };

        var learned = ConfigStore.LoadLearned();
        lines.Add("  Ogrenilmis dogrulama: " + learned.Count + " kayit"
                  + (learned.Count == 0
                      ? " (bu makinede henuz parametre testi tamamlanmamis)"
                      : " · " + string.Join(", ", learned.Select(l => $"{l.IspId}/{l.Section}"))));

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }
}
