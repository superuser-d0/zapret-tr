using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace ZapretTr.Core.Engine;

/// <summary>Bekcinin bir turda yapacagi is.</summary>
public enum DnsGuardAction
{
    /// <summary>Her sey yerinde ya da karar baskasinin.</summary>
    None,

    /// <summary>Servis ayakta: yonlendirmede eksik kalan kartlari tamamla.</summary>
    Reapply,

    /// <summary>Yonlendirmenin arkasinda cozumleyici yok ve gelmeyecek: geri al.</summary>
    Restore,

    /// <summary>Servis kurulu ama cevap vermiyor: geri al, servis donunce yeniden yonlendir.</summary>
    RestoreAndSuspend,

    /// <summary>Askidaki yonlendirmenin servisi geri geldi: yeniden yonlendir.</summary>
    Resume,

    /// <summary>Askida isareti var ama servis artik yok: isareti kaldir.</summary>
    ClearSuspension,
}

/// <summary>Bekcinin karar verirken baktigi olculmus durum.</summary>
/// <param name="HasBackup">DNS yedegi var mi, yani sistem DNS'i bize cevrilmis mi.</param>
/// <param name="Owner">Yedegin sahibi (<see cref="DnsBackupOwner"/>).</param>
/// <param name="IsSuspended">Servis yonlendirmesi askida mi.</param>
/// <param name="DnsServiceInstalled"><c>ZapretTR-DNS</c> servisi kurulu mu.</param>
/// <param name="DnsServiceBinaryExists">Servisin dnscrypt-proxy.exe'si diskte mi.</param>
/// <param name="AppInstanceRunning">ZapretTR arayuzu acik mi.</param>
/// <param name="ResolverResponding">127.0.0.1:53 cevap veriyor mu.</param>
public sealed record DnsGuardFacts(
    bool HasBackup,
    string? Owner,
    bool IsSuspended,
    bool DnsServiceInstalled,
    bool DnsServiceBinaryExists,
    bool AppInstanceRunning,
    bool ResolverResponding);

