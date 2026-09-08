using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

// ZapretTR saha testi araci.
//
// Iki isi var: gelistirirken motoru arayuzden bagimsiz kosturmak, ve baska
// birinin makinesinde (ornegin Superonline hattinda) hicbir sey kurmadan test
// yaptirmak. Ikincisi yuzunden kasitli olarak tek dosya, bagimlilıksiz ve
// ciktisi okunabilir tutuldu.

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

// --- Secili yapilandirmayi uygula ve olc -------------------------------------
// Arayuzdeki "Baslat" dugmesinin urettigi BIRLESIK komutu calistirip once/sonra
// farkini olcer. Tek bir adayi test etmekten farkli: gercekte kullanilan komut
// dort bolumu --new ile birlestiriyor ve o birlesik halin calistigi ayrica
// dogrulanmali.
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

    // Arayuzdekiyle AYNI kural (RuntimeSelection): secilen HTTPS stratejisi +
    // yalnizca dogrulanmis diger bolumler. Ikisi ayrisirsa arayuzde test edilen
    // sey ile gercekte calisan sey farkli olur.
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
    var runtimeArgs = applyBuilder.BuildRuntimeCommand(winners);

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
    }

    // Sifreli DNS istege bagli ama Turkiye'de cogu zaman SART: DNS kacirilmisken
    // baglanti zaten engel sunucusuna gider ve winws stratejisi hicbir sey
    // degistirmez. Ikisini birlikte olcmek, gercek kullanim senaryosu.
    DnsCryptRunner? applyDns = null;
    if (options.UseSecureDns)
    {
        applyDns = new DnsCryptRunner(applyVendor);
        Console.WriteLine("Şifreli DNS başlatılıyor...");
        await applyDns.StartAsync();
        Console.WriteLine("   [+] sistem DNS'i dnscrypt-proxy'ye yönlendirildi");
        Console.WriteLine();
    }

    // Olcum bolume gore secilmeli: QUIC ham QuicConnection ile, discord-voice
    // STUN ile. Burada duz HttpProbeClient kullaniliyordu ve QUIC hedefleri
    // HTTP uzerinden olculup "HTTP 200" donuyordu -- yani QUIC hic olculmuyordu.
    //
    // IP SABITLENMIYOR (pinnedIp: null) ve bu kasitli: --apply'in olctugu sey
    // "gercekten calisiyor mu", yani kullanicinin uygulamasinin gordugu yol.
    // --doh verildiginde dnscrypt zaten SISTEM DNS'ini yonlendiriyor, dolayisiyla
    // sistem cozumlemesi de sifreli oluyor.
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
    applyRunner.Start(runtimeArgs);
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
        // Calisan bir seyi bozmak, calismayan bir seyi duzeltmemekten kotu.
        Console.WriteLine("UYARI: Daha önce açılan bir hedef bu yapılandırmayla kapandı.");
    }

    Console.WriteLine();
    return fixedCount > 0 && brokeCount == 0 ? 0 : 1;
}

