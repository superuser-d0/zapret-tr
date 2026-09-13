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
        var dnsYonlendirildi = false;
        var dnsServisiAyakta = false;

        // Ayni adla ikinci kurulum hata verir; once temizle.
        //
        // DNS burada BILEREK geri alinmiyor: yeniden kurulumda yonlendirme yerinde
        // kalir ve yeni servis ayaga kalkinca devam eder, arada kullanicinin
        // engellenen adlari yeniden ISS'e sormasina gerek yok. Ama bunun bedeli
        // metodun SONUNDA odeniyor -- asagidaki "yetim yonlendirme" bloguna bak.
        await UninstallAsync(restoreDns: false, cancellationToken).ConfigureAwait(false);

        // --- winws servisi ---
        // sc, binPath icindeki tirnaklari kendi ayristirdigi icin ic tirnaklar
        // kacisli yaziliyor; upstream'in service_create.cmd dosyasi da boyle yapiyor.
        var winwsBin = $"\"{vendor.WinwsExe}\" {string.Join(' ', winwsArguments.Select(QuoteIfNeeded))}";
        var winwsStep = await CreateServiceAsync(
            WinwsServiceName, winwsBin, "ZapretTR DPI atlatma", cancellationToken).ConfigureAwait(false);

        // "BASLATILDI" DEMEK "CALISIYOR" DEMEK DEGIL.
        //
        // sc start, surec baslar baslamaz basari donuyor. winws ardindan WinDivert
        // surucusunu acamayip olurse servis birkac saniye sonra STOPPED oluyor --
        // yukseltmeden hemen sonra tam olarak bu oluyordu, cunku onceki surumun
        // surucusu cekirdekte yarim birakilmis bir durdurmada kalmisti. Servisin
        // kurtarma tanimi ise ise yaramiyor: ayni surucuye ayni sekilde carpiyor.
        // Kullanicinin gordugu sey "kurulu ama durmus" ve yeniden baslatinca
        // duzelen bir koruma. Surucu burada bosaltilip servis bir kez yeniden
        // baslatiliyor.
        if (winwsStep.Succeeded
            && !await WaitUntilRunningStableAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false))
        {
            var unload = await WinDivertDriver
                .TryUnloadIdleAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

            if (unload.Cleared)
            {
                await RunScAsync(["start", WinwsServiceName], cancellationToken).ConfigureAwait(false);
            }

            winwsStep = await WaitUntilRunningStableAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false)
                ? new CleanupStep($"{WinwsServiceName} servisi kuruldu ve baslatildi", true,
                    "ilk denemede WinDivert surucusu acilamadi; " + unload.Detail)
                : new CleanupStep($"{WinwsServiceName} servisi kuruldu ama CALISMIYOR", false,
                    "winws WinDivert surucusunu acamadi. " + unload.Detail
                    + " Surucuyu kullanan baska bir araci kapatin; olmazsa bilgisayari bir kez yeniden baslatin.");
        }

        steps.Add(winwsStep);

        // --- dnscrypt servisi ---
        if (includeDns)
        {
            string? configPath = null;
            string? configError = null;
            try
            {
                configPath = File.Exists(vendor.DnsCryptExe)
                    ? await DnsCryptRunner.WriteConfigAsync(vendor, cancellationToken).ConfigureAwait(false)
                    : null;
                configError = configPath is null ? "dnscrypt-proxy.exe bulunamadi: " + vendor.DnsCryptExe : null;
            }
            catch (Exception ex)
            {
                configError = "Yapilandirma dosyasi yazilamadi: " + ex.Message;
            }

            if (configPath is null)
            {
                steps.Add(new CleanupStep("Sifreli DNS servisi kurulamadi", false, configError));
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
                    dnsServisiAyakta = true;

                    // Servis DNS'i devraliyor: sahiplik "service" olarak
                    // isaretleniyor ki uygulama kapanirken geri almasin.
                    try
                    {
                        var changed = await SystemDnsManager
                            .RedirectToLocalAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);
                        dnsYonlendirildi = true;
                        SystemDnsManager.ClearSuspended();
                        steps.Add(new CleanupStep("Sistem DNS'i servise yonlendirildi", true,
                            changed.Count == 0 ? "zaten yonlendirilmisti" : string.Join(", ", changed)));
                    }
                    catch (Exception ex)
                    {
                        steps.Add(new CleanupStep("Sistem DNS'i yonlendirilemedi", false, ex.Message));
                    }
                }
            }
        }

        // YETIM YONLENDIRME: ONCEKI SERVISIN DNS'I, YENI SERVIS OLMADAN.
        //
        // Yukaridaki temizlik eski ZapretTR-DNS servisini sildi ama yonlendirmeyi
        // yerinde birakti. Yeni kurulum DNS'i yeniden devralmadiysa -- sifreli DNS
        // bu sefer kapali secildi, yapilandirma dosyasi yok, servis baslamadi ya
        // da cevap vermedi -- sistem DNS'i 127.0.0.1'i gosteriyor ve orada dinleyen
        // KIMSE YOK. Eskiden bu durumda "Sistem DNS'ine DOKUNULMADI" yaziliyordu;
        // cumle dogruydu ama makine hicbir adi cozemiyordu ve durum acilistan
        // acilisa kaliciydi. Tetikleyen yol sik: servis kurulu ama durmussa arayuz
        // "Servis Olarak Yukle"yi yeniden gosteriyor ve kullanici ona basiyor.
        if (!dnsYonlendirildi && SystemDnsManager.IsOwnedByService)
        {
            try
            {
                var restored = await SystemDnsManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
                steps.Add(new CleanupStep("Onceki servisin DNS yonlendirmesi geri alindi", true,
                    string.Join(", ", restored)));
            }
            catch (Exception ex)
            {
                steps.Add(new CleanupStep("Onceki servisin DNS yonlendirmesi geri alinamadi", false, ex.Message));
            }
        }

        // Servis cevap veriyor ama yonlendirme yapilamadiysa (en olasi sebep: o an
        // internete cikan bir kart yoktu) is DNS bekcisine birakiliyor. "Askida"
        // isareti olmadan bekci yedek gormedigi icin hicbir sey yapmaz ve servis
        // kurulu oldugu halde DNS hic yonlendirilmemis kalirdi.
        if (!dnsYonlendirildi)
        {
            if (dnsServisiAyakta)
            {
                SystemDnsManager.MarkSuspended();
            }
            else
            {
                SystemDnsManager.ClearSuspended();
            }
        }

        return steps;
    }

    /// <summary>
    /// Servis birkac saniye boyunca CALISIR kaliyor mu.
    /// </summary>
    /// <remarks>
    /// Tek bakis yetmiyor: winws surucu hatasiyla oldugunde servis bir an RUNNING
    /// gorunup sonra STOPPED'a dusuyor. Ust uste iki olumlu olcum, aralarinda
    /// winws'in surucuyu acmasina yetecek bir sure ariyoruz.
    /// </remarks>
    private static async Task<bool> WaitUntilRunningStableAsync(string name, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
        var arkaArkaya = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);

            if (await IsRunningAsync(name, cancellationToken).ConfigureAwait(false))
            {
                if (++arkaArkaya >= 3)
                {
                    return true;
                }
            }
            else
            {
                var (_, output) = await RunScAsync(["query", name], cancellationToken).ConfigureAwait(false);
                if (output.Contains("STOPPED", StringComparison.Ordinal))
                {
                    return false;
                }

                arkaArkaya = 0;
            }
        }

        return arkaArkaya > 0;
    }

    /// <summary>Sifreli DNS servisinin durumu.</summary>
    /// <param name="Installed">Servis kaydi var mi.</param>
    /// <param name="Running">Servis su an calisiyor mu.</param>
    /// <param name="BinaryExists">
    /// Kayittaki dnscrypt-proxy.exe diskte duruyor mu. Kaydi olup ikilisi olmayan
    /// servis (virusten koruma karantinaya aldi, klasor elle silindi) bir daha
    /// hic calismaz; yonlendirme onu beklememeli.
    /// </param>
    public sealed record DnsServiceState(bool Installed, bool Running, bool BinaryExists);

    /// <summary>Sifreli DNS servisinin kurulu, calisir ve ikilisinin yerinde olup olmadigini doner.</summary>
    public static async Task<DnsServiceState> GetDnsServiceStateAsync(CancellationToken cancellationToken = default)
    {
        var installed = await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false);
        if (!installed)
        {
            return new DnsServiceState(false, false, false);
        }

        var running = await IsRunningAsync(DnsServiceName, cancellationToken).ConfigureAwait(false);
        return new DnsServiceState(true, running, ServiceBinaryExists(DnsServiceName));
    }

    private static bool ServiceBinaryExists(string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            var imagePath = ConflictScanner.ExtractExecutablePath(key?.GetValue("ImagePath") as string);

            // Okunamiyorsa var sayiyoruz: yanlis tarafa dusmek gerekirse, calisan
            // bir kurulumun yonlendirmesini sokmektense beklemek yeglenir.
            return imagePath is null || File.Exists(Environment.ExpandEnvironmentVariables(imagePath));
        }
        catch (Exception)
        {
            return true;
        }
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

        // DNS, COZUMLEYICI SILINMEDEN ONCE GERI ALINIR.
        //
        // Sira eskiden tersti: once iki servis de siliniyor, sonra DNS geri
        // aliniyordu. Geri alma basarisiz olursa (netsh hatasi, bozuk yedek)
        // sistem DNS'i 127.0.0.1'de kaliyor ve orada dinleyen servis az once
        // silinmis oluyordu -- kaldirmanin birakabilecegi en kotu tablo. Simdi
        // geri alma basarisizsa sifreli DNS servisi YERINDE birakiliyor: koruma
        // kapanmamis olur ama internet de gitmez, ve bir sonraki deneme ayni
        // yedekle yeniden yapilabilir.
        var dnsServisiniBirak = false;

        // Sahibi okunamayan (bozuk) yedek de geri aliniyor: servis silindikten
        // sonra onu geri alacak kimse kalmaz. Yalnizca ACIKCA uygulamaya ait
        // yonlendirmeye dokunulmuyor -- o calisan uygulamanin kendi isi.
        if (restoreDns
            && SystemDnsManager.HasBackup
            && !string.Equals(SystemDnsManager.BackupOwner, DnsBackupOwner.App, StringComparison.Ordinal))
        {
            try
            {
                var restored = await SystemDnsManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
                steps.Add(new CleanupStep("Sistem DNS'i geri alindi", true,
                    restored.Count == 0 ? "degistirilecek kart kalmamisti" : string.Join(", ", restored)));
            }
            catch (Exception ex)
            {
                dnsServisiniBirak = true;
                steps.Add(new CleanupStep("Sistem DNS'i geri alinamadi", false,
                    ex.Message + " Sifreli DNS servisi, internet kesilmesin diye KALDIRILMADI."));
            }
        }

        if (restoreDns)
        {
            SystemDnsManager.ClearSuspended();
        }

        foreach (var name in new[] { WinwsServiceName, DnsServiceName })
        {
            if (name == DnsServiceName && dnsServisiniBirak)
            {
                continue;
            }

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
