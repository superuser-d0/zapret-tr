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
    Console.WriteLine("Rapor dosyası yazılacak: " + options.OutputPath);
    Console.WriteLine("İçeriği: ISS adı, test edilen adresler, denenen parametreler ve sonuçları,");
    Console.WriteLine("         süreler. IP adresiniz, bilgisayar adınız veya kullanıcı adınız YAZILMAZ.");
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

var prober = new StrategyProber(vendor, profiles, targets);

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

    var blockedCount = baseline.Count(b => b.Status == BaselineStatus.Blocked);
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
        var line = $"   {p.TierLabel} · {p.Completed + 1}/{p.Total} · {p.CurrentDescription}";
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
    bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        string? isp = null;
        string? output = null;
        string? diagnose = null;
        int? max = null;
        var baselineOnly = false;
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
                    output = args[++i];
                    break;
                case "--max-candidates" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                    max = parsed;
                    i++;
                    break;
                case "--diagnose" when i + 1 < args.Length:
                    diagnose = args[++i];
                    break;
                case "--baseline-only":
                    baselineOnly = true;
                    break;
                case "-h":
                case "--help":
                    help = true;
                    break;
            }
        }

        return new CliOptions(isp, baselineOnly, max, extras, output, diagnose, help);
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
        Console.WriteLine("  -h, --help              Bu yardım.");
        Console.WriteLine();
        Console.WriteLine("Yönetici yetkisi gerekir.");
        Console.WriteLine();
    }
}
