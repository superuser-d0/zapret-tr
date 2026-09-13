using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ZapretTr.Core.Engine;

/// <summary>DNS yonlendirmesinin sahibi.</summary>
public static class DnsBackupOwner
{
    /// <summary>Calisan uygulama yapti; uygulama kapanirken geri alir.</summary>
    public const string App = "app";

    /// <summary>Kurulu servis yapti; yalnizca servis kaldirilirken geri alinir.</summary>
    public const string Service = "service";
}

/// <summary>Bir ag arayuzunun DNS ayarinin degistirilmeden onceki hali.</summary>
public sealed class DnsBackupEntry
{
    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    [JsonPropertyName("guid")]
    public required string Guid { get; init; }

    /// <summary>
    /// Adresler elle mi girilmisti (static), yoksa DHCP'den mi geliyordu.
    /// </summary>
    /// <remarks>
    /// Bu ayrim geri yuklemenin dogru olmasi icin sart. DHCP'den gelen adresleri
    /// static olarak geri yazmak, kullanicinin agi degistiginde (baska bir wifi,
    /// mobil paylasim) eski ag gecidinin DNS'ine sabitlenmis kalmasina yol acar --
    /// yani biz "geri aldik" deriz ama makine bozuk kalir.
    /// </remarks>
    [JsonPropertyName("wasStatic")]
    public required bool WasStatic { get; init; }

    [JsonPropertyName("addresses")]
    public IReadOnlyList<string> Addresses { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Bu kayit IPv6 DNS bilgisini de tasiyor mu.
    /// </summary>
    /// <remarks>
    /// Ayri bir bayrak, cunku 0.1.18 ve oncesinde yazilmis yedeklerde IPv6 alanlari
    /// HIC YOK. Onlari "IPv6 DHCP'ydi" diye okumak, kullanicinin elle girdigi bir
    /// IPv6 DNS'ini geri alma sirasinda silmek olurdu -- dokunmadigimiz bir seyi
    /// bozmak. Bayrak yoksa IPv6 tarafina hic dokunulmuyor.
    /// </remarks>
    [JsonPropertyName("ipv6Captured")]
    public bool Ipv6Captured { get; init; }

    [JsonPropertyName("ipv6WasStatic")]
    public bool Ipv6WasStatic { get; init; }

    [JsonPropertyName("ipv6Addresses")]
    public IReadOnlyList<string> Ipv6Addresses { get; init; } = Array.Empty<string>();
}

/// <summary>Diske yazilan yedek.</summary>
public sealed class DnsBackup
{
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Bu yonlendirmeyi kim yapti: calisan uygulama mi, yoksa kurulu servis mi.
    /// </summary>
    /// <remarks>
    /// Ayrim sart. Servis kuruldugunda DNS yonlendirmesi acilistan acilisa
    /// SUREKLI olmali; uygulama kapanirken onu geri alirsa kullanici "otomatik
    /// baslatmayi kurdum" der ama makine yeniden baslatildiginda DNS eski haline
    /// donmus olur ve engellemeler geri gelir. Uygulama yalnizca KENDI yaptigi
    /// yonlendirmeyi geri alir.
    /// </remarks>
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = DnsBackupOwner.App;

