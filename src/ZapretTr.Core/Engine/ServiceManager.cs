using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Kurulu servislerin durumu.</summary>
/// <param name="WinwsInstalled">winws servisi kurulu mu.</param>
/// <param name="DnsInstalled">dnscrypt-proxy servisi kurulu mu.</param>
/// <param name="WinwsRunning">
/// Servis su anda CALISIYOR mu. Kurulu olmak calisiyor olmak demek degil.
/// </param>
public sealed record ServiceStatus(
    bool WinwsInstalled, bool DnsInstalled, bool WinwsRunning = false)
{
    public bool AnyInstalled => WinwsInstalled || DnsInstalled;

    /// <summary>
    /// Servis kurulu ama CALISMIYOR: koruma yok, ve kullanicinin haberi olmali.
    /// </summary>
    /// <remarks>
    /// En tehlikeli durum bu. "Kurulu" ile "calisiyor" ayni sey sayildiginda
    /// arayuz "servis modu aktif" deyip Baslat dugmesini kapatiyordu; koruma
    /// yoktu ve kullanicinin yapabilecegi bir sey de yoktu. Gercek bir
    /// kullanicida 0.1.9'dan 0.1.15'e yukseltmeden sonra yasandi.
    /// </remarks>
    public bool InstalledButStopped => WinwsInstalled && !WinwsRunning;
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

    /// <summary>Servislerin kurulu VE calisir olup olmadigini doner.</summary>
    /// <remarks>
    /// "Kurulu" ile "calisiyor" ayri sorular ve ikisini birbirine karistirmak
    /// kullaniciyi kilitliyordu: yukseltmede servis geri kuruluyor ama
    /// <c>sc start</c> basarisiz olursa servis VAR ama DURMUS kaliyor. Arayuz
    /// bunu "servis modu aktif" diye okuyup Baslat dugmesini kapatiyordu --
    /// koruma yok, kullanicinin yapabilecegi de bir sey yok. Gercek bir
    /// kullanicida 0.1.9'dan 0.1.15'e yukseltmeden sonra yasandi.
    /// </remarks>
    public static async Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => new(
            await ExistsAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false),
            await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false),
            await IsRunningAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false));

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
                var dnsStep = await CreateServiceAsync(
                    DnsServiceName, dnsBin, "ZapretTR sifreli DNS", cancellationToken).ConfigureAwait(false);

                steps.Add(dnsStep);

                // SISTEM DNS'I, COZUMLEYICININ GERCEKTEN CEVAP VERDIGI
                // DOGRULANMADAN CEVRILMEZ.
                //
                // Bu kontrol uygulamanin kendi yolunda (DnsCryptRunner.StartAsync)
                // bastan beri vardi, servis yolunda YOKTU: servis kurulur kurulmaz
                // DNS 127.0.0.1'e ceviriliyordu -- "sc start" dusse bile. Sonuc,
                // projedeki en kotu tablo: 127.0.0.1'i dinleyen kimse yok, makine
                // hicbir adi cozemiyor, yani kullaniciya gore internet tamamen
                // gitti. Ustelik bu yol acilistan acilista kalici: uygulama
                // acilmadigi surece kurtarma (RecoverDnsIfNeeded) hic kosmuyor,
                // dolayisiyla kullanici bilgisayari yeniden baslatinca durum
                // duzelmiyor, PEKISIYOR.
                //
                // Cevap gelmiyorsa servis geri sokuluyor: acilista her seferinde
                // ayaga kalkip DNS'i kapmaya calisan olu bir servis birakmak,
                // hic kurmamaktan kotu.
                if (!dnsStep.Succeeded)
                {
                    await RunScAsync(["delete", DnsServiceName], cancellationToken).ConfigureAwait(false);
                    steps.Add(new CleanupStep("Sistem DNS'i YONLENDIRILMEDI", false,
                        "Sifreli DNS servisi baslatilamadi. DNS'i yine de 127.0.0.1'e cevirmek " +
                        "makineyi hicbir adi cozemez halde birakirdi; koruma winws ile devam ediyor."));
                }
                else if (!await WaitForLocalResolverAsync(
                             LocalResolverStartupTimeout, cancellationToken).ConfigureAwait(false))
                {
                    await RunScAsync(["stop", DnsServiceName], cancellationToken).ConfigureAwait(false);
                    await RunScAsync(["delete", DnsServiceName], cancellationToken).ConfigureAwait(false);

                    steps.Add(new CleanupStep("Sistem DNS'i YONLENDIRILMEDI", false,
                        $"Sifreli DNS servisi kuruldu ama {LocalResolverStartupTimeout.TotalSeconds:F0} " +
                        "saniyede DNS sorgularina cevap vermedi; servis geri sokuldu. " +
                        "Sistem DNS ayarina DOKUNULMADI. Koruma winws ile devam ediyor."));
                }
                else
                {
                    // Servis DNS'i devraliyor: sahiplik "service" olarak
                    // isaretleniyor ki uygulama kapanirken geri almasin.
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

    /// <summary>
    /// winws servisini GECICI olarak durdurur; servis kaydina dokunmaz.
    /// </summary>
    /// <returns>
    /// Servis durdu ve ortalikta winws sureci kalmadiysa true. Sure dolduysa false:
    /// cagiran taraf olcumun kirlenebilecegini kullaniciya soylemeli.
    /// </returns>
    /// <remarks>
    /// Parametre testi winws'i aday aday kendisi baslatiyor. Servisin winws'i
    /// arkada calisirken bu iki sekilde bozuluyordu: mevcut durum taramasi
    /// engeli servisin stratejisi ACIKKEN olcuyor ve "engel yok" goruyor, adaylar
    /// ise ayni filtreyle ikinci ornek olarak baslatilamiyor. Uygulama kendi
    /// baslattigi winws'i testten once durduruyordu, servisinkini durdurmuyordu.
    ///
    /// <c>sc stop</c> yalnizca istegi iletir ve hemen doner; servisin gercekten
    /// STOPPED olmasi ve surecin surucuyu birakmasi ayrica bekleniyor. Temiz bir
    /// durdurma servis kurtarma tanimini (<c>sc failure</c>) tetiklemez, yani
    /// servis test sirasinda kendiliginden geri gelmez.
    /// </remarks>
    public static async Task<bool> StopWinwsServiceAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        await RunScAsync(["stop", WinwsServiceName], cancellationToken).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var (_, output) = await RunScAsync(["query", WinwsServiceName], cancellationToken)
                .ConfigureAwait(false);

            if (IsStoppedQueryOutput(output) && !IsAnyWinwsProcessAlive())
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="StopWinwsServiceAsync"/> ile durdurulan winws servisini yeniden baslatir.
    /// </summary>
    public static async Task<CleanupStep> StartWinwsServiceAsync(CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var (exitCode, output) = await RunScAsync(["start", WinwsServiceName], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
            ? new CleanupStep($"{WinwsServiceName} servisi yeniden baslatildi", true)
            : new CleanupStep($"{WinwsServiceName} servisi yeniden baslatilamadi", false, output.Trim());
    }

    /// <summary><c>sc query</c> ciktisi servisin durmus oldugunu mu soyluyor.</summary>
    /// <remarks>
    /// STOP_PENDING durmus SAYILMAZ: o anda surec hala surucuyu tutuyor olabilir.
    /// Servis hic yoksa (1060) da durmus sayilir -- beklenecek bir sey kalmamistir.
    /// </remarks>
    public static bool IsStoppedQueryOutput(string scQueryOutput)
        => scQueryOutput.Contains("STOPPED", StringComparison.Ordinal)
           || scQueryOutput.Contains("1060", StringComparison.Ordinal);

    private static bool IsAnyWinwsProcessAlive()
    {
        try
        {
            return Process.GetProcessesByName("winws").Length > 0;
        }
        catch (Exception)
        {
            // Surec listesi okunamiyorsa servis durumuna guveniyoruz.
            return false;
        }
    }

    /// <summary>
    /// Sifreli DNS servisinin cevap vermesi icin taninan sure.
    /// </summary>
    /// <remarks>
    /// Uygulamanin kendi yolundan (15 sn) daha uzun tutuldu: servis LocalSystem
    /// olarak, kullanici oturumundan bagimsiz aciliyor ve dnscrypt-proxy once
    /// cozumleyici listesini cekmek zorunda kalabiliyor.
    /// </remarks>
    private static readonly TimeSpan LocalResolverStartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>127.0.0.1:53 cevap verene kadar bekler; sure dolarsa false.</summary>
    private static async Task<bool> WaitForLocalResolverAsync(
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await DnsCryptRunner
                    .IsLocalResolverRespondingAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
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

        // OLURSE KENDILIGINDEN GERI GELSIN.
        //
        // Acilista servisler agdan once ayaga kalkabiliyor; winws surucuyu
        // acamayip ya da dnscrypt ag bulamayip hemen olurse, kurtarma tanimi
        // olmadan bir daha HIC baslamiyor. Kullanicinin gordugu sey tam olarak
        // "kurdum, yeniden baslattim, calismiyor" oluyor -- servis listede
        // duruyor ama durmus. Uc kademeli yeniden deneme bu pencereyi kapatiyor.
        //
        // En iyi cabayla: basarisiz olursa adim listesine yazilmiyor. Kurulumun
        // kendisi basarili ve bu yalnizca dayaniklilik; burada bir hata
        // dondurmek kurulum sonunda gereksiz bir uyari penceresi acardi.
        await RunScAsync(
            ["failure", name, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000"],
            cancellationToken).ConfigureAwait(false);

        var (startCode, startOutput) = await RunScAsync(["start", name], cancellationToken)
            .ConfigureAwait(false);

        return startCode == 0
            ? new CleanupStep($"{name} servisi kuruldu ve baslatildi", true)
            : new CleanupStep($"{name} servisi kuruldu ama baslatilamadi", false, startOutput.Trim());
    }

    /// <summary>Servis su anda CALISIYOR mu.</summary>
    /// <remarks>
    /// <c>sc query</c> ciktisindaki STATE satirina bakiliyor. Kurulu olmak
    /// calisiyor olmak demek degil: servis durdurulmus, baslatilamamis ya da
    /// coktukten sonra yeniden baslatilmamis olabilir.
    /// </remarks>
    private static async Task<bool> IsRunningAsync(string name, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["query", name], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
               && output.Contains("RUNNING", StringComparison.Ordinal);
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
