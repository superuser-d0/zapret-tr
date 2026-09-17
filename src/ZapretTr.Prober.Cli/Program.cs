using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

// ZapretTR saha testi aracı.
//
// İki işi var: geliştirirken motoru arayüzden bağımsız koşturmak ve başka
// birinin makinesinde (örneğin Superonline hattında) hiçbir şey kurmadan test
// yaptırmak. İkincisi yüzünden kasıtlı olarak tek dosya, bağımlılıksız ve
// çıktısı okunabilir tutuldu.

// Argümansız = çift tıklama. Saha testini paketteki ayarlarla koştur ve pencereyi
// açık tut; gerekçesi SahaModu'nda (gerçek kullanıcıda ölçüldü).
if (args.Length == 0)
{
    return SahaModu.CiftTiklamaIleCalistir();
}

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintUsage();
    return 0;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine();
Console.WriteLine("ZapretTR — parametre testi");
Console.WriteLine(new string('-', 60));

if (!ElevationGuard.IsElevated())
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("HATA: Yönetici yetkisi gerekli.");
    Console.Error.WriteLine("winws.exe çekirdek modunda çalışan WinDivert sürücüsünü kullanıyor;");
    Console.Error.WriteLine("yükseltilmiş yetki olmadan başlatılamıyor.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Bu dosyaya sağ tıklayıp \"Yönetici olarak çalıştır\" deyin.");
    return 2;
}

