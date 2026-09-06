using System.Diagnostics;
using System.Runtime.Versioning;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>
/// Parametre test motoru. blockcheck.sh'in yerini alir.
/// </summary>
/// <remarks>
/// Hizin kaynagi paralellik degil SIRALAMA. blockcheck tum kombinasyon uzayini
/// bastan sona tarar; burada arama kademeli daraltiliyor:
///
///   Tier 1  secilen ISP'nin profili        (Superonline icin 19 aday)
///   Tier 2  komsu TR profilleri
///   Tier 3  genel kombinatoryal merdiven   (180 aday)
///
/// Her bolum (tcp80 / tcp443 / quic / discord-voice) BAGIMSIZ aranir ve bagimsiz
/// kazanani olur. Bunun sebebi upstream belgesindeki su detay: md5sig yalnizca
/// sunucu TCP MD5 secenegini reddettiginde ise yarar, yani calisan strateji hedef
/// sunucuya da bagli. Tek bir "kazanan parametre" aramak yanlis soru olurdu.
///
/// Su an testler SIRALI kosuyor. Paralel kosum icin gereken izolasyon mekanizmasi
/// (her isciye --ipset-ip ile kendi hedefi) komut kurucuda hazir ama davranisi
/// henuz dogrulanmadi; dogrulanana kadar sirali kalmak dogru olan, cunku birbirine
/// karisan iki winws ornegi sessizce YANLIS sonuc uretir -- yavas olmaktan cok
/// daha kotu.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class StrategyProber(
    VendorPaths vendor,
    ProfileStore profiles,
    IReadOnlyList<ProbeTarget> targets)
{
    private readonly WinwsCommandBuilder _commandBuilder = new(vendor);

    /// <summary>Bir adayin uygulanmasindan sonra ag yiginin oturmasi icin beklenen sure.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Dogrulama tekrari oncesi beklenen sure.</summary>
    private static readonly TimeSpan ConfirmationGap = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Engellenmemesi beklenen hedeflerin kategori adi. Bir bolumun kontrol hedefi
    /// acilmiyorsa o bolumun sonuclari guvenilmez ve arama yapilmaz.
    /// </summary>
    public const string ControlCategory = "kontrol";

    /// <summary>Testi calistirir.</summary>
    /// <param name="profile">
    /// Kullanicinin sectigi ISP profili. null verilirse Tier 1 atlanir ve dogrudan
    /// komsu profillerden baslanir.
    /// </param>
    /// <param name="stopAtFirstSuccess">
    /// true ise her bolumde ilk calisan aday bulununca o bolumun aramasi biter.
    /// false ise tum tier'lar taranip en iyi aday secilir -- kullanici "daha iyisini
    /// ara" dediginde kullanilir.
    /// </param>
    /// <param name="maxCandidatesPerSection">
    /// Bir bolumde denenecek en fazla aday sayisi. null ise sinirsiz.
    /// Butun tier'lar boyunca ortak bir butce olarak sayilir, boylece Tier 3'un
    /// 180 adayi kullaniciyi belirsiz sure bekletemez.
    /// </param>
    /// <param name="knownBaseline">
    /// Daha once hesaplanmis mevcut durum taramasi. Verilirse yeniden taranmaz.
    /// </param>
    /// <remarks>
    /// <paramref name="knownBaseline"/> yalnizca hiz icin degil DOGRULUK icin de var:
    /// tarama iki kez kosuldugunda ayni hedef iki farkli sonuc verebiliyor (DNS
    /// cevabi ya da zaman asimi degisince engel sayfasi yerine zaman asimi gorunuyor),
    /// ve ikinci sonuc birincisini sessizce eziyor. Ilk gercek kosumda tam bu oldu:
    /// DNS yonlendirmesi olarak dogru siniflanmis hedefler ikinci taramada "DPI
    /// engeli" sayilip bosuna dort dakika strateji arandi.
    /// </remarks>
    public async Task<ProbeReport> RunAsync(
        IspProfile? profile,
        IProgress<ProbeProgress>? progress = null,
        bool stopAtFirstSuccess = true,
        int? maxCandidatesPerSection = null,
        IReadOnlyList<BaselineResult>? knownBaseline = null,
        CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        var baseline = knownBaseline
                       ?? await RunBaselineAsync(progress, cancellationToken).ConfigureAwait(false);

        var attempts = new List<CandidateResult>();
        var winners = new List<SectionWinner>();

        // Kontrol hedefi acilmayan bolumler aranmaz. Kontrol, engellenmemesi
        // BEKLENEN bir adres; o da acilmiyorsa olcum yolunda ya da baglantida bir
        // sorun var demektir ve o bolumun "engelli" sonuclari guvenilmez.
        // Ilk saha kosumunda QUIC bolumunde tam bu oldu: butun hedefler "HTTP/3
        // baglantisi kurulamadi" verdi ve bu engelleme sanilip 40 aday bosuna
        // denendi. Oysa ayni hata QUIC'in makinede hic calismamasi durumunda da
        // ciktigi icin ikisi ayirt edilemiyordu.
        var unreliableSections = baseline
            .Where(b => b.Target.Category == ControlCategory && b.Status != BaselineStatus.Accessible)
            .Select(b => b.Target.Section)
            .ToHashSet();

        // Yalnizca gercekten engelli hedefi olan bolumler aranir. Engelli hedefi
        // olmayan bir bolumu aramak anlamsiz: her aday "basarili" gorunurdu.
        // Kontrol hedefleri de aranmaz -- onlar zaten acilmasi beklenen adresler.
        var sectionsToSearch = baseline
            .Where(b => b.Status == BaselineStatus.Blocked
                        && b.Target.Category != ControlCategory
                        && !unreliableSections.Contains(b.Target.Section))
            .GroupBy(b => b.Target.Section)
            .ToList();

        foreach (var group in sectionsToSearch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var winner = await SearchSectionAsync(
                group.Key,
                [.. group],
                profile,
                attempts,
                progress,
                stopAtFirstSuccess,
                maxCandidatesPerSection,
                cancellationToken).ConfigureAwait(false);

            if (winner is not null)
            {
                winners.Add(winner);
            }
        }

        return new ProbeReport(
            startedAt,
            stopwatch.Elapsed,
            profile?.Id,
            Asn: null,
            OrgName: null,
            baseline,
            attempts,
            winners);
    }

    /// <summary>
    /// winws KAPALIYKEN hangi hedeflerin gercekten erisilemedigini belirler.
    /// </summary>
    /// <remarks>
    /// Testin en kritik adimi. Atlanirsa zaten acilan bir hedefle test yapilir ve
    /// her strateji calisiyor gorunur -- blockcheck de bu yuzden once erisim
    /// kontrolu yapiyor.
    /// </remarks>
    public async Task<IReadOnlyList<BaselineResult>> RunBaselineAsync(
        IProgress<ProbeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BaselineResult>();
        using var client = new HttpProbeClient();

        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[i];

            progress?.Report(new ProbeProgress(
                ProbeTier.Baseline,
                "Mevcut durum taraniyor",
                i,
                targets.Count,
                target.Label));

            var outcome = await client
                .TryReachAsync(target.Host, ModeFor(target.Section), pinnedIp: null, cancellationToken)
                .ConfigureAwait(false);

            var status = outcome switch
            {
                // Engel sayfasi kontrolu basari kontrolunden ONCE gelmeli: engel
                // sayfasi HTTP 200 donduruyor, dolayisiyla sirayi ters kurmak onu
                // "aciliyor" olarak isaretlerdi.
                { IsBlockPage: true } => BaselineStatus.DnsRedirected,
                { Succeeded: true } => BaselineStatus.Accessible,
                { ResolvedIp: null } => BaselineStatus.Inconclusive,
                _ => BaselineStatus.Blocked,
            };

            results.Add(new BaselineResult(target, status, outcome.Detail, outcome.ResolvedIp));
        }

        progress?.Report(new ProbeProgress(
            ProbeTier.Baseline, "Mevcut durum taraniyor", targets.Count, targets.Count, "tamamlandi"));

        return PropagateDnsRedirection(results);
    }

    /// <summary>
    /// Bir hedefin herhangi bir bolumde DNS yonlendirmesi tespit edildiyse, ayni
    /// alan adinin diger bolumlerini de oyle isaretler.
    /// </summary>
    /// <remarks>
    /// DNS yonlendirmesi alan adi bazinda olur, protokol bazinda degil: cevap
    /// degistirildiginde TCP de UDP de ayni yanlis sunucuya gider. Ama belirti
    /// protokole gore degisiyor -- engel sunucusu HTTPS'i karsilayip engel sayfasi
    /// donerken QUIC'i hic cevaplamiyor, bu da zaman asimi olarak gorunup "DPI
    /// engeli" sanilmasina yol aciyor. O bolumde yapilacak strateji aramasi
    /// bastan kayip: hicbir aday calismaz cunku sorun DPI degil.
    /// </remarks>
    private static List<BaselineResult> PropagateDnsRedirection(List<BaselineResult> results)
    {
        var redirectedHosts = results
            .Where(r => r.Status == BaselineStatus.DnsRedirected)
            .Select(r => r.Target.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (redirectedHosts.Count == 0)
        {
            return results;
        }

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (result.Status == BaselineStatus.Blocked && redirectedHosts.Contains(result.Target.Host))
            {
                results[i] = result with
                {
                    Status = BaselineStatus.DnsRedirected,
                    Detail = $"{result.Detail} (ayni alan adi baska bir protokolde DNS yonlendirmesi gosterdi)",
                };
            }
        }

        return results;
    }

    private async Task<SectionWinner?> SearchSectionAsync(
        StrategySection section,
        IReadOnlyList<BaselineResult> blockedTargets,
        IspProfile? profile,
        List<CandidateResult> attempts,
        IProgress<ProbeProgress>? progress,
        bool stopAtFirstSuccess,
        int? budget,
        CancellationToken cancellationToken)
    {
        SectionWinner? best = null;
        var remaining = budget ?? int.MaxValue;

        // Arama sirasinda engel sayfasi dondugu icin vazgecilen hedefler.
        var abandoned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tier, label, candidates) in BuildTiers(section, profile))
        {
            if (remaining <= 0)
            {
                break;
            }

            var candidateList = candidates.Take(remaining).ToList();
            if (candidateList.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < candidateList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidateList[i];
                remaining--;

                progress?.Report(new ProbeProgress(
                    tier, label, i, candidateList.Count, $"{section.ToJsonName()} · {candidate.Id}"));

                var verified = await TryCandidateAsync(
                    section, candidate, blockedTargets, attempts, abandoned, cancellationToken).ConfigureAwait(false);

                if (verified.Count == 0)
                {
                    continue;
                }

                var winner = new SectionWinner(section, candidate.Id, candidate.Args, verified);

                // Daha fazla hedef sinifini acan aday daha iyidir.
                if (best is null || verified.Count > best.VerifiedCategories.Count)
                {
                    best = winner;
                }

                if (stopAtFirstSuccess)
                {
                    progress?.Report(new ProbeProgress(
                        tier, label, i + 1, candidateList.Count, $"bulundu: {candidate.Id}"));
                    return best;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Bir adayi calistirip engelli hedeflerde deneyip hangi hedef siniflarini actigini dondurur.
    /// </summary>
    private async Task<IReadOnlyList<string>> TryCandidateAsync(
        StrategySection section,
        ProbeCandidate candidate,
        IReadOnlyList<BaselineResult> blockedTargets,
        List<CandidateResult> attempts,
        HashSet<string> abandoned,
        CancellationToken cancellationToken)
    {
        var opened = new List<string>();

        foreach (var blocked in blockedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (blocked.ResolvedIp is null || abandoned.Contains(blocked.Target.Host))
            {
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var runner = new WinwsRunner(vendor);

            try
            {
                var arguments = _commandBuilder.BuildProbeCommand(section, candidate.Args, blocked.ResolvedIp);

                // Once winws'in kendisine dogrulat. Gecersiz bir aday burada
                // ~50 ms'de elenir; yoksa surucu acilir, ag istegi zaman asimina
                // ugrar ve sonuc "zaman asimi" olarak kaydedilir -- yani gercekte
                // parametre hatasi olan bir sey engelleme sanilir.
                var validationError = await runner.ValidateAsync(arguments, cancellationToken).ConfigureAwait(false);
                if (validationError is not null)
                {
                    stopwatch.Stop();
                    attempts.Add(new CandidateResult(
                        candidate.Id, candidate.Args, section,
                        blocked.Target.Host, blocked.Target.Category,
                        false, "gecersiz parametre: " + validationError, stopwatch.Elapsed));
                    continue;
                }

                runner.Start(arguments);

                // winws'in WinDivert filtresini kurmasi anlik degil; hemen istek
                // atarsak strateji henuz devrede olmaz ve calisan bir aday
                // basarisiz gorunur.
                await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);

                using var client = new HttpProbeClient();
                var outcome = await client
                    .TryReachAsync(blocked.Target.Host, ModeFor(section), blocked.ResolvedIp, cancellationToken)
                    .ConfigureAwait(false);

                stopwatch.Stop();

                attempts.Add(new CandidateResult(
                    candidate.Id,
                    candidate.Args,
                    section,
                    blocked.Target.Host,
                    blocked.Target.Category,
                    outcome.Succeeded,
                    outcome.Detail,
                    stopwatch.Elapsed));

                if (outcome.IsBlockPage)
                {
                    // Engel sayfasi geldi: baglanti kuruldu ama yanlis sunucuya.
                    // Bu DNS yonlendirmesidir ve hicbir desync stratejisi duzeltemez.
                    // Bu hedefte kalan adaylari denemek zaman kaybi.
                    abandoned.Add(blocked.Target.Host);
                }

                if (outcome.Succeeded && !opened.Contains(blocked.Target.Category))
                {
                    // Tek basarili deneme yeterli DEGIL. Ilk gercek kosumda bir aday
                    // calisti, ayni aday sonraki kosumda calismadi -- ag kosullari
                    // gurultulu ve tek olcum bunu ayirt edemiyor. Kullaniciya
                    // "bulundu" deyip sonra calismamasi, hic bulamamaktan kotu.
                    if (await ConfirmAsync(section, blocked, cancellationToken).ConfigureAwait(false))
                    {
                        opened.Add(blocked.Target.Category);
                    }
                    else
                    {
                        attempts.Add(new CandidateResult(
                            candidate.Id, candidate.Args, section,
                            blocked.Target.Host, blocked.Target.Category,
                            false, "dogrulama tekrarinda basarisiz (kararsiz)", stopwatch.Elapsed));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                attempts.Add(new CandidateResult(
                    candidate.Id, candidate.Args, section,
                    blocked.Target.Host, blocked.Target.Category,
                    false, "calistirilamadi: " + ex.Message, stopwatch.Elapsed));
            }
            finally
            {
                await runner.StopAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
        }

        return opened;
    }

    /// <summary>
    /// Basarili bulunan bir denemeyi, winws hala calisirken bir kez daha dogrular.
    /// </summary>
    /// <remarks>
    /// Ag kosullari gurultulu; tek bir basarili istek stratejinin calistigini
    /// kanitlamiyor. Iki ust uste basari, kullaniciya "bulundu" demek icin
    /// gereken en az kanit.
    /// </remarks>
    private static async Task<bool> ConfirmAsync(
        StrategySection section, BaselineResult target, CancellationToken cancellationToken)
    {
        await Task.Delay(ConfirmationGap, cancellationToken).ConfigureAwait(false);

        using var client = new HttpProbeClient();
        var outcome = await client
            .TryReachAsync(target.Target.Host, ModeFor(section), target.ResolvedIp, cancellationToken)
            .ConfigureAwait(false);

        return outcome.Succeeded;
    }

    /// <summary>Bir bolum icin denenecek adaylari tier sirasinda uretir.</summary>
    private IEnumerable<(ProbeTier Tier, string Label, IEnumerable<ProbeCandidate> Candidates)> BuildTiers(
        StrategySection section, IspProfile? profile)
    {
        if (profile is not null)
        {
            yield return (
                ProbeTier.IspProfile,
                $"{profile.DisplayName} profili",
                profile.CandidatesFor(section).Select(c => new ProbeCandidate(c.Id, c.Args)));
        }

        var neighbours = profile is null ? profiles.Profiles : profiles.NeighboursOf(profile);
        yield return (
            ProbeTier.Neighbours,
            "Diger TR profilleri",
            neighbours
                .SelectMany(p => p.CandidatesFor(section).Select(c => new ProbeCandidate($"{p.Id}/{c.Id}", c.Args)))
                .DistinctBy(c => c.Args, StringComparer.Ordinal));

        yield return (
            ProbeTier.GenericLadder,
            "Genel arama",
            profiles.Ladder.Expand(section)
                .Select(c => new ProbeCandidate($"ladder/{c.Family}", c.Args))
                .DistinctBy(c => c.Args, StringComparer.Ordinal));
    }

    /// <summary>
    /// Bolum icin kullanilacak sinama protokolu.
    /// </summary>
    /// <remarks>
    /// Bu eslemeyi yanlis yapmak sessizce yanlis sonuc uretir; ilk saha kosumunda
    /// tam da bu oldu: duz HTTP sunan bir hedefe HTTPS ile gidilmis ve hedef
    /// "engelli" sayilmisti.
    ///
    /// tcp443 icin TLS 1.2 kasitli secildi: sertifika DPI'a acik gorunur, yani DPI'in
    /// en cok mudahale ettigi durum. TLS 1.3'te ServerHello sifreli oldugu icin bazi
    /// engellemeler devreye bile girmez ve test kolay gecerek yaniltir.
    /// </remarks>
    public static ProbeMode ModeFor(StrategySection section) => section switch
    {
        StrategySection.Tcp80 => ProbeMode.PlainHttp,
        StrategySection.Tcp443 => ProbeMode.Tls12,
        StrategySection.Quic => ProbeMode.Http3,
        StrategySection.DiscordVoice => ProbeMode.Http3,
        _ => ProbeMode.Tls12,
    };

    private readonly record struct ProbeCandidate(string Id, string Args);
}
