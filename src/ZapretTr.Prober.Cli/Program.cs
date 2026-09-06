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

    var applyTargets = ProbeTargetStore.Load(applyProfiles.Root);

    Console.WriteLine("Önce (winws kapalı):");
    var before = new Dictionary<string, bool>();
    using (var c = new HttpProbeClient())
    {
        foreach (var t in applyTargets)
        {
            var r = await c.TryReachAsync(t.Host, StrategyProber.ModeFor(t.Section));
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
            var r = await c.TryReachAsync(t.Host, StrategyProber.ModeFor(t.Section));
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
// Tek bir adresi dort protokolle de deneyip ham sonucu basar. Destek istegi
// geldiginde "su komutun ciktisini gonder" diyebilecegimiz sey.
if (options.DiagnoseHost is { } diagnoseHost)
{
    Console.WriteLine($"Teşhis: {diagnoseHost}");
    Console.WriteLine();

    using var diagnostic = new HttpProbeClient(TimeSpan.FromSeconds(10));
    foreach (var mode in Enum.GetValues<ProbeMode>())
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await diagnostic.TryReachAsync(diagnoseHost, mode);
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

    var strategy = options.Strategy
                   ?? engageProfiles.FindById("turk-telekom")?
                       .CandidatesFor(StrategySection.Tcp80).FirstOrDefault()?.Args
                   ?? "--dpi-desync=fake,fakedsplit --dpi-desync-fooling=md5sig";

    Console.WriteLine($"Hedef    : {engageHost}");
    Console.WriteLine($"Strateji : {strategy}");
    Console.WriteLine();

    var addresses = await System.Net.Dns.GetHostAddressesAsync(engageHost);
    var ip = addresses.First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();

    var engageBuilder = new WinwsCommandBuilder(engageVendor);
    var engageArgs = engageBuilder.BuildProbeCommand(StrategySection.Tcp80, strategy, ip).ToList();
    engageArgs.Add("--debug=1");

    var log = new List<string>();
    var runner = new WinwsRunner(engageVendor);
    runner.LogLineReceived += line => { lock (log) { log.Add(line.Text); } };

    runner.Start(engageArgs);
    await Task.Delay(1500);

    using (var engageClient = new HttpProbeClient(TimeSpan.FromSeconds(8)))
    {
        var engageResult = await engageClient.TryReachAsync(engageHost, ProbeMode.PlainHttp, ip);
        Console.WriteLine($"Probe sonucu: {(engageResult.Succeeded ? "BAŞARILI" : "başarısız")} — {engageResult.Detail}");
    }

    await Task.Delay(500);
    await runner.StopAsync();

    Console.WriteLine();
    Console.WriteLine($"winws günlüğü ({log.Count} satır):");
    lock (log)
    {
        foreach (var line in log.Take(40))
        {
            Console.WriteLine("   " + line);
        }
    }

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
IspProfile? profile = null;
if (options.IspId is not null)
{
    profile = profiles.FindById(options.IspId);
    if (profile is null)
    {
        Console.Error.WriteLine($"Bilinmeyen servis sağlayıcı: {options.IspId}");
        Console.Error.WriteLine("Mevcut olanlar: " + string.Join(", ", profiles.Profiles.Select(p => p.Id)));
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

// --- Hedefler ---------------------------------------------------------------
var targets = ProbeTargetStore.Load(profiles.Root).ToList();
foreach (var extra in options.ExtraTargets)
{
    var parsed = ProbeTargetStore.TryParseUserTarget(extra);
    if (parsed is null)
    {
        Console.Error.WriteLine($"Hedef çözümlenemedi, atlanıyor: {extra}");
        continue;
    }

    targets.Insert(0, parsed);
    Console.WriteLine("Ek hedef         : " + parsed.Host);
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
        return 0;
    }

    if (options.BaselineOnly)
    {
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
        stopAtFirstSuccess: true,
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

    if (options.OutputPath is not null)
    {
        WriteReport(options.OutputPath, report, profile);
        Console.WriteLine();
        Console.WriteLine("Rapor yazıldı: " + options.OutputPath);
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
    return 5;
}

static void WriteReport(string path, ProbeReport report, IspProfile? profile)
{
    // Kasitli olarak dar: kisiyi tanimlayabilecek hicbir alan yok.
    // IP adresi (ResolvedIp dahil), makine adi, kullanici adi disarida.
    var document = new
    {
        schema = 1,
        createdAt = report.StartedAt.ToString("O"),
        durationSeconds = Math.Round(report.Duration.TotalSeconds, 1),
        isp = profile?.Id,
        ispDisplayName = profile?.DisplayName,
        baseline = report.Baseline.Select(b => new
        {
            host = b.Target.Host,
            section = b.Target.Section.ToJsonName(),
            category = b.Target.Category,
            status = b.Status.ToString(),
            detail = b.Detail,
        }),
        attempts = report.Attempts.Select(a => new
        {
            candidateId = a.CandidateId,
            args = a.Args,
            section = a.Section.ToJsonName(),
            targetHost = a.TargetHost,
            targetCategory = a.TargetCategory,
            succeeded = a.Succeeded,
            detail = a.Detail,
            durationMs = (int)a.Duration.TotalMilliseconds,
        }),
        winners = report.Winners.Select(w => new
        {
            section = w.Section.ToJsonName(),
            candidateId = w.CandidateId,
            args = w.Args,
            verifiedFor = w.VerifiedCategories,
        }),
    };

    var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    File.WriteAllText(path, json);
}

internal sealed record CliOptions(
    string? IspId,
    bool BaselineOnly,
    int? MaxCandidates,
    IReadOnlyList<string> ExtraTargets,
    string? OutputPath,
    string? DiagnoseHost,
    string? EngageCheckHost,
    string? Strategy,
    bool Cleanup,
    bool Apply,
    bool UseSecureDns,
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
        int? max = null;
        var baselineOnly = false;
        var cleanup = false;
        var apply = false;
        var useSecureDns = false;
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

        return new CliOptions(isp, baselineOnly, max, extras, output, diagnose, engage, strategy, cleanup, apply, useSecureDns, assumeYes, help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("ZapretTR parametre testi");
        Console.WriteLine();
        Console.WriteLine("  --isp <id>              Servis sağlayıcı profili (ör. superonline, turk-telekom)");
        Console.WriteLine("  --target <adres>        Sizde açılmayan bir adres ekler. Birden fazla verilebilir.");
        Console.WriteLine("  --baseline-only         Yalnızca neyin engelli olduğunu ölçer, winws başlatmaz.");
        Console.WriteLine("  --max-candidates <n>    Bölüm başına denenecek en fazla aday.");
        Console.WriteLine("  --out <dosya.json>      Sonuç raporunu yazar (kişisel veri içermez).");
        Console.WriteLine("  --diagnose <adres>      Tek adresi dört protokolle dener, ham sonucu basar.");
        Console.WriteLine("  --engage-check <adres>  winws'i --debug=1 ile çalıştırıp paketleri görüp görmediğini gösterir.");
        Console.WriteLine("  --strategy \"<args>\"     --engage-check ile kullanılacak winws parametreleri.");
        Console.WriteLine("  --doh                   Hedefleri şifreli DNS ile çözer (DNS kaçırma varsa şart).");
        Console.WriteLine("  --apply                 Seçili ISS yapılandırmasını çalıştırıp önce/sonra farkını ölçer.");
        Console.WriteLine("  --cleanup               winws'i durdurur ve WinDivert sürücüsünü kaldırır.");
        Console.WriteLine("  -y, --yes               Onay sorusunu sormaz (otomatik çalıştırma için).");
        Console.WriteLine("  -h, --help              Bu yardım.");
        Console.WriteLine();
        Console.WriteLine("Yönetici yetkisi gerekir.");
        Console.WriteLine();
    }
}
