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
/// PARALELLIK: izolasyon --ipset-ip ile saglaniyor ve calistigi winws --debug=1
/// ciktisiyla dogrulandi ("include ipset check for <ip> : positive / desync
/// profile 1 matches"). Ama izolasyon HEDEF BAZINDA: ayni hedefe ayni anda iki
/// farkli strateji uygulanamaz, cunku ikisi de ayni paketleri gorur ve hangi
/// sonucun hangi stratejiye ait oldugu belirsizlesir.
///
/// Bu yuzden paralellik ADAYLAR arasinda degil HEDEFLER arasinda: bir aday, ayrik
/// hedeflerde es zamanli sinaniyor. Kazanc en cok cok hedefli bolumlerde (tcp443:
/// discord.com, gateway.discord.gg, kullanicinin kendi adresi) hissediliyor.
/// Adaylari paralellestirmek cazip gorunuyor ama sessizce yanlis sonuc uretirdi --
/// yavas olmaktan cok daha kotu.
///
/// Paralel olan yalnizca AG ISTEKLERI. winws TEK ornek olarak, bolumun butun
/// hedeflerini kapsayan tek bir ipset ile calisiyor. Hedef basina ayri ornek
/// baslatmak denendi ve winws bunu reddediyor ("A copy of winws is already running
/// with the same filter"): --ipset-ip global WinDivert filtresine girmedigi icin
/// iki isçi birebir ayni filtreyi kuruyor. Bu hata SESSIZDI -- her adayda
/// isçilerden biri oluyor, aday hedeflerin yalnizca birinde olculuyordu ve ayni
/// strateji kosumdan kosuma farkli sonuc veriyordu.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class StrategyProber(
    VendorPaths vendor,
    ProfileStore profiles,
    IReadOnlyList<ProbeTarget> targets,
    bool useSecureDns = false)
{
    private readonly WinwsCommandBuilder _commandBuilder = new(vendor);

    /// <summary>Bir adayin uygulanmasindan sonra ag yiginin oturmasi icin beklenen sure.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Ayni anda kac hedefin sinanacagi.
    /// </summary>
    /// <remarks>
    /// Isçiler artik surec baslatmiyor, yalnizca ag istegi atiyor. Sinir yine de
    /// duruyor: ayni anda cok sayida istek atmak olcumun kendisini bozar (istekler
    /// birbirinin gecikmesini etkiler ve zaman asimi esigi anlamsizlasir).
    /// </remarks>
    private const int MaxParallelProbes = 3;

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
        using var resolver = useSecureDns ? new DohResolver() : null;

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

            // Sifreli DNS istenmisse adresi ONCE oradan cozuyoruz. Sistem DNS'i
            // kacirilmis oldugunda baglanti engel sunucusuna gider ve alttaki
            // asil DPI katmani hic gorunmez -- her strateji basarisiz olur.
            string? pinnedIp = null;
            if (resolver is not null)
            {
                pinnedIp = await resolver.ResolveIPv4Async(target.Host, cancellationToken).ConfigureAwait(false);

                // Sifreli DNS ISTENDI ama cozumleme basarisiz oldu. Buradan devam
                // etmek olcumu sessizce baska bir seye cevirir: pinnedIp null
                // kalinca baglanti SISTEM DNS'ine duser ve DNS kacirmasi olan bir
                // hatta -- Turkiye'de olagan durum -- engel sunucusuna gider.
                // O zaman "engelli mi" sorusunun cevabi DPI'i degil DNS katmanini
                // olcer, ve arama bunun uzerine kurulur.
                //
                // Gercek bir hatta bu somut: sistem DNS'i discord.com'u
                // 195.175.254.2'ye (saglayicinin engel sunucusu) cozuyor. Oradan
                // gelen bir yonlendirme "acildi" diye okunabilir.
                //
                // Belirsiz isaretlemek dogru davranis: belirsiz hedefte strateji
                // aranmaz, kontrol hedefi belirsizse bolum tumuyle atlanir.
                if (pinnedIp is null)
                {
                    results.Add(new BaselineResult(
                        target,
                        BaselineStatus.Inconclusive,
                        "sifreli DNS ile cozulemedi -- olcum yapilmadi (sistem DNS'i kacirilmis olabilir)",
                        null));
                    continue;
                }
            }

            var outcome = await ProbeAsync(
                    target.Section, target.Host, pinnedIp, client, cancellationToken, target.Port)
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

        // Tier'lar arasi tekrar: her tier kendi icinde argumana gore tekillestiriliyordu
        // ama TIER'LAR ARASINDA degil. Profiller birbirinden turedigi ve merdiven de
        // ayni kombinasyonlari uretebildigi icin ayni strateji farkli adla ikinci kez
        // deneniyordu. Olculdu: TTNET'te 40 denemenin 8'i (%20) birebir ayni argumanin
        // tekrariydi -- ornegin "tt-quic-fake-plain" ile "superonline/sol-quic-fake-plain"
        // ayni komut. Butce sinirli oldugu icin bunun bedeli dogrudan: denenmeyen
        // 8 gercek aday.
        var triedArgs = new HashSet<string>(StringComparer.Ordinal);

        // Anahtar NORMALLESTIRILMIS: bayrak sirasi disinda ayni olan iki aday ayni
        // adaydir. Olculdu: "tt-80-fake-fakedsplit" ile merdivenin urettigi
        // "fake-fakedsplit#1" ayni uc bayragi farkli sirada tasiyor ve uc bagimsiz
        // kosumun ucunde de birebir ayni sonucu verdiler -- yani sira sonucu
        // degistirmiyor, yalnizca butce yiyordu.

        foreach (var (tier, label, candidates) in BuildTiers(section, profile))
        {
            if (remaining <= 0)
            {
                break;
            }

            // Where'in yan etkisi kasitli: Take tembel oldugu icin yalnizca listeye
            // GERCEKTEN giren adaylar "denendi" diye isaretleniyor. Butce yuzunden hic
            // cekilmeyen bir aday isaretlenmis olsaydi, ayni argumani tasiyan baska bir
            // aday sonradan sessizce elenirdi.
            var candidateList = candidates
                .Where(c => triedArgs.Add(NormalizeArgs(c.Args)))
                .Take(remaining)
                .ToList();
            if (candidateList.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < candidateList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Motor art arda hic baslamadiysa devam etmenin anlami yok:
                // her aday saniyenin onda birinde "denenip" ayni sebeple
                // dusuyor ve kullanici olcum yapildigini saniyor.
                if (MotorSurekliDusuyor(attempts, out var sebep))
                {
                    throw new ProbeEngineException(sebep);
                }

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

    /// <summary>Motorun art arda kac denemede hic baslamadigina bakar.</summary>
    /// <remarks>
    /// Esik bilerek yuksek: tek tuk basarisizlik normal (gecersiz parametre,
    /// gecici kilit). Aranan sey BU DEGIL -- surucunun cekirdekte takili
    /// kalmasi gibi, her adayi ayni sekilde dusuren kalici bir bozukluk.
    /// </remarks>
    /// <summary>Motorun hic baslamadigini anlatan hata onek.</summary>
    public const string EngineFailurePrefix = "calistirilamadi: ";

    public const int MotorHataEsigi = 25;

    public static bool MotorSurekliDusuyor(
        IReadOnlyList<CandidateResult> attempts, out string sebep)
    {
        sebep = string.Empty;

        if (attempts.Count < MotorHataEsigi)
        {
            return false;
        }

        var son = attempts.Skip(attempts.Count - MotorHataEsigi).ToList();
        if (son.Any(a => a.Succeeded) ||
            !son.All(a => (a.Detail ?? string.Empty).StartsWith(EngineFailurePrefix, StringComparison.Ordinal)))
        {
            return false;
        }

        sebep = son[^1].Detail ?? "winws baslatilamadi";
        return true;
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

        var targets = blockedTargets
            .Where(b => b.ResolvedIp is not null && !abandoned.Contains(b.Target.Host))
            .ToList();

        if (targets.Count == 0)
        {
            return opened;
        }

        var results = new List<CandidateResult>();
        var lockObject = new object();

        // TEK winws ornegi, butun hedefleri kapsayan ipset. Hedef basina ayri ornek
        // baslatmak winws tarafindan reddediliyor ("A copy of winws is already
        // running with the same filter") cunku --ipset-ip global WinDivert
        // filtresine girmiyor -- iki isçi birebir ayni filtreyi kuruyor.
        // Ayrintili gerekce WinwsCommandBuilder.BuildProbeCommand'da.
        var runner = new WinwsRunner(vendor);
        var stopwatch = Stopwatch.StartNew();

        CandidateResult Failure(BaselineResult target, string detail) => new(
            candidate.Id, candidate.Args, section,
            target.Target.Host, target.Target.Category,
            false, detail, stopwatch.Elapsed);

        try
        {
            var arguments = _commandBuilder.BuildProbeCommand(
                section, candidate.Args, [.. targets.Select(t => t.ResolvedIp!)]);

            // Once winws'in kendisine dogrulat. Gecersiz bir aday burada ~50 ms'de
            // elenir; yoksa surucu acilir, ag istegi zaman asimina ugrar ve sonuc
            // "zaman asimi" olarak kaydedilir -- yani gercekte parametre hatasi
            // olan bir sey engelleme sanilir.
            var validationError = await runner.ValidateAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (validationError is not null)
            {
                attempts.AddRange(targets
                    .Select(t => Failure(t, "gecersiz parametre: " + validationError))
                    .OrderBy(r => r.TargetHost, StringComparer.Ordinal));
                return opened;
            }

            await runner.StartAsync(arguments, cancellationToken).ConfigureAwait(false);

            // winws'in WinDivert filtresini kurmasi anlik degil; hemen istek
            // atarsak strateji henuz devrede olmaz ve calisan bir aday basarisiz
            // gorunur.
            await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);

            // Artik yalnizca AG ISTEKLERI paralel. Surec baslatma tek sefer
            // yapildigi icin isçiler arasinda paylasilan hicbir surec durumu yok.
            await Parallel.ForEachAsync(
                targets,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelProbes,
                    CancellationToken = cancellationToken,
                },
                async (blocked, token) =>
                {
                    var (result, openedCategory, isBlockPage) =
                        await ProbeSingleTargetAsync(section, candidate, blocked, stopwatch.Elapsed, token)
                            .ConfigureAwait(false);

                    lock (lockObject)
                    {
                        results.Add(result);

                        if (isBlockPage)
                        {
                            abandoned.Add(blocked.Target.Host);
                        }

                        if (openedCategory is not null && !opened.Contains(openedCategory))
                        {
                            opened.Add(openedCategory);
                        }
                    }
                }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            attempts.AddRange(targets
                .Select(t => Failure(t, "calistirilamadi: " + ex.Message))
                .OrderBy(r => r.TargetHost, StringComparer.Ordinal));
            return opened;
        }
        finally
        {
            await runner.StopAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        // Sonuclar deterministik sirada eklensin: paralel kosumda tamamlanma
        // sirasi degisken ve rapor her seferinde farkli siralanirsa
        // karsilastirilamaz hale gelir.
        attempts.AddRange(results.OrderBy(r => r.TargetHost, StringComparer.Ordinal));

        return opened;
    }

    /// <summary>
    /// Tek bir hedefte tek bir adayi dener.
    /// </summary>
    /// <returns>
    /// Denemenin kaydi, acilan hedef sinifi (acilmadiysa null) ve engel sayfasi
    /// gorulup gorulmedigi.
    /// </returns>
    /// <remarks>
    /// Paralel cagrilabilmesi icin PAYLASILAN DURUMA DOKUNMUYOR: sonuclari
    /// listelere kendisi eklemek yerine geri donduruyor. Cagiran taraf onlari
    /// kilit altinda topluyor.
    ///
    /// winws SURECINI BASLATMAZ. Surec, bolumun butun hedeflerini kapsayan tek bir
    /// ornek olarak cagiran tarafta baslatiliyor; hedef basina ayri ornek winws
    /// tarafindan reddediliyor (ayrintili gerekce
    /// <see cref="WinwsCommandBuilder.BuildProbeCommand(StrategySection, string, IReadOnlyList{string})"/>).
    /// </remarks>
    private async Task<(CandidateResult Result, string? OpenedCategory, bool IsBlockPage)>
        ProbeSingleTargetAsync(
            StrategySection section,
            ProbeCandidate candidate,
            BaselineResult blocked,
            TimeSpan elapsedBeforeProbe,
            CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        TimeSpan Elapsed() => elapsedBeforeProbe + stopwatch.Elapsed;

        CandidateResult Failure(string detail)
        {
            stopwatch.Stop();
            return new CandidateResult(
                candidate.Id, candidate.Args, section,
                blocked.Target.Host, blocked.Target.Category,
                false, detail, Elapsed());
        }

        try
        {
            using var client = new HttpProbeClient();
            var outcome = await ProbeAsync(
                    section, blocked.Target.Host, blocked.ResolvedIp, client, cancellationToken,
                    blocked.Target.Port)
                .ConfigureAwait(false);

            if (!outcome.Succeeded)
            {
                stopwatch.Stop();
                return (
                    new CandidateResult(
                        candidate.Id, candidate.Args, section,
                        blocked.Target.Host, blocked.Target.Category,
                        false, outcome.Detail, Elapsed()),
                    null,
                    outcome.IsBlockPage);
            }

            // Tek basarili deneme yeterli DEGIL. Ilk gercek kosumda bir aday
            // calisti, ayni aday sonraki kosumda calismadi -- ag kosullari
            // gurultulu ve tek olcum bunu ayirt edemiyor. Kullaniciya "bulundu"
            // deyip sonra calismamasi, hic bulamamaktan kotu.
            var confirmed = await ConfirmAsync(section, blocked, cancellationToken).ConfigureAwait(false);

            if (!confirmed)
            {
                return (Failure("dogrulama tekrarinda basarisiz (kararsiz)"), null, false);
            }

            stopwatch.Stop();

            return (
                new CandidateResult(
                    candidate.Id, candidate.Args, section,
                    blocked.Target.Host, blocked.Target.Category,
                    true, outcome.Detail, Elapsed()),
                blocked.Target.Category,
                false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Failure("calistirilamadi: " + ex.Message), null, false);
        }
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
        var outcome = await ProbeAsync(
                section, target.Target.Host, target.ResolvedIp, client, cancellationToken,
                target.Target.Port)
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
                .DistinctBy(c => NormalizeArgs(c.Args), StringComparer.Ordinal));

        // Aile adi tek basina KIMLIK DEGIL: bir aile eksenlerin kartezyen carpimi
        // kadar aday uretiyor, dolayisiyla "ladder/fake-quic-anyproto" adinda
        // onlarca farkli aday oluyordu. Ilerleme satirlarinda ayni ad pes pese
        // tekrarliyor ve -- asil sorun -- dogrulanan bir aday learned.json'a bu
        // adla yazilinca hangi varyantin calistigi kayboluyordu. Aile icinde
        // sira numarasi veriyoruz; asil kimlik yine argumanlar, ad okunabilirlik icin.
        //
        // GroupBy burada siralamayi bozmuyor: Expand bir ailenin butun
        // kombinasyonlarini pes pese uretiyor, yani aileler zaten bitisik geliyor.
        yield return (
            ProbeTier.GenericLadder,
            "Genel arama",
            InterleaveFamilies(profiles.Ladder.Expand(section)));
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
    /// <summary>
    /// Bir bolumu kendi protokoluyle olcer.
    /// </summary>
    /// <remarks>
    /// Bolume gore istemci secimi TEK YERDE tutuluyor. Uc ayri cagri yerinde
    /// tekrarlaniyordu ve QUIC istemcisi eklenirken birine yazip digerini atlamak,
    /// sessizce yanlis olcen bir kod yolu birakirdi -- bu bolumde tam olarak bu tur
    /// bir hata zaten bir kez yasandi.
    ///
    /// public olmasinin sebebi teshis yolu (--engage-check): teshisin, arama
    /// motorunun olctugu SEYIN AYNISINI olcmesi gerekiyor. Ayri bir dallanma
    /// yazilmisti ve discord-voice'u STUN yerine HTTP/3 ile olcuyordu -- yani
    /// teshis, motorun gordugunden baska bir sey gosteriyordu.
    ///
    /// QUIC'in ayri istemcisi olmasinin sebebi: <see cref="HttpProbeClient"/> HTTP/3'u
    /// cozumlenmis IP'ye SABITLEYEMIYOR (ConnectCallback yalnizca TCP'de calisir).
    /// DNS kacirmasi olan bir hatta baglanti engel sunucusuna gidiyor, winws'in
    /// --ipset-ip kontrolu negatif donuyor ve strateji hic uygulanmadan paket geciyor.
    /// </remarks>
    /// <param name="port">
    /// Yalnizca discord-voice bolumunde kullanilir; verilmezse STUN icin 19302.
    /// Hedef listesinden gelir, cunku Google disindaki STUN sunuculari 3478'i
    /// kullaniyor ve bolumun kontrol hedefi baska bir isletmeciden olmali.
    /// </param>
    public static async Task<ProbeOutcome> ProbeAsync(
        StrategySection section,
        string host,
        string? pinnedIp,
        HttpProbeClient httpClient,
        CancellationToken cancellationToken = default,
        int? port = null)
        => section switch
        {
            // Discord ses UDP uzerinden calisiyor; HTTP istemcisiyle olculemez.
            StrategySection.DiscordVoice => await new StunProbeClient()
                .TryReachAsync(host, port ?? 19302, pinnedIp, cancellationToken)
                .ConfigureAwait(false),

            StrategySection.Quic => await new QuicProbeClient()
                .TryReachAsync(host, pinnedIp, cancellationToken: cancellationToken)
                .ConfigureAwait(false),

            _ => await httpClient
                .TryReachAsync(host, ModeFor(section), pinnedIp, cancellationToken)
                .ConfigureAwait(false),
        };

    public static ProbeMode ModeFor(StrategySection section) => section switch
    {
        StrategySection.Tcp80 => ProbeMode.PlainHttp,
        StrategySection.Tcp443 => ProbeMode.Tls12,
        StrategySection.Quic => ProbeMode.Http3,
        StrategySection.DiscordVoice => ProbeMode.Http3,
        _ => ProbeMode.Tls12,
    };

    /// <summary>
    /// Merdiven adaylarini AILELER ARASINDA sirayla dizer: once her ailenin ilk
    /// varyanti, sonra ikincileri, sonra ucunculeri...
    /// </summary>
    /// <remarks>
    /// Onceden aileler pes pese, her aile sonuna kadar deneniyordu. Butce sinirsiz
    /// olsaydi sira onemsizdi; ama butce var (arayuzde bolum basina 60) ve sonuc
    /// olculdu: BASKA bir TTNET hattinda 176 aday denendi, 1105 saniye surdu ve
    /// hicbiri tutmadi. `tcp443` genel aramasinda 7 aile / 136 varyant var ve ilk
    /// aile (`fake-fooling`) tek basina 45 varyant -- genel aramaya kalan ~33
    /// butcenin tamamini yiyor. Yani `multisplit-pure`, `multidisorder-pure`,
    /// `fakedsplit`, `fake-tls-mod` ve `syndata` aileleri HIC DENENMEDI. Oysa bunlar
    /// mekanizma olarak tamamen farkli seyler; aralarinda sahte paket hic uretmeyenler
    /// bile var.
    ///
    /// Bir ailenin ilk varyanti tutmuyorsa o ailenin TTL/fooling varyasyonlarini
    /// tuketmek, hic denenmemis bir mekanizmayi denemekten daha az bilgi veriyor.
    /// Bu yuzden once genislik, sonra derinlik.
    ///
    /// Aile ICINDEKI eksen sirasi degismedi -- o `generic-ladder.json`'da profil
    /// yazarinin karari ve `GenericLadder.Expand` orada birakildi. Burasi aramanin
    /// stratejisi, merdivenin icerigi degil.
    /// </remarks>
    public static IReadOnlyList<ProbeCandidate> InterleaveFamilies(IEnumerable<LadderCandidate> expanded)
    {
        var families = expanded
            .DistinctBy(c => NormalizeArgs(c.Args), StringComparer.Ordinal)
            .GroupBy(c => c.Family, StringComparer.Ordinal)
            .Select(group => group
                .Select((c, index) => new ProbeCandidate($"ladder/{c.Family}#{index + 1}", c.Args))
                .ToList())
            .ToList();

        if (families.Count == 0)
        {
            return [];
        }

        var ordered = new List<ProbeCandidate>();
        var deepest = families.Max(f => f.Count);

        for (var round = 0; round < deepest; round++)
        {
            foreach (var family in families)
            {
                if (round < family.Count)
                {
                    ordered.Add(family[round]);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// Tekillestirme anahtari: bayraklar siralanmis halde. YALNIZCA karsilastirma
    /// icin; winws'e her zaman adayin kendi yazimi veriliyor.
    /// </summary>
    private static string NormalizeArgs(string args)
        => string.Join(' ', args
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(part => part, StringComparer.Ordinal));

    /// <summary>Denenecek tek bir aday: gorunur kimligi ve winws argumanlari.</summary>
    public readonly record struct ProbeCandidate(string Id, string Args);
}
