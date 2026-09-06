using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ZapretTr.Core.Engine;

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
}

/// <summary>Diske yazilan yedek.</summary>
public sealed class DnsBackup
{
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string BackupPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-backup.json");

    /// <summary>Diskte bir yedek duruyor mu (yani DNS bizim tarafimizdan degistirilmis mi).</summary>
    public static bool HasBackup => File.Exists(BackupPath);

    /// <summary>
    /// Aktif arayuzlerin DNS ayarini yedekleyip <see cref="LocalResolver"/>'a cevirir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Uygun bir arayuz bulunamazsa.</exception>
    public static async Task<IReadOnlyList<string>> RedirectToLocalAsync(
        CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var interfaces = GetActiveInterfaces();
        if (interfaces.Count == 0)
        {
            throw new InvalidOperationException(
                "Varsayilan ag gecidi olan bir arayuz bulunamadi; DNS degistirilmedi.");
        }

        // Zaten bir yedek varsa uzerine YAZMIYORUZ: ikinci kez cagrilirsa mevcut
        // (bizim koydugumuz 127.0.0.1) degeri "orijinal" diye kaydeder ve geri
        // donus yolu tamamen kaybolurdu.
        if (!HasBackup)
        {
            var backup = new DnsBackup
            {
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Entries = interfaces.Select(Capture).ToList(),
            };

            Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
            await File.WriteAllTextAsync(
                BackupPath, JsonSerializer.Serialize(backup, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
        }

        var changed = new List<string>();
        foreach (var nic in interfaces)
        {
            var (exitCode, output) = await RunNetshAsync(
                ["interface", "ipv4", "set", "dnsservers", $"name={nic.Name}", "source=static",
                 $"address={LocalResolver}", "validate=no"],
                cancellationToken).ConfigureAwait(false);

            if (exitCode == 0)
            {
                changed.Add(nic.Name);
            }
            else
            {
                throw new InvalidOperationException(
                    $"'{nic.Name}' arayuzunun DNS ayari degistirilemedi: {output.Trim()}");
            }
        }

        return changed;
    }

    /// <summary>
    /// Yedekteki ayarlari geri yukler ve yedegi siler.
    /// </summary>
    /// <returns>Geri yuklenen arayuz adlari. Yedek yoksa bos liste.</returns>
    public static async Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!HasBackup)
        {
            return [];
        }

        DnsBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<DnsBackup>(
                await File.ReadAllTextAsync(BackupPath, cancellationToken).ConfigureAwait(false));
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

        var restored = new List<string>();
        foreach (var entry in backup.Entries)
        {
            if (entry.WasStatic && entry.Addresses.Count > 0)
            {
                await RunNetshAsync(
                    ["interface", "ipv4", "set", "dnsservers", $"name={entry.Alias}", "source=static",
                     $"address={entry.Addresses[0]}", "validate=no"],
                    cancellationToken).ConfigureAwait(false);

                for (var i = 1; i < entry.Addresses.Count; i++)
                {
                    await RunNetshAsync(
                        ["interface", "ipv4", "add", "dnsservers", $"name={entry.Alias}",
                         $"address={entry.Addresses[i]}", $"index={i + 1}", "validate=no"],
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // DHCP'ye don. Adresleri static yazmak yanlis olurdu: baska bir aga
                // baglandiginda eski ag gecidinin DNS'ine sabitlenmis kalirdi.
                await RunNetshAsync(
                    ["interface", "ipv4", "set", "dnsservers", $"name={entry.Alias}", "source=dhcp"],
                    cancellationToken).ConfigureAwait(false);
            }

            restored.Add(entry.Alias);
        }

        // Yedek ancak geri yukleme bittikten SONRA siliniyor. Once silinseydi ve
        // geri yukleme yarida kalsaydi, kullanicinin donebilecegi bir kayit kalmazdi.
        File.Delete(BackupPath);

        return restored;
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

    /// <summary>Varsayilan ag gecidi olan, calisan IPv4 arayuzleri.</summary>
    private static List<NetworkInterface> GetActiveInterfaces()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(n => n.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                          && !g.Address.Equals(System.Net.IPAddress.Any)))
            .ToList();

    private static DnsBackupEntry Capture(NetworkInterface nic)
    {
        var addresses = nic.GetIPProperties().DnsAddresses
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .ToList();

        return new DnsBackupEntry
        {
            Alias = nic.Name,
            Guid = nic.Id,
            WasStatic = IsStaticallyConfigured(nic.Id),
            Addresses = addresses,
        };
    }

    /// <summary>
    /// Arayuzun DNS'i elle mi ayarlanmis. Kayit defterindeki NameServer degeri
    /// doluysa static, bossa DHCP'den geliyor.
    /// </summary>
    /// <remarks>
    /// .NET API'si bu ayrimi vermiyor; <c>DnsAddresses</c> her iki durumda da ayni
    /// gorunuyor. Kayit defteri bunu ayirt edebilecegimiz tek yer.
    /// </remarks>
    private static bool IsStaticallyConfigured(string interfaceGuid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceGuid}");

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