// --- Otomatik baslatma servisi ----------------------------------------------
if (options.ServiceCommand is { } serviceCommand)
{
    var svcVendor = VendorPaths.Locate();

    switch (serviceCommand)
    {
        case "durum":
        case "status":
        {
            var status = await ServiceManager.GetStatusAsync();
            Console.WriteLine($"winws servisi      : {(status.WinwsInstalled ? "kurulu" : "yok")}");
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
            var svcArgs = new WinwsCommandBuilder(svcVendor).BuildRuntimeCommand(svcWinners);

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

        default:
            Console.Error.WriteLine($"Bilinmeyen servis komutu: {serviceCommand} (kur | kaldir | durum)");
            return 4;
    }
}

// --- Sifreli DNS ------------------------------------------------------------
// Turkiye'de engelleme cogu zaman once DNS katmaninda; o katman asilmadan DPI
// stratejisi ise yaramiyor. Bu mod dnscrypt-proxy'yi calistirip sistem DNS'ini
// ona yonlendirir.
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

            // Yedek var ama proxy cevap vermiyorsa makine su an ad cozemiyor.
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
                try { stale.Kill(); } catch { /* zaten olmus */ }
                finally { stale.Dispose(); }
            }

            return 0;
        }

        case "test":
        {
            // Tam dongu, kendi kendini geri alarak. Amac guvenlik yolunu
            // dogrulamak: DNS degistirilebiliyor mu VE her kosulda geri
            // alinabiliyor mu. Test sonunda sistem mutlaka eski haline doner.
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

                // Geri alma gercekten oldu mu, yedegin yoklugundan degil
                // sistemin kendisinden dogrulaniyor.
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
// Bu arac baska birinin makinesinde calisiyor ve calisirken cekirdek modunda bir
// paket surucusu yukluyor. Onu kaldirabilmesi bir "ekstra" degil, sorumluluk.
if (options.Cleanup)
{
    Console.WriteLine("Temizlik yapılıyor...");
    Console.WriteLine();

    // removeConfig: false -- buradaki amac surucuyu ve calisan sureci kaldirmak,
    // kullanicinin ayarlarini silmek degil.
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

// --- Teshis modu ------------------------------------------------------------
// --- Hat tespiti ------------------------------------------------------------
// Olcum yapmadan once "hangi hattayim" sorusunu cevaplar. Yanlis hatta kosup
// sonucu yanlis profile yazmak bu projedeki en pahali sessiz hata: veri kirlenir
// ve hangi olcumun hangi sebekeye ait oldugu geri kazanilamaz.
//
// Ayrica IspDetector'i CLI'dan calistirabilen TEK yol bu. Onemi kirpilmis
// yayinlarda ortaya cikiyor: kirpma bir JSON yolunu bozarsa belirti calisma
// aninda gorunur, ve sinanamayan kod yolu sinanmamis kod yoludur.
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

// Tek bir adresi dort protokolle de deneyip ham sonucu basar. Destek istegi
// geldiginde "su komutun ciktisini gonder" diyebilecegimiz sey.
if (options.DiagnoseHost is { } diagnoseHost)
{
    Console.WriteLine($"Teşhis: {diagnoseHost}");
    Console.WriteLine();

    // --doh verilirse cozumleme sifreli yoldan yapilir. DNS kacirmasi olan bir
    // hatta sistem DNS'i engel sunucusunu dondurur ve teshis, DPI katmanini degil
    // DNS katmanini olcer.
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

        // Http3 slotu HttpProbeClient ile OLCULEMEZ: HTTP/3 IP'ye sabitlenemedigi
        // icin adres sistem DNS'i ile cozulur ve --doh sessizce etkisiz kalir.
        // Ham QUIC istemcisi IP ile SNI'yi ayri verebiliyor.
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

// --- Motor devrede mi kontrolu ----------------------------------------------
// "Strateji calismadi" ile "winws trafige hic dokunmadi" birbirinden cok farkli
// iki sonuc, ama disaridan ikisi de zaman asimi olarak gorunuyor. Bu mod winws'i
// --debug=1 ile calistirip paketleri gercekten gordugunu gosteriyor.
if (options.EngageCheckHost is { } engageHost)
{
    var engageVendor = VendorPaths.Locate();
    var engageProfiles = ProfileStore.Load();

    // Bolum secilebilir olmali. Sabit tcp80 ile QUIC hic teshis edilemiyordu:
    // winws QUIC Initial'i cozup SNI bulamazsa stratejiyi HIC uygulamiyor ve
    // disaridan bu, "strateji ise yaramadi" ile birebir ayni gorunuyor.
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
    // Bolumun NASIL olculdugu yaziliyor, ProbeMode degil: quic ve discord-voice
    // HttpProbeClient kullanmiyor, dolayisiyla ProbeMode orada anlamsiz bir deger.
    var engageHow = engageSection switch
    {
        StrategySection.Quic => "ham QUIC el sıkışması",
        StrategySection.DiscordVoice => "STUN (UDP)",
        _ => engageMode.ToString(),
    };

    Console.WriteLine($"Bölüm    : {engageSection.ToJsonName()} ({engageHow})");
    Console.WriteLine($"Strateji : {strategy}");
    Console.WriteLine();

    // Sistem DNS'i kacirilmissa hedef IP engel sunucusunu gosterir ve olcum
    // DPI'i degil DNS katmanini olcer. --doh verildiginde cozumleme sifreli
    // yoldan yapiliyor; boylece alttaki DPI katmani gorunur hale geliyor.
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

    runner.Start(engageArgs);
    await Task.Delay(1500);

    // Teshis, arama motorunun olctugu seyin AYNISINI olcmeli. Bu yuzden bolume
    // gore istemci secimi burada tekrar yazilmiyor, motorun kendi dagitimi
    // cagriliyor: QUIC ham QuicConnection ile, discord-voice STUN ile olculur.
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

    // winws'in kendi karar satirlari. "Paketi gordu mu" ile "gordugu paketi
    // degistirdi mi" ayri sorular; ikincisi sessizce hayir olabiliyor ve
    // gunlugun tamami icinde kaybolmasin diye ayrica ozetleniyor.
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

    // Global WinDivert filtresi butun udp/443 trafigini yakaladigi icin gunluk
    // makinedeki her QUIC baglantisini iceriyor -- on binlerce satir. Teshis icin
    // anlamli olan yalnizca HEDEF IP'ye ait olanlar; gerisi ekrani doldurup asil
    // satirlarin kaybolmasina yol aciyordu.
    var engageRelevant = engageSnapshot
        .Where(l => l.Contains(ip, StringComparison.Ordinal))
        .ToList();

    var verdicts = engageSnapshot
        .Where(l => verdictMarkers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    // Tam gunluk her zaman diske yazilir: bir teshis kosumunu yalnizca ciktiyi
    // kirptigi icin tekrarlamak, yonetici onayi gerektirdigi icin pahali.
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

// --- ISS secimi -------------------------------------------------------------
//
// "auto", saha paketi icin var. Paket herkese acik yayinlaniyor ve indiren
// kisinin hangi ISS'te oldugunu bilmiyoruz; sabit bir ISS yazmak, baska bir
// hattaki kullanicinin testini YANLIS profille baslatir (Tier 1 alakasiz
// adaylari once dener, butce onlara harcanir). Tespit basarisiz olursa hata
// degil: genel aramaya duserek test yine calisir.
IspProfile? profile = null;
if (string.Equals(options.IspId, "auto", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Servis sağlayıcı tespit ediliyor...");
    using var autoDetector = new IspDetector();
    var autoDetection = await autoDetector.DetectAsync(profiles);

    // Birden fazla profil eslesirse ilki aliniyor: siralama en iyi eslesme
    // once. Yanlis secim olumcul degil -- kazanan bulunamazsa arama zaten
    // diger profillere ve genel merdivene geciyor.
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
// --target hangi bolume yazilacak: varsayilan tcp443, --section ile degistirilir.
// Onceden HER kullanici hedefi tcp443'e gidiyordu, yani kullanici duz HTTP'de
// acilmayan bir adresi test edemiyordu -- tcp80 bolumu icin kendi hedefini
// ekleyemedigi gibi, verdigi adres yanlis bolumde olculuyordu.
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
        Console.Error.WriteLine($"Hedef çözümlenemedi, atlanıyor: {extra}");
        continue;
    }

    targets.Insert(0, parsed);
    Console.WriteLine($"Ek hedef         : {parsed.Host} ({parsed.Section.ToJsonName()})");
}

Console.WriteLine();

// --- Ne paylasilacagi konusunda seffaflik -----------------------------------
// Bu arac baska birinin makinesinde calisacak. Ne kaydettigini calismadan ONCE
// soylemek, sonradan aciklamaktan farkli bir sey.
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
        Console.Write("Devam edilsin mi? (E/h): ");
        var answer = Console.ReadLine()?.Trim();

        // Bos cevap (dogrudan Enter) onay SAYILMAZ. Baskasinin makinesinde
        // calisan ve cekirdek surucusu yukleyen bir arac icin varsayilan "hayir"
        // olmali; kullanici bilerek "evet" demeli.
        var accepted = answer is not null
                       && (answer.Equals("e", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("evet", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                           || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));

        if (!accepted)
        {
            Console.WriteLine();
            Console.WriteLine("İptal edildi. Hiçbir değişiklik yapılmadı.");
            return 0;
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
    // --- Koruma zaten acik mi ----------------------------------------------
    //
    // Aciksa bu olcum YANILTICI olur: hedefler zaten aciliyor, arac da "engel
    // yok" diyor. Gercek bir kullanicida tam olarak bu oldu -- makinesinde
    // ZapretTR servisi calisirken saha testini kosturdu, 11 hedefin 11'i
    // "aciliyor" cikti ve sonuc "DPI ile engellenen hedef yok" oldu. Olcum
    // dogruydu, yalnizca olculen sey engelleme degil KENDI KORUMAMIZDI.
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

    // --- Mevcut durum taramasi ---------------------------------------------
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

    // Kontrol hedefi engellenmemesi BEKLENEN bir adres. Erisilemiyorsa sorun
    // DPI'da degil olcum yolumuzda ya da baglantida demektir; bu durumda tum
    // baseline sonuclari supheli ve strateji aramasi anlamsiz olur.
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

    // DNS yonlendirmesi zapret'in cozebilecegi bir sey degil ve bunu soylememek
    // kullaniciyi bosuna bekletmek olur: strateji aramasi yuzlerce aday deneyip
    // hicbiri calismadigi icin "bulunamadi" derdi.
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

        // "Engel yok" DA bir sonuctur ve rapor edilmeli. Eskiden bu yolda
        // hicbir dosya yazilmadan cikiliyordu: kullanici testi calistiriyor,
        // ekranda dolu dolu cikti goruyor, sonra klasorde JSON bulamiyordu.
        // Gercek bir kullanicida tam olarak bu yasandi -- ustelik o kosum bize
        // "bu hatta su an engel yok" bilgisini veriyordu, ki toplamak
        // istedigimiz seyin ta kendisi.
        WriteBaselineReport(options.OutputPath, baseline, profile, "engel-yok");
        return 0;
    }

    if (options.BaselineOnly)
    {
        WriteBaselineReport(options.OutputPath, baseline, profile, "yalnizca-tarama");
        return 0;
    }

    // --- Strateji aramasi ---------------------------------------------------
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
        // Normalde ilk calisan adayda durulur; kullaniciyi bekletmemek icin dogru
        // davranis bu. --exhaustive ise bunun tersini ister: amac calisan BIR
        // strateji bulmak degil, o hatta hangi adaylarin calistigini haritalamak.
        // Profillerin siralamasi ancak bu veriyle duzeltilebiliyor.
        stopAtFirstSuccess: !options.Exhaustive,
        maxCandidatesPerSection: options.MaxCandidates,
        // Yukarida zaten tarandi; tekrar taramak hem bir dakikadan fazla surer
        // hem de farkli siniflandirma uretip yanlis bolumlerde arama baslatir.
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

    // --- Calisan HER aday --------------------------------------------------
    // Kazanan tek adaydir; ama profil siralamasini duzeltmek ve adaylari
    // "verified"e cekmek icin gereken sey calisan adaylarin TAMAMI. Bu liste
    // ancak --exhaustive ile anlamli doluyor, cunku normal koşumda arama ilk
    // basaridan sonra duruyor.
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

    // --- Ogrenilenleri kalici hale getir ------------------------------------
    // Arayuz bunu kendi test akisinda zaten yapiyordu; CLI yapmiyordu. Yani bu
    // araci calistirip calisan strateji bulan biri, arayuzu actiginda bulunanin
    // hicbirini gormuyordu. Bayrakla opsiyonel: bu arac baskasinin makinesinde de
    // calisiyor ve oradaki soz "sonuclar yalnizca rapor dosyasina yazilir".
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

    // Hata raporu da bir rapordur. Eskiden bu yolda HICBIR dosya
    // yazilmiyordu: test patlayinca kullanicinin elinde gonderecek bir sey
    // kalmiyor, "test yaptim ama json olusmadi" diyordu -- ve neyin
    // patladigini kimse ogrenemiyordu. Saha paketinin tek isi veri
    // toplamak; en cok da is ters gittiginde veri gerekiyor.
    if (options.OutputPath is not null)
    {
        WriteFailureReport(options.OutputPath, ex);
    }

    return 5;
}

/// <summary>Strateji aranmadan cikildiginda mevcut durumu dosyaya yazar.</summary>
/// <remarks>
/// Strateji bulunmamis olmasi raporu degersiz yapmiyor: hangi hedefin acildigi,
/// hangisinin engellendigi ve DNS'in yonlendirilip yonlendirilmedigi tek basina
/// bir olcum. Saha paketinin isi zaten bu veriyi toplamak.
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

/// <summary>Test patladiginda ne olduguna dair bir dosya birakir.</summary>
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

/// <summary>Bir dizgiyi JSON degeri olarak guvenli hale getirir.</summary>
/// <remarks>
/// JsonSerializer yerine elle: serializer yansima gerektiriyor ve kirpma
/// analizoru bunu hakli olarak reddediyor (paket kirpilmis yayinlaniyor).
/// Tek bir hata mesajini kacirmak icin o makineye gerek yok.
/// </remarks>
static string JsonKacir(string metin)
    => metin
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .ReplaceLineEndings(" ");

/// <summary>Goreli yolu mutlaklastirir.</summary>
/// <remarks>
/// Kullaniciya HANGI dosyayi gonderecegini soylemek icin tam yol sart:
/// "zapret-tr-rapor.json" yazmak, dosyanin nerede olustugunu bilmeyen bir
/// kisiye hicbir sey anlatmiyor.
/// </remarks>
static string MutlakYol(string path)
    => Path.IsPathRooted(path) ? path : Path.GetFullPath(path);

static void WriteReport(string path, ProbeReport report, IspProfile? profile)
{
    // Kasitli olarak dar: kisiyi tanimlayabilecek hicbir alan yok.
    // IP adresi (ResolvedIp dahil), makine adi, kullanici adi disarida.
    //
    // Anonim tip DEGIL, gercek DTO: anonim tipler kaynak uretimiyle ele alinamiyor
    // ve bu yol kirpilmis yayinda calisan tek yansima yolu olarak kalirdi.
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
                    // Goreli yol verilirse EXE'nin yanina yaziyoruz. Yukseltilmis bir
                    // surecin calisma dizini C:\Windows\System32 oluyor; goreli yolu
                    // oldugu gibi kullanmak raporu oraya dusuruyordu ve kullanici
                    // dosyayi bulamiyordu.
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
        Console.WriteLine("  --service kur|kaldir|durum   Otomatik başlatma servisi (--isp ve --doh ile).");
        Console.WriteLine("  --apply                 Seçili ISS yapılandırmasını çalıştırıp önce/sonra farkını ölçer.");
        Console.WriteLine("  --cleanup               winws'i durdurur ve WinDivert sürücüsünü kaldırır.");
        Console.WriteLine("  -y, --yes               Onay sorusunu sormaz (otomatik çalıştırma için).");
        Console.WriteLine("  -h, --help              Bu yardım.");
        Console.WriteLine();
        Console.WriteLine("Yönetici yetkisi gerekir.");
        Console.WriteLine();
    }
}
