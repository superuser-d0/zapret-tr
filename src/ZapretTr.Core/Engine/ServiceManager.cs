using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Kurulu servislerin durumu.</summary>
/// <param name="WinwsInstalled">winws servisi kurulu mu.</param>
/// <param name="DnsInstalled">dnscrypt-proxy servisi kurulu mu.</param>
public sealed record ServiceStatus(bool WinwsInstalled, bool DnsInstalled)
{
    public bool AnyInstalled => WinwsInstalled || DnsInstalled;
}

/// <summary>
/// ZapretTR'yi Windows servisi olarak kurar ve kaldirir.
/// </summary>
/// <remarks>
/// Servisler winws.exe ve dnscrypt-proxy.exe'yi DOGRUDAN calistirir; araya
/// ZapretTR uygulamasi girmez. Boylece koruma, arayuz hic acilmasa da acilista
/// devrede oluyor -- kullanicinin istedigi sey de bu: "bir kere ayarla, unut".
///
/// DNS yonlendirmesi servis kuruldugunda <see cref="DnsBackupOwner.Service"/>
/// sahipligiyle yapiliyor. Bu ayrim onemli: uygulama kapanirken kendi yaptigi
/// yonlendirmeyi geri alir ama servisinkine dokunmaz. Dokunsaydi kullanici
/// "otomatik baslatmayi kurdum" der, uygulamayi kapatir ve DNS eski haline
/// donerdi.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ServiceManager
{
    /// <summary>winws'i calistiran servisin adi.</summary>
    public const string WinwsServiceName = "ZapretTR";

    /// <summary>dnscrypt-proxy'yi calistiran servisin adi.</summary>
    public const string DnsServiceName = "ZapretTR-DNS";

    /// <summary>Hangi servislerin kurulu oldugunu doner.</summary>
    public static async Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => new(
            await ExistsAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false),
            await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Servisleri kurar ve baslatir.
    /// </summary>
    /// <param name="vendor">Ikililerin yeri.</param>
    /// <param name="winwsArguments">winws'e verilecek argumanlar.</param>
    /// <param name="includeDns">Sifreli DNS servisi de kurulsun mu.</param>
    /// <remarks>
    /// Once varsa eskiler kaldirilir: ayni adla ikinci kez kurmaya calismak
    /// hata verir ve kullanici "ayari degistirdim ama eskisi calisiyor" durumunda
    /// kalirdi.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> InstallAsync(
        VendorPaths vendor,
        IReadOnlyList<string> winwsArguments,
        bool includeDns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(winwsArguments);
        ElevationGuard.EnsureElevated();

        if (winwsArguments.Count == 0)
        {
            throw new ArgumentException(
                "Servis bos arguman listesiyle kurulamaz.", nameof(winwsArguments));
        }

        var steps = new List<CleanupStep>();

        // Ayni adla ikinci kurulum hata verir; once temizle.
        await UninstallAsync(restoreDns: false, cancellationToken).ConfigureAwait(false);

        // --- winws servisi ---
        // sc, binPath icindeki tirnaklari kendi ayristirdigi icin ic tirnaklar
        // kacisli yaziliyor; upstream'in service_create.cmd dosyasi da boyle yapiyor.
        var winwsBin = $"\"{vendor.WinwsExe}\" {string.Join(' ', winwsArguments.Select(QuoteIfNeeded))}";
        steps.Add(await CreateServiceAsync(
            WinwsServiceName, winwsBin, "ZapretTR DPI atlatma", cancellationToken).ConfigureAwait(false));

        // --- dnscrypt servisi ---
        if (includeDns)
        {
            var configPath = Path.Combine(
                Path.GetDirectoryName(vendor.DnsCryptExe)!, "zapret-tr-dnscrypt.toml");

            if (!File.Exists(configPath))
            {
                steps.Add(new CleanupStep("Sifreli DNS servisi", false,
                    "Yapilandirma dosyasi yok; once uygulamadan bir kez baslatin."));
            }
            else
            {
                var dnsBin = $"\"{vendor.DnsCryptExe}\" -config \"{configPath}\"";
                steps.Add(await CreateServiceAsync(
                    DnsServiceName, dnsBin, "ZapretTR sifreli DNS", cancellationToken).ConfigureAwait(false));

                // Servis DNS'i devraliyor: sahiplik "service" olarak isaretleniyor
                // ki uygulama kapanirken geri almasin.
                try
                {
                    var changed = await SystemDnsManager
                        .RedirectToLocalAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);
                    steps.Add(new CleanupStep("Sistem DNS'i servise yonlendirildi", true,
                        string.Join(", ", changed)));
                }
                catch (Exception ex)
                {
                    steps.Add(new CleanupStep("Sistem DNS'i yonlendirilemedi", false, ex.Message));
                }
            }
        }

        return steps;
    }

    /// <summary>
    /// Servisleri durdurur ve siler.
    /// </summary>
    /// <param name="restoreDns">
    /// Servisin yaptigi DNS yonlendirmesi de geri alinsin mi. Yeniden kurulum
    /// oncesi temizlikte false verilir; kullanici otomatik baslatmayi kapattiginda
    /// true.
    /// </param>
    public static async Task<IReadOnlyList<CleanupStep>> UninstallAsync(
        bool restoreDns = true, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var steps = new List<CleanupStep>();

        foreach (var name in new[] { WinwsServiceName, DnsServiceName })
        {
            if (!await ExistsAsync(name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await RunScAsync(["stop", name], cancellationToken).ConfigureAwait(false);
            var (exitCode, output) = await RunScAsync(["delete", name], cancellationToken).ConfigureAwait(false);

            steps.Add(exitCode == 0
                ? new CleanupStep($"{name} servisi kaldirildi", true)
                : new CleanupStep($"{name} servisi kaldirilamadi", false, output.Trim()));
        }

        if (restoreDns && SystemDnsManager.IsOwnedByService)
        {
            try
            {
                var restored = await SystemDnsManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
                steps.Add(new CleanupStep("Sistem DNS'i geri alindi", true, string.Join(", ", restored)));
            }
            catch (Exception ex)
            {
                steps.Add(new CleanupStep("Sistem DNS'i geri alinamadi", false, ex.Message));
            }
        }

        return steps;
    }

    private static async Task<CleanupStep> CreateServiceAsync(
        string name, string binPath, string displayName, CancellationToken cancellationToken)
    {
        // sc'nin bicimi katidir: "binPath=" ile degerin ARASINDA bosluk olmali.
        var (createCode, createOutput) = await RunScAsync(
            ["create", name, "binPath=", binPath, "DisplayName=", displayName, "start=", "auto"],
            cancellationToken).ConfigureAwait(false);

        if (createCode != 0)
        {
            return new CleanupStep($"{name} servisi kurulamadi", false, createOutput.Trim());
        }

        var (startCode, startOutput) = await RunScAsync(["start", name], cancellationToken)
            .ConfigureAwait(false);

        return startCode == 0
            ? new CleanupStep($"{name} servisi kuruldu ve baslatildi", true)
            : new CleanupStep($"{name} servisi kuruldu ama baslatilamadi", false, startOutput.Trim());
    }

    private static async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["qc", name], cancellationToken).ConfigureAwait(false);

        // 1060 = "belirtilen servis yuklu degil".
        return exitCode == 0 && !output.Contains("1060", StringComparison.Ordinal);
    }

    /// <summary>Icinde bosluk olan argumani tirnaklar; digerlerine dokunmaz.</summary>
    private static string QuoteIfNeeded(string argument)
        => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;

    private static async Task<(int ExitCode, string Output)> RunScAsync(
        string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
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
                return (-1, "sc.exe baslatilamadi.");
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
