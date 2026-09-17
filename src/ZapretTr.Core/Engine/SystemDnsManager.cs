using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ZapretTr.Core.Engine;

/// <summary>DNS yönlendirmesinin sahibi.</summary>
public static class DnsBackupOwner
{
    /// <summary>Çalışan uygulama yaptı; uygulama kapanırken geri alır.</summary>
    public const string App = "app";

    /// <summary>Kurulu servis yaptı; yalnızca servis kaldırılırken geri alınır.</summary>
    public const string Service = "service";
}

/// <summary>Bir ağ arayüzünün DNS ayarının değiştirilmeden önceki hâli.</summary>
public sealed class DnsBackupEntry
{
    [JsonPropertyName("alias")]
    public required string Alias { get; init; }

    [JsonPropertyName("guid")]
    public required string Guid { get; init; }

    /// <summary>
    /// Adresler elle mi girilmişti (statik), yoksa DHCP'den mi geliyordu.
    /// </summary>
    /// <remarks>
    /// Bu ayrım geri yüklemenin doğru olması için şart. DHCP'den gelen adresleri
    /// statik olarak geri yazmak, kullanıcının ağı değiştiğinde (başka bir WiFi,
    /// mobil paylaşım) eski ağ geçidinin DNS'ine sabitlenmiş kalmasına yol açar;
    /// yani biz "geri aldık" deriz ama makine bozuk kalır.
    /// </remarks>
    [JsonPropertyName("wasStatic")]
    public required bool WasStatic { get; init; }

    [JsonPropertyName("addresses")]
    public IReadOnlyList<string> Addresses { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Bu kayıt IPv6 DNS bilgisini de taşıyor mu.
    /// </summary>
    /// <remarks>
    /// Ayrı bir bayrak, çünkü 0.1.18 ve öncesinde yazılmış yedeklerde IPv6 alanları
    /// HİÇ YOK. Onları "IPv6 DHCP'ydi" diye okumak, kullanıcının elle girdiği bir
    /// IPv6 DNS'ini geri alma sırasında silmek olurdu; dokunmadığımız bir şeyi
    /// bozmak. Bayrak yoksa IPv6 tarafına hiç dokunulmuyor.
    /// </remarks>
    [JsonPropertyName("ipv6Captured")]
    public bool Ipv6Captured { get; init; }

    [JsonPropertyName("ipv6WasStatic")]
    public bool Ipv6WasStatic { get; init; }

    [JsonPropertyName("ipv6Addresses")]
    public IReadOnlyList<string> Ipv6Addresses { get; init; } = Array.Empty<string>();
}

/// <summary>Diske yazılan yedek.</summary>
public sealed class DnsBackup
{
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Bu yönlendirmeyi kim yaptı: çalışan uygulama mı, yoksa kurulu servis mi.
    /// </summary>
    /// <remarks>
    /// Ayrım şart. Servis kurulduğunda DNS yönlendirmesi açılıştan açılışa
    /// SÜREKLİ olmalı; uygulama kapanırken onu geri alırsa kullanıcı "otomatik
    /// başlatmayı kurdum" der ama makine yeniden başlatıldığında DNS eski hâline
    /// dönmüş olur ve engellemeler geri gelir. Uygulama yalnızca KENDİ yaptığı
    /// yönlendirmeyi geri alır.
    /// </remarks>
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = DnsBackupOwner.App;