    [JsonPropertyName("entries")]
    public IReadOnlyList<DnsBackupEntry> Entries { get; init; } = Array.Empty<DnsBackupEntry>();
}

/// <summary>
/// Sistemin DNS sunucusunu degistirir ve her kosulda geri alabilir.
/// </summary>
/// <remarks>
/// Bu sinif uygulamanin en riskli parcasi. DNS'i 127.0.0.1'e cevirip dnscrypt-proxy
/// calismazsa kullanici HICBIR adi cozemez -- yani ona gore internet tamamen gitmis
/// olur. Uc savunma var:
///
///   1. Degisiklikten ONCE yedek diske yazilir. Uygulama cokse, oldurulse, makine
///      yeniden baslasa bile geri alinabilir; yedek bellekte tutulsaydi o senaryoda
///      kaybolurdu.
///   2. DNS ancak dnscrypt-proxy'nin gercekten cevap verdigi dogrulandiktan sonra
///      degistirilir (<see cref="DnsCryptRunner"/> bunu yapar).
///   3. Acilista <see cref="TryRecoverAsync"/> cagrilir: diskte yedek varken DNS
///      hala bizim adresimizi gosteriyorsa ve proxy calismiyorsa kendiliginden
///      eski haline doner.
///
/// Yalnizca varsayilan ag gecidi olan arayuzlere dokunulur. Butun adaptorlere
/// dokunmak (sanal makine, VPN, bluetooth adaptorleri dahil) gereksiz genis bir
/// degisiklik olurdu.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SystemDnsManager
{
    /// <summary>dnscrypt-proxy'nin dinledigi adres.</summary>
    public const string LocalResolver = "127.0.0.1";

    private static string BackupPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-backup.json");

    /// <summary>
    /// Servisin yonlendirmesi, cozumleyici uzun sure cevap vermedigi icin
    /// <see cref="DnsGuard"/> tarafindan ASKIYA alindi mi.
    /// </summary>
    private static string SuspendedMarkerPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-askida.flag");

    /// <summary>Diskte bir yedek duruyor mu (yani DNS bizim tarafimizdan degistirilmis mi).</summary>
    public static bool HasBackup => File.Exists(BackupPath);

    /// <summary>
    /// Servis yonlendirmesi askida mi: yedek geri yuklendi ama servis hala kurulu
    /// ve cozumleyici geri geldiginde yonlendirme yeniden yapilmali.
    /// </summary>
    public static bool IsSuspended => File.Exists(SuspendedMarkerPath);

    internal static void MarkSuspended()
    {
        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(SuspendedMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Askida isaretini kaldirir. Yoksa bir sey yapmaz.</summary>
    public static void ClearSuspended()
    {
        try
        {
            File.Delete(SuspendedMarkerPath);
        }
        catch (Exception)
        {
            // Isaret silinemezse bekci bir sonraki turda servisin yoklugunu
            // gorup kendisi temizler.
        }
    }

    /// <summary>
    /// DNS ayarini degistiren islemleri SURECLER ARASINDA siraya sokan kilit.
    /// </summary>
    /// <remarks>
    /// Sistem DNS'ine artik ayni anda uc ayri surec dokunabiliyor: arayuz,
    /// kaldirici/kurulum (<c>--uninstall-services</c>) ve SYSTEM olarak calisan
    /// bekci (<c>--dns-guard</c>). Iki geri alma ayni anda kosarsa biri yedegi
    /// silerken oteki okuyor olabilir; bir yonlendirme ile bir geri alma ic ice
    /// girerse kartlarin yarisi 127.0.0.1'de, yarisi DHCP'de kalir ve yedek
    /// hicbirini dogru anlatmaz.
    ///
    /// Mutex degil semafor: Mutex is parcacigina bagli ve async metotlar
    /// <c>await</c> sonrasi baska is parcacigindan devam ediyor -- Mutex'i orada
    /// birakmak istisna firlatir. Kilit alinamazsa (sure doldu, yetki yok) islem
    /// YINE DE yapiliyor: DNS'i geri almayi bir kilit yuzunden atlamak, kullaniciyi
    /// ad cozemez halde birakabilir.
    /// </remarks>
    internal static IDisposable AcquireLock(TimeSpan? timeout = null)
        => DnsLock.Acquire(timeout ?? TimeSpan.FromSeconds(60));

    private sealed class DnsLock : IDisposable
    {
        private Semaphore? _semaphore;

        public static DnsLock Acquire(TimeSpan timeout)
        {
            var dnsLock = new DnsLock();

            try
            {
                var semaphore = new Semaphore(1, 1, @"Global\ZapretTR-dns");
                if (semaphore.WaitOne(timeout))
                {
                    dnsLock._semaphore = semaphore;
                }
                else
                {
                    semaphore.Dispose();
                }
            }
            catch (Exception)
            {
                // Kilitsiz devam: gerekcesi AcquireLock'ta.
            }

            return dnsLock;
        }

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            if (semaphore is null)
            {
                return;
            }

            try
            {
                semaphore.Release();
            }
            catch (Exception)
            {
                // Birakilamadiysa tutacak kimse kalmayinca cekirdek nesneyi siliyor.
            }

            semaphore.Dispose();
        }
    }

    /// <summary>
    /// Mevcut yonlendirmenin sahibi. Yedek yoksa null.
    /// </summary>
    public static string? BackupOwner
    {
        get
        {
            try
            {
                if (!File.Exists(BackupPath))
                {
                    return null;
                }

                return JsonSerializer.Deserialize(File.ReadAllText(BackupPath), CoreJsonContext.Default.DnsBackup)?.Owner;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Yonlendirmeyi kurulu servis yaptiysa true; uygulama buna dokunmamali.</summary>
    public static bool IsOwnedByService
        => string.Equals(BackupOwner, DnsBackupOwner.Service, StringComparison.Ordinal);

    /// <summary>
    /// Aktif arayuzlerin DNS ayarini yedekleyip <see cref="LocalResolver"/>'a cevirir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Uygun bir arayuz bulunamazsa.</exception>
    /// <param name="owner">
    /// Yonlendirmeyi kim yapiyor. <see cref="DnsBackupOwner.Service"/> verildiginde
    /// uygulama kapanirken geri ALMAZ.
    /// </param>
    public static async Task<IReadOnlyList<string>> RedirectToLocalAsync(
        string owner = DnsBackupOwner.App,
        CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        using (AcquireLock())
        {
            return await RedirectCoreAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="RedirectToLocalAsync"/>'in kilitsiz govdesi. Kilidi zaten tutan
    /// <see cref="DnsGuard"/> bunu cagiriyor.
    /// </summary>
    /// <remarks>
    /// Tekrar tekrar cagrilabilir ve her cagri YALNIZCA eksigi tamamlar: yedekte
    /// olmayan karti yedege ekler, DNS'i henuz 127.0.0.1 olmayan karti cevirir.
    /// Bekci bunu her ag degisikliginde calistiriyor; zaten cevrilmis kartlara
    /// yeniden netsh kosmak ve onbellegi bosaltmak her seferinde gereksiz bir
    /// kesinti olurdu.
    /// </remarks>
    internal static async Task<IReadOnlyList<string>> RedirectCoreAsync(
        string owner, CancellationToken cancellationToken)
    {
        var targets = GetRedirectTargets();

        // Su an internete cikan bir arayuz yoksa yapilacak anlamli bir sey yok:
        // yalnizca bagli olmayan kartlara yazip "DNS cevrildi" demek yanlis olur.
        if (!targets.Any(t => t.Required))
        {
            throw new InvalidOperationException(
                "Varsayilan ag gecidi olan bir arayuz bulunamadi; DNS degistirilmedi.");
        }

        // Mevcut bir yedegin uzerine YAZMIYORUZ: ikinci kez cagrilirsa bizim
        // koydugumuz 127.0.0.1 degerini "orijinal" diye kaydeder ve geri donus
        // yolu tamamen kaybolurdu.
        //
        // Ama yedegin VARLIGI da yetmiyor: yedekte olmayan bir arayuze yazacaksak
        // onu yedege EKLEMEK zorundayiz. Aksi halde geri alma o arayuzu atlar,
        // DNS'i 127.0.0.1'de kalir ve kaldirmadan sonra o baglantida hicbir ad
        // cozulmez. Kablo takiliyken kurup sonra WiFi'ye gecen kullanicida tam
        // olarak bu oluyordu: WiFi yeni hedefe girdi ama eski yedekte yoktu.
        var existing = await ReadBackupAsync(cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            await WriteBackupAsync(
                new DnsBackup
                {
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Owner = owner,
                    Entries = targets.Select(t => Capture(t.Nic)).ToList(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var bilinen = existing.Entries
                .Select(e => e.Guid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var eksik = targets
                .Where(t => !bilinen.Contains(t.Nic.Id))
                .Select(t => Capture(t.Nic))
                .ToList();

            // SERVIS, UYGULAMANIN YONLENDIRMESINI DEVRALIR.
            //
            // Uygulama korumayi calistirirken "Servis Olarak Yukle"ye basilirsa
            // yedek "app" sahipligiyle duruyordu ve oyle KALIYORDU: sahip
            // korunuyordu. Uygulama kapaninca yonlendirmeyi kendisininki sanip
            // geri aliyor, servis kurulu oldugu halde sifreli DNS sessizce devreden
            // cikiyordu. Tarih korunuyor; ters yon (servisinkini uygulamanin
            // devralmasi) yok, cunku uygulamanin kapanmasi kalici bir kurulumu
            // bozmamali.
            var sahip = DecideOwner(existing.Owner, owner);

            if (eksik.Count > 0 || !string.Equals(sahip, existing.Owner, StringComparison.Ordinal))
            {
                await WriteBackupAsync(
                    new DnsBackup
                    {
                        CreatedAt = existing.CreatedAt,
                        Owner = sahip,
                        Entries = [.. existing.Entries, .. eksik],
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var changed = new List<string>();
        foreach (var (nic, required) in targets)
        {
            // Zaten bizde: dokunma. Gerekcesi RedirectCoreAsync'in aciklamasinda.
            if (IsOnlyLocalResolver(ReadStaticNameServer(Ipv4ParametersKey, nic.Id)))
            {
                continue;
            }

            var (exitCode, output) = await RunNetshAsync(
                ["interface", "ipv4", "set", "dnsservers", $"name={nic.Name}", "source=static",
                 $"address={LocalResolver}", "validate=no"],
                cancellationToken).ConfigureAwait(false);

            if (exitCode == 0)
            {
                changed.Add(nic.Name);

                // IPv6 DNS'i AYRICA bosaltmak zorundayiz. Yalnizca IPv4'u
                // 127.0.0.1'e cevirmek yetmiyor: arayuzde ISS'in verdigi bir IPv6
                // cozumleyicisi duruyorsa (yonlendirici duyurusu ya da DHCPv6 ile
                // gelir) Windows sorguyu pekala oraya yollar ve DNS kacirma
                // katmani ayakta kalir. Disaridan gorunen sey tam olarak "sifreli
                // DNS acik ama site yine acilmiyor" olur -- yani belirtisi
                // stratejinin tutmamasiyla ayni.
                //
                // Yonlendirmek yerine BOSALTIYORUZ: dnscrypt-proxy yalnizca
                // 127.0.0.1'i dinliyor ve onu [::1] de dinlemeye zorlamak,
                // IPv6'nin kapali oldugu makinelerde baglanamayip surecin hic
                // acilmamasina yol acardi. Bos birakinca Windows IPv4'e, yani
                // bize duser.
                //
                // En iyi cabayla: bu adimin basarisizligi IPv4 yonlendirmesini
                // gecersiz kilmaz, yalnizca IPv6 sizintisi ihtimali kalir.
                await RunNetshAsync(
                    ["interface", "ipv6", "delete", "dnsservers", $"name={nic.Name}", "all"],
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            // Bagli olmayan bir kartta netsh basarisiz olabiliyor. Bunun yuzunden
            // tum islemi dusurmek, KULLANILAN arayuzu zaten cevirmisken kullaniciyi
            // hata ekranina goturmek olurdu -- kazanilani kaybettirmeden gec.
            if (!required)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"'{nic.Name}' arayuzunun DNS ayari degistirilemedi: {output.Trim()}");
        }

        if (changed.Count > 0)
        {
            // Bir ONLEM, olculmus bir sebep degil. Bu satir "Windows zehirli kaydi
            // DNS cevrildikten sonra da tutuyor" varsayimiyla eklenmisti; 2026-09-13'te
            // Windows 11 (26200) uzerinde olculdu ve TEKRARLANMADI: sunucu degisince
            // onbellek kendiliginden gecersiz oluyor. Baska surumlerde olculmedigi ve
            // zararsiz oldugu icin duruyor.
            await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    /// <summary>
    /// Mevcut yedek varken yeni bir yonlendirme istendiginde yedegin sahibi.
    /// </summary>
    /// <remarks>
    /// Servis her zaman kazanir: servis kurulu oldugu surece yonlendirme acilistan
    /// acilisa surmeli ve uygulamanin kapanmasi onu geri almamali.
    /// </remarks>
    public static string DecideOwner(string existingOwner, string requestedOwner)
        => string.Equals(existingOwner, DnsBackupOwner.Service, StringComparison.Ordinal)
           || string.Equals(requestedOwner, DnsBackupOwner.Service, StringComparison.Ordinal)
            ? DnsBackupOwner.Service
            : existingOwner;

    /// <summary>
    /// Windows'un DNS onbellegini bosaltir. En iyi cabayla: basarisizligi
    /// yonlendirmeyi gecersiz kilmaz.
    /// </summary>
    private static async Task FlushDnsCacheAsync(CancellationToken cancellationToken)
        => await RunProcessAsync("ipconfig.exe", ["/flushdns"], cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Yedekteki ayarlari geri yukler ve yedegi siler.
    /// </summary>
    /// <returns>Geri yuklenen arayuz adlari. Yedek yoksa bos liste.</returns>
    /// <exception cref="InvalidOperationException">
    /// Makinede DURAN bir arayuzun ayari geri alinamazsa. O durumda yedek
    /// SILINMEZ: silinirse geri donus yolu tamamen kaybolur.
    /// </exception>
    /// <remarks>
    /// Bu metot iki hatayi birden kapatiyor ve ikisi de sessizdi:
    ///
    /// 1. <b>netsh cikis kodlari hic okunmuyordu.</b> Geri alma basarisiz olsa bile
    ///    metot "geri aldim" deyip yedegi SILIYORDU. Sonuc, DEVAM'in "projedeki en
    ///    kotu sonuc" dedigi tablonun ta kendisi: sistem DNS'i 127.0.0.1'de kalir,
    ///    dnscrypt calismaz, makine hicbir adi cozemez ve elde geri donulecek kayit
    ///    da kalmaz. Artik yalnizca HEPSI basarili olursa yedek siliniyor.
    ///
    /// 2. <b>Arayuz ADIYLA araniyordu.</b> Kullanici baglantiyi yeniden adlandirirsa
    ///    ("Ethernet" -> "Ev") netsh o adi bulamaz; birinci hatayla birlesince geri
    ///    alma sessizce hicbir sey yapmaz. Artik once yedekteki GUID ile su anki ad
    ///    bulunuyor, ad yalnizca yedek cozum.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!HasBackup)
        {
            return [];
        }

        using (AcquireLock())
        {
            return await RestoreCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary><see cref="RestoreAsync"/>'in kilitsiz govdesi.</summary>
    internal static async Task<IReadOnlyList<string>> RestoreCoreAsync(CancellationToken cancellationToken)
    {
        // Kilit beklenirken baska bir surec geri almis olabilir.
        if (!HasBackup)
        {
            return [];
        }

        var backup = await ReadBackupAsync(cancellationToken).ConfigureAwait(false);

        if (backup is null)
        {
            // BOZUK YEDEK: orijinal ayar okunamiyor, ama yonlendirme bu yuzden
            // KALICI olmamali. Eskiden burada hata firlatiliyordu ve yedek diskte
            // kaliyordu; her geri alma denemesi ayni hatayla dusuyor, makine
            // 127.0.0.1'de ve cozumleyicisiz kalabiliyordu. Bilinebilecek tek
            // dogru sey "127.0.0.1 bizimdi": o kartlar otomatige donuyor. Elle
            // girilmis eski bir DNS varsa kaybolur -- ama o bilgi zaten okunamiyor.
            return await ResetOrphanedRedirectsCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        var nics = SafeGetInterfaces();
        var restored = new List<string>();
        var failed = new List<string>();

        foreach (var entry in backup.Entries)
        {
            var alias = ResolveAlias(nics, entry);
            if (alias is null)
            {
                // Kart artik makinede yok (sokulmus USB WiFi, kaldirilmis sanal
                // adaptor). Geri alinacak bir sey yok ve bu bir basarisizlik
                // DEGIL: yoksa yedek sonsuza kadar diskte kalir ve her acilista
                // ayni hata mesaji cikardi.
                continue;
            }

            // KARTIN DNS'I ARTIK BIZDE DEGILSE DOKUNMA.
            //
            // Yonlendirme ile geri alma arasinda aylar gecebiliyor (servis modu).
            // Kullanici o arada bir kartin DNS'ini elle degistirmis ya da Windows'un
            // "ag sifirlama"si onu otomatige dondurmus olabilir. Eskiden yedek KORU
            // KORUNE yaziliyordu ve kullanicinin sonradan yaptigi ayar sessizce
            // eziliyordu. Bizim birakacagimiz tek iz 127.0.0.1; o yoksa geri
            // alinacak bir sey de yok ve bu bir basarisizlik degil.
            if (!ShouldRestore(ReadStaticNameServer(Ipv4ParametersKey, entry.Guid)))
            {
                continue;
            }

            if (await RestoreEntryAsync(entry, alias, cancellationToken).ConfigureAwait(false))
            {
                restored.Add(alias);
            }
            else
            {
                failed.Add(alias);
            }
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                "Su baglantilarin DNS ayari geri alinamadi: " + string.Join(", ", failed) + ". " +
                $"Yedek SILINMEDI ({BackupPath}); bir sonraki acilista yeniden denenecek. " +
                "Hemen duzeltmek icin: Ag Baglantilari > ilgili baglanti > Ozellikler > " +
                "\"Internet Protokolu Surum 4 (TCP/IPv4)\" > \"DNS sunucu adresini otomatik al\".");
        }

        // Yedek ancak geri yukleme bittikten SONRA siliniyor. Once silinseydi ve
        // geri yukleme yarida kalsaydi, kullanicinin donebilecegi bir kayit kalmazdi.
        File.Delete(BackupPath);

        // Geri alirken de bosaltiliyor: onbellekte bizim cozumleyicimizden gelen
        // kayitlar duruyor ve kullanici artik baska bir cozumleyiciye dondu.
        await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);

        return restored;
    }

    /// <summary>Tek bir arayuzun ayarini geri yukler. IPv4 gercekten geri geldiyse true.</summary>
    private static async Task<bool> RestoreEntryAsync(
        DnsBackupEntry entry, string alias, CancellationToken cancellationToken)
    {
        // Yedekte 127.0.0.1 "orijinal" diye durabilir: yedek bir sekilde kaybolmus
        // ve yonlendirme ustune ikinci kez yapilmis olabilir (0.1.19 ve oncesinde
        // Capture bunu elemiyordu). Onu geri yazmak, yonlendirmeyi KALICI yapmak
        // demek -- kaldirmadan sonra dnscrypt yok ve makine hicbir adi cozemez.
        var (wasStatic, addresses) = SanitizeCaptured(entry.WasStatic, entry.Addresses);

        var ipv4Ok = wasStatic
            ? await SetStaticAsync("ipv4", alias, addresses, cancellationToken).ConfigureAwait(false)
            : await SetDhcpAsync("ipv4", alias, cancellationToken).ConfigureAwait(false);

        // IPv6 yalnizca BIZIM bosalttigimiz kayitlarda geri yukleniyor.
        //
        // Sonucu bilerek dikkate almiyoruz: ad cozumu IPv4 uzerinden geri geldiyse
        // makine calisiyor demektir, ve IPv6 tarafindaki bir aksilik yuzunden yedegi
        // diskte tutmak kullaniciyi her acilista tekrarlanan bir hata mesajina
        // mahkum ederdi. Kaybedilen sey en kotu ihtimalle o arayuzun IPv6 DNS'i;
        // Windows IPv4'e duser.
        if (entry.Ipv6Captured)
        {
            if (entry.Ipv6WasStatic && entry.Ipv6Addresses.Count > 0)
            {
                await SetStaticAsync("ipv6", alias, entry.Ipv6Addresses, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetDhcpAsync("ipv6", alias, cancellationToken).ConfigureAwait(false);
            }
        }

        return ipv4Ok;
    }

    private static async Task<bool> SetStaticAsync(
        string family, string alias, IReadOnlyList<string> addresses, CancellationToken cancellationToken)
    {
        var (exitCode, _) = await RunNetshAsync(
            ["interface", family, "set", "dnsservers", $"name={alias}", "source=static",
             $"address={addresses[0]}", "validate=no"],
            cancellationToken).ConfigureAwait(false);

        for (var i = 1; i < addresses.Count; i++)
        {
            await RunNetshAsync(
                ["interface", family, "add", "dnsservers", $"name={alias}",
                 $"address={addresses[i]}", $"index={i + 1}", "validate=no"],
                cancellationToken).ConfigureAwait(false);
        }

        return exitCode == 0;
    }

    /// <summary>
    /// Otomatik (DHCP) ayara dondurur. Adresleri static yazmak yanlis olurdu: baska
    /// bir aga baglandiginda eski ag gecidinin DNS'ine sabitlenmis kalirdi.
    /// </summary>
    private static async Task<bool> SetDhcpAsync(
        string family, string alias, CancellationToken cancellationToken)
    {
        var (exitCode, _) = await RunNetshAsync(
            ["interface", family, "set", "dnsservers", $"name={alias}", "source=dhcp"],
            cancellationToken).ConfigureAwait(false);

        return exitCode == 0;
    }

    /// <summary>
    /// Yedekteki kaydin BUGUNKU arayuz adi. Kart artik yoksa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// GUID once geliyor: kullanici baglantiyi yeniden adlandirmis olabilir ve
    /// netsh yalnizca guncel adi taniyor. Arayuz listesi hic okunamadiysa (nadir,
    /// ama olur) yedekteki ada guveniyoruz -- hicbir sey denememekten iyidir.
    /// </remarks>
    private static string? ResolveAlias(IReadOnlyList<NetworkInterface> nics, DnsBackupEntry entry)
    {
        if (nics.Count == 0)
        {
            return entry.Alias;
        }

        var byGuid = nics.FirstOrDefault(n =>
            string.Equals(n.Id, entry.Guid, StringComparison.OrdinalIgnoreCase));

        if (byGuid is not null)
        {
            return byGuid.Name;
        }

        return nics.FirstOrDefault(n =>
            string.Equals(n.Name, entry.Alias, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    private static IReadOnlyList<NetworkInterface> SafeGetInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Acilista cagrilir: onceki bir cokme sonrasi DNS bizde kalmissa geri alir.
    /// </summary>
    /// <param name="localResolverResponds">
    /// dnscrypt-proxy su an cevap veriyor mu. Veriyorsa kurtarma yapilmaz --
    /// muhtemelen baska bir ZapretTR ornegi calisiyordur.
    /// </param>
    public static async Task<IReadOnlyList<string>> TryRecoverAsync(
        bool localResolverResponds, CancellationToken cancellationToken = default)
    {
        if (!HasBackup || localResolverResponds)
        {
            return [];
        }

        // Yedek var, proxy cevap vermiyor: makine su an ad cozemiyor demektir.
        // Bu, uygulamanin duzgun kapanmadigi anlamina gelir ve duzeltilmesi
        // kullanicidan onay beklemeyi kaldirmaz -- internetin yok.
        return await RestoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Diskteki yedegi okur; yoksa ya da bozuksa <c>null</c> doner.</summary>
    /// <remarks>
    /// Bozuk yedek eskiden hata olarak yukseliyordu, cunku <c>null</c> donmek
    /// cagiranin uzerine yeni bir yedek yazmasina yol aciyor ve o an DNS bizdeyse
    /// 127.0.0.1 "orijinal" olarak kaydediliyordu. Bunun bedeli agirdi: bozuk bir
    /// dosya sifreli DNS'i KALICI olarak acilamaz yapiyordu -- her yonlendirme ayni
    /// okuma hatasiyla dusuyordu ve kullanicinin dosyanin yerini bilmesi gerekiyordu.
    ///
    /// Artik <see cref="Capture"/> 127.0.0.1'i asla orijinal saymadigi icin o
    /// korunmaya gerek kalmadi. Bozuk dosya silinmiyor, yanina tasiniyor: teshis
    /// icin icerigi lazim olabilir.
    /// </remarks>
    private static async Task<DnsBackup?> ReadBackupAsync(CancellationToken cancellationToken)
    {
        if (!HasBackup)
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(BackupPath, cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize(json, CoreJsonContext.Default.DnsBackup) is { } backup)
            {
                return backup;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            // Asagida karantinaya aliniyor.
        }

        QuarantineBackup();
        return null;
    }

    private static void QuarantineBackup()
    {
        try
        {
            File.Move(BackupPath,
                Path.Combine(WinDivertCleanup.ConfigDirectory, $"dns-backup.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                File.Delete(BackupPath);
            }
            catch (Exception)
            {
                // Silinemiyorsa bir sonraki deneme ayni yoldan gecer.
            }
        }
    }

    private static async Task WriteBackupAsync(DnsBackup backup, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        await File.WriteAllTextAsync(
            BackupPath,
            JsonSerializer.Serialize(backup, CoreJsonContext.Default.DnsBackup),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>DNS yonlendirmesi uygulanacak bir arayuz ve zorunlulugu.</summary>
    /// <param name="Required">
    /// Su an internete cikan arayuz mu. Zorunlu bir arayuzde yazma basarisiz
    /// olursa islem hata verir; istege bagli olanda yalnizca atlanir.
    /// </param>
    private readonly record struct RedirectTarget(NetworkInterface Nic, bool Required);

    /// <summary>
    /// DNS'i cevrilecek arayuzler: su an internete cikanlar VE su an bagli
    /// olmayan fiziksel arayuzler.
    /// </summary>
    /// <remarks>
    /// Eskiden yalnizca "calisan + varsayilan ag gecidi olan" arayuzler
    /// aliniyordu. Kablo takiliyken kurulum yapan bir kullanicida WiFi
    /// <c>Disconnected</c> oldugu icin atlaniyordu; sonra kabloyu cikarip WiFi'ye
    /// gecince o arayuzun DNS'i ISS'in sunucusunda kaliyor ve DNS engellemesi
    /// geri geliyordu. Belirtisi de yok: winws calismaya devam ettigi icin arayuz
    /// "KORUMA AKTIF" gosteriyor, sifreli DNS ise devrede degil.
    ///
    /// Bu yuzden bagli olmayan Ethernet/WiFi arayuzlerine de simdiden yaziliyor:
    /// ayar kalici, arayuz bagliginca gecerli oluyor. Yedek de onlari kapsiyor,
    /// dolayisiyla geri alma simetrik kaliyor.
    ///
    /// Kurulumdan SONRA takilan yeni bir adaptor (USB WiFi, telefonla USB paylasim)
    /// bu listeye ancak bir sonraki cagrida girer. Servis modunda o cagriyi
    /// <see cref="DnsGuard"/> her ag baglantisinda yapiyor; eskiden hic yapilmiyordu
    /// ve o kartta DNS engellemesi sessizce geri geliyordu.
    /// </remarks>
    private static List<RedirectTarget> GetRedirectTargets()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Select(n => new RedirectTarget(n, IsOnlineWithGateway(n)))
            .Where(t => t.Required || IsOfflinePhysical(t.Nic))
            .ToList();

    private static bool IsOnlineWithGateway(NetworkInterface nic)
        => nic.OperationalStatus == OperationalStatus.Up && HasIpv4Gateway(nic);

    /// <summary>Ag gecidi olmayan sanal adaptorler (VM, tunel) burada elenir.</summary>
    private static bool HasIpv4Gateway(NetworkInterface nic)
        => nic.GetIPProperties().GatewayAddresses
            .Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                      && !g.Address.Equals(System.Net.IPAddress.Any));

    /// <summary>
    /// Su an bagli olmayan ama kullaniciyi internete cikarabilecek bir arayuz mu.
    /// </summary>
    /// <remarks>
    /// Tur suzgeci kasitli olarak dar: sanal adaptorlerin cogu <c>Up</c> ve ag
    /// gecidi olmadigi icin zaten eleniyor, buraya ancak gercekten bagli olmayan
    /// bir kart giriyor. Bluetooth kisisel ag (PAN) da dahil -- telefondan
    /// baglanti paylasan kullanicinin o baglantida da korunmasi gerekiyor.
    /// </remarks>
    public static bool IsOfflinePhysical(
        OperationalStatus status, NetworkInterfaceType type)
        => status != OperationalStatus.Up
           && type is NetworkInterfaceType.Ethernet
                  or NetworkInterfaceType.GigabitEthernet
                  or NetworkInterfaceType.FastEthernetT
                  or NetworkInterfaceType.FastEthernetFx
                  or NetworkInterfaceType.Wireless80211;

    private static bool IsOfflinePhysical(NetworkInterface nic)
        => IsOfflinePhysical(nic.OperationalStatus, nic.NetworkInterfaceType)
           && !IsVirtualAdapterDescription(nic.Description);

    /// <summary>
    /// Adaptorun surucu aciklamasi onun sanal bir kart oldugunu mu soyluyor.
    /// </summary>
    /// <remarks>
    /// Tur suzgeci (<see cref="IsOfflinePhysical(OperationalStatus, NetworkInterfaceType)"/>)
    /// sanal kartlari AYIRAMIYOR: Windows'un Wi-Fi Direct icin actigi
    /// "Local Area Connection* 1/2" kartlari kendini <c>Wireless80211</c> olarak
    /// bildiriyor ve bagli olmadiklari icin "bagli olmayan fiziksel kart" sayiliyordu.
    /// Gercek bir kullanicinin raporunda ikisinin de DNS'i 127.0.0.1'e cevrilmisti.
    /// O kartlar kullaniciyi internete cikarmiyor; DNS'lerini degistirmek yalnizca
    /// geri alinacak bir degisiklik daha biriktiriyor.
    ///
    /// Aciklama surucu adidir ve Turkce Windows'ta da Ingilizce gelir
    /// ("Microsoft Wi-Fi Direct Virtual Adapter #2"). Ayni kelime Hyper-V, VMware ve
    /// VirtualBox kartlarini da yakaliyor. Bluetooth kisisel ag kasitli olarak
    /// DISARIDA kaliyor ("Bluetooth Device (Personal Area Network)"): telefondan
    /// baglanti paylasan kullanici o kartla internete cikiyor.
    /// </remarks>
    public static bool IsVirtualAdapterDescription(string? description)
        => description is not null
           && description.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu arayuzun DNS'i cevrilecek mi.</summary>
    /// <remarks>
    /// Disari aciliyor cunku bu kararin GERCEK makinede ne verdigi, tek basina
    /// birim testiyle ogrenilemiyor: arayuz listesi isletim sisteminden geliyor.
    /// Hicbir sey degistirmeden "hangi kartlar secilirdi" diye sorabilmek,
    /// sessizce yanlis secim yapan bir hatanin tekrarini onluyor.
    /// </remarks>
    public static bool IsRedirectTarget(NetworkInterface nic)
        => IsOnlineWithGateway(nic) || IsOfflinePhysical(nic);

    private static DnsBackupEntry Capture(NetworkInterface nic)
    {
        var dns = nic.GetIPProperties().DnsAddresses;

        var ipv6Addresses = dns
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            .Select(a => a.ToString())
            .ToList();

        // BIZIM 127.0.0.1'IMIZ ASLA "ORIJINAL" DIYE KAYDEDILMEZ.
        //
        // Yedek yokken kartin DNS'i zaten 127.0.0.1 olabilir: yedek dosyasi elle ya
        // da bir temizlik aracinca silinmis, onceki bir surum kaldirilirken yarim
        // kalmis. Eskiden bu deger "elle girilmis DNS" diye yedege giriyordu ve
        // geri alma onu SADAKATLE geri yaziyordu -- uygulama kaldirildiktan sonra
        // o kart hicbir adi cozemiyordu, projedeki en kotu tablo.
        var (wasStatic, addresses) = SanitizeCaptured(
            IsStaticallyConfigured(Ipv4ParametersKey, nic.Id),
            dns.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList());

        return new DnsBackupEntry
        {
            Alias = nic.Name,
            Guid = nic.Id,
            WasStatic = wasStatic,
            Addresses = addresses,

            // IPv6 tarafi da yedekleniyor cunku yonlendirme onu BOSALTIYOR
            // (gerekcesi RedirectToLocalAsync icinde). Yedeklemeden bosaltmak,
            // DEVAM'in "yonlendirmenin kapsamini genisletmek yedegin kapsamini
            // genisletmez" kuralini ikinci kez cignemek olurdu.
            Ipv6Captured = true,
            Ipv6WasStatic = IsStaticallyConfigured(Ipv6ParametersKey, nic.Id),
            Ipv6Addresses = ipv6Addresses,
        };
    }

    private const string Ipv4ParametersKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string Ipv6ParametersKey = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces";

    /// <summary>
    /// Arayuzun DNS'i elle mi ayarlanmis. Kayit defterindeki NameServer degeri
    /// doluysa static, bossa DHCP'den geliyor.
    /// </summary>
    /// <remarks>
    /// .NET API'si bu ayrimi vermiyor; <c>DnsAddresses</c> her iki durumda da ayni
    /// gorunuyor. Kayit defteri bunu ayirt edebilecegimiz tek yer.
    /// </remarks>
    private static bool IsStaticallyConfigured(string parametersKey, string interfaceGuid)
        // Okuyamiyorsak (null) DHCP varsayiyoruz: yanlis tarafa dusmek istersek,
        // DHCP'ye dondurmek static yazmaktan daha guvenli.
        => !string.IsNullOrWhiteSpace(ReadStaticNameServer(parametersKey, interfaceGuid));

    /// <summary>
    /// Kartin elle yazilmis DNS degeri. DHCP'deyse bos dize, okunamazsa <c>null</c>.
    /// </summary>
    private static string? ReadStaticNameServer(string parametersKey, string interfaceGuid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{parametersKey}\{interfaceGuid}");
            if (key is null)
            {
                return null;
            }

            return key.GetValue("NameServer") as string ?? string.Empty;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Kayit defterindeki NameServer degeri ("a,b" ya da "a b") adreslere ayrilir.</summary>
    private static IReadOnlyList<string> SplitNameServers(string? nameServer)
        => string.IsNullOrWhiteSpace(nameServer)
            ? []
            : nameServer.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Kartin elle yazilmis DNS'i yalnizca bizim cozumleyicimiz mi.</summary>
    public static bool IsOnlyLocalResolver(string? staticNameServer)
    {
        var adresler = SplitNameServers(staticNameServer);
        return adresler.Count > 0 && adresler.All(a => a == LocalResolver);
    }

    /// <summary>
    /// Yedekteki bir kart geri alinmali mi, kartin SU ANKI elle yazilmis DNS'ine gore.
    /// </summary>
    /// <param name="currentStaticNameServer">
    /// Kayit defterindeki deger; DHCP'de bos dize, okunamadiysa <c>null</c>.
    /// </param>
    /// <remarks>
    /// Okunamiyorsa geri aliniyor: bizim yonlendirmemiz duruyor olabilir ve onu
    /// yerinde birakmak kullaniciyi ad cozemez halde birakabilir. Gereksiz bir
    /// geri alma ise en kotu ihtimalle kartin DNS'ini yedekteki haline dondurur.
    /// </remarks>
    public static bool ShouldRestore(string? currentStaticNameServer)
        => currentStaticNameServer is null
           || SplitNameServers(currentStaticNameServer).Contains(LocalResolver);

    /// <summary>
    /// Yedege girecek (ya da yedekten geri yazilacak) IPv4 DNS bilgisinden bizim
    /// cozumleyicimizi ayiklar.
    /// </summary>
    /// <returns>
    /// Elle yazilmis adreslerden geriye bir sey kalmiyorsa kart DHCP sayilir.
    /// </returns>
    public static (bool WasStatic, IReadOnlyList<string> Addresses) SanitizeCaptured(
        bool wasStatic, IReadOnlyList<string> addresses)
    {
        var gercek = addresses.Where(a => a != LocalResolver).ToList();
        return (wasStatic && gercek.Count > 0, gercek);
    }

    /// <summary>
    /// YEDEGI OLMAYAN 127.0.0.1 yonlendirmelerini otomatige dondurur.
    /// </summary>
    /// <returns>Duzeltilen kartlarin adlari.</returns>
    /// <remarks>
    /// Yalnizca "Tum Ayarlari Sifirla" ve kaldirma yolunda, bizim cozumleyicimiz
    /// silindikten SONRA ve 127.0.0.1:53 cevap VERMIYORKEN cagrilmali. O anda
    /// DNS'i yalnizca 127.0.0.1 olan bir kart hicbir adi cozemiyor demektir;
    /// geri alinacak yedek kaybolmus ya da geri yukleme basarisiz olmus olabilir.
    /// Kullanicinin kendi yerel cozumleyicisi (AdGuard Home, Acrylic...) cevap
    /// verdigi icin bu kosula girmez.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> ResetOrphanedRedirectsAsync(
        CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        using (AcquireLock())
        {
            return await ResetOrphanedRedirectsCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> ResetOrphanedRedirectsCoreAsync(CancellationToken cancellationToken)
    {
        var fixedNames = new List<string>();

        foreach (var nic in SafeGetInterfaces())
        {
            if (!IsOnlyLocalResolver(ReadStaticNameServer(Ipv4ParametersKey, nic.Id)))
            {
                continue;
            }

            if (await SetDhcpAsync("ipv4", nic.Name, cancellationToken).ConfigureAwait(false))
            {
                await SetDhcpAsync("ipv6", nic.Name, cancellationToken).ConfigureAwait(false);
                fixedNames.Add(nic.Name);
            }
        }

        if (fixedNames.Count > 0)
        {
            await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        return fixedNames;
    }

    private static Task<(int ExitCode, string Output)> RunNetshAsync(
        string[] arguments, CancellationToken cancellationToken)
        => RunProcessAsync("netsh.exe", arguments, cancellationToken);

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, $"{fileName} baslatilamadi.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
