using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace ZapretTr.Core.Engine;

/// <summary>Bekçinin bir turda yapacağı iş.</summary>
public enum DnsGuardAction
{
    /// <summary>Her şey yerinde ya da karar başkasının.</summary>
    None,

    /// <summary>Servis ayakta: yönlendirmede eksik kalan kartları tamamla.</summary>
    Reapply,

    /// <summary>Yönlendirmenin arkasında çözümleyici yok ve gelmeyecek: geri al.</summary>
    Restore,

    /// <summary>Servis kurulu ama cevap vermiyor: geri al, servis dönünce yeniden yönlendir.</summary>
    RestoreAndSuspend,

    /// <summary>Askıdaki yönlendirmenin servisi geri geldi: yeniden yönlendir.</summary>
    Resume,

    /// <summary>Askıda işareti var ama servis artık yok: işareti kaldır.</summary>
    ClearSuspension,
}

/// <summary>Bekçinin karar verirken baktığı ölçülmüş durum.</summary>
/// <param name="HasBackup">DNS yedeği var mı, yani sistem DNS'i bize çevrilmiş mi.</param>
/// <param name="Owner">Yedeğin sahibi (<see cref="DnsBackupOwner"/>).</param>
/// <param name="IsSuspended">Servis yönlendirmesi askıda mı.</param>
/// <param name="DnsServiceInstalled"><c>ZapretTR-DNS</c> servisi kurulu mu.</param>
/// <param name="DnsServiceBinaryExists">Servisin dnscrypt-proxy.exe'si diskte mi.</param>
/// <param name="AppInstanceRunning">ZapretTR arayüzü açık mı.</param>
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
/// Sistem DNS'i ile onu taşıyan çözümleyicinin birbirinden kopmadığını denetler.
/// </summary>
/// <remarks>
/// SERVİS MODUNDA ARKADA İZLEYEN KİMSE YOKTU. Servisler winws.exe ve
/// dnscrypt-proxy.exe'yi doğrudan çalıştırıyor; DNS yönlendirmesi kurulum anında
/// BİR KEZ yapılıyor ve uygulama açılmadıkça hiçbir şey ona bir daha bakmıyordu.
/// Üç gerçek sonucu vardı:
///
///   1. Kurulumdan sonra takılan kart (telefonla USB paylaşım, USB WiFi) İSS'in
///      DNS'inde kalıyordu. O kart tek bağlantıysa DNS engellemesi tamamen geri
///      geliyordu.
///   2. dnscrypt-proxy kalıcı olarak ölürse (virüsten koruma karantinaya aldı,
///      klasör silindi, servis devre dışı bırakıldı) sistem DNS'i 127.0.0.1'de
///      kalıyor ve makine, uygulama açılana kadar HİÇBİR adı çözemiyordu.
///   3. Uygulama modunda (servissiz) uygulama çökerse ya da elektrik kesilirse
///      aynı tablo açılıştan sonra da sürüyordu.
///
/// Bekçi SYSTEM olarak çalışan bir zamanlanmış görev (<see cref="DnsGuardTask"/>):
/// açılışta, her ağ bağlantısında ve belirli aralıklarla <c>ZapretTR.exe
/// --dns-guard</c> koşar, bir karar verir ve çıkar. Kalıcı bir süreç değil.
///
/// Kararın özü: KORUMA KESİLEBİLİR, İNTERNET KESİLMEMELİ. Çözümleyici uzun süre
/// cevap vermiyorsa yönlendirme geri alınıyor (engeller geri gelir ama makine
/// çalışır) ve servis döndüğünde yeniden yapılıyor.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DnsGuard
{
    /// <summary>
    /// Ölçülmüş duruma göre yapılacak işi seçer. Yan etkisi yok.
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
            // Uygulamanın yönlendirmesi. Uygulama açıksa karar onun: kendi
            // dnscrypt'ini yönetiyor, çökmesini kendisi görüyor. Kapalıysa ve
            // çözümleyici cevap vermiyorsa bu önceki bir oturumun kalıntısı.
            //
            // Cevap VERİYORSA dokunulmuyor: uygulama öldürülmüş ama dnscrypt
            // süreci sağ kalmış olabilir. O süre boyunca ad çözümü çalışıyor ve
            // şifreli; süreci en geç yeniden başlatma bitirir, bekçi de o zaman
            // geri alır.
            return facts.AppInstanceRunning || facts.ResolverResponding
                ? DnsGuardAction.None
                : DnsGuardAction.Restore;
        }

        // Servisin yönlendirmesi, ama servis yok: bir daha hiç cevap vermeyecek,
        // beklemenin ve askıya almanın anlamı yok.
        if (!facts.DnsServiceInstalled)
        {
            return DnsGuardAction.Restore;
        }

        // İkilisi yoksa (karantina, silinmiş klasör) de cevap veremez; ama servis
        // kaydı duruyor ve dosya geri gelebilir (virüsten koruma istisnası, onarım
        // kurulumu). Askıya alınıyor ki döndüğünde yönlendirme de dönsün.
        if (!facts.DnsServiceBinaryExists)
        {
            return DnsGuardAction.RestoreAndSuspend;
        }

        return facts.ResolverResponding ? DnsGuardAction.Reapply : DnsGuardAction.RestoreAndSuspend;
    }

    /// <summary>
    /// Kararı vermeden önce çözümleyiciyi beklemek gerekiyor mu.
    /// </summary>
    /// <remarks>
    /// Yalnızca "cevap vermiyor" yüzünden geri alınacaksa. Açılışta servis ağdan
    /// önce kalkıyor ve dnscrypt önce ağı, sonra çözümleyici listesini bekliyor;
    /// ilk bakışta "ölü" görünmesi normal. Servisin yokluğu yüzünden verilen geri
    /// alma kararı ise beklemez.
    /// </remarks>
    public static bool NeedsResolverWait(DnsGuardFacts facts, DnsGuardAction action)
        => !facts.ResolverResponding
           && ((action == DnsGuardAction.RestoreAndSuspend && facts.DnsServiceBinaryExists)
               || (action == DnsGuardAction.Restore
                   && !string.Equals(facts.Owner, DnsBackupOwner.Service, StringComparison.Ordinal)));

    /// <summary>
    /// Bir bekçi turu koşar ve yapılanları satır satır döner. Hiçbir şey
    /// yapılmadıysa liste boş.
    /// </summary>
    /// <param name="resolverWait">Çözümleyicinin geri gelmesi için tanınan en uzun süre.</param>
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
            // Kilit beklenirken arayüz ya da kaldırıcı durumu değiştirmiş olabilir;
            // karar kilidin İÇİNDE, taze ölçümle yeniden veriliyor. Bekleme burada
            // tekrarlanmıyor: kilidi dakikalarca tutmak arayüzü kilitlerdi.
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

                        // Değişen yoksa günlüğe yazılmıyor: bekçi her ağ olayında
                        // ve periyodik çalışıyor, boş satırlar asıl olayları gömer.
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

                        // İşaret, geri alma BAŞARILI olduktan sonra yazılıyor: başarısızsa
                        // yedek duruyor ve bir sonraki tur aynı kararı yeniden verir.
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
                // Bekçi bir sonraki turda yeniden deniyor; hatayı günlüğe yazmak
                // yeterli. En olası sebep o an internete çıkan kart olmaması.
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
    /// Arayüzün tek örnek kilidinin adı. <c>App.xaml.cs</c> aynı adı kullanıyor;
    /// ayrışırsa bekçi açık bir arayüzün yönlendirmesini geri alır.
    /// </summary>
    public const string AppInstanceMutexName = @"Global\ZapretTR-tek-ornek";

    /// <summary>
    /// Arayüz açık mı. Süreç adına bakılmıyor: bekçinin kendisi de ZapretTR.exe.
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
            // Nesne var ama açmaya yetkimiz yok: yine de var demektir.
            return true;
        }
        catch (Exception)
        {
            // Bilemiyorsak açık sayıyoruz: yanlış tarafa düşmek gerekirse, açık bir
            // arayüzün yönlendirmesini sökmektense bir tur beklemek yeğlenir.
            return true;
        }
    }

    /// <summary>Bekçi günlüğünün yolu.</summary>
    public static string LogPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "dns-bekci.log");

    /// <summary>Satırları tarih damgasıyla günlüğe ekler; dosyayı son 300 satırda tutar.</summary>
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

            // Aynı satır art arda yazılmıyor: internete bağlı olmayan bir makinede
            // bekçi her 10 dakikada aynı "yapılamadı" satırını üretir ve 300 satırlık
            // pencere birkaç günde tek bir mesajla dolardı.
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
            // Günlüğün yazılamaması bekçinin işini geçersiz kılmaz.
        }
    }

    private static string StripTimestamp(string line)
    {
        // "yyyy-MM-dd HH:mm:ss  mesaj"
        const int prefix = 21;
        return line.Length > prefix ? line[prefix..] : line;
    }

    /// <summary>Günlüğün son satırı; yoksa null.</summary>
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
/// DNS bekçisini çalıştıran zamanlanmış görevi kurar ve kaldırır.
/// </summary>
/// <remarks>
/// Neden bir Windows servisi değil de görev: bekçinin işi saniyeler sürüyor ve
/// yalnızca belirli anlarda gerekiyor. Sürekli ayakta duran bir süreç hem bir
/// servis ana makinesi yazmayı hem de onun çökmesini izleyecek ikinci bir
/// mekanizmayı gerektirirdi. Görev Zamanlayıcı bu tetikleyicileri zaten
/// sağlıyor.
///
/// Görevi kurulum paketi kuruyor (<c>--register-dns-guard</c>) ve kaldırıcı
/// siliyor. Yönlendirmeden bağımsız olarak hep kurulu duruyor: uygulama modunda
/// çökme sonrası kurtarma, görevin ÇÖKMEDEN önce var olmasını gerektiriyor.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DnsGuardTask
{
    /// <summary>Görevin adı.</summary>
    public const string TaskName = "ZapretTR DNS Bekcisi";

    /// <summary>Bekçiyi çalıştıran komut satırı bayrağı.</summary>
    public const string GuardFlag = "--dns-guard";

    /// <summary>
    /// Görev tanımını üretir. Saf fonksiyon: test edilen o.
    /// </summary>
    /// <remarks>
    /// Tetikleyiciler:
    ///   * Açılış (1 dk gecikmeyle): uygulama modunda çökmeden kalan yönlendirme
    ///     burada yakalanıyor.
    ///   * Ağ bağlandı (NetworkProfile 10000): yeni kart, uykudan uyanma, WiFi
    ///     değişikliği.
    ///   * 10 dakikada bir: askıya alınmış yönlendirmeyi servis döndüğünde geri
    ///     getirmek için; bunun bir ağ olayı yok.
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

    /// <summary>Görevi kurar ya da günceller. Başarılıysa true.</summary>
    public static async Task<(bool Succeeded, string Output)> RegisterAsync(
        string exePath, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var xmlPath = Path.Combine(Path.GetTempPath(), $"zapret-tr-dns-bekci-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks, bildirimde UTF-16 yazan bir dosyayı başka kodlamayla
            // okumaya çalışırsa "XML biçimi hatalı" diyor.
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
                // Geçici dosya; kalması zararsız.
            }
        }
    }

    /// <summary>Görevi siler. Zaten yoksa da başarılı sayılır.</summary>
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

    /// <summary>Görev kurulu mu.</summary>
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
