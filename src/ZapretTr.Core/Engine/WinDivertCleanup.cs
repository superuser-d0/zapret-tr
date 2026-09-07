using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Bir temizlik adiminin sonucu. Arayuzde tek tek gosterilir.</summary>
public sealed record CleanupStep(string Description, bool Succeeded, string? Detail = null);

/// <summary>
/// WinDivert surucusunu ve ZapretTR'nin biraktigi izleri kaldirir.
/// </summary>
/// <remarks>
/// "Tum ayarlari sifirla" dugmesinin arkasindaki is. Yikici oldugu icin davranisi
/// sabit ve acik tutuldu: ne yaptigi adim adim raporlanir.
///
/// DNS ayari EN BASTA geri alinir. Bu dugmeye basan kullanici cogu zaman "bir seyler
/// bozuldu" diye geliyor; sifreli DNS acikken uygulama duzgun kapanmadiysa sistem
/// hala 127.0.0.1'i gosteriyor ve hicbir ad cozulmuyor olabilir. O durumda once
/// ad cozumu duzelmeli, diger adimlar beklesin. Kullanicinin KENDI koydugu DNS
/// ayarina dokunulmaz -- yalnizca bizim yaptigimiz degisiklik, diskteki yedekten
/// geri yuklenir.
///
/// Surucu kaldirma adimlari upstream'in kendi windivert_delete.cmd dosyasiyla ayni:
/// sc stop windivert, sc delete windivert.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WinDivertCleanup
{
    /// <summary>ZapretTR'nin kurdugu Windows servisinin adi.</summary>
    public const string ServiceName = "ZapretTR";

    /// <summary>WinDivert surucusunun servis adi.</summary>
    public const string DriverServiceName = "windivert";
    /// <summary>
    /// Kaldirilacak SURUCU servis adlarinin tamami.
    /// </summary>
    /// <remarks>
    /// Yalnizca "windivert" YETMIYOR. WinDivert'i baska araclar da kuruyor ve farkli
    /// servis adlari birakiyor: "WinDivert14" (WinDivert 1.4 ve GoodbyeDPI'in kullandigi
    /// ad) ve bazi dagitimlarda "monkey". Bunlardan biri geride kalmis ve surucusu hala
    /// cekirdege yukluyse winws kendi surucusunu yukleyemiyor ve BUTUN adaylar ayni
    /// sekilde basarisiz oluyor -- disaridan "hicbir strateji calismadi" gibi gorunuyor.
    ///
    /// Bu liste, Zapret Win TR (Ali Mali) projesinin temizlik adimlarindan ogrenildi;
    /// orada da tam olarak bu uc ad sokuluyor. GoodbyeDPI Turkiye'de ayni is icin cok
    /// yaygin, dolayisiyla bu kalintinin gercekten bulunma ihtimali yuksek.
    /// </remarks>
    public static readonly string[] DriverServiceNames = ["windivert", "WinDivert14", "monkey"];

    /// <summary>
    /// Ayni anda calisan ve WinDivert'i ele geciren baska bir DPI atlatma araci var mi.
    /// Bulunanlarin surec adlarini doner; bos liste = temiz.
    /// </summary>
    /// <remarks>
    /// Neden gerekli: WinDivert'i ayni anda iki arac kullanamiyor. GoodbyeDPI acikken
    /// winws paketleri goremiyor ve BUTUN adaylar ayni sekilde dusuyor. Kullanicinin
    /// gordugu sey "176 aday denendi, hicbiri calismadi" oluyor -- yani stratejilerin
    /// hepsi kotu saniliyor, oysa olcum hic yapilamamis.
    ///
    /// GoodbyeDPI Turkiye'de tam olarak ayni is icin cok yaygin, dolayisiyla bu
    /// carpisma teorik degil. Zapret Win TR de acilista bu kontrolu yapiyor.
    ///
    /// Burada SUREC OLDURULMUYOR: baska bir aracin kapatilmasi kullanicinin karari.
    /// Yapilan tek sey durumu gorunur kilmak.
    /// </remarks>
    public static IReadOnlyList<string> DetectConflictingTools()
    {
        string[] known = ["goodbyedpi", "ciadpi", "spoofdpi", "zapret", "winws2"];

        var found = new List<string>();
        foreach (var name in known)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    found.Add(name + ".exe");
                }
            }
            catch (Exception)
            {
                // Surec listesi okunamiyorsa teshis ugruna akisi durdurmuyoruz.
            }
        }

        return found;
    }

    /// <summary>Yapilandirmanin tutuldugu dizin.</summary>
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ZapretTR");

    /// <summary>
    /// Sifirlamayi yurutur ve her adimin sonucunu dondurur.
    /// </summary>
    /// <param name="removeConfig">
    /// Yapilandirmayi ve ogrenilmis agirliklari da silsin mi. false verilirse yalnizca
    /// calisan seyler durdurulur -- "internetim bozuldu" durumundan cikmak icin
    /// kullanicinin profil secimlerini kaybetmesi gerekmiyor.
    /// </param>
    public static async Task<IReadOnlyList<CleanupStep>> RunAsync(
        bool removeConfig = true,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<CleanupStep>();

        // DNS EN BASTA geri aliniyor. Sifirlamayi calistiran kullanici cogu zaman
        // "bir seyler bozuldu" diye buraya geliyor; ad cozumu calismiyorsa once o
        // duzelmeli, diger adimlar beklesin.
        steps.Add(await RestoreDnsAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await KillDnsCryptProcessesAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await KillWinwsProcessesAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await RunScAsync("stop", ServiceName, "ZapretTR servisi durduruldu", cancellationToken).ConfigureAwait(false));
        steps.Add(await RunScAsync("delete", ServiceName, "ZapretTR servisi silindi", cancellationToken).ConfigureAwait(false));
        foreach (var driver in DriverServiceNames)
        {
            steps.Add(await RunScAsync("stop", driver, $"{driver} surucusu durduruldu", cancellationToken).ConfigureAwait(false));
            steps.Add(await RunScAsync("delete", driver, $"{driver} surucusu kaldirildi", cancellationToken).ConfigureAwait(false));
        }

        if (removeConfig)
        {
            steps.Add(RemoveConfigDirectory());
        }

        steps.Add(await FlushDnsAsync(cancellationToken).ConfigureAwait(false));

        return steps;
    }

    private static async Task<CleanupStep> RestoreDnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!SystemDnsManager.HasBackup)
            {
                return new CleanupStep("Sistem DNS ayari", true, "degistirilmemis");
            }

            var restored = await SystemDnsManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
            return new CleanupStep("Sistem DNS ayari geri alindi", true, string.Join(", ", restored));
        }
        catch (Exception ex)
        {
            // Bu adimin sessizce gecmesi kabul edilemez: basarisiz olursa
            // kullanicinin ad cozumu calismiyor olabilir ve bunu bilmesi gerekir.
            return new CleanupStep("Sistem DNS ayari geri alinamadi", false, ex.Message);
        }
    }

    private static async Task<CleanupStep> KillDnsCryptProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var processes = Process.GetProcessesByName("dnscrypt-proxy");
            if (processes.Length == 0)
            {
                return new CleanupStep("Calisan dnscrypt-proxy sureci", true, "yok");
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    process.Dispose();
                }
            }

            return new CleanupStep("Calisan dnscrypt-proxy sureci", true, $"{processes.Length} tanesi durduruldu");
        }
        catch (Exception ex)
        {
            return new CleanupStep("Calisan dnscrypt-proxy sureci", false, ex.Message);
        }
    }

    private static async Task<CleanupStep> KillWinwsProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var processes = Process.GetProcessesByName("winws");
            if (processes.Length == 0)
            {
                return new CleanupStep("Calisan winws sureci", true, "yok");
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    process.Dispose();
                }
            }

            return new CleanupStep("Calisan winws sureci", true, $"{processes.Length} tanesi durduruldu");
        }
        catch (Exception ex)
        {
            return new CleanupStep("Calisan winws sureci", false, ex.Message);
        }
    }

    private static async Task<CleanupStep> RunScAsync(
        string verb, string serviceName, string description, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunProcessAsync("sc.exe", [verb, serviceName], cancellationToken)
            .ConfigureAwait(false);

        // 1060 = "belirtilen servis yuklu degil". Sifirlama acisindan bu basaridir:
        // ortada kaldirilacak bir sey yok demek.
        if (exitCode == 0)
        {
            return new CleanupStep(description, true);
        }

        if (output.Contains("1060", StringComparison.Ordinal))
        {
            return new CleanupStep(description, true, "zaten yoktu");
        }

        return new CleanupStep(description, false, output.Trim());
    }

    private static CleanupStep RemoveConfigDirectory()
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                return new CleanupStep("Yapilandirma silindi", true, "zaten yoktu");
            }

            Directory.Delete(ConfigDirectory, recursive: true);
            return new CleanupStep("Yapilandirma silindi", true, ConfigDirectory);
        }
        catch (Exception ex)
        {
            return new CleanupStep("Yapilandirma silindi", false, ex.Message);
        }
    }

    private static async Task<CleanupStep> FlushDnsAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunProcessAsync("ipconfig.exe", ["/flushdns"], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
            ? new CleanupStep("DNS onbellegi temizlendi", true)
            : new CleanupStep("DNS onbellegi temizlendi", false, output.Trim());
    }

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
