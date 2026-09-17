using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Kurulu servislerin durumu.</summary>
/// <param name="WinwsInstalled">winws servisi kurulu mu.</param>
/// <param name="DnsInstalled">dnscrypt-proxy servisi kurulu mu.</param>
/// <param name="WinwsRunning">
/// Servis şu anda ÇALIŞIYOR mu. Kurulu olmak çalışıyor olmak demek değil.
/// </param>
/// <param name="WinwsPaused">
/// Servis kullanıcı tarafından DURAKLATILDI mı: kurulu, açılışta başlamayacak
/// şekilde ayarlı ve çalışmıyor. Bkz. <see cref="ServiceManager.PauseAsync"/>.
/// </param>
public sealed record ServiceStatus(
    bool WinwsInstalled, bool DnsInstalled, bool WinwsRunning = false, bool WinwsPaused = false)
{
    public bool AnyInstalled => WinwsInstalled || DnsInstalled;

    /// <summary>
    /// Servis kurulu ama ÇALIŞMIYOR: koruma yok ve kullanıcının haberi olmalı.
    /// </summary>
    /// <remarks>
    /// En tehlikeli durum bu. "Kurulu" ile "çalışıyor" aynı şey sayıldığında
    /// arayüz "SERVİS MODU AKTİF" deyip Başlat düğmesini kapatıyordu; koruma
    /// yoktu ve kullanıcının yapabileceği bir şey de yoktu. Gerçek bir
    /// kullanıcıda 0.1.9'dan 0.1.15'e yükseltmeden sonra yaşandı.
    ///
    /// Kullanıcının kendi duraklattığı servis bu sayılmaz: orada koruma BİLEREK
    /// kapalı ve "servis durmuş" uyarısı yanlış alarm olurdu.
    /// </remarks>
    public bool InstalledButStopped => WinwsInstalled && !WinwsRunning && !WinwsPaused;
}

