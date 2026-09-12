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

    /// <summary>Diskte bir yedek duruyor mu (yani DNS bizim tarafimizdan degistirilmis mi).</summary>
    public static bool HasBackup => File.Exists(BackupPath);

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

            if (eksik.Count > 0)
            {
                await WriteBackupAsync(
                    new DnsBackup
                    {
                        // Sahip ve tarih KORUNUYOR: yonlendirmeyi kimin yaptigi
                        // degismedi, yalnizca kapsami genisledi.
                        CreatedAt = existing.CreatedAt,
                        Owner = existing.Owner,
                        Entries = [.. existing.Entries, .. eksik],
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var changed = new List<string>();
        foreach (var (nic, required) in targets)
        {
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

        return changed;
    }

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

        DnsBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(BackupPath, cancellationToken).ConfigureAwait(false),
                CoreJsonContext.Default.DnsBackup);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"DNS yedegi okunamadi ({BackupPath}): {ex.Message}. " +
                "Ag ayarlarindan DNS'i elle 'otomatik' yapabilirsiniz.", ex);
        }

        if (backup is null)
        {
            return [];
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

        return restored;
    }

    /// <summary>Tek bir arayuzun ayarini geri yukler. IPv4 gercekten geri geldiyse true.</summary>
    private static async Task<bool> RestoreEntryAsync(
        DnsBackupEntry entry, string alias, CancellationToken cancellationToken)
    {
        var ipv4Ok = entry.WasStatic && entry.Addresses.Count > 0
            ? await SetStaticAsync("ipv4", alias, entry.Addresses, cancellationToken).ConfigureAwait(false)
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
    /// Bozuk yedek burada <c>null</c> sayilmiyor -- <c>null</c> donmek cagiranin
    /// uzerine yeni bir yedek yazmasina yol acar ve o an DNS bizde ise 127.0.0.1
    /// "orijinal" olarak kaydedilir. Bozuk dosya bilerek hata olarak yukseliyor.
    /// </remarks>
    private static async Task<DnsBackup?> ReadBackupAsync(CancellationToken cancellationToken)
    {
        if (!HasBackup)
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(BackupPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CoreJsonContext.Default.DnsBackup)
               ?? throw new InvalidOperationException(
                   $"DNS yedegi okunamadi: {BackupPath}. Elle geri almak icin --cleanup kullanin.");
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
    /// Kapsam disinda kalan tek durum: kurulumdan SONRA takilan yeni bir adaptor
    /// (or. USB WiFi). Onun icin uygulamayi acip servisi yeniden kurmak gerekir.
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
        => IsOfflinePhysical(nic.OperationalStatus, nic.NetworkInterfaceType);

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

        var addresses = dns
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .ToList();

        var ipv6Addresses = dns
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            .Select(a => a.ToString())
            .ToList();

        return new DnsBackupEntry
        {
            Alias = nic.Name,
            Guid = nic.Id,
            WasStatic = IsStaticallyConfigured(Ipv4ParametersKey, nic.Id),
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
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{parametersKey}\{interfaceGuid}");

            var nameServer = key?.GetValue("NameServer") as string;
            return !string.IsNullOrWhiteSpace(nameServer);
        }
        catch (Exception)
        {
            // Okuyamiyorsak DHCP varsayiyoruz: yanlis tarafa dusmek istersek,
            // DHCP'ye dondurmek static yazmaktan daha guvenli.
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunNetshAsync(
        string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
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
                return (-1, "netsh baslatilamadi.");
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
