using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Bir temizlik adımının sonucu. Arayüzde tek tek gösterilir.</summary>
public sealed record CleanupStep(string Description, bool Succeeded, string? Detail = null);

/// <summary>
/// WinDivert sürücüsünü ve ZapretTR'nin bıraktığı izleri kaldırır.
/// </summary>
/// <remarks>
/// "Tüm Ayarları Sıfırla" düğmesinin arkasındaki iş. Yıkıcı olduğu için davranışı
/// sabit ve açık tutuldu: ne yaptığı adım adım raporlanır.
///
/// DNS ayarı EN BAŞTA geri alınır. Bu düğmeye basan kullanıcı çoğu zaman "bir şeyler
/// bozuldu" diye geliyor; şifreli DNS açıkken uygulama düzgün kapanmadıysa sistem
/// hâlâ 127.0.0.1'i gösteriyor ve hiçbir ad çözülmüyor olabilir. O durumda önce
/// ad çözümü düzelmeli, diğer adımlar beklesin. Kullanıcının KENDİ koyduğu DNS
/// ayarına dokunulmaz; yalnızca bizim yaptığımız değişiklik, diskteki yedekten
/// geri yüklenir.
///
/// Sürücü kaldırma adımları upstream'in kendi windivert_delete.cmd dosyasıyla aynı:
/// sc stop windivert, sc delete windivert.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WinDivertCleanup
{
    /// <summary>ZapretTR'nin kurduğu Windows servisinin adı.</summary>
    public const string ServiceName = ServiceManager.WinwsServiceName;

    /// <summary>
    /// Sıfırlamada kaldırılan ZapretTR servislerinin tamamı.
    /// </summary>
    /// <remarks>
    /// Burada eskiden yalnızca <see cref="ServiceName"/> vardı; şifreli DNS servisi
    /// (<c>ZapretTR-DNS</c>) hiç silinmiyordu. Sıfırlama onun sürecini öldürüyor,
    /// servisin kurtarma tanımı ("çöktüyse 5 sn sonra yeniden başlat") de süreci
    /// geri getiriyordu. Sonuç: "her şeyi sildim" diyen kullanıcıda açılışta
    /// kendiliğinden başlayan ve 127.0.0.1:53'ü tutan bir servis kalıyordu.
    /// </remarks>
    public static readonly string[] ServiceNames =
        [ServiceManager.WinwsServiceName, ServiceManager.DnsServiceName];

    /// <summary>WinDivert sürücüsünün servis adı.</summary>
    public const string DriverServiceName = "windivert";
    /// <summary>
    /// Kaldırılacak SÜRÜCÜ servis adlarının tamamı.
    /// </summary>
    /// <remarks>
    /// Yalnızca "windivert" YETMİYOR. WinDivert'i başka araçlar da kuruyor ve farklı
    /// servis adları bırakıyor: "WinDivert14" (WinDivert 1.4 ve GoodbyeDPI'ın kullandığı
    /// ad) ve bazı dağıtımlarda "monkey". Bunlardan biri geride kalmış ve sürücüsü hâlâ
    /// çekirdeğe yüklüyse winws kendi sürücüsünü yükleyemiyor ve BÜTÜN adaylar aynı
    /// şekilde başarısız oluyor; dışarıdan "hiçbir strateji çalışmadı" gibi görünüyor.
    ///
    /// Bu liste, Zapret Win TR (Ali Mali) projesinin temizlik adımlarından öğrenildi;
    /// orada da tam olarak bu üç ad sökülüyor. GoodbyeDPI Türkiye'de aynı iş için çok
    /// yaygın, dolayısıyla bu kalıntının gerçekten bulunma ihtimali yüksek.
    /// </remarks>
    public static readonly string[] DriverServiceNames = ["windivert", "WinDivert14", "monkey"];

    // Çakışan araç tespiti burada DEĞİL: <see cref="ConflictScanner"/> içinde.
    //
    // Burada da bir tane vardı ve yalnızca ÇALIŞAN sürece bakıyordu; kapalı ama
    // kurulu kalıntıyı, yani en sık karşılaşılan hâli, hiç görmüyordu. İkisini
    // birden tutmak, bu depoda bir kez pahalıya patlamış bir hatanın aynısı
    // olurdu: aynı karar iki ayrı yerde, biri güncellenince ötekinin sessizce
    // geride kalması. Tek yer var, orası ConflictScanner.

    /// <summary>Yapılandırmanın tutulduğu dizin.</summary>
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ZapretTR");

    /// <summary>
    /// Sıfırlamayı yürütür ve her adımın sonucunu döndürür.
    /// </summary>
    /// <param name="removeConfig">
    /// Yapılandırmayı ve öğrenilmiş ağırlıkları da silsin mi. false verilirse yalnızca
    /// çalışan şeyler durdurulur; "internetim bozuldu" durumundan çıkmak için
    /// kullanıcının profil seçimlerini kaybetmesi gerekmiyor.
    /// </param>
    public static async Task<IReadOnlyList<CleanupStep>> RunAsync(
        bool removeConfig = true,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<CleanupStep>();

        // DNS EN BAŞTA geri alınıyor. Sıfırlamayı çalıştıran kullanıcı çoğu zaman
        // "bir şeyler bozuldu" diye buraya geliyor; ad çözümü çalışmıyorsa önce o
        // düzelmeli, diğer adımlar beklesin.
        steps.Add(await RestoreDnsAsync(cancellationToken).ConfigureAwait(false));

        // SERVİSLER SÜREÇLERDEN ÖNCE.
        //
        // Sıra eskiden tersti: önce süreçler öldürülüyor, sonra servise "dur"
        // deniyordu. İki sonucu vardı. (1) Servisin süreci zaten ölmüş olduğu için
        // "sc stop" 1062 ("servis başlatılmamış") dönüyor ve kullanıcı başarılı bir
        // adımı kırmızı hata olarak görüyordu. (2) Daha önemlisi: servisin süreci
        // dışarıdan öldürüldüğünde Windows bunu ÇÖKME sayıp kurtarma tanımını
        // çalıştırıyor ve servisi 5 sn sonra geri getiriyordu.
        //
        // Şimdi: önce temiz durdurma (kurtarma tetiklenmez), sonra silme (silinmek
        // üzere işaretlenen servis yeniden başlatılamaz), en son geride kalan
        // süreçler; yani uygulamanın kendi başlattıkları ya da durmakta geç kalanlar.
        foreach (var service in ServiceNames)
        {
            steps.Add(await RunScAsync("stop", service, $"{service} servisi durduruldu", cancellationToken).ConfigureAwait(false));
            steps.Add(await RunScAsync("delete", service, $"{service} servisi silindi", cancellationToken).ConfigureAwait(false));
        }

        steps.Add(await KillDnsCryptProcessesAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await KillWinwsProcessesAsync(cancellationToken).ConfigureAwait(false));

        // Çözümleyici artık yok; DNS'i hâlâ yalnızca 127.0.0.1 olan kart kaldıysa
        // o kart hiçbir adı çözemez. İlk adımdaki geri alma bunu yedekten yapıyor,
        // bu adım YEDEĞİN YETMEDİĞİ yeri kapatıyor.
        steps.Add(await ResetOrphanedDnsAsync(cancellationToken).ConfigureAwait(false));
        SystemDnsManager.ClearSuspended();
        foreach (var driver in DriverServiceNames)
        {
            steps.Add(await RunScAsync("stop", driver, $"{driver} surucusu durduruldu", cancellationToken).ConfigureAwait(false));
            steps.Add(await RunScAsync("delete", driver, $"{driver} surucusu kaldirildi", cancellationToken).ConfigureAwait(false));
        }

        // "sc stop" yalnızca isteği iletiyor. Sürücü gerçekten düşmeden
        // "sıfırlandı" demek, hemen ardından yapılan parametre testinde motorun
        // takılı sürücüye çarpıp "ÖLÇÜM YAPILAMADI" demesine yol açıyordu;
        // arayüz de buna karşılık "bilgisayarı yeniden başlatın" diyordu.
        steps.Add(await WaitDriversGoneAsync(cancellationToken).ConfigureAwait(false));

        if (removeConfig)
        {
            steps.Add(RemoveConfigDirectory());
        }

        steps.Add(await FlushDnsAsync(cancellationToken).ConfigureAwait(false));

        return steps;
    }

    /// <summary>
    /// "Duraklat": sıfırlamanın durdurduğu HER ŞEYİ durdurur, hiçbir şeyi SİLMEZ.
    /// </summary>
    /// <remarks>
    /// Duraklat eskiden yalnızca uygulamanın kendi başlattığı winws ile şifreli
    /// DNS'i kapatıyordu. Gerçek makinede ölçüldü (2026-09-13): elle "Başlat"
    /// sonrası "Duraklat" dendiğinde winws ve dnscrypt kapanıyor, DNS geri
    /// alınıyordu ama WinDivert SÜRÜCÜSÜ 20 saniye sonra bile çekirdekte RUNNING
    /// duruyordu. Arkada kurulu bir servis varsa ona hiç dokunulmuyordu. Aynı
    /// kullanıcıda VPN (Proton) koruma kapatıldıktan sonra da bağlanmadı ve ancak
    /// "Tüm Ayarları Sıfırla" ile, sürücüyü çekirdekten düşürdükten bir saniye
    /// sonra bağlandı. Kullanıcının haklı beklentisi: "duraklat" dediğinde
    /// arkada hiçbir şey kalmamalı.
    ///
    /// Adımlar <see cref="RunAsync"/> ile aynı sırada; farklar:
    ///
    ///   * servisler SİLİNMİYOR, duraklatılıyor (<see cref="ServiceManager.PauseAsync"/>):
    ///     durur ve açılışta başlamaz, kayıt ve ayar yerinde kalır;
    ///   * sürücü kaydı silinmiyor, yalnızca durduruluyor (winws bir sonraki
    ///     başlatmada zaten yeniden kuruyor);
    ///   * yapılandırma ve öğrenilmiş sonuçlar SİLİNMİYOR.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> StopEverythingAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<CleanupStep>();

        var status = await ServiceManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.AnyInstalled)
        {
            steps.AddRange(await ServiceManager.PauseAsync(cancellationToken).ConfigureAwait(false));
        }

        // Servise ait olmayan (uygulamaya ait ya da yetim) yönlendirme de geri alınıyor.
        steps.Add(await RestoreDnsAsync(cancellationToken).ConfigureAwait(false));

        steps.Add(await KillDnsCryptProcessesAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await KillWinwsProcessesAsync(cancellationToken).ConfigureAwait(false));

        // Geri alma bir yerde başarısız olduysa çözümleyici artık yok ve kart hâlâ
        // 127.0.0.1'de: otomatiğe döndür. Sıfırlamadaki gerekçeyle aynı.
        steps.Add(await ResetOrphanedDnsAsync(cancellationToken).ConfigureAwait(false));
        SystemDnsManager.ClearSuspended();

        foreach (var driver in DriverServiceNames)
        {
            steps.Add(await RunScAsync("stop", driver, $"{driver} surucusu durduruldu", cancellationToken).ConfigureAwait(false));
        }

        steps.Add(await WaitDriversGoneAsync(cancellationToken).ConfigureAwait(false));
        steps.Add(await FlushDnsAsync(cancellationToken).ConfigureAwait(false));

        return steps;
    }

    /// <summary>
    /// Duraklatmadan sonra arkada hâlâ çalışan ya da yerinde duran şeyleri ölçer.
    /// Boş liste: gerçekten hiçbir şey kalmamış.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindLeftoversAsync(CancellationToken cancellationToken = default)
    {
        var yukluSuruculer = new List<string>();
        foreach (var driver in DriverServiceNames)
        {
            var (_, output) = await RunProcessAsync("sc.exe", ["query", driver], cancellationToken).ConfigureAwait(false);
            if (!WinDivertDriver.IsGoneOrStopped(output))
            {
                yukluSuruculer.Add(driver);
            }
        }

        var status = await ServiceManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        return DescribeLeftovers(
            ProcessCount("winws"), ProcessCount("dnscrypt-proxy"), yukluSuruculer,
            SystemDnsManager.HasBackup, status.WinwsRunning);
    }

    /// <summary>Ölçülen kalıntıları kullanıcıya yazılacak satırlara çevirir.</summary>
    public static IReadOnlyList<string> DescribeLeftovers(
        int winwsProcesses, int dnsCryptProcesses, IReadOnlyList<string> loadedDrivers,
        bool dnsRedirected, bool serviceRunning)
    {
        ArgumentNullException.ThrowIfNull(loadedDrivers);

        var kalan = new List<string>();

        if (winwsProcesses > 0)
        {
            kalan.Add($"{winwsProcesses} winws sureci calisiyor");
        }

        if (dnsCryptProcesses > 0)
        {
            kalan.Add($"{dnsCryptProcesses} dnscrypt-proxy sureci calisiyor");
        }

        if (loadedDrivers.Count > 0)
        {
            kalan.Add("ag surucusu cekirdekte (" + string.Join(", ", loadedDrivers) + ")");
        }

        if (dnsRedirected)
        {
            kalan.Add("sistem DNS'i hala ZapretTR'ye yonlendirilmis");
        }

        if (serviceRunning)
        {
            kalan.Add("otomatik baslatma servisi calisiyor");
        }

        return kalan;
    }

    private static int ProcessCount(string name)
    {
        try
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length;
        }
        catch (Exception)
        {
            return 0;
        }
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
            // Bu adımın sessizce geçmesi kabul edilemez: başarısız olursa
            // kullanıcının ad çözümü çalışmıyor olabilir ve bunu bilmesi gerekir.
            return new CleanupStep("Sistem DNS ayari geri alinamadi", false, ex.Message);
        }
    }

    private static async Task<CleanupStep> WaitDriversGoneAsync(CancellationToken cancellationToken)
    {
        const string ad = "Surucu cekirdekten dustu";
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        var kalan = new List<string>();

        while (true)
        {
            kalan.Clear();
            foreach (var driver in DriverServiceNames)
            {
                var (_, output) = await RunProcessAsync("sc.exe", ["query", driver], cancellationToken)
                    .ConfigureAwait(false);
                if (!WinDivertDriver.IsGoneOrStopped(output))
                {
                    kalan.Add(driver);
                }
            }

            if (kalan.Count == 0)
            {
                return new CleanupStep(ad, true);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new CleanupStep(ad, false,
                    string.Join(", ", kalan) + " 10 sn icinde dusmedi; bilgisayari bir kez yeniden baslatmak gerekebilir.");
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Yedeği olmayan ya da yedekten geri alınamamış 127.0.0.1 yönlendirmelerini
    /// otomatiğe döndürür.
    /// </summary>
    /// <remarks>
    /// İki gerçek yol: geri alma başarısız oldu (yedek duruyor ama netsh düştü) ya
    /// da yedek hiç yok (elle silinmiş, bozuk, eski bir sürümden kalma). İkisinde de
    /// sıfırlama biterken sistem DNS'i 127.0.0.1'i gösteriyor, dnscrypt az önce
    /// öldürülmüş ve makine ad çözemiyordu: "sıfırladım, internetim gitti".
    ///
    /// 127.0.0.1:53 hâlâ cevap veriyorsa dokunulmuyor: orada bizim olmayan bir
    /// çözümleyici var (AdGuard Home, Acrylic...) ve o kullanıcının kendi ayarı.
    /// </remarks>
    private static async Task<CleanupStep> ResetOrphanedDnsAsync(CancellationToken cancellationToken)
    {
        const string ad = "Yetim DNS yonlendirmesi";

        try
        {
            if (await DnsCryptRunner.IsLocalResolverRespondingAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
            {
                return new CleanupStep(ad, true, "127.0.0.1 baska bir cozumleyiciye ait; dokunulmadi");
            }

            var duzeltilen = await SystemDnsManager.ResetOrphanedRedirectsAsync(cancellationToken)
                .ConfigureAwait(false);

            return duzeltilen.Count == 0
                ? new CleanupStep(ad, true, "yok")
                : new CleanupStep(ad, true, "otomatige donduruldu: " + string.Join(", ", duzeltilen));
        }
        catch (Exception ex)
        {
            return new CleanupStep(ad, false, ex.Message);
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

        return InterpretScResult(description, exitCode, output);
    }

    /// <summary>
    /// <c>sc stop</c> / <c>sc delete</c> sonucunu sıfırlama açısından yorumlar.
    /// </summary>
    /// <remarks>
    /// Sıfırlamanın amacı bir DURUMA varmak; o durum zaten sağlanıyorsa adım
    /// başarılıdır. Bunları hata göstermek kullanıcıyı olmayan bir sorunun
    /// peşine düşürüyordu:
    ///
    ///   1060 = servis yüklü değil        -> kaldırılacak bir şey yok
    ///   1062 = servis başlatılmamış      -> durdurulacak bir şey yok
    ///   1072 = servis silinmek üzere işaretli -> silme zaten istenmiş
    /// </remarks>
    public static CleanupStep InterpretScResult(string description, int exitCode, string output)
    {
        if (exitCode == 0)
        {
            return new CleanupStep(description, true);
        }

        if (output.Contains("1060", StringComparison.Ordinal))
        {
            return new CleanupStep(description, true, "zaten yoktu");
        }

        if (output.Contains("1062", StringComparison.Ordinal))
        {
            return new CleanupStep(description, true, "zaten durmustu");
        }

        if (output.Contains("1072", StringComparison.Ordinal))
        {
            return new CleanupStep(description, true, "zaten silinmek uzere isaretli");
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