    [JsonPropertyName("entries")]
    public IReadOnlyList<DnsBackupEntry> Entries { get; init; } = Array.Empty<DnsBackupEntry>();
}

/// <summary>
/// Sistemin DNS sunucusunu değiştirir ve her koşulda geri alabilir.
/// </summary>
/// <remarks>
/// Bu sınıf uygulamanın en riskli parçası. DNS'i 127.0.0.1'e çevirip dnscrypt-proxy
/// çalışmazsa kullanıcı HİÇBİR adı çözemez; yani ona göre internet tamamen gitmiş
/// olur. Üç savunma var:
///
///   1. Değişiklikten ÖNCE yedek diske yazılır. Uygulama çökse, öldürülse, makine
///      yeniden başlasa bile geri alınabilir; yedek bellekte tutulsaydı o senaryoda
///      kaybolurdu.
///   2. DNS ancak dnscrypt-proxy'nin gerçekten cevap verdiği doğrulandıktan sonra
///      değiştirilir (<see cref="DnsCryptRunner"/> bunu yapar).
///   3. Açılışta <see cref="TryRecoverAsync"/> çağrılır: diskte yedek varken DNS
///      hâlâ bizim adresimizi gösteriyorsa ve proxy çalışmıyorsa kendiliğinden
///      eski hâline döner.
///
/// Yalnızca varsayılan ağ geçidi olan arayüzlere dokunulur. Bütün bağdaştırıcılara
/// dokunmak (sanal makine, VPN, Bluetooth bağdaştırıcıları dahil) gereksiz geniş bir
/// değişiklik olurdu.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SystemDnsManager
{
    /// <summary>dnscrypt-proxy'nin dinlediği adres.</summary>
    public const string LocalResolver = "127.0.0.1";

    private static string BackupPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-backup.json");

    /// <summary>
    /// Servisin yönlendirmesi, çözümleyici uzun süre cevap vermediği için
    /// <see cref="DnsGuard"/> tarafından ASKIYA alındı mı.
    /// </summary>
    private static string SuspendedMarkerPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-askida.flag");

    /// <summary>Diskte bir yedek duruyor mu (yani DNS bizim tarafımızdan değiştirilmiş mi).</summary>
    public static bool HasBackup => File.Exists(BackupPath);

    /// <summary>
    /// Servis yönlendirmesi askıda mı: yedek geri yüklendi ama servis hâlâ kurulu
    /// ve çözümleyici geri geldiğinde yönlendirme yeniden yapılmalı.
    /// </summary>
    public static bool IsSuspended => File.Exists(SuspendedMarkerPath);

    internal static void MarkSuspended()
    {
        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(SuspendedMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Askıda işaretini kaldırır. Yoksa bir şey yapmaz.</summary>
    public static void ClearSuspended()
    {
        try
        {
            File.Delete(SuspendedMarkerPath);
        }
        catch (Exception)
        {
            // İşaret silinemezse bekçi bir sonraki turda servisin yokluğunu
            // görüp kendisi temizler.
        }
    }

    /// <summary>
    /// DNS ayarını değiştiren işlemleri SÜREÇLER ARASINDA sıraya sokan kilit.
    /// </summary>
    /// <remarks>
    /// Sistem DNS'ine artık aynı anda üç ayrı süreç dokunabiliyor: arayüz,
    /// kaldırıcı/kurulum (<c>--uninstall-services</c>) ve SYSTEM olarak çalışan
    /// bekçi (<c>--dns-guard</c>). İki geri alma aynı anda koşarsa biri yedeği
    /// silerken öteki okuyor olabilir; bir yönlendirme ile bir geri alma iç içe
    /// girerse kartların yarısı 127.0.0.1'de, yarısı DHCP'de kalır ve yedek
    /// hiçbirini doğru anlatmaz.
    ///
    /// Mutex değil semafor: Mutex iş parçacığına bağlı ve async metotlar
    /// <c>await</c> sonrası başka iş parçacığından devam ediyor; Mutex'i orada
    /// bırakmak istisna fırlatır. Kilit alınamazsa (süre doldu, yetki yok) işlem
    /// YİNE DE yapılıyor: DNS'i geri almayı bir kilit yüzünden atlamak, kullanıcıyı
    /// ad çözemez hâlde bırakabilir.
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
                // Kilitsiz devam: gerekçesi AcquireLock'ta.
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
                // Bırakılamadıysa, tutacak kimse kalmayınca çekirdek nesneyi siliyor.
            }

            semaphore.Dispose();
        }
    }

    /// <summary>
    /// Mevcut yönlendirmenin sahibi. Yedek yoksa null.
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

    /// <summary>Yönlendirmeyi kurulu servis yaptıysa true; uygulama buna dokunmamalı.</summary>
    public static bool IsOwnedByService
        => string.Equals(BackupOwner, DnsBackupOwner.Service, StringComparison.Ordinal);