/// <summary>
/// Sistem DNS'i ile onu tasiyan cozumleyicinin birbirinden kopmadigini denetler.
/// </summary>
/// <remarks>
/// SERVIS MODUNDA ARKADA IZLEYEN KIMSE YOKTU. Servisler winws.exe ve
/// dnscrypt-proxy.exe'yi dogrudan calistiriyor; DNS yonlendirmesi kurulum aninda
/// BIR KEZ yapiliyor ve uygulama acilmadikca hicbir sey ona bir daha bakmiyordu.
/// Uc gercek sonucu vardi:
///
///   1. Kurulumdan sonra takilan kart (telefonla USB paylasim, USB WiFi) ISS'in
///      DNS'inde kaliyordu. O kart tek baglantiysa DNS engellemesi tamamen geri
///      geliyordu.
///   2. dnscrypt-proxy kalici olarak olurse (virusten koruma karantinaya aldi,
///      klasor silindi, servis devre disi birakildi) sistem DNS'i 127.0.0.1'de
///      kaliyor ve makine HICBIR adi cozemiyordu -- uygulama acilana kadar.
///   3. Uygulama modunda (servissiz) uygulama cokerse ya da elektrik kesilirse
///      ayni tablo acilistan sonra da suruyordu.
///
/// Bekci SYSTEM olarak calisan bir zamanlanmis gorev (<see cref="DnsGuardTask"/>):
/// acilista, her ag baglantisinda ve belirli araliklarla <c>ZapretTR.exe
/// --dns-guard</c> kosar, bir karar verir ve cikar. Kalici bir surec degil.
///
/// Kararin ozu: KORUMA KESILEBILIR, INTERNET KESILMEMELI. Cozumleyici uzun sure
/// cevap vermiyorsa yonlendirme geri aliniyor (engeller geri gelir ama makine
/// calisir) ve servis dondugunde yeniden yapiliyor.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DnsGuard
{
    /// <summary>
    /// Olculmus duruma gore yapilacak isi secer. Yan etkisi yok.
    /// </summary>
    public static DnsGuardAction Decide(DnsGuardFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!facts.HasBackup)
        {
            if (!facts.IsSuspended)
            {
                return DnsGuardAction.None;
            }

            if (!facts.DnsServiceInstalled)
            {
                return DnsGuardAction.ClearSuspension;
            }

            return facts.ResolverResponding ? DnsGuardAction.Resume : DnsGuardAction.None;
        }

        if (!string.Equals(facts.Owner, DnsBackupOwner.Service, StringComparison.Ordinal))
        {
            // Uygulamanin yonlendirmesi. Uygulama aciksa karar onun: kendi
            // dnscrypt'ini yonetiyor, cokmesini kendisi goruyor. Kapaliysa ve
            // cozumleyici cevap vermiyorsa bu onceki bir oturumun kalintisi.
            //
            // Cevap VERIYORSA dokunulmuyor: uygulama oldurulmus ama dnscrypt
            // sureci sag kalmis olabilir. O sure boyunca ad cozumu calisiyor ve
            // sifreli; sureci en gec yeniden baslatma bitirir, bekci de o zaman
            // geri alir.
            return facts.AppInstanceRunning || facts.ResolverResponding
                ? DnsGuardAction.None
                : DnsGuardAction.Restore;
        }

        // Servisin yonlendirmesi, ama servis yok: bir daha hic cevap vermeyecek,
        // beklemenin ve askiya almanin anlami yok.
        if (!facts.DnsServiceInstalled)
        {
            return DnsGuardAction.Restore;
        }

        // Ikilisi yoksa (karantina, silinmis klasor) de cevap veremez; ama servis
        // kaydi duruyor ve dosya geri gelebilir (virusten koruma istisnasi, onarim
        // kurulumu). Askiya aliniyor ki dondugunde yonlendirme de donsun.
        if (!facts.DnsServiceBinaryExists)
        {
            return DnsGuardAction.RestoreAndSuspend;
        }

        return facts.ResolverResponding ? DnsGuardAction.Reapply : DnsGuardAction.RestoreAndSuspend;
    }

    /// <summary>
    /// Karari vermeden once cozumleyiciyi beklemek gerekiyor mu.
    /// </summary>
    /// <remarks>
    /// Yalnizca "cevap vermiyor" yuzunden geri alinacaksa. Acilista servis agdan
    /// once kalkiyor ve dnscrypt once agi, sonra cozumleyici listesini bekliyor;
    /// ilk bakista "olu" gorunmesi normal. Servisin yoklugu yuzunden verilen geri
    /// alma karari ise beklemez.
    /// </remarks>
    public static bool NeedsResolverWait(DnsGuardFacts facts, DnsGuardAction action)
        => !facts.ResolverResponding
           && ((action == DnsGuardAction.RestoreAndSuspend && facts.DnsServiceBinaryExists)
               || (action == DnsGuardAction.Restore
                   && !string.Equals(facts.Owner, DnsBackupOwner.Service, StringComparison.Ordinal)));

    /// <summary>
    /// Bir bekci turu kosar ve yapilanlari satir satir doner. Hicbir sey
    /// yapilmadiysa liste bos.
    /// </summary>
    /// <param name="resolverWait">Cozumleyicinin geri gelmesi icin taninan en uzun sure.</param>
    public static async Task<IReadOnlyList<string>> RunAsync(
        TimeSpan resolverWait, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var log = new List<string>();

        var facts = await GatherAsync(cancellationToken).ConfigureAwait(false);
        var action = Decide(facts);

        if (NeedsResolverWait(facts, action))
        {
            var deadline = DateTimeOffset.UtcNow + resolverWait;
            while (DateTimeOffset.UtcNow < deadline
                   && !await DnsCryptRunner.IsLocalResolverRespondingAsync(cancellationToken: cancellationToken)
                       .ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }

            facts = await GatherAsync(cancellationToken).ConfigureAwait(false);
            action = Decide(facts);
        }

        if (action == DnsGuardAction.None)
        {
            return log;
        }

        using (SystemDnsManager.AcquireLock())
        {
            // Kilit beklenirken arayuz ya da kaldirici durumu degistirmis olabilir;
            // karar kilidin ICINDE, taze olcumle yeniden veriliyor. Bekleme burada
            // tekrarlanmiyor: kilidi dakikalarca tutmak arayuzu kilitlerdi.
            facts = await GatherAsync(cancellationToken).ConfigureAwait(false);
            action = Decide(facts);

            try
            {
                switch (action)
                {
                    case DnsGuardAction.Reapply:
                    {
                        var changed = await SystemDnsManager
                            .RedirectCoreAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);

                        // Degisen yoksa gunluge yazilmiyor: bekci her ag olayinda
                        // ve periyodik calisiyor, bos satirlar asil olaylari gomer.
                        if (changed.Count > 0)
                        {
                            log.Add("Yonlendirmede eksik kalan kartlar tamamlandi: " + string.Join(", ", changed));
                        }

                        break;
                    }

                    case DnsGuardAction.Restore:
                    {
                        var restored = await SystemDnsManager.RestoreCoreAsync(cancellationToken).ConfigureAwait(false);
                        SystemDnsManager.ClearSuspended();
                        log.Add(DescribeRestoreReason(facts) + " Yonlendirme geri alindi: "
                                + (restored.Count == 0 ? "(degisecek kart kalmamisti)" : string.Join(", ", restored)));
                        break;
                    }

                    case DnsGuardAction.RestoreAndSuspend:
                    {
                        var restored = await SystemDnsManager.RestoreCoreAsync(cancellationToken).ConfigureAwait(false);

                        // Isaret geri alma BASARILI olduktan sonra yaziliyor: basarisizsa
                        // yedek duruyor ve bir sonraki tur ayni karari yeniden verir.
                        SystemDnsManager.MarkSuspended();
                        log.Add((facts.DnsServiceBinaryExists
                                    ? $"Sifreli DNS servisi {resolverWait.TotalSeconds:F0} sn cevap vermedi"
                                    : "Sifreli DNS servisinin dnscrypt-proxy.exe dosyasi diskte yok")
                                + "; internet kesilmesin diye yonlendirme ASKIYA alindi: "
                                + (restored.Count == 0 ? "(degisecek kart kalmamisti)" : string.Join(", ", restored))
                                + ". Servis dondugunde yeniden yonlendirilecek.");
                        break;
                    }

                    case DnsGuardAction.Resume:
                    {
                        var changed = await SystemDnsManager
                            .RedirectCoreAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);
                        SystemDnsManager.ClearSuspended();
                        log.Add("Sifreli DNS servisi geri geldi; yonlendirme yeniden yapildi: " + string.Join(", ", changed));
                        break;
                    }

                    case DnsGuardAction.ClearSuspension:
                        SystemDnsManager.ClearSuspended();
                        log.Add("Sifreli DNS servisi kaldirilmis; askidaki yonlendirme isareti silindi.");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Bekci bir sonraki turda yeniden deniyor; hatayi gunluge yazmak
                // yeterli. En olasi sebep o an internete cikan kart olmamasi.
                log.Add($"{action} yapilamadi: {ex.Message}");
            }
        }

        return log;
    }

    private static string DescribeRestoreReason(DnsGuardFacts facts)
    {
        if (!string.Equals(facts.Owner, DnsBackupOwner.Service, StringComparison.Ordinal))
        {
            return "Onceki bir uygulama oturumundan kalan yonlendirmenin cozumleyicisi yok (uygulama kapali).";
        }

        return "Sifreli DNS servisi kaldirilmis ama yonlendirme yerinde kalmis.";
    }

    private static async Task<DnsGuardFacts> GatherAsync(CancellationToken cancellationToken)
    {
        var service = await ServiceManager.GetDnsServiceStateAsync(cancellationToken).ConfigureAwait(false);
        var responding = await DnsCryptRunner
            .IsLocalResolverRespondingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new DnsGuardFacts(
            HasBackup: SystemDnsManager.HasBackup,
            Owner: SystemDnsManager.BackupOwner,
            IsSuspended: SystemDnsManager.IsSuspended,
            DnsServiceInstalled: service.Installed,
            DnsServiceBinaryExists: service.BinaryExists,
            AppInstanceRunning: IsAppInstanceRunning(),
            ResolverResponding: responding);
    }

    /// <summary>
    /// Arayuzun tek ornek kilidinin adi. <c>App.xaml.cs</c> ayni adi kullaniyor;
    /// ayrisirsa bekci acik bir arayuzun yonlendirmesini geri alir.
    /// </summary>
    public const string AppInstanceMutexName = @"Global\ZapretTR-tek-ornek";

    /// <summary>
    /// Arayuz acik mi. Surec adina bakilmiyor: bekcinin kendisi de ZapretTR.exe.
    /// </summary>
    private static bool IsAppInstanceRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(AppInstanceMutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Nesne var ama acmaya yetkimiz yok: yine de var demektir.
            return true;
        }
        catch (Exception)
        {
            // Bilemiyorsak acik sayiyoruz: yanlis tarafa dusmek gerekirse, acik bir
            // arayuzun yonlendirmesini sokmektense bir tur beklemek yeglenir.
            return true;
        }
    }

    /// <summary>Bekci gunlugunun yolu.</summary>
    public static string LogPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-bekci.log");

    /// <summary>Satirlari tarih damgasiyla gunluge ekler; dosyayi son 300 satirda tutar.</summary>
    public static void AppendLog(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);

            var mevcut = File.Exists(LogPath) ? File.ReadAllLines(LogPath).ToList() : [];
            var damga = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Ayni satir art arda yazilmiyor: internete bagli olmayan bir makinede
            // bekci her 10 dakikada ayni "yapilamadi" satirini uretir ve 300 satirlik
            // pencere birkac gunde tek bir mesajla dolardi.
            var sonMesaj = mevcut.Count == 0 ? null : StripTimestamp(mevcut[^1]);
            var yeni = lines.Where(l => l != sonMesaj).ToList();
            if (yeni.Count == 0)
            {
                return;
            }

            mevcut.AddRange(yeni.Select(l => $"{damga}  {l}"));

            File.WriteAllLines(LogPath, mevcut.Skip(Math.Max(0, mevcut.Count - 300)));
        }
        catch (Exception)
        {
            // Gunluk yazilamamasi bekcinin isini gecersiz kilmaz.
        }
    }

    private static string StripTimestamp(string line)
    {
        // "yyyy-MM-dd HH:mm:ss  mesaj"
        const int prefix = 21;
        return line.Length > prefix ? line[prefix..] : line;
    }

    /// <summary>Gunlugun son satiri; yoksa null.</summary>
    public static string? LastLogLine()
    {
        try
        {
            return File.Exists(LogPath) ? File.ReadLines(LogPath).LastOrDefault() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// DNS bekcisini calistiran zamanlanmis gorevi kurar ve kaldirir.
/// </summary>
/// <remarks>
/// Neden bir Windows servisi degil de gorev: bekcinin isi saniyeler suruyor ve
/// yalnizca belirli anlarda gerekiyor. Surekli ayakta duran bir surec hem bir
/// servis ana makinesi yazmayi hem de onun cokmesini izleyecek ikinci bir
/// mekanizmayi gerektirirdi. Gorev Zamanlayicisi bu tetikleyicileri zaten
/// sagliyor.
///
/// Gorevi kurulum paketi kuruyor (<c>--register-dns-guard</c>) ve kaldirici
/// siliyor. Yonlendirmeden bagimsiz olarak hep kurulu duruyor: uygulama modunda
/// cokme sonrasi kurtarma, gorevin CIKMEDEN once var olmasini gerektiriyor.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DnsGuardTask
{
    /// <summary>Gorevin adi.</summary>
    public const string TaskName = "ZapretTR DNS Bekcisi";

    /// <summary>Bekciyi calistiran komut satiri bayragi.</summary>
    public const string GuardFlag = "--dns-guard";

    /// <summary>
    /// Gorev tanimini uretir. Saf fonksiyon: test edilen o.
    /// </summary>
    /// <remarks>
    /// Tetikleyiciler:
    ///   * Acilis (1 dk gecikmeyle): uygulama modunda cokmeden kalan yonlendirme
    ///     burada yakalaniyor.
    ///   * Ag baglandi (NetworkProfile 10000): yeni kart, uykudan uyanma, WiFi
    ///     degisikligi.
    ///   * 10 dakikada bir: askiya alinmis yonlendirmeyi servis dondugunde geri
    ///     getirmek icin; bunun bir ag olayi yok.
    /// </remarks>
    public static string BuildTaskXml(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        var komut = SecurityElement.Escape(exePath);
        var olay = SecurityElement.Escape(
            "<QueryList><Query Id=\"0\" Path=\"Microsoft-Windows-NetworkProfile/Operational\">"
            + "<Select Path=\"Microsoft-Windows-NetworkProfile/Operational\">"
            + "*[System[Provider[@Name='Microsoft-Windows-NetworkProfile'] and EventID=10000]]"
            + "</Select></Query></QueryList>");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>ZapretTR</Author>
                <Description>Sistem DNS'i ZapretTR'nin sifreli DNS'ine yonlendirilmisken cozumleyicinin ayakta oldugunu ve yeni ag kartlarinin da yonlendirildigini denetler. Cozumleyici yoksa yonlendirmeyi geri alir; internetin kesilmesini onler.</Description>
              </RegistrationInfo>
              <Triggers>
                <BootTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT1M</Delay>
                </BootTrigger>
                <EventTrigger>
                  <Enabled>true</Enabled>
                  <Subscription>{olay}</Subscription>
                  <Delay>PT10S</Delay>
                </EventTrigger>
                <TimeTrigger>
                  <Repetition>
                    <Interval>PT10M</Interval>
                    <StopAtDurationEnd>false</StopAtDurationEnd>
                  </Repetition>
                  <StartBoundary>2026-01-01T00:00:00</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>S-1-5-18</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{komut}</Command>
                  <Arguments>{GuardFlag}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>Gorevi kurar ya da gunceller. Basariliysa true.</summary>
    public static async Task<(bool Succeeded, string Output)> RegisterAsync(
        string exePath, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var xmlPath = Path.Combine(Path.GetTempPath(), $"zapret-tr-dns-bekci-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks, bildirimde UTF-16 yazan bir dosyayi baska kodlamayla
            // okumaya calisirsa "XML bicimi hatali" diyor.
            await File.WriteAllTextAsync(xmlPath, BuildTaskXml(exePath), Encoding.Unicode, cancellationToken)
                .ConfigureAwait(false);

            var (exitCode, output) = await RunSchtasksAsync(
                ["/Create", "/TN", TaskName, "/XML", xmlPath, "/F"], cancellationToken).ConfigureAwait(false);

            return (exitCode == 0, output.Trim());
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (Exception)
            {
                // Gecici dosya; kalmasi zararsiz.
            }
        }
    }

    /// <summary>Gorevi siler. Zaten yoksa da basarili sayilir.</summary>
    public static async Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        if (!await IsRegisteredAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var (exitCode, _) = await RunSchtasksAsync(["/Delete", "/TN", TaskName, "/F"], cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0;
    }

    /// <summary>Gorev kurulu mu.</summary>
    public static async Task<bool> IsRegisteredAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, _) = await RunSchtasksAsync(["/Query", "/TN", TaskName], cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0;
    }

    private static async Task<(int ExitCode, string Output)> RunSchtasksAsync(
        string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
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
                return (-1, "schtasks.exe baslatilamadi.");
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