// --- Seçili yapılandırmayı uygula ve ölç -------------------------------------
// Arayüzdeki "Başlat" düğmesinin ürettiği BİRLEŞİK komutu çalıştırıp önce/sonra
// farkını ölçer. Tek bir adayı test etmekten farklı: gerçekte kullanılan komut
// dört bölümü --new ile birleştiriyor ve o birleşik hâlin çalıştığı ayrıca
// doğrulanmalı.
if (options.Apply)
{
    var applyVendor = VendorPaths.Locate();
    var applyProfiles = ProfileStore.Load();

    var applyProfile = options.IspId is null ? null : applyProfiles.FindById(options.IspId);
    if (applyProfile is null)
    {
        Console.Error.WriteLine("--apply icin gecerli bir --isp gerekli.");
        Console.Error.WriteLine("Mevcut olanlar: " + string.Join(", ", applyProfiles.Profiles.Select(x => x.Id)));
        return 4;
    }

    // Arayüzdekiyle AYNI kural (RuntimeSelection): seçilen HTTPS stratejisi +
    // yalnızca doğrulanmış diğer bölümler. İkisi ayrışırsa arayüzde test edilen
    // şey ile gerçekte çalışan şey farklı olur.
    var primary = applyProfile.CandidatesFor(StrategySection.Tcp443).FirstOrDefault();
    if (primary is null)
    {
        Console.Error.WriteLine($"{applyProfile.DisplayName} profilinde HTTPS adayi yok.");
        return 4;
    }

    var winners = RuntimeSelection.Build(applyProfile, primary.Args);

    var unprotected = RuntimeSelection.UnprotectedSections(applyProfile);
    if (unprotected.Count > 0)
    {
        Console.WriteLine("Not: şu bölümlerde doğrulanmış strateji yok, dokunulmayacak:");
        Console.WriteLine("     " + string.Join(", ", unprotected.Select(x => x.ToJsonName())));
        Console.WriteLine("     (parametre testi çalıştırılınca doğrulanıp devreye girerler)");
        Console.WriteLine();
    }

    var applyBuilder = new WinwsCommandBuilder(applyVendor);

    // Arayüzle AYNI daraltma. Saha aracı kullanıcının çalıştıracağından farklı bir
    // komut ölçerse, ölçtüğü şey kullanıcının yaşadığı şey olmaz (issue #1).
    var applyDomains = HostlistStore
        .Load(applyProfiles.Root)
        .DomainsFor(RuntimeSelection.VerifiedCategories(applyProfile, winners));

    var runtimeArgs = applyBuilder.BuildRuntimeCommand(winners, applyDomains);

    Console.WriteLine("Servis sağlayıcı : " + applyProfile.DisplayName);
    Console.WriteLine($"Bölüm sayısı     : {winners.Count}");
    Console.WriteLine();
    Console.WriteLine("Çalıştırılacak komut:");
    Console.WriteLine("   " + WinwsCommandBuilder.ToDisplayString(runtimeArgs));
    Console.WriteLine();

    var applySection = StrategySection.Tcp443;
    if (options.Section is { } applySectionName
        && !StrategySectionExtensions.TryParseJsonName(applySectionName, out applySection))
    {
        Console.Error.WriteLine($"Bilinmeyen bölüm: {applySectionName} (tcp80 | tcp443 | quic | discord-voice)");
        return 4;
    }

    var applyTargets = ProbeTargetStore.Load(applyProfiles.Root).ToList();
    foreach (var extraHost in options.ExtraTargets)
    {
        var parsedExtra = ProbeTargetStore.TryParseUserTarget(extraHost, applySection);
        if (parsedExtra is not null)
        {
            applyTargets.Insert(0, parsedExtra);
        }
        else
        {
            // Sessizce düşürmek burada daha da kötü: ölçümü BİZ yapıyoruz ve
            // eksik hedefle çıkan bir rapor "o site sorunsuz" gibi okunuyor.
            Console.Error.WriteLine(
                HostlistStore.DescribeUnusableTarget(extraHost) ?? $"Hedef anlasilamadi: {extraHost}");
        }
    }

    // Şifreli DNS isteğe bağlı ama Türkiye'de çoğu zaman ŞART: DNS kaçırılmışken
    // bağlantı zaten engel sunucusuna gider ve winws stratejisi hiçbir şey
    // değiştirmez. İkisini birlikte ölçmek, gerçek kullanım senaryosu.
    DnsCryptRunner? applyDns = null;
    if (options.UseSecureDns)
    {
        applyDns = new DnsCryptRunner(applyVendor);
        Console.WriteLine("Şifreli DNS başlatılıyor...");
        await applyDns.StartAsync();
        Console.WriteLine("   [+] sistem DNS'i dnscrypt-proxy'ye yönlendirildi");
        Console.WriteLine();
    }

    // Ölçüm bölüme göre seçilmeli: QUIC ham QuicConnection ile, discord-voice
    // STUN ile. Burada düz HttpProbeClient kullanılıyordu ve QUIC hedefleri
    // HTTP üzerinden ölçülüp "HTTP 200" dönüyordu; yani QUIC hiç ölçülmüyordu.
    //
    // IP SABİTLENMİYOR (pinnedIp: null) ve bu kasıtlı: --apply'ın ölçtüğü şey
    // "gerçekten çalışıyor mu", yani kullanıcının uygulamasının gördüğü yol.
    // --doh verildiğinde dnscrypt zaten SİSTEM DNS'ini yönlendiriyor, dolayısıyla
    // sistem çözümlemesi de şifreli oluyor.
    Console.WriteLine("Önce (winws kapalı):");
    var before = new Dictionary<string, bool>();
    using (var c = new HttpProbeClient())
    {
        foreach (var t in applyTargets)
        {
            var r = await StrategyProber.ProbeAsync(t.Section, t.Host, null, c);
            before[t.Label] = r.Succeeded;
            Console.WriteLine($"   {(r.Succeeded ? "açık " : "KAPALI")}  {t.Label,-34} {r.Detail}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("winws başlatılıyor...");
    var applyRunner = new WinwsRunner(applyVendor);
    await applyRunner.StartAsync(runtimeArgs);
    await Task.Delay(2000);

    Console.WriteLine();
    Console.WriteLine("Sonra (winws açık):");
    var fixedCount = 0;
    var brokeCount = 0;
    using (var c = new HttpProbeClient())
    {
        foreach (var t in applyTargets)
        {
            var r = await StrategyProber.ProbeAsync(t.Section, t.Host, null, c);
            var was = before.TryGetValue(t.Label, out var b) && b;

            var change = (was, r.Succeeded) switch
            {
                (false, true) => "  <<< DÜZELDİ",
                (true, false) => "  <<< BOZULDU",
                _ => string.Empty,
            };

            if (change.Contains("DÜZELDİ", StringComparison.Ordinal)) { fixedCount++; }
            if (change.Contains("BOZULDU", StringComparison.Ordinal)) { brokeCount++; }

            Console.WriteLine($"   {(r.Succeeded ? "açık " : "KAPALI")}  {t.Label,-34} {r.Detail}{change}");
        }
    }

    await applyRunner.StopAsync();

    if (applyDns is not null)
    {
        await applyDns.StopAsync();
        Console.WriteLine();
        Console.WriteLine("Şifreli DNS kapatıldı, sistem DNS'i geri alındı.");
    }

    Console.WriteLine();
    Console.WriteLine($"Sonuç: {fixedCount} hedef düzeldi, {brokeCount} hedef bozuldu.");
    if (brokeCount > 0)
    {
        // Çalışan bir şeyi bozmak, çalışmayan bir şeyi düzeltmemekten kötü.
        Console.WriteLine("UYARI: Daha önce açılan bir hedef bu yapılandırmayla kapandı.");
    }

    Console.WriteLine();
    return fixedCount > 0 && brokeCount == 0 ? 0 : 1;
}

// --- Otomatik başlatma servisi ----------------------------------------------
if (options.ServiceCommand is { } serviceCommand)
{
    var svcVendor = VendorPaths.Locate();

    switch (serviceCommand)
    {
        case "durum":
        case "status":
        {
            var status = await ServiceManager.GetStatusAsync();
            Console.WriteLine($"winws servisi      : {(!status.WinwsInstalled ? "yok" : status.WinwsRunning ? "kurulu, calisiyor" : status.WinwsPaused ? "kurulu, DURAKLATILDI" : "kurulu, DURMUS")}");
            Console.WriteLine($"DNS servisi        : {(status.DnsInstalled ? "kurulu" : "yok")}");
            Console.WriteLine($"DNS yonlendirmesi  : {SystemDnsManager.BackupOwner ?? "yok"}");
            return 0;
        }

        case "kur":
        case "install":
        {
            var svcProfiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
            var svcProfile = options.IspId is null ? null : svcProfiles.FindById(options.IspId);
            if (svcProfile is null)
            {
                Console.Error.WriteLine("--service kur icin gecerli bir --isp gerekli.");
                return 4;
            }

            var primaryCandidate = svcProfile.CandidatesFor(StrategySection.Tcp443).FirstOrDefault();
            if (primaryCandidate is null)
            {
                Console.Error.WriteLine($"{svcProfile.DisplayName} profilinde HTTPS adayi yok.");
                return 4;
            }

            var svcWinners = RuntimeSelection.Build(svcProfile, primaryCandidate.Args);
            var svcDomains = HostlistStore
                .Load(svcProfiles.Root)
                .DomainsFor(RuntimeSelection.VerifiedCategories(svcProfile, svcWinners));

            var svcArgs = new WinwsCommandBuilder(svcVendor).BuildRuntimeCommand(svcWinners, svcDomains);

            Console.WriteLine("Kurulacak komut:");
            Console.WriteLine("   " + WinwsCommandBuilder.ToDisplayString(svcArgs));
            Console.WriteLine();

            foreach (var step in await ServiceManager.InstallAsync(svcVendor, svcArgs, options.UseSecureDns))
            {
                var mark = step.Succeeded ? "[+]" : "[!]";
                var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail;
                Console.WriteLine($"   {mark} {step.Description}{detail}");
            }

            return 0;
        }

        case "kaldir":
        case "uninstall":
        {
            var removed = await ServiceManager.UninstallAsync();
            if (removed.Count == 0)
            {
                Console.WriteLine("Kaldirilacak servis yok.");
                return 0;
            }

            foreach (var step in removed)
            {
                var mark = step.Succeeded ? "[+]" : "[!]";
                var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail;
                Console.WriteLine($"   {mark} {step.Description}{detail}");
            }

            return 0;
        }

        case "duraklat":
        case "pause":
        case "devam":
        case "resume":
        {
            var sonuc = serviceCommand is "duraklat" or "pause"
                ? await ServiceManager.PauseAsync()
                : await ServiceManager.ResumeAsync();

            foreach (var step in sonuc)
            {
                var mark = step.Succeeded ? "[+]" : "[!]";
                var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail;
                Console.WriteLine($"   {mark} {step.Description}{detail}");
            }

            return sonuc.All(s => s.Succeeded) ? 0 : 1;
        }

        default:
            Console.Error.WriteLine($"Bilinmeyen servis komutu: {serviceCommand} (kur | kaldir | durum | duraklat | devam)");
            return 4;
    }
}

// --- Şifreli DNS ------------------------------------------------------------
// Türkiye'de engelleme çoğu zaman önce DNS katmanında; o katman aşılmadan DPI
// stratejisi işe yaramıyor. Bu mod dnscrypt-proxy'yi çalıştırıp sistem DNS'ini
// ona yönlendirir.
if (options.DnsCommand is { } dnsCommand)
{
    var dnsVendor = VendorPaths.Locate();
    var dnsRunner = new DnsCryptRunner(dnsVendor);
    dnsRunner.LogLineReceived += line => Console.WriteLine("   " + line);

    switch (dnsCommand)
    {
        case "durum":
        case "status":
        {
            var responding = await DnsCryptRunner.IsLocalResolverRespondingAsync();
            Console.WriteLine($"Yerel çözümleyici : {(responding ? "cevap veriyor" : "cevap vermiyor")}");
            Console.WriteLine($"DNS yedeği        : {(SystemDnsManager.HasBackup ? "VAR (sistem DNS'i bize yönlendirilmiş)" : "yok")}");

            // Yedek var ama proxy cevap vermiyorsa makine şu an ad çözemiyor.
            if (SystemDnsManager.HasBackup && !responding)
            {
                Console.WriteLine();
                Console.WriteLine("UYARI: DNS bize yönlendirilmiş ama çözümleyici çalışmıyor.");
                Console.WriteLine("Bu durumda hiçbir adres çözülemez. Kurtarılıyor...");
                var recovered = await SystemDnsManager.TryRecoverAsync(localResolverResponds: false);
                Console.WriteLine("Geri alındı: " + string.Join(", ", recovered));
            }

            return 0;
        }

        case "ac":
        case "on":
        {
            Console.WriteLine("dnscrypt-proxy başlatılıyor...");
            Console.WriteLine("(sistem DNS'i, çözümleyicinin cevap verdiği DOĞRULANDIKTAN sonra değiştirilecek)");
            Console.WriteLine();

            await dnsRunner.StartAsync();

            Console.WriteLine();
            Console.WriteLine("Şifreli DNS aktif. Kapatmak için: --dns kapat");
            Console.WriteLine("Bu pencere kapanırsa DNS otomatik geri alınır.");
            Console.WriteLine();
            Console.WriteLine("Kapatmak için Enter'a basın...");
            Console.ReadLine();

            await dnsRunner.StopAsync();
            return 0;
        }

        case "kapat":
        case "off":
        {
            var restored = await SystemDnsManager.RestoreAsync();
            Console.WriteLine(restored.Count > 0
                ? "DNS geri alındı: " + string.Join(", ", restored)
                : "Geri alınacak bir DNS yedeği yok.");

            foreach (var stale in System.Diagnostics.Process.GetProcessesByName("dnscrypt-proxy"))
            {
                try { stale.Kill(); } catch { /* zaten ölmüş */ }
                finally { stale.Dispose(); }
            }

            return 0;
        }

        case "test":
        {
            // Tam döngü, kendi kendini geri alarak. Amaç güvenlik yolunu
            // doğrulamak: DNS değiştirilebiliyor mu VE her koşulda geri
            // alınabiliyor mu. Test sonunda sistem mutlaka eski hâline döner.
            Console.WriteLine("DNS güvenlik testi — sistem sonunda eski haline döndürülecek.");
            Console.WriteLine();

            var ok = true;
            try
            {
                Console.WriteLine("1) dnscrypt-proxy başlatılıyor ve doğrulanıyor...");
                await dnsRunner.StartAsync();
                Console.WriteLine("   [+] çözümleyici cevap veriyor, sistem DNS'i yönlendirildi");

                Console.WriteLine();
                Console.WriteLine("2) Şifreli DNS üzerinden çözümleme deneniyor...");
                foreach (var probe in new[] { "discord.com", "pornhub.com", "example.com" })
                {
                    try
                    {
                        var addresses = await System.Net.Dns.GetHostAddressesAsync(probe);
                        var v4 = addresses.FirstOrDefault(a =>
                            a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        var hijacked = v4 is not null && v4.ToString().StartsWith("195.175.254.", StringComparison.Ordinal);
                        Console.WriteLine($"   {probe,-18} {v4}{(hijacked ? "   <<< HALA ENGEL SUNUCUSU" : "   (gerçek adres)")}");
                        if (hijacked) { ok = false; }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   {probe,-18} çözümlenemedi: {ex.Message}");
                        ok = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("   [!] " + ex.Message);
                ok = false;
            }
            finally
            {
                Console.WriteLine();
                Console.WriteLine("3) Geri alınıyor...");
                await dnsRunner.StopAsync();

                // Geri alma gerçekten oldu mu, yedeğin yokluğundan değil
                // sistemin kendisinden doğrulanıyor.
                var after = SystemDnsManager.HasBackup;
                Console.WriteLine(after
                    ? "   [!] DNS yedeği hâlâ duruyor — geri alma tamamlanmadı!"
                    : "   [+] DNS eski haline döndü, yedek temizlendi");
            }

            Console.WriteLine();
            Console.WriteLine(ok ? "SONUÇ: şifreli DNS çalışıyor." : "SONUÇ: sorun var, yukarıya bakın.");
            return ok ? 0 : 1;
        }

        default:
            Console.Error.WriteLine($"Bilinmeyen DNS komutu: {dnsCommand} (ac | kapat | durum | test)");
            return 4;
    }
}

// --- Temizlik ---------------------------------------------------------------
// Bu araç başka birinin makinesinde çalışıyor ve çalışırken çekirdek modunda bir
// paket sürücüsü yüklüyor. Onu kaldırabilmesi bir "ekstra" değil, sorumluluk.
if (options.Cleanup)
{
    Console.WriteLine("Temizlik yapılıyor...");
    Console.WriteLine();

    // removeConfig: false. Buradaki amaç sürücüyü ve çalışan süreci kaldırmak,
    // kullanıcının ayarlarını silmek değil.
    var steps = await WinDivertCleanup.RunAsync(removeConfig: false);
    foreach (var step in steps)
    {
        var mark = step.Succeeded ? "[+]" : "[!]";
        var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail;
        Console.WriteLine($"   {mark} {step.Description}{detail}");
    }

    Console.WriteLine();
    return steps.All(s => s.Succeeded) ? 0 : 1;
}

// --- Teşhis modu ------------------------------------------------------------
// --- Hat tespiti ------------------------------------------------------------
// Ölçüm yapmadan önce "hangi hattayım" sorusunu cevaplar. Yanlış hatta koşup
// sonucu yanlış profile yazmak bu projedeki en pahalı sessiz hata: veri kirlenir
// ve hangi ölçümün hangi şebekeye ait olduğu geri kazanılamaz.
//
// Ayrıca IspDetector'ı CLI'dan çalıştırabilen TEK yol bu. Önemi kırpılmış
// yayınlarda ortaya çıkıyor: kırpma bir JSON yolunu bozarsa belirti çalışma
// anında görünür ve sınanamayan kod yolu sınanmamış kod yoludur.
if (options.DetectIsp)
{
    using var detector = new IspDetector();
    var detection = await detector.DetectAsync(ProfileStore.Load());

    Console.WriteLine();
    if (detection.Identity is not { IsKnown: true } identity)
    {
        Console.WriteLine("Hat tespit edilemedi (ağ erişimi yok ya da servisler cevap vermedi).");
        return 1;
    }

    Console.WriteLine($"ASN      : {(identity.Asn is null ? "bilinmiyor" : "AS" + identity.Asn)}");
    Console.WriteLine($"Kuruluş  : {identity.OrgName ?? "bilinmiyor"}");
    Console.WriteLine($"Kaynak   : {identity.Source}");
    Console.WriteLine();

    if (detection.Matches.Count == 0)
    {
        Console.WriteLine("Eşleşen profil yok — genel arama gerekir.");
        return 0;
    }

    Console.WriteLine("Eşleşen profiller (en iyi önce):");
    foreach (var match in detection.Matches)
    {
        Console.WriteLine($"   {match.Id,-18} {match.DisplayName}");
    }

    if (detection.IsAmbiguous)
    {
        Console.WriteLine();
        Console.WriteLine("Birden fazla eşleşme var; --isp ile hangisi olduğunu açıkça belirt.");
    }

    Console.WriteLine();
    return 0;
}

// Tek bir adresi dört protokolle de deneyip ham sonucu basar. Destek isteği
// geldiğinde "şu komutun çıktısını gönder" diyebileceğimiz şey.
if (options.DiagnoseHost is { } diagnoseHost)
{
    Console.WriteLine($"Teşhis: {diagnoseHost}");
    Console.WriteLine();

    // --doh verilirse çözümleme şifreli yoldan yapılır. DNS kaçırması olan bir
    // hatta sistem DNS'i engel sunucusunu döndürür ve teşhis, DPI katmanını değil
    // DNS katmanını ölçer.
    string? diagnoseIp = null;
    if (options.UseSecureDns)
    {
        using var diagnoseDoh = new DohResolver();
        diagnoseIp = await diagnoseDoh.ResolveIPv4Async(diagnoseHost);
        Console.WriteLine($"   şifreli DNS: {diagnoseIp ?? "çözümlenemedi"}");
        Console.WriteLine();
    }

    using var diagnostic = new HttpProbeClient(TimeSpan.FromSeconds(10));
    foreach (var mode in Enum.GetValues<ProbeMode>())
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        // Http3 yuvası HttpProbeClient ile ÖLÇÜLEMEZ: HTTP/3 IP'ye sabitlenemediği
        // için adres sistem DNS'i ile çözülür ve --doh sessizce etkisiz kalır.
        // Ham QUIC istemcisi IP ile SNI'yi ayrı verebiliyor.
        var result = mode == ProbeMode.Http3
            ? await new QuicProbeClient(TimeSpan.FromSeconds(10)).TryReachAsync(diagnoseHost, diagnoseIp)
            : await diagnostic.TryReachAsync(diagnoseHost, mode, diagnoseIp);

        started.Stop();

        var verdict = result.Succeeded ? "BAŞARILI" : "başarısız";
        Console.WriteLine($"   {mode,-10} {verdict,-9} {started.ElapsedMilliseconds,6} ms  ip={result.ResolvedIp}");
        Console.WriteLine($"   {"",-10} {result.Detail}");
        if (result.BodyPreview is not null)
        {
            Console.WriteLine($"   {"",-10} gövde: {result.BodyPreview}");
        }

        Console.WriteLine();
    }

    return 0;
}

// --- Motor devrede mi kontrolü ----------------------------------------------
// "Strateji çalışmadı" ile "winws trafiğe hiç dokunmadı" birbirinden çok farklı
// iki sonuç, ama dışarıdan ikisi de zaman aşımı olarak görünüyor. Bu mod winws'i
// --debug=1 ile çalıştırıp paketleri gerçekten gördüğünü gösteriyor.
if (options.EngageCheckHost is { } engageHost)
{
    var engageVendor = VendorPaths.Locate();
    var engageProfiles = ProfileStore.Load();

    // Bölüm seçilebilir olmalı. Sabit tcp80 ile QUIC hiç teşhis edilemiyordu:
    // winws QUIC Initial'ı çözüp SNI bulamazsa stratejiyi HİÇ uygulamıyor ve
    // dışarıdan bu, "strateji işe yaramadı" ile birebir aynı görünüyor.
    var engageSection = StrategySection.Tcp80;
    if (options.Section is { } sectionName)
    {
        if (!StrategySectionExtensions.TryParseJsonName(sectionName, out engageSection))
        {
            Console.Error.WriteLine($"Bilinmeyen bölüm: '{sectionName}'. Beklenen: tcp80, tcp443, quic, discord-voice.");
            return 4;
        }
    }

    var strategy = options.Strategy
                   ?? engageProfiles.FindById("turk-telekom")?
                       .CandidatesFor(engageSection).FirstOrDefault()?.Args
                   ?? engageProfiles.Ladder.Expand(engageSection).FirstOrDefault().Args
                   ?? "--dpi-desync=fake,fakedsplit --dpi-desync-fooling=md5sig";

    var engageMode = StrategyProber.ModeFor(engageSection);

    Console.WriteLine($"Hedef    : {engageHost}");
    // Bölümün NASIL ölçüldüğü yazılıyor, ProbeMode değil: quic ve discord-voice
    // HttpProbeClient kullanmıyor, dolayısıyla ProbeMode orada anlamsız bir değer.
    var engageHow = engageSection switch
    {
        StrategySection.Quic => "ham QUIC el sıkışması",
        StrategySection.DiscordVoice => "STUN (UDP)",
        _ => engageMode.ToString(),
    };

    Console.WriteLine($"Bölüm    : {engageSection.ToJsonName()} ({engageHow})");
    Console.WriteLine($"Strateji : {strategy}");
    Console.WriteLine();

    // Sistem DNS'i kaçırılmışsa hedef IP engel sunucusunu gösterir ve ölçüm
    // DPI'ı değil DNS katmanını ölçer. --doh verildiğinde çözümleme şifreli
    // yoldan yapılıyor; böylece alttaki DPI katmanı görünür hâle geliyor.
    string? ip = null;
    if (options.UseSecureDns)
    {
        using var engageDoh = new DohResolver();
        ip = await engageDoh.ResolveIPv4Async(engageHost);
        if (ip is null)
        {
            Console.Error.WriteLine($"Şifreli DNS ile çözümlenemedi: {engageHost}");
            return 5;
        }
    }
    else
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(engageHost);
        ip = addresses.First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();
    }

    Console.WriteLine($"Hedef IP : {ip} ({(options.UseSecureDns ? "şifreli DNS" : "sistem DNS")})");

    var engageBuilder = new WinwsCommandBuilder(engageVendor);
    var engageArgs = engageBuilder.BuildProbeCommand(engageSection, strategy, ip).ToList();
    engageArgs.Add("--debug=1");

    Console.WriteLine("Komut    : " + WinwsCommandBuilder.ToDisplayString(engageArgs));
    Console.WriteLine();

    var log = new List<string>();
    var runner = new WinwsRunner(engageVendor);
    runner.LogLineReceived += line => { lock (log) { log.Add(line.Text); } };

    await runner.StartAsync(engageArgs);
    await Task.Delay(1500);

    // Teşhis, arama motorunun ölçtüğü şeyin AYNISINI ölçmeli. Bu yüzden bölüme
    // göre istemci seçimi burada tekrar yazılmıyor, motorun kendi dağıtımı
    // çağrılıyor: QUIC ham QuicConnection ile, discord-voice STUN ile ölçülür.
    using (var engageClient = new HttpProbeClient(TimeSpan.FromSeconds(8)))
    {
        var engageResult = await StrategyProber.ProbeAsync(engageSection, engageHost, ip, engageClient);
        Console.WriteLine($"Probe sonucu: {(engageResult.Succeeded ? "BAŞARILI" : "başarısız")} — {engageResult.Detail}");
    }

    await Task.Delay(500);
    await runner.StopAsync();

    string[] engageSnapshot;
    lock (log)
    {
        engageSnapshot = [.. log];
    }

    // winws'in kendi karar satırları. "Paketi gördü mü" ile "gördüğü paketi
    // değiştirdi mi" ayrı sorular; ikincisi sessizce hayır olabiliyor ve
    // günlüğün tamamı içinde kaybolmasın diye ayrıca özetleniyor.
    var verdictMarkers = new[]
    {
        "packet contains QUIC initial",
        "QUIC initial decryption failed",
        "QUIC initial defrag CRYPTO failed",
        "QUIC initial fragmented CRYPTO",
        "QUIC initial without ClientHello",
        "without hostname in the SNI",
        "not applying tampering",
        "applying tampering",
        "desync",
    };

    // Global WinDivert filtresi bütün udp/443 trafiğini yakaladığı için günlük
    // makinedeki her QUIC bağlantısını içeriyor; on binlerce satır. Teşhis için
    // anlamlı olan yalnızca HEDEF IP'ye ait olanlar; gerisi ekranı doldurup asıl
    // satırların kaybolmasına yol açıyordu.
    var engageRelevant = engageSnapshot
        .Where(l => l.Contains(ip, StringComparison.Ordinal))
        .ToList();

    var verdicts = engageSnapshot
        .Where(l => verdictMarkers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    // Tam günlük her zaman diske yazılır: bir teşhis koşumunu yalnızca çıktıyı
    // kırptığı için tekrarlamak, yönetici onayı gerektirdiği için pahalı.
    var engageLogPath = options.OutputPath
                        ?? Path.Combine(AppContext.BaseDirectory, "engage-check.log");
    File.WriteAllLines(engageLogPath, engageSnapshot);

    Console.WriteLine();
    Console.WriteLine($"Hedef IP'ye ait satırlar ({engageRelevant.Count}):");
    if (engageRelevant.Count == 0)
    {
        Console.WriteLine("   (yok — winws bu hedefe giden hiçbir paket görmedi)");
    }

    foreach (var line in engageRelevant.Take(60))
    {
        Console.WriteLine("   " + line);
    }

    Console.WriteLine();
    Console.WriteLine($"Karar satırları ({verdicts.Count}):");
    if (verdicts.Count == 0)
    {
        Console.WriteLine("   (yok — winws bu trafiğe dair hiçbir karar kaydetmedi)");
    }

    foreach (var line in verdicts.Take(40))
    {
        Console.WriteLine("   " + line);
    }

    Console.WriteLine();
    Console.WriteLine($"winws günlüğü: {engageSnapshot.Length} satır → {engageLogPath}");

    return 0;
}

VendorPaths vendor;
ProfileStore profiles;
try
{
    vendor = VendorPaths.Locate();
    profiles = ProfileStore.Load();
}
catch (Exception ex)
{
    Console.Error.WriteLine("Kurulum eksik: " + ex.Message);
    return 3;
}

var missing = vendor.FindMissingFiles();
if (missing.Count > 0)
{
    Console.Error.WriteLine($"Eksik upstream dosyası ({missing.Count}):");
    foreach (var file in missing)
    {
        Console.Error.WriteLine("  " + file);
    }

    return 3;
}

// --- İSS seçimi -------------------------------------------------------------
//
// "auto", saha paketi için var. Paket herkese açık yayınlanıyor ve indiren
// kişinin hangi İSS'te olduğunu bilmiyoruz; sabit bir İSS yazmak, başka bir
// hattaki kullanıcının testini YANLIŞ profille başlatır (Tier 1 alakasız
// adayları önce dener, bütçe onlara harcanır). Tespit başarısız olursa hata
// değil: genel aramaya düşerek test yine çalışır.
IspProfile? profile = null;
if (string.Equals(options.IspId, "auto", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Servis sağlayıcı tespit ediliyor...");
    using var autoDetector = new IspDetector();
    var autoDetection = await autoDetector.DetectAsync(profiles);

    // Birden fazla profil eşleşirse ilki alınıyor: sıralama en iyi eşleşme
    // önce. Yanlış seçim ölümcül değil; kazanan bulunamazsa arama zaten
    // diğer profillere ve genel merdivene geçiyor.
    profile = autoDetection.Matches.Count > 0 ? autoDetection.Matches[0] : null;

    Console.WriteLine(profile is null
        ? "Hat tanınmadı; genel aramayla devam edilecek."
        : $"Tespit edildi: {profile.DisplayName}");
}
else if (options.IspId is not null)
{
    profile = profiles.FindById(options.IspId);
    if (profile is null)
    {
        Console.Error.WriteLine($"Bilinmeyen servis sağlayıcı: {options.IspId}");
        Console.Error.WriteLine("Mevcut olanlar: auto, " + string.Join(", ", profiles.Profiles.Select(p => p.Id)));
        return 4;
    }
}

Console.WriteLine("Servis sağlayıcı : " + (profile?.DisplayName ?? "(seçilmedi — genel arama)"));
Console.WriteLine("Motor            : " + vendor.WinwsExe);
Console.WriteLine("Mod              : " + (options.BaselineOnly ? "yalnızca mevcut durum taraması" : "tam test"));
Console.WriteLine("DNS              : " + (options.UseSecureDns ? "şifreli (DoH)" : "sistem"));
if (options.MaxCandidates is { } budget)
{
    Console.WriteLine($"Bölüm başı bütçe : {budget} aday");
}

if (options.Exhaustive)
{
    Console.WriteLine("Arama            : kapsamlı — ilk başarıda durulmayacak");
}

// --- Hedefler ---------------------------------------------------------------
// --target hangi bölüme yazılacak: varsayılan tcp443, --section ile değiştirilir.
// Önceden HER kullanıcı hedefi tcp443'e gidiyordu, yani kullanıcı düz HTTP'de
// açılmayan bir adresi test edemiyordu: tcp80 bölümü için kendi hedefini
// ekleyemediği gibi, verdiği adres yanlış bölümde ölçülüyordu.
var extraSection = StrategySection.Tcp443;
if (options.Section is { } extraSectionName
    && !StrategySectionExtensions.TryParseJsonName(extraSectionName, out extraSection))
{
    Console.Error.WriteLine($"Bilinmeyen bölüm: {extraSectionName} (tcp80 | tcp443 | quic | discord-voice)");
    return 4;
}

var targets = ProbeTargetStore.Load(profiles.Root).ToList();
foreach (var extra in options.ExtraTargets)
{
    var parsed = ProbeTargetStore.TryParseUserTarget(extra, extraSection);
    if (parsed is null)
    {
        // Sebebi de söyleniyor: "çözümlenemedi" tek başına kullanıcıya ne
        // yapacağını söylemiyor, virgül kullandıysa hiç söylemiyor.
        Console.Error.WriteLine(
            HostlistStore.DescribeUnusableTarget(extra) ?? $"Hedef çözümlenemedi: {extra}");
        Console.Error.WriteLine($"Atlanıyor: {extra}");
        continue;
    }

    targets.Insert(0, parsed);
    Console.WriteLine($"Ek hedef         : {parsed.Host} ({parsed.Section.ToJsonName()})");
}

Console.WriteLine();

// --- Ne paylaşılacağı konusunda şeffaflık -----------------------------------
// Bu araç başka birinin makinesinde çalışacak. Ne kaydettiğini çalışmadan ÖNCE
// söylemek, sonradan açıklamaktan farklı bir şey.
if (options.OutputPath is not null)
{
    Console.WriteLine("Bu test sırasında olacaklar:");
    Console.WriteLine();
    Console.WriteLine("  1. Birkaç adrese bağlanıp hangilerinin açıldığı ölçülecek.");
    Console.WriteLine("  2. winws.exe çalıştırılacak. Bu, WinDivert adlı bir ağ sürücüsünü");
    Console.WriteLine("     geçici olarak yükler. Test bitince kaldırabilirsiniz:");
    Console.WriteLine("     bu programı --cleanup ile çalıştırmanız yeterli.");
    Console.WriteLine("  3. Sonuçlar şu dosyaya yazılacak:");
    Console.WriteLine("     " + options.OutputPath);
    Console.WriteLine();
    Console.WriteLine("  Rapora YAZILANLAR   : servis sağlayıcı adı, test edilen adresler,");
    Console.WriteLine("                        denenen parametreler ve sonuçları, süreler.");
    Console.WriteLine("  Rapora YAZILMAYANLAR: IP adresiniz, bilgisayar adınız, kullanıcı adınız,");
    Console.WriteLine("                        gezdiğiniz siteler, başka hiçbir kişisel bilgi.");
    Console.WriteLine();
    Console.WriteLine("  Rapor hiçbir yere GÖNDERİLMEZ; yalnızca diske yazılır. Ne yapacağına");
    Console.WriteLine("  siz karar verirsiniz — dosyayı açıp okuyabilirsiniz, düz metindir.");
    Console.WriteLine();

    if (!options.AssumeYes)
    {
        // "(e/H)": büyük harf VARSAYILANI gösterir ve varsayılan "hayır".
        //
        // Eskiden "(E/h)" yazıyordu; yani Enter "evet" gibi okunuyordu, kod ise
        // tam tersini yapıyordu. Gerçek kullanıcı (issue #1, 2026-09-16) Enter'a
        // bastı, test hiç başlamadı ve TESTI-BASLAT.bat yine "Test bitti. Sonuc
        // dosyasi: zapret-tr-rapor.json" dedi; dosya yoktu.
        Console.WriteLine("Devam etmek için E yazıp Enter'a basın. Boş bırakmak testi iptal eder.");
        Console.Write("Devam edilsin mi? (e/H): ");
        var answer = Console.ReadLine()?.Trim();

        // Boş cevap (doğrudan Enter) onay SAYILMAZ. Başkasının makinesinde
        // çalışan ve çekirdek sürücüsü yükleyen bir araç için varsayılan "hayır"
        // olmalı; kullanıcı bilerek "evet" demeli.
        var accepted = answer is not null
                       && (answer.Equals("e", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("evet", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));

        if (!accepted)
        {
            Console.WriteLine();
            Console.WriteLine("İptal edildi. Hiçbir değişiklik yapılmadı, rapor yazılmadı.");

            // 0 DEĞİL. 0 "test bitti, rapor yazıldı" demek; saha paketinin
            // başlatma betiği çıkış koduna bakarak kullanıcıya "dosyayı gönderin"
            // ya da "rapor oluşmadı" diyor. İptal, Ctrl+C yoluyla aynı kodu
            // (130) döndürüyor.
            return 130;
        }
    }

    Console.WriteLine();
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("İptal ediliyor, winws durduruluyor...");
    cancellation.Cancel();
};

var prober = new StrategyProber(vendor, profiles, targets, options.UseSecureDns);

try
{
    // --- Koruma zaten açık mı ----------------------------------------------
    //
    // Açıksa bu ölçüm YANILTICI olur: hedefler zaten açılıyor, araç da "engel
    // yok" diyor. Gerçek bir kullanıcıda tam olarak bu oldu: makinesinde
    // ZapretTR servisi çalışırken saha testini koşturdu, 11 hedefin 11'i
    // "açılıyor" çıktı ve sonuç "DPI ile engellenen hedef yok" oldu. Ölçüm
    // doğruydu, yalnızca ölçülen şey engelleme değil KENDİ KORUMAMIZDI.
    var korumaSurecleri = System.Diagnostics.Process.GetProcessesByName("winws");
    if (korumaSurecleri.Length > 0)
    {
        foreach (var surec in korumaSurecleri)
        {
            surec.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("DİKKAT: bu makinede winws zaten çalışıyor.");
        Console.WriteLine();
        Console.WriteLine("  Yani koruma şu anda AÇIK. Bu durumda ölçüm yanıltıcı olur:");
        Console.WriteLine("  engelli adresler zaten açılıyor ve test \"engel yok\" der.");
        Console.WriteLine();
        Console.WriteLine("  Doğru ölçüm için önce korumayı durdurun:");
        Console.WriteLine("    - ZapretTR kuruluysa uygulamayı açıp \"Çıkış\" deyin, ya da");
        Console.WriteLine("    - otomatik başlatma servisi kuruluysa \"Otomatik Başlatmayı Kaldır\".");
        Console.WriteLine();
        Console.WriteLine("  Test yine de sürecek; sonuç bu uyarıyla birlikte okunmalı.");
        Console.WriteLine();
    }

    // --- Mevcut durum taraması ---------------------------------------------
    Console.WriteLine("[1/2] Mevcut durum taranıyor (winws kapalı)...");
    var baseline = await prober.RunBaselineAsync(cancellationToken: cancellation.Token);

    foreach (var item in baseline)
    {
        var mark = item.Status switch
        {
            BaselineStatus.Accessible => "açılıyor    ",
            BaselineStatus.Blocked => "DPI ENGELİ  ",
            BaselineStatus.DnsRedirected => "DNS YÖNLEND.",
            _ => "belirsiz    ",
        };
        Console.WriteLine($"   {mark} {item.Target.Label,-24} {item.Detail}");
    }

    // Kontrol hedefi engellenmemesi BEKLENEN bir adres. Erişilemiyorsa sorun
    // DPI'da değil ölçüm yolumuzda ya da bağlantıda demektir; bu durumda tüm
    // baseline sonuçları şüpheli ve strateji araması anlamsız olur.
    var failedControls = baseline
        .Where(b => b.Target.Category == StrategyProber.ControlCategory
                    && b.Status != BaselineStatus.Accessible)
        .ToList();

    foreach (var control in failedControls)
    {
        Console.WriteLine();
        Console.WriteLine($"UYARI: {control.Target.Section.ToJsonName()} kontrol hedefi de açılmıyor.");
        Console.WriteLine($"  {control.Target.Host} engellenmemesi beklenen bir adres ({control.Detail}).");
        Console.WriteLine("  Bu bölümün sonuçları DPI engellemesini değil bir bağlantı/ölçüm");
        Console.WriteLine("  sorununu yansıtıyor olabilir, o yüzden bu bölümde strateji");
        Console.WriteLine("  ARANMAYACAK. Ayırt edemediğimiz bir şey için dakikalarca aday");
        Console.WriteLine("  denemenin anlamı yok.");
        Console.WriteLine();
    }

    var blockedCount = baseline.Count(b => b.Status == BaselineStatus.Blocked
                                           && b.Target.Category != StrategyProber.ControlCategory
                                           && !failedControls.Any(c => c.Target.Section == b.Target.Section));
    var redirectedCount = baseline.Count(b => b.Status == BaselineStatus.DnsRedirected);

    Console.WriteLine();
    Console.WriteLine($"   {blockedCount} hedef DPI ile engelli, {redirectedCount} hedef DNS ile yönlendirilmiş.");
    Console.WriteLine();

    // DNS yönlendirmesi zapret'in çözebileceği bir şey değil ve bunu söylememek
    // kullanıcıyı boşuna bekletmek olur: strateji araması yüzlerce aday deneyip
    // hiçbiri çalışmadığı için "bulunamadı" derdi.
    if (redirectedCount > 0)
    {
        Console.WriteLine("DİKKAT: DNS yönlendirmesi tespit edildi.");
        Console.WriteLine();
        Console.WriteLine("  Bu hedeflerde bağlantı kuruluyor ama servis sağlayıcınızın engel");
        Console.WriteLine("  sunucusuna gidiyor — yani DNS cevabı değiştirilmiş. zapret paketleri");
        Console.WriteLine("  kurcalayarak bunu çözemez; ortada atlatılacak bir DPI yok.");
        Console.WriteLine();
        Console.WriteLine("  Çözüm DNS tarafında: şifreli DNS (DoH/DoT) kullanın.");
        Console.WriteLine("  Bu hedefler strateji aramasından çıkarılıyor.");
        Console.WriteLine();
    }

    if (blockedCount == 0)
    {
        Console.WriteLine("DPI ile engellenen hedef yok; strateji testi anlamsız olurdu.");
        Console.WriteLine("Sizde açılmayan bir adresi --target ile verip tekrar çalıştırın.");

        // "Engel yok" DA bir sonuçtur ve rapor edilmeli. Eskiden bu yolda
        // hiçbir dosya yazılmadan çıkılıyordu: kullanıcı testi çalıştırıyor,
        // ekranda dolu dolu çıktı görüyor, sonra klasörde JSON bulamıyordu.
        // Gerçek bir kullanıcıda tam olarak bu yaşandı; üstelik o koşum bize
        // "bu hatta şu an engel yok" bilgisini veriyordu, ki toplamak
        // istediğimiz şeyin ta kendisi.
        WriteBaselineReport(options.OutputPath, baseline, profile, "engel-yok");
        return 0;
    }

    if (options.BaselineOnly)
    {
        WriteBaselineReport(options.OutputPath, baseline, profile, "yalnizca-tarama");
        return 0;
    }

    // --- Strateji araması ---------------------------------------------------
    Console.WriteLine("[2/2] Strateji aranıyor (winws başlatılacak, WinDivert sürücüsü yüklenecek)...");
    Console.WriteLine();

    var lastLine = string.Empty;
    var progress = new Progress<ProbeProgress>(p =>
    {
        var shown = Math.Min(p.Completed + 1, Math.Max(p.Total, 1));
        var line = $"   {p.TierLabel} · {shown}/{p.Total} · {p.CurrentDescription}";
        if (line == lastLine)
        {
            return;
        }

        lastLine = line;
        Console.WriteLine(line);
    });

    var report = await prober.RunAsync(
        profile,
        progress,
        // Normalde ilk çalışan adayda durulur; kullanıcıyı bekletmemek için doğru
        // davranış bu. --exhaustive ise bunun tersini ister: amaç çalışan BİR
        // strateji bulmak değil, o hatta hangi adayların çalıştığını haritalamak.
        // Profillerin sıralaması ancak bu veriyle düzeltilebiliyor.
        stopAtFirstSuccess: !options.Exhaustive,
        maxCandidatesPerSection: options.MaxCandidates,
        // Yukarıda zaten tarandı; tekrar taramak hem bir dakikadan fazla sürer
        // hem de farklı sınıflandırma üretip yanlış bölümlerde arama başlatır.
        knownBaseline: baseline,
        cancellationToken: cancellation.Token);

    Console.WriteLine();
    Console.WriteLine(new string('-', 60));
    Console.WriteLine($"Süre: {report.Duration.TotalSeconds:F1} sn · {report.Attempts.Count} deneme");
    Console.WriteLine();

    if (report.IsEmpty)
    {
        Console.WriteLine("Çalışan strateji bulunamadı.");
        Console.WriteLine();
        Console.WriteLine("En sık görülen başarısızlık sebepleri:");
        foreach (var group in report.Attempts
                     .GroupBy(a => a.Detail ?? "(sebep yok)")
                     .OrderByDescending(g => g.Count())
                     .Take(5))
        {
            Console.WriteLine($"   {group.Count(),4}x  {group.Key}");
        }
    }
    else
    {
        Console.WriteLine("ÇALIŞAN STRATEJİLER:");
        foreach (var winner in report.Winners)
        {
            Console.WriteLine();
            Console.WriteLine($"   [{winner.Section.ToJsonName()}]  {winner.CandidateId}");
            Console.WriteLine($"   {winner.Args}");
            Console.WriteLine($"   açılan: {string.Join(", ", winner.VerifiedCategories)}");
        }

        Console.WriteLine();
        Console.WriteLine("Birleşik komut:");
        var builder = new WinwsCommandBuilder(vendor);
        Console.WriteLine("   " + WinwsCommandBuilder.ToDisplayString(builder.BuildRuntimeCommand(report.ToWinnerMap())));
    }

    // --- Çalışan HER aday --------------------------------------------------
    // Kazanan tek adaydır; ama profil sıralamasını düzeltmek ve adayları
    // "verified"e çekmek için gereken şey çalışan adayların TAMAMI. Bu liste
    // ancak --exhaustive ile anlamlı doluyor, çünkü normal koşumda arama ilk
    // başarıdan sonra duruyor.
    var verifiedCandidates = report.Attempts
        .Where(a => a.Succeeded)
        .GroupBy(a => (a.Section, a.CandidateId, a.Args))
        .Select(g => (
            g.Key.Section,
            g.Key.CandidateId,
            g.Key.Args,
            Categories: g.Select(a => a.TargetCategory)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList()))
        .OrderBy(x => x.Section)
        .ThenByDescending(x => x.Categories.Count)
        .ThenBy(x => x.CandidateId, StringComparer.Ordinal)
        .ToList();

    if (options.Exhaustive)
    {
        Console.WriteLine();
        Console.WriteLine($"ÇALIŞAN TÜM ADAYLAR ({verifiedCandidates.Count}):");

        StrategySection? lastSection = null;
        foreach (var candidate in verifiedCandidates)
        {
            if (lastSection != candidate.Section)
            {
                Console.WriteLine();
                Console.WriteLine($"   [{candidate.Section.ToJsonName()}]");
                lastSection = candidate.Section;
            }

            Console.WriteLine($"      {candidate.CandidateId,-34} açılan: {string.Join(", ", candidate.Categories)}");
        }

        if (verifiedCandidates.Count == 0)
        {
            Console.WriteLine("   (yok)");
        }
    }

    // --- Öğrenilenleri kalıcı hâle getir ------------------------------------
    // Arayüz bunu kendi test akışında zaten yapıyordu; CLI yapmıyordu. Yani bu
    // aracı çalıştırıp çalışan strateji bulan biri, arayüzü açtığında bulunanın
    // hiçbirini görmüyordu. Bayrakla isteğe bağlı: bu araç başkasının makinesinde de
    // çalışıyor ve oradaki söz "sonuçlar yalnızca rapor dosyasına yazılır".
    if (options.SaveLearned)
    {
        Console.WriteLine();
        if (profile is null)
        {
            Console.Error.WriteLine("--save-learned için --isp gerekli; kaydedilmedi.");
        }
        else if (verifiedCandidates.Count == 0)
        {
            Console.WriteLine("Kaydedilecek doğrulanmış aday yok.");
        }
        else
        {
            ConfigStore.AddLearned(verifiedCandidates.Select(candidate => new LearnedCandidate
            {
                IspId = profile.Id,
                CandidateId = candidate.CandidateId,
                Section = candidate.Section.ToJsonName(),
                Args = candidate.Args,
                VerifiedFor = candidate.Categories,
                LastVerified = DateTime.Now.ToString("yyyy-MM-dd"),
            }));

            Console.WriteLine($"learned.json güncellendi ({verifiedCandidates.Count} aday): {ConfigStore.LearnedPath}");
        }
    }

    if (options.OutputPath is not null)
    {
        WriteReport(options.OutputPath, report, profile);
        Console.WriteLine();
        Console.WriteLine("Rapor yazıldı: " + MutlakYol(options.OutputPath));
    }

    Console.WriteLine();
    return report.IsEmpty ? 1 : 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("İptal edildi.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Test sırasında hata: " + ex.Message);
    Console.Error.WriteLine(ex.StackTrace);

    // Hata raporu da bir rapordur. Eskiden bu yolda HİÇBİR dosya
    // yazılmıyordu: test patlayınca kullanıcının elinde gönderecek bir şey
    // kalmıyor, "test yaptım ama json oluşmadı" diyordu ve neyin
    // patladığını kimse öğrenemiyordu. Saha paketinin tek işi veri
    // toplamak; en çok da iş ters gittiğinde veri gerekiyor.
    if (options.OutputPath is not null)
    {
        WriteFailureReport(options.OutputPath, ex);
    }

    return 5;
}

/// <summary>Strateji aranmadan çıkıldığında mevcut durumu dosyaya yazar.</summary>
/// <remarks>
/// Strateji bulunmamış olması raporu değersiz yapmıyor: hangi hedefin açıldığı,
/// hangisinin engellendiği ve DNS'in yönlendirilip yönlendirilmediği tek başına
/// bir ölçüm. Saha paketinin işi zaten bu veriyi toplamak.
/// </remarks>
static void WriteBaselineReport(
    string? path, IReadOnlyList<BaselineResult> baseline, IspProfile? profile, string sonuc)
{
    if (path is null)
    {
        return;
    }

    try
    {
        var tam = MutlakYol(path);
        var govde = new System.Text.StringBuilder();
        govde.AppendLine("{");
        govde.AppendLine("  \"sonuc\": \"" + sonuc + "\",");
        govde.AppendLine("  \"tarih\": \"" + DateTimeOffset.Now.ToString("O") + "\",");
        govde.AppendLine("  \"isp\": " + (profile is null
            ? "null"
            : "\"" + JsonKacir(profile.Id) + "\"") + ",");
        govde.AppendLine("  \"hedefler\": [");

        for (var i = 0; i < baseline.Count; i++)
        {
            var b = baseline[i];
            var virgul = i == baseline.Count - 1 ? string.Empty : ",";
            govde.AppendLine(
                "    { \"host\": \"" + JsonKacir(b.Target.Host) + "\", " +
                "\"bolum\": \"" + b.Target.Section.ToJsonName() + "\", " +
                "\"kategori\": \"" + JsonKacir(b.Target.Category) + "\", " +
                "\"durum\": \"" + b.Status + "\", " +
                "\"ayrinti\": \"" + JsonKacir(b.Detail ?? string.Empty) + "\" }" + virgul);
        }

        govde.AppendLine("  ]");
        govde.AppendLine("}");

        File.WriteAllText(tam, govde.ToString());

        Console.WriteLine();
        Console.WriteLine("Rapor yazıldı: " + tam);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Rapor yazılamadı: " + ex.Message);
    }
}

/// <summary>Test patladığında ne olduğuna dair bir dosya bırakır.</summary>
static void WriteFailureReport(string path, Exception ex)
{
    try
    {
        var tam = MutlakYol(path);
        var govde = new System.Text.StringBuilder();
        govde.AppendLine("{");
        govde.AppendLine("  \"sonuc\": \"hata\",");
        govde.AppendLine("  \"tarih\": \"" + DateTimeOffset.Now.ToString("O") + "\",");
        govde.AppendLine("  \"hata\": \"" + JsonKacir(ex.Message) + "\",");
        govde.AppendLine("  \"tur\": \"" + ex.GetType().Name + "\"");
        govde.AppendLine("}");

        File.WriteAllText(tam, govde.ToString());

        Console.Error.WriteLine();
        Console.Error.WriteLine("Hata raporu yazıldı: " + tam);
        Console.Error.WriteLine("Bu dosyayı gönderirseniz sorunun sebebini bulabiliriz.");
    }
    catch (Exception yazmaHatasi)
    {
        Console.Error.WriteLine("Hata raporu yazılamadı: " + yazmaHatasi.Message);
    }
}

/// <summary>Bir dizgiyi JSON değeri olarak güvenli hâle getirir.</summary>
/// <remarks>
/// JsonSerializer yerine elle: serializer yansıma gerektiriyor ve kırpma
/// çözümleyicisi bunu haklı olarak reddediyor (paket kırpılmış yayınlanıyor).
/// Tek bir hata mesajını kaçışlamak için o makineye gerek yok.
/// </remarks>
static string JsonKacir(string metin)
    => metin
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .ReplaceLineEndings(" ");

/// <summary>Göreli yolu mutlaklaştırır.</summary>
/// <remarks>
/// Kullanıcıya HANGİ dosyayı göndereceğini söylemek için tam yol şart:
/// "zapret-tr-rapor.json" yazmak, dosyanın nerede oluştuğunu bilmeyen bir
/// kişiye hiçbir şey anlatmıyor.
/// </remarks>
static string MutlakYol(string path)
    => Path.IsPathRooted(path) ? path : Path.GetFullPath(path);

static void WriteReport(string path, ProbeReport report, IspProfile? profile)
{
    // Kasıtlı olarak dar: kişiyi tanımlayabilecek hiçbir alan yok.
    // IP adresi (ResolvedIp dahil), makine adı, kullanıcı adı dışarıda.
    //
    // Anonim tip DEĞİL, gerçek DTO: anonim tipler kaynak üretimiyle ele alınamıyor
    // ve bu yol kırpılmış yayında çalışan tek yansıma yolu olarak kalırdı.
    var document = new ReportDocument
    {
        CreatedAt = report.StartedAt.ToString("O"),
        DurationSeconds = Math.Round(report.Duration.TotalSeconds, 1),
        Isp = profile?.Id,
        IspDisplayName = profile?.DisplayName,
        Baseline = [.. report.Baseline.Select(b => new ReportBaseline
        {
            Host = b.Target.Host,
            Section = b.Target.Section.ToJsonName(),
            Category = b.Target.Category,
            Status = b.Status.ToString(),
            Detail = b.Detail,
        })],
        Attempts = [.. report.Attempts.Select(a => new ReportAttempt
        {
            CandidateId = a.CandidateId,
            Args = a.Args,
            Section = a.Section.ToJsonName(),
            TargetHost = a.TargetHost,
            TargetCategory = a.TargetCategory,
            Succeeded = a.Succeeded,
            Detail = a.Detail,
            DurationMs = (int)a.Duration.TotalMilliseconds,
        })],
        Winners = [.. report.Winners.Select(w => new ReportWinner
        {
            Section = w.Section.ToJsonName(),
            CandidateId = w.CandidateId,
            Args = w.Args,
            VerifiedFor = [.. w.VerifiedCategories],
        })],
    };

    var json = JsonSerializer.Serialize(document, ReportJsonContext.Relaxed.ReportDocument);

    File.WriteAllText(path, json);
}

internal sealed record CliOptions(
    string? IspId,
    bool BaselineOnly,
    int? MaxCandidates,
    bool Exhaustive,
    bool SaveLearned,
    IReadOnlyList<string> ExtraTargets,
    string? OutputPath,
    string? DiagnoseHost,
    string? EngageCheckHost,
    string? Strategy,
    string? Section,
    bool DetectIsp,
    bool Cleanup,
    bool Apply,
    bool UseSecureDns,
    string? DnsCommand,
    string? ServiceCommand,
    bool AssumeYes,
    bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        string? isp = null;
        string? output = null;
        string? diagnose = null;
        string? engage = null;
        string? strategy = null;
        string? section = null;
        var detectIsp = false;
        int? max = null;
        var exhaustive = false;
        var saveLearned = false;
        var baselineOnly = false;
        var cleanup = false;
        var apply = false;
        var useSecureDns = false;
        string? dnsCommand = null;
        string? serviceCommand = null;
        var assumeYes = false;
        var help = false;
        var extras = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--isp" when i + 1 < args.Length:
                    isp = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    extras.Add(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    // Göreli yol verilirse EXE'nin yanına yazıyoruz. Yükseltilmiş bir
                    // sürecin çalışma dizini C:\Windows\System32 oluyor; göreli yolu
                    // olduğu gibi kullanmak raporu oraya düşürüyordu ve kullanıcı
                    // dosyayı bulamıyordu.
                    var requested = args[++i];
                    output = Path.IsPathRooted(requested)
                        ? requested
                        : Path.Combine(AppContext.BaseDirectory, requested);
                    break;
                case "--max-candidates" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                    max = parsed;
                    i++;
                    break;
                case "--diagnose" when i + 1 < args.Length:
                    diagnose = args[++i];
                    break;
                case "--engage-check" when i + 1 < args.Length:
                    engage = args[++i];
                    break;
                case "--strategy" when i + 1 < args.Length:
                    strategy = args[++i];
                    break;
                case "--section" when i + 1 < args.Length:
                    section = args[++i].ToLowerInvariant();
                    break;
                case "--exhaustive":
                    exhaustive = true;
                    break;
                case "--save-learned":
                    saveLearned = true;
                    break;
                case "--detect-isp":
                    detectIsp = true;
                    break;
                case "--baseline-only":
                    baselineOnly = true;
                    break;
                case "--cleanup":
                    cleanup = true;
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--doh":
                    useSecureDns = true;
                    break;
                case "--dns" when i + 1 < args.Length:
                    dnsCommand = args[++i].ToLowerInvariant();
                    break;
                case "--service" when i + 1 < args.Length:
                    serviceCommand = args[++i].ToLowerInvariant();
                    break;
                case "--yes":
                case "-y":
                    assumeYes = true;
                    break;
                case "-h":
                case "--help":
                    help = true;
                    break;
            }
        }

        return new CliOptions(isp, baselineOnly, max, exhaustive, saveLearned, extras, output, diagnose, engage, strategy, section, detectIsp, cleanup, apply, useSecureDns, dnsCommand, serviceCommand, assumeYes, help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("ZapretTR parametre testi");
        Console.WriteLine();
        Console.WriteLine("  --isp <id|auto>         Servis sağlayıcı profili (ör. turk-telekom).");
        Console.WriteLine("                          auto = hattan tespit et, tanınmazsa genel arama.");
        Console.WriteLine("  --target <adres>        Sizde açılmayan bir adres ekler. Birden fazla verilebilir.");
        Console.WriteLine("                          Hangi bölüme yazılacağı --section ile belirlenir (varsayılan tcp443).");
        Console.WriteLine("  --baseline-only         Yalnızca neyin engelli olduğunu ölçer, winws başlatmaz.");
        Console.WriteLine("  --max-candidates <n>    Bölüm başına denenecek en fazla aday.");
        Console.WriteLine("  --exhaustive            İlk çalışan adayda durmaz, bütçe bitene kadar hepsini dener.");
        Console.WriteLine("                          (doğrulama verisi toplamak için; normal kullanımda gereksiz)");
        Console.WriteLine("  --save-learned          Doğrulanan adayları %ProgramData%\\ZapretTR\\learned.json'a yazar,");
        Console.WriteLine("                          böylece arayüz ve servis de kullanır. --isp gerekir.");
        Console.WriteLine("  --out <dosya.json>      Sonuç raporunu yazar (kişisel veri içermez).");
        Console.WriteLine("  --diagnose <adres>      Tek adresi dört protokolle dener, ham sonucu basar.");
        Console.WriteLine("  --engage-check <adres>  winws'i --debug=1 ile çalıştırıp paketleri görüp görmediğini gösterir.");
        Console.WriteLine("  --strategy \"<args>\"     --engage-check ile kullanılacak winws parametreleri.");
        Console.WriteLine("  --section <ad>          Bölüm adı: tcp80, tcp443, quic, discord-voice.");
        Console.WriteLine("                          --engage-check için varsayılan tcp80, --target için tcp443.");
        Console.WriteLine("  --detect-isp            Hangi hatta olduğunuzu tespit eder ve eşleşen profilleri listeler.");
        Console.WriteLine("  --doh                   Hedefleri şifreli DNS ile çözer (DNS kaçırma varsa şart).");
        Console.WriteLine("  --dns ac|kapat|durum    Sistem geneli şifreli DNS (dnscrypt-proxy).");
        Console.WriteLine("  --dns test              Tam döngüyü dener ve sistemi mutlaka eski haline döndürür.");
        Console.WriteLine("  --service kur|kaldir|durum|duraklat|devam   Otomatik başlatma servisi (--isp ve --doh ile).");
        Console.WriteLine("  --apply                 Seçili ISS yapılandırmasını çalıştırıp önce/sonra farkını ölçer.");
        Console.WriteLine("  --cleanup               winws'i durdurur ve WinDivert sürücüsünü kaldırır.");
        Console.WriteLine("  -y, --yes               Onay sorusunu sormaz (otomatik çalıştırma için).");
        Console.WriteLine("  -h, --help              Bu yardım.");
        Console.WriteLine();
        Console.WriteLine("Yönetici yetkisi gerekir.");
        Console.WriteLine();
    }
}