    /// <summary>
    /// Etkin arayüzlerin DNS ayarını yedekleyip <see cref="LocalResolver"/>'a çevirir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Uygun bir arayüz bulunamazsa.</exception>
    /// <param name="owner">
    /// Yönlendirmeyi kim yapıyor. <see cref="DnsBackupOwner.Service"/> verildiğinde
    /// uygulama kapanırken geri ALMAZ.
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
    /// <see cref="RedirectToLocalAsync"/>'in kilitsiz gövdesi. Kilidi zaten tutan
    /// <see cref="DnsGuard"/> bunu çağırıyor.
    /// </summary>
    /// <remarks>
    /// Tekrar tekrar çağrılabilir ve her çağrı YALNIZCA eksiği tamamlar: yedekte
    /// olmayan kartı yedeğe ekler, DNS'i henüz 127.0.0.1 olmayan kartı çevirir.
    /// Bekçi bunu her ağ değişikliğinde çalıştırıyor; zaten çevrilmiş kartlara
    /// yeniden netsh koşmak ve önbelleği boşaltmak her seferinde gereksiz bir
    /// kesinti olurdu.
    /// </remarks>
    internal static async Task<IReadOnlyList<string>> RedirectCoreAsync(
        string owner, CancellationToken cancellationToken)
    {
        var targets = GetRedirectTargets();

        // Şu an internete çıkan bir arayüz yoksa yapılacak anlamlı bir şey yok:
        // yalnızca bağlı olmayan kartlara yazıp "DNS çevrildi" demek yanlış olur.
        if (!targets.Any(t => t.Required))
        {
            throw new InvalidOperationException(
                "Varsayilan ag gecidi olan bir arayuz bulunamadi; DNS degistirilmedi.");
        }

        // Mevcut bir yedeğin üzerine YAZMIYORUZ: ikinci kez çağrılırsa bizim
        // koyduğumuz 127.0.0.1 değerini "orijinal" diye kaydeder ve geri dönüş
        // yolu tamamen kaybolurdu.
        //
        // Ama yedeğin VARLIĞI da yetmiyor: yedekte olmayan bir arayüze yazacaksak
        // onu yedeğe EKLEMEK zorundayız. Aksi hâlde geri alma o arayüzü atlar,
        // DNS'i 127.0.0.1'de kalır ve kaldırmadan sonra o bağlantıda hiçbir ad
        // çözülmez. Kablo takılıyken kurup sonra WiFi'ye geçen kullanıcıda tam
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

            // SERVİS, UYGULAMANIN YÖNLENDİRMESİNİ DEVRALIR.
            //
            // Uygulama korumayı çalıştırırken "Servis Olarak Yükle"ye basılırsa
            // yedek "app" sahipliğiyle duruyordu ve öyle KALIYORDU: sahip
            // korunuyordu. Uygulama kapanınca yönlendirmeyi kendisininki sanıp
            // geri alıyor, servis kurulu olduğu hâlde şifreli DNS sessizce devreden
            // çıkıyordu. Tarih korunuyor; ters yön (servisinkini uygulamanın
            // devralması) yok, çünkü uygulamanın kapanması kalıcı bir kurulumu
            // bozmamalı.
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
            // Zaten bizde: dokunma. Gerekçesi RedirectCoreAsync'in açıklamasında.
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

                // IPv6 DNS'i AYRICA boşaltmak zorundayız. Yalnızca IPv4'ü
                // 127.0.0.1'e çevirmek yetmiyor: arayüzde İSS'in verdiği bir IPv6
                // çözümleyicisi duruyorsa (yönlendirici duyurusu ya da DHCPv6 ile
                // gelir) Windows sorguyu pekâlâ oraya yollar ve DNS kaçırma
                // katmanı ayakta kalır. Dışarıdan görünen şey tam olarak "şifreli
                // DNS açık ama site yine açılmıyor" olur; yani belirtisi
                // stratejinin tutmamasıyla aynı.
                //
                // Yönlendirmek yerine BOŞALTIYORUZ: dnscrypt-proxy yalnızca
                // 127.0.0.1'i dinliyor ve onu [::1]'i de dinlemeye zorlamak,
                // IPv6'nın kapalı olduğu makinelerde bağlanamayıp sürecin hiç
                // açılmamasına yol açardı. Boş bırakınca Windows IPv4'e, yani
                // bize düşer.
                //
                // En iyi çabayla: bu adımın başarısızlığı IPv4 yönlendirmesini
                // geçersiz kılmaz, yalnızca IPv6 sızıntısı ihtimali kalır.
                await RunNetshAsync(
                    ["interface", "ipv6", "delete", "dnsservers", $"name={nic.Name}", "all"],
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            // Bağlı olmayan bir kartta netsh başarısız olabiliyor. Bunun yüzünden
            // tüm işlemi düşürmek, KULLANILAN arayüzü zaten çevirmişken kullanıcıyı
            // hata ekranına götürmek olurdu; kazanılanı kaybettirmeden geç.
            if (!required)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"'{nic.Name}' arayuzunun DNS ayari degistirilemedi: {output.Trim()}");
        }