/// <summary>
/// ZapretTR'yi Windows servisi olarak kurar ve kaldırır.
/// </summary>
/// <remarks>
/// Servisler winws.exe ve dnscrypt-proxy.exe'yi DOĞRUDAN çalıştırır; araya
/// ZapretTR uygulaması girmez. Böylece koruma, arayüz hiç açılmasa da açılışta
/// devrede oluyor; kullanıcının istediği şey de bu: "bir kere ayarla, unut".
///
/// DNS yönlendirmesi servis kurulduğunda <see cref="DnsBackupOwner.Service"/>
/// sahipliğiyle yapılıyor. Bu ayrım önemli: uygulama kapanırken kendi yaptığı
/// yönlendirmeyi geri alır ama servisinkine dokunmaz. Dokunsaydı kullanıcı
/// "otomatik başlatmayı kurdum" der, uygulamayı kapatır ve DNS eski hâline
/// dönerdi.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ServiceManager
{
    /// <summary>winws'i çalıştıran servisin adı.</summary>
    public const string WinwsServiceName = "ZapretTR";

    /// <summary>dnscrypt-proxy'yi çalıştıran servisin adı.</summary>
    public const string DnsServiceName = "ZapretTR-DNS";

    /// <summary>Servislerin kurulu VE çalışır olup olmadığını döner.</summary>
    /// <remarks>
    /// "Kurulu" ile "çalışıyor" ayrı sorular ve ikisini birbirine karıştırmak
    /// kullanıcıyı kilitliyordu: yükseltmede servis geri kuruluyor ama
    /// <c>sc start</c> başarısız olursa servis VAR ama DURMUŞ kalıyor. Arayüz
    /// bunu "SERVİS MODU AKTİF" diye okuyup Başlat düğmesini kapatıyordu;
    /// koruma yok, kullanıcının yapabileceği de bir şey yok. Gerçek bir
    /// kullanıcıda 0.1.9'dan 0.1.15'e yükseltmeden sonra yaşandı.
    /// </remarks>
    public static async Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var (winwsInstalled, winwsQc) = await QueryConfigAsync(WinwsServiceName, cancellationToken)
            .ConfigureAwait(false);
        var dnsInstalled = await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false);
        var running = await IsRunningAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false);

        return new ServiceStatus(
            winwsInstalled, dnsInstalled, running,
            WinwsPaused: winwsInstalled && !running && IsDemandStartQcOutput(winwsQc));
    }

    /// <summary><c>sc qc</c> çıktısı servisin açılışta KENDİLİĞİNDEN başlamayacağını mı söylüyor.</summary>
    /// <remarks>
    /// Duraklatmanın diskteki izi bu: <see cref="PauseAsync"/> servisi "demand"
    /// başlangıcına çeviriyor. Durum ayrı bir dosyada tutulmuyor, çünkü servisin
    /// kendi ayarı zaten gerçeğin ta kendisi; dosya ile servis ayrışabilirdi.
    /// </remarks>
    public static bool IsDemandStartQcOutput(string scQcOutput)
        => scQcOutput.Contains("DEMAND_START", StringComparison.Ordinal);

    /// <summary>
    /// Servisleri kurar ve başlatır.
    /// </summary>
    /// <param name="vendor">İkililerin yeri.</param>
    /// <param name="winwsArguments">winws'e verilecek argümanlar.</param>
    /// <param name="includeDns">Şifreli DNS servisi de kurulsun mu.</param>
    /// <param name="startPaused">
    /// Servisler DURAKLATILMIŞ olarak kurulsun mu: kayıt yazılır ama hiçbiri
    /// başlatılmaz ve sistem DNS'ine dokunulmaz. Yükseltme bunu kullanıyor;
    /// kullanıcı VPN için duraklattıysa güncelleme korumayı habersizce geri
    /// açmamalı.
    /// </param>
    /// <remarks>
    /// Önce varsa eskiler kaldırılır: aynı adla ikinci kez kurmaya çalışmak
    /// hata verir ve kullanıcı "ayarı değiştirdim ama eskisi çalışıyor" durumunda
    /// kalırdı.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> InstallAsync(
        VendorPaths vendor,
        IReadOnlyList<string> winwsArguments,
        bool includeDns,
        CancellationToken cancellationToken = default,
        bool startPaused = false)
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

        // Aynı adla ikinci kurulum hata verir; önce temizle.
        //
        // DNS burada BİLEREK geri alınmıyor: yeniden kurulumda yönlendirme yerinde
        // kalır ve yeni servis ayağa kalkınca devam eder, arada kullanıcının
        // engellenen adları yeniden İSS'e sormasına gerek yok. Ama bunun bedeli
        // metodun SONUNDA ödeniyor; aşağıdaki "yetim yönlendirme" bloğuna bak.
        await UninstallAsync(restoreDns: false, cancellationToken).ConfigureAwait(false);

        // --- winws servisi ---
        // sc, binPath içindeki tırnakları kendi ayrıştırdığı için iç tırnaklar
        // kaçışlı yazılıyor; upstream'in service_create.cmd dosyası da böyle yapıyor.
        var winwsBin = $"\"{vendor.WinwsExe}\" {string.Join(' ', winwsArguments.Select(QuoteIfNeeded))}";
        var winwsStep = await CreateServiceAsync(
            WinwsServiceName, winwsBin, "ZapretTR DPI atlatma", start: !startPaused, cancellationToken)
            .ConfigureAwait(false);

        if (!startPaused)
        {
            winwsStep = await EnsureWinwsRunningAsync(
                winwsStep, "kuruldu ve baslatildi", "kuruldu ama CALISMIYOR", cancellationToken).ConfigureAwait(false);
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
                    DnsServiceName, dnsBin, "ZapretTR sifreli DNS", start: !startPaused, cancellationToken)
                    .ConfigureAwait(false);

                steps.Add(dnsStep);

                // SİSTEM DNS'İ, ÇÖZÜMLEYİCİNİN GERÇEKTEN CEVAP VERDİĞİ
                // DOĞRULANMADAN ÇEVRİLMEZ.
                //
                // Bu kontrol uygulamanın kendi yolunda (DnsCryptRunner.StartAsync)
                // baştan beri vardı, servis yolunda YOKTU: servis kurulur kurulmaz
                // DNS 127.0.0.1'e çevriliyordu; "sc start" düşse bile. Sonuç,
                // projedeki en kötü tablo: 127.0.0.1'i dinleyen kimse yok, makine
                // hiçbir adı çözemiyor, yani kullanıcıya göre internet tamamen
                // gitti. Üstelik bu yol açılıştan açılışa kalıcı: uygulama
                // açılmadığı sürece kurtarma (RecoverDnsIfNeeded) hiç koşmuyor,
                // dolayısıyla kullanıcı bilgisayarı yeniden başlatınca durum
                // düzelmiyor, PEKİŞİYOR.
                //
                // Cevap gelmiyorsa servis geri sökülüyor: açılışta her seferinde
                // ayağa kalkıp DNS'i kapmaya çalışan ölü bir servis bırakmak,
                // hiç kurmamaktan kötü.
                if (!dnsStep.Succeeded)
                {
                    await RunScAsync(["delete", DnsServiceName], cancellationToken).ConfigureAwait(false);
                    steps.Add(new CleanupStep("Sistem DNS'i YONLENDIRILMEDI", false,
                        "Sifreli DNS servisi baslatilamadi. DNS'i yine de 127.0.0.1'e cevirmek " +
                        "makineyi hicbir adi cozemez halde birakirdi; koruma winws ile devam ediyor."));
                }
                else if (startPaused)
                {
                    // Duraklatılmış kurulum: çözümleyici çalışmıyor, DNS'e dokunulmaz.
                    // Aşağıdaki yetim yönlendirme bloğu önceki servisin yönlendirmesini
                    // de geri alıyor.
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

                    // Servis DNS'i devralıyor: sahiplik "service" olarak
                    // işaretleniyor ki uygulama kapanırken geri almasın.
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

        // YETİM YÖNLENDİRME: ÖNCEKİ SERVİSİN DNS'İ, YENİ SERVİS OLMADAN.
        //
        // Yukarıdaki temizlik eski ZapretTR-DNS servisini sildi ama yönlendirmeyi
        // yerinde bıraktı. Yeni kurulum DNS'i yeniden devralmadıysa (şifreli DNS
        // bu sefer kapalı seçildi, yapılandırma dosyası yok, servis başlamadı ya
        // da cevap vermedi) sistem DNS'i 127.0.0.1'i gösteriyor ve orada dinleyen
        // KİMSE YOK. Eskiden bu durumda "Sistem DNS'ine DOKUNULMADI" yazılıyordu;
        // cümle doğruydu ama makine hiçbir adı çözemiyordu ve durum açılıştan
        // açılışa kalıcıydı. Tetikleyen yol sık: servis kurulu ama durmuşsa arayüz
        // "Servis Olarak Yükle"yi yeniden gösteriyor ve kullanıcı ona basıyor.
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

        // Servis cevap veriyor ama yönlendirme yapılamadıysa (en olası sebep: o an
        // internete çıkan bir kart yoktu) iş DNS bekçisine bırakılıyor. "Askıda"
        // işareti olmadan bekçi yedek görmediği için hiçbir şey yapmaz ve servis
        // kurulu olduğu hâlde DNS hiç yönlendirilmemiş kalırdı.
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
    /// Başlatma isteği başarılı dönen winws servisinin gerçekten ÇALIŞIR kaldığını doğrular;
    /// kalmadıysa sürücüyü boşaltıp bir kez yeniden dener.
    /// </summary>
    /// <param name="startStep">Başlatma isteğinin sonucu.</param>
    /// <param name="basariEylemi">Başarıda adımda yazacak eylem, örneğin "kuruldu ve başlatıldı".</param>
    /// <param name="basarisizlikEylemi">Başarısızlıkta adımda yazacak eylem.</param>
    /// <remarks>
    /// "BAŞLATILDI" DEMEK "ÇALIŞIYOR" DEMEK DEĞİL.
    ///
    /// sc start, süreç başlar başlamaz başarı dönüyor. winws ardından WinDivert
    /// sürücüsünü açamayıp ölürse servis birkaç saniye sonra STOPPED oluyor;
    /// yükseltmeden hemen sonra tam olarak bu oluyordu, çünkü önceki sürümün
    /// sürücüsü çekirdekte yarım bırakılmış bir durdurmada kalmıştı. Servisin
    /// kurtarma tanımı ise işe yaramıyor: aynı sürücüye aynı şekilde çarpıyor.
    /// Kullanıcının gördüğü şey "kurulu ama durmuş" ve yeniden başlatınca
    /// düzelen bir koruma. Sürücü burada boşaltılıp servis bir kez yeniden
    /// başlatılıyor.
    /// </remarks>
    private static async Task<CleanupStep> EnsureWinwsRunningAsync(
        CleanupStep startStep, string basariEylemi, string basarisizlikEylemi, CancellationToken cancellationToken)
    {
        if (!startStep.Succeeded
            || await WaitUntilRunningStableAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false))
        {
            return startStep;
        }

        var unload = await WinDivertDriver
            .TryUnloadIdleAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

        if (unload.Cleared)
        {
            await RunScAsync(["start", WinwsServiceName], cancellationToken).ConfigureAwait(false);
        }

        return await WaitUntilRunningStableAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false)
            ? new CleanupStep($"{WinwsServiceName} servisi {basariEylemi}", true,
                "ilk denemede WinDivert surucusu acilamadi; " + unload.Detail)
            : new CleanupStep($"{WinwsServiceName} servisi {basarisizlikEylemi}", false,
                "winws WinDivert surucusunu acamadi. " + unload.Detail
                + " Surucuyu kullanan baska bir araci kapatin; olmazsa bilgisayari bir kez yeniden baslatin.");
    }

    /// <summary>
    /// Servis birkaç saniye boyunca ÇALIŞIR kalıyor mu.
    /// </summary>
    /// <remarks>
    /// Tek bakış yetmiyor: winws sürücü hatasıyla öldüğünde servis bir an RUNNING
    /// görünüp sonra STOPPED'a düşüyor. Üst üste iki olumlu ölçüm, aralarında
    /// winws'in sürücüyü açmasına yetecek bir süre arıyoruz.
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

    /// <summary>Şifreli DNS servisinin durumu.</summary>
    /// <param name="Installed">Servis kaydı var mı.</param>
    /// <param name="Running">Servis şu an çalışıyor mu.</param>
    /// <param name="BinaryExists">
    /// Kayıttaki dnscrypt-proxy.exe diskte duruyor mu. Kaydı olup ikilisi olmayan
    /// servis (virüsten koruma karantinaya aldı, klasör elle silindi) bir daha
    /// hiç çalışmaz; yönlendirme onu beklememeli.
    /// </param>
    public sealed record DnsServiceState(bool Installed, bool Running, bool BinaryExists);

    /// <summary>Şifreli DNS servisinin kurulu, çalışır ve ikilisinin yerinde olup olmadığını döner.</summary>
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

            // Okunamıyorsa var sayıyoruz: yanlış tarafa düşmek gerekirse, çalışan
            // bir kurulumun yönlendirmesini sökmektense beklemek yeğlenir.
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
    /// Servisin yaptığı DNS yönlendirmesi de geri alınsın mı. Yeniden kurulum
    /// öncesi temizlikte false verilir; kullanıcı otomatik başlatmayı kapattığında
    /// true.
    /// </param>
    public static async Task<IReadOnlyList<CleanupStep>> UninstallAsync(
        bool restoreDns = true, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var steps = new List<CleanupStep>();

        // DNS, ÇÖZÜMLEYİCİ SİLİNMEDEN ÖNCE GERİ ALINIR.
        //
        // Sıra eskiden tersti: önce iki servis de siliniyor, sonra DNS geri
        // alınıyordu. Geri alma başarısız olursa (netsh hatası, bozuk yedek)
        // sistem DNS'i 127.0.0.1'de kalıyor ve orada dinleyen servis az önce
        // silinmiş oluyordu; kaldırmanın bırakabileceği en kötü tablo. Şimdi
        // geri alma başarısızsa şifreli DNS servisi YERİNDE bırakılıyor: koruma
        // kapanmamış olur ama internet de gitmez ve bir sonraki deneme aynı
        // yedekle yeniden yapılabilir.
        var dnsServisiniBirak = false;

        // Sahibi okunamayan (bozuk) yedek de geri alınıyor: servis silindikten
        // sonra onu geri alacak kimse kalmaz. Yalnızca AÇIKÇA uygulamaya ait
        // yönlendirmeye dokunulmuyor; o, çalışan uygulamanın kendi işi.
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
    /// winws servisini GEÇİCİ olarak durdurur; servis kaydına dokunmaz.
    /// </summary>
    /// <returns>
    /// Servis durdu ve ortalıkta winws süreci kalmadıysa true. Süre dolduysa false:
    /// çağıran taraf ölçümün kirlenebileceğini kullanıcıya söylemeli.
    /// </returns>
    /// <remarks>
    /// Parametre testi winws'i aday aday kendisi başlatıyor. Servisin winws'i
    /// arkada çalışırken bu iki şekilde bozuluyordu: mevcut durum taraması
    /// engeli servisin stratejisi AÇIKKEN ölçüyor ve "engel yok" görüyor, adaylar
    /// ise aynı filtreyle ikinci örnek olarak başlatılamıyor. Uygulama kendi
    /// başlattığı winws'i testten önce durduruyordu, servisinkini durdurmuyordu.
    ///
    /// <c>sc stop</c> yalnızca isteği iletir ve hemen döner; servisin gerçekten
    /// STOPPED olması ve sürecin sürücüyü bırakması ayrıca bekleniyor. Temiz bir
    /// durdurma servis kurtarma tanımını (<c>sc failure</c>) tetiklemez, yani
    /// servis test sırasında kendiliğinden geri gelmez.
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
    /// <see cref="StopWinwsServiceAsync"/> ile durdurulan winws servisini yeniden başlatır.
    /// </summary>
    public static async Task<CleanupStep> StartWinwsServiceAsync(CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var (exitCode, output) = await RunScAsync(["start", WinwsServiceName], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
            ? new CleanupStep($"{WinwsServiceName} servisi yeniden baslatildi", true)
            : new CleanupStep($"{WinwsServiceName} servisi yeniden baslatilamadi", false, StartFailureDetail(WinwsServiceName, output));
    }

    /// <summary>
    /// Otomatik başlatmayı KALDIRMADAN korumayı tamamen kapatır: winws durur, sürücü
    /// boşaltılır, sistem DNS'i geri alınır, şifreli DNS durur ve servisler
    /// açılışta kendiliğinden başlamaz.
    /// </summary>
    /// <remarks>
    /// VPN'LE YAN YANA KULLANIM İÇİN VAR.
    ///
    /// Servis modunda korumayı geçici olarak kapatmanın hiçbir yolu yoktu:
    /// "Duraklat" yalnızca uygulamanın kendi başlattığı winws'i durduruyordu,
    /// pencereyi kapatmak servise dokunmuyordu. Geriye "Otomatik Başlatmayı
    /// Kaldır" ve "Tüm Ayarları Sıfırla" kalıyordu; ikisi de ayarı siliyor.
    /// Gerçek bir kullanıcıda ölçüldü (2026-09-13): servis çalışırken Proton VPN
    /// (WireGuard, TCP 443 üzerinden TLS) her denemede <c>dial tcp ...:443: i/o
    /// timeout</c> verdi; sıfırlama winws'i durdurup WinDivert sürücüsü
    /// çekirdekten düştükten BİR SANİYE sonra bağlandı. Proton sunucu adını her
    /// seferinde sorunsuz çözmüştü: engel DNS değil, paket yolundaki winws.
    ///
    /// Sıra önemli:
    ///
    ///   * Önce winws, çünkü VPN'i bozan o.
    ///   * DNS, çözümleyici DURMADAN ÖNCE geri alınır; ters sıra sistem DNS'ini
    ///     dinleyeni olmayan 127.0.0.1'de bırakırdı (<see cref="UninstallAsync"/>
    ///     ile aynı gerekçe). Geri alma başarısızsa şifreli DNS servisi çalışır
    ///     ve açılışta başlar hâlde BIRAKILIR: koruma kısmen açık kalır ama
    ///     internet gitmez.
    ///
    /// Duraklatmanın izi servislerin başlangıç türü ("demand"); yeniden
    /// başlatmada da duraklatılmış kalır. Bkz. <see cref="ResumeAsync"/>.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> PauseAsync(CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var steps = new List<CleanupStep>();

        if (await ExistsAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false))
        {
            // Başlangıç türü DURDURMADAN ÖNCE değişiyor: durdurma yarıda kalıp
            // bilgisayar yeniden başlatılırsa servis geri gelmesin.
            if (await SetStartTypeAsync(WinwsServiceName, "demand", cancellationToken).ConfigureAwait(false) is { } hata)
            {
                steps.Add(hata);
            }

            if (await StopWinwsServiceAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false))
            {
                // Servis durunca winws sürücüyü bırakıyor; sürücünün çekirdekten
                // gerçekten düştüğünü de bekliyoruz ki "duraklatıldı" dendiğinde
                // paket yolunda hiçbir şey kalmamış olsun.
                var unload = await WinDivertDriver
                    .TryUnloadIdleAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                steps.Add(new CleanupStep($"{WinwsServiceName} servisi durduruldu", true,
                    unload.Cleared ? "WinDivert surucusu cekirdekten dustu" : unload.Detail));
            }
            else
            {
                steps.Add(new CleanupStep($"{WinwsServiceName} servisi durdurulamadi", false,
                    "15 saniyede durmadi ya da baska bir winws calisiyor."));
            }
        }

        var dnsServisiniBirak = false;

        if (SystemDnsManager.HasBackup
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
                    ex.Message + " Sifreli DNS servisi, internet kesilmesin diye DURDURULMADI."));
            }
        }

        var dnsServisiVar = await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false);

        // Geri alma başarısız olsa da açılışta BAŞLAMASIN. Şu an çalışmaya devam
        // ediyor (internet kesilmesin), ama yeniden başlatmada kalkarsa DNS bekçisi
        // yedeği ve cevap veren çözümleyiciyi görüp yönlendirmeyi yeniden yapardı;
        // duraklatılmış bir korumanın yarısı geri gelirdi. Kalkmazsa bekçi
        // yönlendirmeyi geri alır.
        if (dnsServisiVar
            && await SetStartTypeAsync(DnsServiceName, "demand", cancellationToken).ConfigureAwait(false) is { } baslangicHatasi)
        {
            steps.Add(baslangicHatasi);
        }

        if (!dnsServisiniBirak)
        {
            SystemDnsManager.ClearSuspended();

            if (dnsServisiVar)
            {
                var (exitCode, output) = await RunScAsync(["stop", DnsServiceName], cancellationToken)
                    .ConfigureAwait(false);

                // 1062 = "servis başlatılmamış": zaten durmuş, amaç gerçekleşmiş.
                steps.Add(exitCode == 0 || output.Contains("1062", StringComparison.Ordinal)
                    ? new CleanupStep($"{DnsServiceName} servisi durduruldu", true)
                    : new CleanupStep($"{DnsServiceName} servisi durdurulamadi", false, output.Trim()));
            }
        }

        return steps;
    }

    /// <summary>
    /// <see cref="PauseAsync"/> ile duraklatılan servisleri KAYITLI AYARLARIYLA geri açar.
    /// </summary>
    /// <remarks>
    /// Yeniden kurulum yapılmıyor: servisin komutu kayıt defterinde duruyor ve
    /// kullanıcının duraklattığı şey tam olarak o. "Kaldığı yerden devam" bu.
    ///
    /// Sıra başlatmadaki gibi: önce şifreli DNS, çünkü winws engel sunucusuna
    /// giden trafiği kurcalarsa hiçbir şey kazanılmaz. Sistem DNS'i yine ANCAK
    /// çözümleyici cevap verdikten sonra çevriliyor. Vermezse DNS'e dokunulmuyor
    /// ve "askıda" işareti bırakılıyor: DNS bekçisi çözümleyici ayağa kalkınca
    /// yönlendirmeyi kendisi yapar.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> ResumeAsync(CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var steps = new List<CleanupStep>();

        if (await ExistsAsync(DnsServiceName, cancellationToken).ConfigureAwait(false))
        {
            if (await SetStartTypeAsync(DnsServiceName, "auto", cancellationToken).ConfigureAwait(false) is { } hata)
            {
                steps.Add(hata);
            }

            var dnsStep = await StartExistingServiceAsync(DnsServiceName, cancellationToken).ConfigureAwait(false);

            if (!dnsStep.Succeeded)
            {
                steps.Add(dnsStep);
                steps.Add(new CleanupStep("Sistem DNS'i YONLENDIRILMEDI", false,
                    "Sifreli DNS servisi baslatilamadi; DNS ayariniza dokunulmadi."));
            }
            else if (!await WaitForLocalResolverAsync(LocalResolverStartupTimeout, cancellationToken)
                         .ConfigureAwait(false))
            {
                SystemDnsManager.MarkSuspended();
                steps.Add(dnsStep);
                steps.Add(new CleanupStep("Sistem DNS'i henuz YONLENDIRILMEDI", false,
                    $"Sifreli DNS servisi {LocalResolverStartupTimeout.TotalSeconds:F0} saniyede cevap vermedi; " +
                    "DNS ayariniza dokunulmadi. Cevap vermeye baslayinca DNS bekcisi yonlendirmeyi yapacak."));
            }
            else
            {
                steps.Add(dnsStep);

                try
                {
                    var changed = await SystemDnsManager
                        .RedirectToLocalAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);
                    SystemDnsManager.ClearSuspended();
                    steps.Add(new CleanupStep("Sistem DNS'i servise yonlendirildi", true,
                        changed.Count == 0 ? "zaten yonlendirilmisti" : string.Join(", ", changed)));
                }
                catch (Exception ex)
                {
                    SystemDnsManager.MarkSuspended();
                    steps.Add(new CleanupStep("Sistem DNS'i yonlendirilemedi", false, ex.Message));
                }
            }
        }

        if (await ExistsAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false))
        {
            if (await SetStartTypeAsync(WinwsServiceName, "auto", cancellationToken).ConfigureAwait(false) is { } hata)
            {
                steps.Add(hata);
            }

            var winwsStep = await StartExistingServiceAsync(WinwsServiceName, cancellationToken).ConfigureAwait(false);
            steps.Add(await EnsureWinwsRunningAsync(
                winwsStep, "yeniden baslatildi", "baslatildi ama CALISMIYOR", cancellationToken).ConfigureAwait(false));
        }

        return steps;
    }

    /// <summary>Servisin başlangıç türünü değiştirir. Başarılıysa null, değilse hata adımı döner.</summary>
    private static async Task<CleanupStep?> SetStartTypeAsync(
        string name, string startType, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["config", name, "start=", startType], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
            ? null
            : new CleanupStep($"{name} servisinin baslangic turu '{startType}' yapilamadi", false, output.Trim());
    }

    /// <summary>Kurulu bir servisi başlatır; zaten çalışıyorsa (1056) başarılı sayar.</summary>
    private static async Task<CleanupStep> StartExistingServiceAsync(string name, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["start", name], cancellationToken).ConfigureAwait(false);

        return exitCode == 0 || output.Contains("1056", StringComparison.Ordinal)
            ? new CleanupStep($"{name} servisi baslatildi", true)
            : new CleanupStep($"{name} servisi baslatilamadi", false, StartFailureDetail(name, output));
    }

    /// <summary>
    /// <c>sc start</c> hatasının ayrıntısı; engel Defender ya da Akıllı Uygulama
    /// Denetimi'yse ne yapılacağını da ekler.
    /// </summary>
    /// <remarks>
    /// Servis ikiliyi LocalSystem olarak başlatıyor ama engel aynı: imzasız winws.exe
    /// ve dnscrypt-proxy.exe. Gerekçesi <see cref="SecurityBlockAdvice"/>'ta.
    /// </remarks>
    public static string StartFailureDetail(string serviceName, string scOutput)
    {
        var dosya = string.Equals(serviceName, DnsServiceName, StringComparison.OrdinalIgnoreCase)
            ? "dnscrypt-proxy.exe"
            : "winws.exe";

        var ayrinti = (scOutput ?? string.Empty).Trim();
        return SecurityBlockAdvice.DescribeScOutput(scOutput, dosya) is { } engel
            ? ayrinti + " " + engel
            : ayrinti;
    }

    /// <summary><c>sc query</c> çıktısı servisin durmuş olduğunu mu söylüyor.</summary>
    /// <remarks>
    /// STOP_PENDING durmuş SAYILMAZ: o anda süreç hâlâ sürücüyü tutuyor olabilir.
    /// Servis hiç yoksa (1060) da durmuş sayılır; beklenecek bir şey kalmamıştır.
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
            // Süreç listesi okunamıyorsa servis durumuna güveniyoruz.
            return false;
        }
    }

    /// <summary>
    /// Şifreli DNS servisinin cevap vermesi için tanınan süre.
    /// </summary>
    /// <remarks>
    /// Uygulamanın kendi yolundan (15 sn) daha uzun tutuldu: servis LocalSystem
    /// olarak, kullanıcı oturumundan bağımsız açılıyor ve dnscrypt-proxy önce
    /// çözümleyici listesini çekmek zorunda kalabiliyor.
    /// </remarks>
    private static readonly TimeSpan LocalResolverStartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>127.0.0.1:53 cevap verene kadar bekler; süre dolarsa false.</summary>
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
        string name, string binPath, string displayName, bool start, CancellationToken cancellationToken)
    {
        // sc'nin biçimi katıdır: "binPath=" ile değerin ARASINDA boşluk olmalı.
        var (createCode, createOutput) = await RunScAsync(
            ["create", name, "binPath=", binPath, "DisplayName=", displayName, "start=", start ? "auto" : "demand"],
            cancellationToken).ConfigureAwait(false);

        if (createCode != 0)
        {
            return new CleanupStep($"{name} servisi kurulamadi", false, createOutput.Trim());
        }

        // ÖLÜRSE KENDİLİĞİNDEN GERİ GELSİN.
        //
        // Açılışta servisler ağdan önce ayağa kalkabiliyor; winws sürücüyü
        // açamayıp ya da dnscrypt ağ bulamayıp hemen ölürse, kurtarma tanımı
        // olmadan bir daha HİÇ başlamıyor. Kullanıcının gördüğü şey tam olarak
        // "kurdum, yeniden başlattım, çalışmıyor" oluyor; servis listede
        // duruyor ama durmuş. Üç kademeli yeniden deneme bu pencereyi kapatıyor.
        //
        // En iyi çabayla: başarısız olursa adım listesine yazılmıyor. Kurulumun
        // kendisi başarılı ve bu yalnızca dayanıklılık; burada bir hata
        // döndürmek kurulum sonunda gereksiz bir uyarı penceresi açardı.
        await RunScAsync(
            ["failure", name, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000"],
            cancellationToken).ConfigureAwait(false);

        if (!start)
        {
            return new CleanupStep($"{name} servisi kuruldu (DURAKLATILMIS, baslatilmadi)", true);
        }

        var (startCode, startOutput) = await RunScAsync(["start", name], cancellationToken)
            .ConfigureAwait(false);

        return startCode == 0
            ? new CleanupStep($"{name} servisi kuruldu ve baslatildi", true)
            : new CleanupStep($"{name} servisi kuruldu ama baslatilamadi", false, StartFailureDetail(name, startOutput));
    }

    /// <summary>Servis şu anda ÇALIŞIYOR mu.</summary>
    /// <remarks>
    /// <c>sc query</c> çıktısındaki STATE satırına bakılıyor. Kurulu olmak
    /// çalışıyor olmak demek değil: servis durdurulmuş, başlatılamamış ya da
    /// çöktükten sonra yeniden başlatılmamış olabilir.
    /// </remarks>
    private static async Task<bool> IsRunningAsync(string name, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["query", name], cancellationToken)
            .ConfigureAwait(false);

        return exitCode == 0
               && output.Contains("RUNNING", StringComparison.Ordinal);
    }

    private static async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
        => (await QueryConfigAsync(name, cancellationToken).ConfigureAwait(false)).Exists;

    /// <summary>Servis kurulu mu ve kuruluysa <c>sc qc</c> çıktısı.</summary>
    private static async Task<(bool Exists, string Output)> QueryConfigAsync(
        string name, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunScAsync(["qc", name], cancellationToken).ConfigureAwait(false);

        // 1060 = "belirtilen servis yüklü değil".
        return (exitCode == 0 && !output.Contains("1060", StringComparison.Ordinal), output);
    }

    /// <summary>İçinde boşluk olan argümanı tırnaklar; diğerlerine dokunmaz.</summary>
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
