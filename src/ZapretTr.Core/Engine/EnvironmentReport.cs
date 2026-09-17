using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Makinenin O ANKİ durumunu satır satır yazar: "Raporu Kaydet"in günlükten
/// sonraki yarısı.
/// </summary>
/// <remarks>
/// Neden ayrı bir sınıf ve neden bu kadar çok alan: raporun tek işi, uzaktan
/// gelen "olmadı" cümlesini teşhis edilebilir bir şeye çevirmek. Bugüne kadar
/// rapor yalnızca görünüm modelinin BİLDİĞİ şeyleri taşıyordu: seçili profil,
/// seçili strateji, günlük. Oysa "olmadı" bildirimlerinin çoğunda görünüm
/// modelinin bildiği hiçbir şey yanlış değil; yanlış olan, görünüm modelinin
/// BAKMADIĞI şeyler:
///
///   * uygulama yönetici olarak açılmamış (winws hiçbir şey yapamaz),
///   * upstream ikilileri eksik ya da bozuk kurulmuş,
///   * winws/dnscrypt süreçleri hiç ayakta değil,
///   * otomatik başlatma servisi kurulu ama DURMUŞ,
///   * sistem DNS'i hâlâ bizde ama dinleyen kimse yok (ad çözümü tamamen ölü),
///   * GoodbyeDPI gibi başka bir araç WinDivert'i tutuyor.
///
/// Bunların hiçbiri günlükte görünmüyor; özellikle de kullanıcı bilgisayarı
/// yeniden başlatıp uygulamayı YENİ açtıysa: o durumda günlük neredeyse boş ve
/// eski rapor biçimi hiçbir şey anlatmıyordu. Bu bölüm kullanıcıya tek tek
/// soru sormadan o altı soruyu birden cevaplıyor.
///
/// Hiçbir adım dışarı istek yapmaz ve genel IP adresi yazılmaz.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class EnvironmentReport
{
    /// <summary>Ortam özetini toplar. Hiçbir koşulda fırlatmaz.</summary>
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
    /// Tek bir bölümü ekler ve o bölümün hatasını kendi içinde tutar.
    /// </summary>
    /// <remarks>
    /// Bölümlerden biri patlarsa rapor yine de yazılmalı. Rapor bir teşhis aracı;
    /// teşhis araçlarının en kötü özelliği, tam ihtiyaç duyulduğu anda hiçbir şey
    /// üretmemeleridir.
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

    /// <summary>Aynı addaki süreçten kaç tane var.</summary>
    /// <remarks>
    /// ZapretTR için sayının BİRDEN büyük olması tek başına bir teşhis: ikinci
    /// bir örnek winws'i aynı filtreyle açamaz ve kullanıcı "Başlat çalışmıyor"
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
            winws = status.WinwsRunning ? "kurulu ve calisiyor"
                : status.WinwsPaused ? "kurulu, KULLANICI DURAKLATTI (acilista baslamaz)"
                : "KURULU AMA DURMUS";
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

        // Bekçinin kendisi de sessizce eksik olabilir (görev silinmiş, kurulum
        // paketi değil saha paketi kullanılıyor); o zaman yeni kartlar ve ölü
        // çözümleyici yine kimsenin gözünde değil.
        lines.Add("  DNS bekcisi gorevi            : "
                  + (await DnsGuardTask.IsRegisteredAsync(cancellationToken).ConfigureAwait(false)
                      ? "kurulu"
                      : "KURULU DEGIL"));

        if (SystemDnsManager.IsSuspended)
        {
            lines.Add("  Servis yonlendirmesi ASKIDA: sifreli DNS servisi cevap vermedigi icin");
            lines.Add("  sistem DNS'i geri alinmis. Servis donunce bekci yeniden yonlendirir.");
        }

        if (DnsGuard.LastLogLine() is { } sonBekci)
        {
            lines.Add("  Bekcinin son kaydi            : " + sonBekci);
        }

        // EN TEHLİKELİ BİLEŞİM ve raporda tek satırda görünmesi gereken şey:
        // sistem DNS'i bize çevrilmiş ama dinleyen kimse yok. O makine hiçbir adı
        // çözemez ve kullanıcının bildirdiği şey "internetim gitti" olur; bu da
        // dışarıdan "program çalışmadı" ile aynı cümleyle anlatılır.
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
    /// Raporda gösterilecek arayüzler: yönlendirmenin dokunduğu küme.
    /// </summary>
    /// <remarks>
    /// Kasıtlı olarak <see cref="SystemDnsManager.IsRedirectTarget"/> ile aynı
    /// soruyu soruyor. Raporun değeri, "hangi kartların DNS'i değiştirilecekti"
    /// ile "hangi kartların DNS'i gerçekten değişmiş" arasındaki farkı
    /// gösterebilmesinde: ikisi ayrışıyorsa şifreli DNS yarım kalmış demektir ve
    /// belirtisi yok; arayüz yine "KORUMA AKTİF" der.
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

    /// <summary>
    /// Başka DPI araçlarından kalan izler ve DNS'i bozan durumlar.
    /// </summary>
    /// <remarks>
    /// Burası eskiden yalnızca ÇALIŞAN sürece bakıyordu ve en sık karşılaşılan
    /// hâli kaçırıyordu: kapalı ama kurulu kalıntı. Kullanıcıların çoğu bu araca
    /// başka bir araçtan geliyor (Türkiye'de en yaygını GoodbyeDPI); eski araç
    /// "kaldırıldı" sanılıyor ama servis kaydı kalıyor ve açılışta WinDivert'i
    /// kapıyor. Rapor bunu taşımazsa gelen bildirim yine "hiçbir strateji
    /// çalışmadı" cümlesinden ibaret kalıyor.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> CakismaAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        // Hedef listesi ProbeTargetStore'da ve o Prober projesinde; Core oraya
        // bağımlı değil. hosts kontrolü bu yüzden raporda boş geçmiyor, ama
        // adları veremediğimiz için yalnızca arayüz tarafında dolu koşuyor.
        var bulgular = await ConflictScanner.ScanAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (bulgular.Count == 0)
        {
            lines.Add("  yok");
            return lines;
        }

        foreach (var bulgu in bulgular)
        {
            lines.Add($"  [{bulgu.Kind}] {bulgu.Description}");

            if (!string.IsNullOrWhiteSpace(bulgu.Detail))
            {
                lines.Add("      " + bulgu.Detail);
            }
        }

        return lines;
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