        if (changed.Count > 0)
        {
            // Bir ÖNLEM, ölçülmüş bir sebep değil. Bu satır "Windows zehirli kaydı
            // DNS çevrildikten sonra da tutuyor" varsayımıyla eklenmişti; 2026-09-13'te
            // Windows 11 (26200) üzerinde ölçüldü ve TEKRARLANMADI: sunucu değişince
            // önbellek kendiliğinden geçersiz oluyor. Başka sürümlerde ölçülmediği ve
            // zararsız olduğu için duruyor.
            await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    /// <summary>
    /// Mevcut yedek varken yeni bir yönlendirme istendiğinde yedeğin sahibi.
    /// </summary>
    /// <remarks>
    /// Servis her zaman kazanır: servis kurulu olduğu sürece yönlendirme açılıştan
    /// açılışa sürmeli ve uygulamanın kapanması onu geri almamalı.
    /// </remarks>
    public static string DecideOwner(string existingOwner, string requestedOwner)
        => string.Equals(existingOwner, DnsBackupOwner.Service, StringComparison.Ordinal)
           || string.Equals(requestedOwner, DnsBackupOwner.Service, StringComparison.Ordinal)
            ? DnsBackupOwner.Service
            : existingOwner;

    /// <summary>
    /// Windows'un DNS önbelleğini boşaltır. En iyi çabayla: başarısızlığı
    /// yönlendirmeyi geçersiz kılmaz.
    /// </summary>
    private static async Task FlushDnsCacheAsync(CancellationToken cancellationToken)
        => await RunProcessAsync("ipconfig.exe", ["/flushdns"], cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Yedekteki ayarları geri yükler ve yedeği siler.
    /// </summary>
    /// <returns>Geri yüklenen arayüz adları. Yedek yoksa boş liste.</returns>
    /// <exception cref="InvalidOperationException">
    /// Makinede DURAN bir arayüzün ayarı geri alınamazsa. O durumda yedek
    /// SİLİNMEZ: silinirse geri dönüş yolu tamamen kaybolur.
    /// </exception>
    /// <remarks>
    /// Bu metot iki hatayı birden kapatıyor ve ikisi de sessizdi:
    ///
    /// 1. <b>netsh çıkış kodları hiç okunmuyordu.</b> Geri alma başarısız olsa bile
    ///    metot "geri aldım" deyip yedeği SİLİYORDU. Sonuç, DEVAM'ın "projedeki en
    ///    kötü sonuç" dediği tablonun ta kendisi: sistem DNS'i 127.0.0.1'de kalır,
    ///    dnscrypt çalışmaz, makine hiçbir adı çözemez ve elde geri dönülecek kayıt
    ///    da kalmaz. Artık yalnızca HEPSİ başarılı olursa yedek siliniyor.
    ///
    /// 2. <b>Arayüz ADIYLA aranıyordu.</b> Kullanıcı bağlantıyı yeniden adlandırırsa
    ///    ("Ethernet" -> "Ev") netsh o adı bulamaz; birinci hatayla birleşince geri
    ///    alma sessizce hiçbir şey yapmaz. Artık önce yedekteki GUID ile şu anki ad
    ///    bulunuyor, ad yalnızca yedek çözüm.
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

    /// <summary><see cref="RestoreAsync"/>'in kilitsiz gövdesi.</summary>
    internal static async Task<IReadOnlyList<string>> RestoreCoreAsync(CancellationToken cancellationToken)
    {
        // Kilit beklenirken başka bir süreç geri almış olabilir.
        if (!HasBackup)
        {
            return [];
        }

        var backup = await ReadBackupAsync(cancellationToken).ConfigureAwait(false);

        if (backup is null)
        {
            // BOZUK YEDEK: orijinal ayar okunamıyor, ama yönlendirme bu yüzden
            // KALICI olmamalı. Eskiden burada hata fırlatılıyordu ve yedek diskte
            // kalıyordu; her geri alma denemesi aynı hatayla düşüyor, makine
            // 127.0.0.1'de ve çözümleyicisiz kalabiliyordu. Bilinebilecek tek
            // doğru şey "127.0.0.1 bizimdi": o kartlar otomatiğe dönüyor. Elle
            // girilmiş eski bir DNS varsa kaybolur; ama o bilgi zaten okunamıyor.
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
                // Kart artık makinede yok (sökülmüş USB WiFi, kaldırılmış sanal
                // bağdaştırıcı). Geri alınacak bir şey yok ve bu bir başarısızlık
                // DEĞİL: yoksa yedek sonsuza kadar diskte kalır ve her açılışta
                // aynı hata mesajı çıkardı.
                continue;
            }

            // KARTIN DNS'İ ARTIK BİZDE DEĞİLSE DOKUNMA.
            //
            // Yönlendirme ile geri alma arasında aylar geçebiliyor (servis modu).
            // Kullanıcı o arada bir kartın DNS'ini elle değiştirmiş ya da Windows'un
            // "ağ sıfırlama"sı onu otomatiğe döndürmüş olabilir. Eskiden yedek KÖRÜ
            // KÖRÜNE yazılıyordu ve kullanıcının sonradan yaptığı ayar sessizce
            // eziliyordu. Bizim bırakacağımız tek iz 127.0.0.1; o yoksa geri
            // alınacak bir şey de yok ve bu bir başarısızlık değil.
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

        // Yedek ancak geri yükleme bittikten SONRA siliniyor. Önce silinseydi ve
        // geri yükleme yarıda kalsaydı, kullanıcının dönebileceği bir kayıt kalmazdı.
        File.Delete(BackupPath);

        // Geri alırken de boşaltılıyor: önbellekte bizim çözümleyicimizden gelen
        // kayıtlar duruyor ve kullanıcı artık başka bir çözümleyiciye döndü.
        await FlushDnsCacheAsync(cancellationToken).ConfigureAwait(false);

        return restored;
    }

    /// <summary>Tek bir arayüzün ayarını geri yükler. IPv4 gerçekten geri geldiyse true.</summary>
    private static async Task<bool> RestoreEntryAsync(
        DnsBackupEntry entry, string alias, CancellationToken cancellationToken)
    {
        // Yedekte 127.0.0.1 "orijinal" diye durabilir: yedek bir şekilde kaybolmuş
        // ve yönlendirme üstüne ikinci kez yapılmış olabilir (0.1.19 ve öncesinde
        // Capture bunu elemiyordu). Onu geri yazmak, yönlendirmeyi KALICI yapmak
        // demek; kaldırmadan sonra dnscrypt yok ve makine hiçbir adı çözemez.
        var (wasStatic, addresses) = SanitizeCaptured(entry.WasStatic, entry.Addresses);

        var ipv4Ok = wasStatic
            ? await SetStaticAsync("ipv4", alias, addresses, cancellationToken).ConfigureAwait(false)
            : await SetDhcpAsync("ipv4", alias, cancellationToken).ConfigureAwait(false);

        // IPv6 yalnızca BİZİM boşalttığımız kayıtlarda geri yükleniyor.
        //
        // Sonucu bilerek dikkate almıyoruz: ad çözümü IPv4 üzerinden geri geldiyse
        // makine çalışıyor demektir ve IPv6 tarafındaki bir aksilik yüzünden yedeği
        // diskte tutmak kullanıcıyı her açılışta tekrarlanan bir hata mesajına
        // mahkûm ederdi. Kaybedilen şey en kötü ihtimalle o arayüzün IPv6 DNS'i;
        // Windows IPv4'e düşer.
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
    /// Otomatik (DHCP) ayara döndürür. Adresleri statik yazmak yanlış olurdu: başka
    /// bir ağa bağlandığında eski ağ geçidinin DNS'ine sabitlenmiş kalırdı.
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
    /// Yedekteki kaydın BUGÜNKÜ arayüz adı. Kart artık yoksa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// GUID önce geliyor: kullanıcı bağlantıyı yeniden adlandırmış olabilir ve
    /// netsh yalnızca güncel adı tanıyor. Arayüz listesi hiç okunamadıysa (nadir,
    /// ama olur) yedekteki ada güveniyoruz; hiçbir şey denememekten iyidir.
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
    /// Açılışta çağrılır: önceki bir çökme sonrası DNS bizde kalmışsa geri alır.
    /// </summary>
    /// <param name="localResolverResponds">
    /// dnscrypt-proxy şu an cevap veriyor mu. Veriyorsa kurtarma yapılmaz;
    /// muhtemelen başka bir ZapretTR örneği çalışıyordur.
    /// </param>
    public static async Task<IReadOnlyList<string>> TryRecoverAsync(
        bool localResolverResponds, CancellationToken cancellationToken = default)
    {
        if (!HasBackup || localResolverResponds)
        {
            return [];
        }

        // Yedek var, proxy cevap vermiyor: makine şu an ad çözemiyor demektir.
        // Bu, uygulamanın düzgün kapanmadığı anlamına gelir ve düzeltilmesi
        // kullanıcıdan onay beklemeyi kaldırmaz: interneti yok.
        return await RestoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Diskteki yedeği okur; yoksa ya da bozuksa <c>null</c> döner.</summary>
    /// <remarks>
    /// Bozuk yedek eskiden hata olarak yükseliyordu, çünkü <c>null</c> dönmek
    /// çağıranın üzerine yeni bir yedek yazmasına yol açıyor ve o an DNS bizdeyse
    /// 127.0.0.1 "orijinal" olarak kaydediliyordu. Bunun bedeli ağırdı: bozuk bir
    /// dosya şifreli DNS'i KALICI olarak açılamaz yapıyordu; her yönlendirme aynı
    /// okuma hatasıyla düşüyordu ve kullanıcının dosyanın yerini bilmesi gerekiyordu.
    ///
    /// Artık <see cref="Capture"/> 127.0.0.1'i asla orijinal saymadığı için o
    /// korumaya gerek kalmadı. Bozuk dosya silinmiyor, yanına taşınıyor: teşhis
    /// için içeriği lazım olabilir.
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
            // Aşağıda karantinaya alınıyor.
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
                // Silinemiyorsa bir sonraki deneme aynı yoldan geçer.
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

    /// <summary>DNS yönlendirmesi uygulanacak bir arayüz ve zorunluluğu.</summary>
    /// <param name="Required">
    /// Şu an internete çıkan arayüz mü. Zorunlu bir arayüzde yazma başarısız
    /// olursa işlem hata verir; isteğe bağlı olanda yalnızca atlanır.
    /// </param>
    private readonly record struct RedirectTarget(NetworkInterface Nic, bool Required);

    /// <summary>
    /// DNS'i çevrilecek arayüzler: şu an internete çıkanlar VE şu an bağlı
    /// olmayan fiziksel arayüzler.
    /// </summary>
    /// <remarks>
    /// Eskiden yalnızca "çalışan + varsayılan ağ geçidi olan" arayüzler
    /// alınıyordu. Kablo takılıyken kurulum yapan bir kullanıcıda WiFi
    /// <c>Disconnected</c> olduğu için atlanıyordu; sonra kabloyu çıkarıp WiFi'ye
    /// geçince o arayüzün DNS'i İSS'in sunucusunda kalıyor ve DNS engellemesi
    /// geri geliyordu. Belirtisi de yok: winws çalışmaya devam ettiği için arayüz
    /// "KORUMA AKTİF" gösteriyor, şifreli DNS ise devrede değil.
    ///
    /// Bu yüzden bağlı olmayan Ethernet/WiFi arayüzlerine de şimdiden yazılıyor:
    /// ayar kalıcı, arayüz bağlanınca geçerli oluyor. Yedek de onları kapsıyor,
    /// dolayısıyla geri alma simetrik kalıyor.
    ///
    /// Kurulumdan SONRA takılan yeni bir bağdaştırıcı (USB WiFi, telefonla USB
    /// paylaşım) bu listeye ancak bir sonraki çağrıda girer. Servis modunda o çağrıyı
    /// <see cref="DnsGuard"/> her ağ bağlantısında yapıyor; eskiden hiç yapılmıyordu
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

    /// <summary>Ağ geçidi olmayan sanal bağdaştırıcılar (VM, tünel) burada elenir.</summary>
    private static bool HasIpv4Gateway(NetworkInterface nic)
        => nic.GetIPProperties().GatewayAddresses
            .Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                      && !g.Address.Equals(System.Net.IPAddress.Any));

    /// <summary>
    /// Şu an bağlı olmayan ama kullanıcıyı internete çıkarabilecek bir arayüz mü.
    /// </summary>
    /// <remarks>
    /// Tür süzgeci kasıtlı olarak dar: sanal bağdaştırıcıların çoğu <c>Up</c> ve ağ
    /// geçidi olmadığı için zaten eleniyor, buraya ancak gerçekten bağlı olmayan
    /// bir kart giriyor. Bluetooth kişisel ağ (PAN) da dahil; telefondan
    /// bağlantı paylaşan kullanıcının o bağlantıda da korunması gerekiyor.
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
    /// Bağdaştırıcının sürücü açıklaması onun sanal bir kart olduğunu mu söylüyor.
    /// </summary>
    /// <remarks>
    /// Tür süzgeci (<see cref="IsOfflinePhysical(OperationalStatus, NetworkInterfaceType)"/>)
    /// sanal kartları AYIRAMIYOR: Windows'un Wi-Fi Direct için açtığı
    /// "Local Area Connection* 1/2" kartları kendini <c>Wireless80211</c> olarak
    /// bildiriyor ve bağlı olmadıkları için "bağlı olmayan fiziksel kart" sayılıyordu.
    /// Gerçek bir kullanıcının raporunda ikisinin de DNS'i 127.0.0.1'e çevrilmişti.
    /// O kartlar kullanıcıyı internete çıkarmıyor; DNS'lerini değiştirmek yalnızca
    /// geri alınacak bir değişiklik daha biriktiriyor.
    ///
    /// Açıklama sürücü adıdır ve Türkçe Windows'ta da İngilizce gelir
    /// ("Microsoft Wi-Fi Direct Virtual Adapter #2"). Aynı kelime Hyper-V, VMware ve
    /// VirtualBox kartlarını da yakalıyor. Bluetooth kişisel ağ kasıtlı olarak
    /// DIŞARIDA kalıyor ("Bluetooth Device (Personal Area Network)"): telefondan
    /// bağlantı paylaşan kullanıcı o kartla internete çıkıyor.
    /// </remarks>
    public static bool IsVirtualAdapterDescription(string? description)
        => description is not null
           && description.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu arayüzün DNS'i çevrilecek mi.</summary>
    /// <remarks>
    /// Dışarı açılıyor, çünkü bu kararın GERÇEK makinede ne verdiği tek başına
    /// birim testiyle öğrenilemiyor: arayüz listesi işletim sisteminden geliyor.
    /// Hiçbir şey değiştirmeden "hangi kartlar seçilirdi" diye sorabilmek,
    /// sessizce yanlış seçim yapan bir hatanın tekrarını önlüyor.
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

        // BİZİM 127.0.0.1'İMİZ ASLA "ORİJİNAL" DİYE KAYDEDİLMEZ.
        //
        // Yedek yokken kartın DNS'i zaten 127.0.0.1 olabilir: yedek dosyası elle ya
        // da bir temizlik aracınca silinmiş, önceki bir sürüm kaldırılırken yarım
        // kalmış. Eskiden bu değer "elle girilmiş DNS" diye yedeğe giriyordu ve
        // geri alma onu SADAKATLE geri yazıyordu; uygulama kaldırıldıktan sonra
        // o kart hiçbir adı çözemiyordu, projedeki en kötü tablo.
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

            // IPv6 tarafı da yedekleniyor, çünkü yönlendirme onu BOŞALTIYOR
            // (gerekçesi RedirectToLocalAsync içinde). Yedeklemeden boşaltmak,
            // DEVAM'ın "yönlendirmenin kapsamını genişletmek yedeğin kapsamını
            // genişletmez" kuralını ikinci kez çiğnemek olurdu.
            Ipv6Captured = true,
            Ipv6WasStatic = IsStaticallyConfigured(Ipv6ParametersKey, nic.Id),
            Ipv6Addresses = ipv6Addresses,
        };
    }

    private const string Ipv4ParametersKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string Ipv6ParametersKey = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces";

    /// <summary>
    /// Arayüzün DNS'i elle mi ayarlanmış. Kayıt defterindeki NameServer değeri
    /// doluysa statik, boşsa DHCP'den geliyor.
    /// </summary>
    /// <remarks>
    /// .NET API'si bu ayrımı vermiyor; <c>DnsAddresses</c> her iki durumda da aynı
    /// görünüyor. Kayıt defteri bunu ayırt edebileceğimiz tek yer.
    /// </remarks>
    private static bool IsStaticallyConfigured(string parametersKey, string interfaceGuid)
        // Okuyamıyorsak (null) DHCP varsayıyoruz: yanlış tarafa düşmek gerekirse,
        // DHCP'ye döndürmek statik yazmaktan daha güvenli.
        => !string.IsNullOrWhiteSpace(ReadStaticNameServer(parametersKey, interfaceGuid));

    /// <summary>
    /// Kartın elle yazılmış DNS değeri. DHCP'deyse boş dize, okunamazsa <c>null</c>.
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

    /// <summary>Kayıt defterindeki NameServer değeri ("a,b" ya da "a b") adreslere ayrılır.</summary>
    private static IReadOnlyList<string> SplitNameServers(string? nameServer)
        => string.IsNullOrWhiteSpace(nameServer)
            ? []
            : nameServer.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Kartın elle yazılmış DNS'i yalnızca bizim çözümleyicimiz mi.</summary>
    public static bool IsOnlyLocalResolver(string? staticNameServer)
    {
        var adresler = SplitNameServers(staticNameServer);
        return adresler.Count > 0 && adresler.All(a => a == LocalResolver);
    }

    /// <summary>
    /// Yedekteki bir kart geri alınmalı mı, kartın ŞU ANKİ elle yazılmış DNS'ine göre.
    /// </summary>
    /// <param name="currentStaticNameServer">
    /// Kayıt defterindeki değer; DHCP'de boş dize, okunamadıysa <c>null</c>.
    /// </param>
    /// <remarks>
    /// Okunamıyorsa geri alınıyor: bizim yönlendirmemiz duruyor olabilir ve onu
    /// yerinde bırakmak kullanıcıyı ad çözemez hâlde bırakabilir. Gereksiz bir
    /// geri alma ise en kötü ihtimalle kartın DNS'ini yedekteki hâline döndürür.
    /// </remarks>
    public static bool ShouldRestore(string? currentStaticNameServer)
        => currentStaticNameServer is null
           || SplitNameServers(currentStaticNameServer).Contains(LocalResolver);

    /// <summary>
    /// Yedeğe girecek (ya da yedekten geri yazılacak) IPv4 DNS bilgisinden bizim
    /// çözümleyicimizi ayıklar.
    /// </summary>
    /// <returns>
    /// Elle yazılmış adreslerden geriye bir şey kalmıyorsa kart DHCP sayılır.
    /// </returns>
    public static (bool WasStatic, IReadOnlyList<string> Addresses) SanitizeCaptured(
        bool wasStatic, IReadOnlyList<string> addresses)
    {
        var gercek = addresses.Where(a => a != LocalResolver).ToList();
        return (wasStatic && gercek.Count > 0, gercek);
    }

    /// <summary>
    /// YEDEĞİ OLMAYAN 127.0.0.1 yönlendirmelerini otomatiğe döndürür.
    /// </summary>
    /// <returns>Düzeltilen kartların adları.</returns>
    /// <remarks>
    /// Yalnızca "Tüm Ayarları Sıfırla" ve kaldırma yolunda, bizim çözümleyicimiz
    /// silindikten SONRA ve 127.0.0.1:53 cevap VERMİYORKEN çağrılmalı. O anda
    /// DNS'i yalnızca 127.0.0.1 olan bir kart hiçbir adı çözemiyor demektir;
    /// geri alınacak yedek kaybolmuş ya da geri yükleme başarısız olmuş olabilir.
    /// Kullanıcının kendi yerel çözümleyicisi (AdGuard Home, Acrylic...) cevap
    /// verdiği için bu koşula girmez.
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
