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
    public async Task<ProbeReport> RunAsync(
        IspProfile? profile,
        IProgress<ProbeProgress>? progress = null,
        bool stopAtFirstSuccess = true,
        int? maxCandidatesPerSection = null,
        CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        var baseline = await RunBaselineAsync(progress, cancellationToken).ConfigureAwait(false);

        var attempts = new List<CandidateResult>();
        var winners = new List<SectionWinner>();

        // Yalnizca gercekten engelli hedefi olan bolumler aranir. Engelli hedefi
        // olmayan bir bolumu aramak anlamsiz: her aday "basarili" gorunurdu.
        var sectionsToSearch = baseline
            .Where(b => b.Status == BaselineStatus.Blocked)
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
                    section, candidate, blockedTargets, attempts, cancellationToken).ConfigureAwait(false);

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
                        tier, label, candidateList.Count, candidateList.Count, $"bulundu: {candidate.Id}"));
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
        CancellationToken cancellationToken)
    {
        var opened = new List<string>();

        foreach (var blocked in blockedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (blocked.ResolvedIp is null)
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

                if (outcome.Succeeded && !opened.Contains(blocked.Target.Category))
                {
                    opened.Add(blocked.Target.Category);
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
    private static ProbeMode ModeFor(StrategySection section) => section switch
    {
        StrategySection.Tcp80 => ProbeMode.PlainHttp,
        StrategySection.Tcp443 => ProbeMode.Tls12,
        StrategySection.Quic => ProbeMode.Http3,
        StrategySection.DiscordVoice => ProbeMode.Http3,
        _ => ProbeMode.Tls12,
    };

    private readonly record struct ProbeCandidate(string Id, string Args);
}
