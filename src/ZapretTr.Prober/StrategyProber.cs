using System.Diagnostics;
using System.Runtime.Versioning;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>
/// Parametre test motoru. blockcheck.sh'ın yerini alır.
/// </summary>
/// <remarks>
/// Hızın kaynağı paralellik değil SIRALAMA. blockcheck tüm kombinasyon uzayını
/// baştan sona tarar; burada arama kademeli daraltılıyor:
///
///   Tier 1  seçilen İSS'nin profili        (Superonline için 19 aday)
///   Tier 2  komşu TR profilleri
///   Tier 3  genel kombinatoryal merdiven   (180 aday)
///
/// Her bölüm (tcp80 / tcp443 / quic / discord-voice) BAĞIMSIZ aranır ve bağımsız
/// kazananı olur. Bunun sebebi upstream belgesindeki şu ayrıntı: md5sig yalnızca
/// sunucu TCP MD5 seçeneğini reddettiğinde işe yarar, yani çalışan strateji hedef
/// sunucuya da bağlı. Tek bir "kazanan parametre" aramak yanlış soru olurdu.
///
/// PARALELLİK: yalıtım --ipset-ip ile sağlanıyor ve çalıştığı winws --debug=1
/// çıktısıyla doğrulandı ("include ipset check for <ip> : positive / desync
/// profile 1 matches"). Ama yalıtım HEDEF BAZINDA: aynı hedefe aynı anda iki
/// farklı strateji uygulanamaz, çünkü ikisi de aynı paketleri görür ve hangi
/// sonucun hangi stratejiye ait olduğu belirsizleşir.
///
/// Bu yüzden paralellik ADAYLAR arasında değil HEDEFLER arasında: bir aday, ayrık
/// hedeflerde eş zamanlı sınanıyor. Kazanç en çok çok hedefli bölümlerde (tcp443:
/// discord.com, gateway.discord.gg, kullanıcının kendi adresi) hissediliyor.
/// Adayları paralelleştirmek cazip görünüyor ama sessizce yanlış sonuç üretirdi;
/// bu da yavaş olmaktan çok daha kötü.
///
/// Paralel olan yalnızca AĞ İSTEKLERİ. winws TEK örnek olarak, bölümün bütün
/// hedeflerini kapsayan tek bir ipset ile çalışıyor. Hedef başına ayrı örnek
/// başlatmak denendi ve winws bunu reddediyor ("A copy of winws is already running
/// with the same filter"): --ipset-ip global WinDivert filtresine girmediği için
/// iki işçi birebir aynı filtreyi kuruyor. Bu hata SESSİZDİ: her adayda
/// işçilerden biri ölüyor, aday hedeflerin yalnızca birinde ölçülüyordu ve aynı
/// strateji koşumdan koşuma farklı sonuç veriyordu.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class StrategyProber(
    VendorPaths vendor,
    ProfileStore profiles,
    IReadOnlyList<ProbeTarget> targets,
    bool useSecureDns = false)
{
    private readonly WinwsCommandBuilder _commandBuilder = new(vendor);

    /// <summary>Bir adayın uygulanmasından sonra ağ yığınının oturması için beklenen süre.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Aynı anda kaç hedefin sınanacağı.
    /// </summary>
    /// <remarks>
    /// İşçiler artık süreç başlatmıyor, yalnızca ağ isteği atıyor. Sınır yine de
    /// duruyor: aynı anda çok sayıda istek atmak ölçümün kendisini bozar (istekler
    /// birbirinin gecikmesini etkiler ve zaman aşımı eşiği anlamsızlaşır).
    /// </remarks>
    private const int MaxParallelProbes = 3;

    /// <summary>Doğrulama tekrarı öncesi beklenen süre.</summary>
    private static readonly TimeSpan ConfirmationGap = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Engellenmemesi beklenen hedeflerin kategori adı. Bir bölümün kontrol hedefi
    /// açılmıyorsa o bölümün sonuçları güvenilmez ve arama yapılmaz.
    /// </summary>
    public const string ControlCategory = "kontrol";

    /// <summary>Testi çalıştırır.</summary>
    /// <param name="profile">
    /// Kullanıcının seçtiği İSS profili. null verilirse Tier 1 atlanır ve doğrudan
    /// komşu profillerden başlanır.
    /// </param>
    /// <param name="stopAtFirstSuccess">
    /// true ise her bölümde ilk çalışan aday bulununca o bölümün araması biter.
    /// false ise tüm tier'lar taranıp en iyi aday seçilir; kullanıcı "daha iyisini
    /// ara" dediğinde kullanılır.
    /// </param>
    /// <param name="maxCandidatesPerSection">
    /// Bir bölümde denenecek en fazla aday sayısı. null ise sınırsız.
    /// Bütün tier'lar boyunca ortak bir bütçe olarak sayılır, böylece Tier 3'ün
    /// 180 adayı kullanıcıyı belirsiz süre bekletemez.
    /// </param>
    /// <param name="knownBaseline">
    /// Daha önce hesaplanmış mevcut durum taraması. Verilirse yeniden taranmaz.
    /// </param>
    /// <remarks>
    /// <paramref name="knownBaseline"/> yalnızca hız için değil DOĞRULUK için de var:
    /// tarama iki kez koşulduğunda aynı hedef iki farklı sonuç verebiliyor (DNS
    /// cevabı ya da zaman aşımı değişince engel sayfası yerine zaman aşımı görünüyor)
    /// ve ikinci sonuç birincisini sessizce eziyor. İlk gerçek koşumda tam bu oldu:
    /// DNS yönlendirmesi olarak doğru sınıflanmış hedefler ikinci taramada "DPI
    /// engeli" sayılıp boşuna dört dakika strateji arandı.
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

        // Kontrol hedefi açılmayan bölümler aranmaz. Kontrol, engellenmemesi
        // BEKLENEN bir adres; o da açılmıyorsa ölçüm yolunda ya da bağlantıda bir
        // sorun var demektir ve o bölümün "engelli" sonuçları güvenilmez.
        // İlk saha koşumunda QUIC bölümünde tam bu oldu: bütün hedefler "HTTP/3
        // bağlantısı kurulamadı" verdi ve bu engelleme sanılıp 40 aday boşuna
        // denendi. Oysa aynı hata QUIC'in makinede hiç çalışmaması durumunda da
        // çıktığı için ikisi ayırt edilemiyordu.
        var unreliableSections = baseline
            .Where(b => b.Target.Category == ControlCategory && b.Status != BaselineStatus.Accessible)
            .Select(b => b.Target.Section)
            .ToHashSet();

        // Yalnızca gerçekten engelli hedefi olan bölümler aranır. Engelli hedefi
        // olmayan bir bölümü aramak anlamsız: her aday "başarılı" görünürdü.
        // Kontrol hedefleri de aranmaz; onlar zaten açılması beklenen adresler.
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
    /// winws KAPALIYKEN hangi hedeflerin gerçekten erişilemediğini belirler.
    /// </summary>
    /// <remarks>
    /// Testin en kritik adımı. Atlanırsa zaten açılan bir hedefle test yapılır ve
    /// her strateji çalışıyor görünür; blockcheck de bu yüzden önce erişim
    /// kontrolü yapıyor.
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

            // Şifreli DNS istenmişse adresi ÖNCE oradan çözüyoruz. Sistem DNS'i
            // kaçırılmış olduğunda bağlantı engel sunucusuna gider ve alttaki
            // asıl DPI katmanı hiç görünmez; her strateji başarısız olur.
            string? pinnedIp = null;
            if (resolver is not null)
            {
                pinnedIp = await resolver.ResolveIPv4Async(target.Host, cancellationToken).ConfigureAwait(false);

                // Şifreli DNS İSTENDİ ama çözümleme başarısız oldu. Buradan devam
                // etmek ölçümü sessizce başka bir şeye çevirir: pinnedIp null
                // kalınca bağlantı SİSTEM DNS'ine düşer ve DNS kaçırması olan bir
                // hatta (Türkiye'de olağan durum) engel sunucusuna gider.
                // O zaman "engelli mi" sorusunun cevabı DPI'ı değil DNS katmanını
                // ölçer ve arama bunun üzerine kurulur.
                //
                // Gerçek bir hatta bu somut: sistem DNS'i discord.com'u
                // 195.175.254.2'ye (sağlayıcının engel sunucusu) çözüyor. Oradan
                // gelen bir yönlendirme "açıldı" diye okunabilir.
                //
                // Belirsiz işaretlemek doğru davranış: belirsiz hedefte strateji
                // aranmaz, kontrol hedefi belirsizse bölüm tümüyle atlanır.
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
                // Engel sayfası kontrolü başarı kontrolünden ÖNCE gelmeli: engel
                // sayfası HTTP 200 döndürüyor, dolayısıyla sırayı ters kurmak onu
                // "açılıyor" olarak işaretlerdi.
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
    /// Bir hedefin herhangi bir bölümde DNS yönlendirmesi tespit edildiyse, aynı
    /// alan adının diğer bölümlerini de öyle işaretler.
    /// </summary>
    /// <remarks>
    /// DNS yönlendirmesi alan adı bazında olur, protokol bazında değil: cevap
    /// değiştirildiğinde TCP de UDP de aynı yanlış sunucuya gider. Ama belirti
    /// protokole göre değişiyor: engel sunucusu HTTPS'i karşılayıp engel sayfası
    /// dönerken QUIC'i hiç cevaplamıyor, bu da zaman aşımı olarak görünüp "DPI
    /// engeli" sanılmasına yol açıyor. O bölümde yapılacak strateji araması
    /// baştan kayıp: hiçbir aday çalışmaz, çünkü sorun DPI değil.
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

        // Arama sırasında engel sayfası döndüğü için vazgeçilen hedefler.
        var abandoned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tier'lar arası tekrar: her tier kendi içinde argümana göre tekilleştiriliyordu
        // ama TIER'LAR ARASINDA değil. Profiller birbirinden türediği ve merdiven de
        // aynı kombinasyonları üretebildiği için aynı strateji farklı adla ikinci kez
        // deneniyordu. Ölçüldü: TTNET'te 40 denemenin 8'i (%20) birebir aynı argümanın
        // tekrarıydı; örneğin "tt-quic-fake-plain" ile "superonline/sol-quic-fake-plain"
        // aynı komut. Bütçe sınırlı olduğu için bunun bedeli doğrudan: denenmeyen
        // 8 gerçek aday.
        var triedArgs = new HashSet<string>(StringComparer.Ordinal);

        // Tamamen cevapsız bölümü bırakmak için: art arda kaç aday hiç cevap almadı
        // ve aralarında kaç FARKLI desync yöntemi var. Gerekçesi BolumCevapsiz'da.
        var ardisikSessiz = 0;
        var sessizYontemler = new HashSet<string>(StringComparer.Ordinal);

        // Anahtar NORMALLEŞTİRİLMİŞ: bayrak sırası dışında aynı olan iki aday aynı
        // adaydır. Ölçüldü: "tt-80-fake-fakedsplit" ile merdivenin ürettiği
        // "fake-fakedsplit#1" aynı üç bayrağı farklı sırada taşıyor ve üç bağımsız
        // koşumun üçünde de birebir aynı sonucu verdiler; yani sıra sonucu
        // değiştirmiyor, yalnızca bütçe yiyordu.

        foreach (var (tier, label, candidates) in BuildTiers(section, profile))
        {
            if (remaining <= 0)
            {
                break;
            }

            // Where'in yan etkisi kasıtlı: Take tembel olduğu için yalnızca listeye
            // GERÇEKTEN giren adaylar "denendi" diye işaretleniyor. Bütçe yüzünden hiç
            // çekilmeyen bir aday işaretlenmiş olsaydı, aynı argümanı taşıyan başka bir
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

                // Motor art arda hiç başlamadıysa devam etmenin anlamı yok:
                // her aday saniyenin onda birinde "denenip" aynı sebeple
                // düşüyor ve kullanıcı ölçüm yapıldığını sanıyor.
                if (MotorSurekliDusuyor(attempts, out var sebep))
                {
                    throw new ProbeEngineException(sebep);
                }

                var candidate = candidateList[i];
                remaining--;

                progress?.Report(new ProbeProgress(
                    tier, label, i, candidateList.Count, $"{section.ToJsonName()} · {candidate.Id}"));

                // Bu adayın kendi denemelerini ayırt edebilmek için: liste bölüm
                // boyunca birikiyor, aday başına da hedef sayısı kadar satır ekleniyor.
                var oncekiDenemeSayisi = attempts.Count;

                var verified = await TryCandidateAsync(
                    section, candidate, blockedTargets, attempts, abandoned, cancellationToken).ConfigureAwait(false);

                var buAdayinDenemeleri = attempts.Skip(oncekiDenemeSayisi).ToList();
                var tamamenCevapsiz = buAdayinDenemeleri.Count > 0
                    && buAdayinDenemeleri.All(a => !a.Succeeded
                        && string.Equals(a.Detail, CevapsizlikDetayi, StringComparison.Ordinal));

                if (tamamenCevapsiz)
                {
                    ardisikSessiz++;
                    sessizYontemler.Add(DesyncYontemi(candidate.Args));
                }
                else
                {
                    // Herhangi bir CEVAP geldiyse (RST, engel sayfası, başarı) sayaç
                    // sıfırlanır: paketlerimiz karşı tarafa ulaşıyor demektir.
                    ardisikSessiz = 0;
                    sessizYontemler.Clear();
                }

                if (verified.Count == 0)
                {
                    if (BolumCevapsiz(ardisikSessiz, sessizYontemler.Count))
                    {
                        progress?.Report(new ProbeProgress(
                            tier, label, i + 1, candidateList.Count,
                            $"{section.ToJsonName()}: {ardisikSessiz} aday üst üste hiç cevap alamadı "
                            + $"({sessizYontemler.Count} farklı yöntem denendi). Bu bölüm bırakılıyor; "
                            + "cevap gelmeyen bir yolu desync hilesiyle açmak mümkün değil."));

                        return best;
                    }

                    continue;
                }

                var winner = new SectionWinner(section, candidate.Id, candidate.Args, verified);

                // Daha fazla hedef sınıfını açan aday daha iyidir.
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

    /// <summary>Motorun art arda kaç denemede hiç başlamadığına bakar.</summary>
    /// <remarks>
    /// Eşik bilerek yüksek: tek tük başarısızlık normal (geçersiz parametre,
    /// geçici kilit). Aranan şey BU DEĞİL; sürücünün çekirdekte takılı
    /// kalması gibi, her adayı aynı şekilde düşüren kalıcı bir bozukluk.
    /// </remarks>
    /// <summary>Motorun hiç başlamadığını anlatan hata öneki.</summary>
    public const string EngineFailurePrefix = "calistirilamadi: ";

    public const int MotorHataEsigi = 25;

    /// <summary>Hiçbir cevap gelmediğini anlatan ayrıntı.</summary>
    public const string CevapsizlikDetayi = "zaman asimi";

    /// <summary>
    /// Bir bölümde art arda bu kadar aday HİÇBİR cevap alamazsa bölüm bırakılır.
    /// </summary>
    public const int SessizlikEsigi = 12;

    /// <summary>
    /// ...ve sessiz kalan adaylar arasında en az bu kadar FARKLI desync yöntemi
    /// bulunmalı. Sayı tek başına yetmiyor: aynı yöntemin 12 parametre varyasyonu
    /// "her şeyi denedik" demek değil.
    /// </summary>
    public const int SessizlikYontemEsigi = 4;

    /// <summary>
    /// Bölüm tamamen cevapsız mı: art arda <see cref="SessizlikEsigi"/> aday, hepsi
    /// zaman aşımı ve aralarında en az <see cref="SessizlikYontemEsigi"/> farklı
    /// desync yöntemi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ÖLÇÜLDÜ (issue #1, Vodafone Net, 2026-09-15): QUIC bölümünde 25 adayın HEPSİ
    /// zaman aşımına uğradı, her biri ~11.7 sn, toplam 293 sn. Yani ~5 dakika,
    /// sonucu baştan belli bir arama için harcandı. Aynı raporda tcp443 ve tcp80
    /// kazananları 1.4-1.9 sn'de bulunmuştu.
    /// </para>
    /// <para>
    /// Eşikler o rapordaki gerçek sıraya bakılarak seçildi: 12. adaya gelindiğinde
    /// dört farklı yöntem (fake, udplen, fake+udplen, ipfrag2) denenmiş ve dördü de
    /// tam sessizlikle dönmüş oluyor; kalan 17 deneme aynı ailelerin parametre
    /// varyasyonları. Yani yöntem çeşitliliği tükendikten SONRA vazgeçiliyor,
    /// sayaç dolduğu için değil.
    /// </para>
    /// <para>
    /// Neden yalnızca ZAMAN AŞIMI sayılıyor: RST ya da engel sayfası bir CEVAPTIR,
    /// yani paketlerimiz karşı tarafa ulaşıyor ve başka bir aday işe yarayabilir.
    /// Tam sessizlik ise isteğin hiç gitmediğini gösterir; bunu bir desync hilesiyle
    /// çözemiyorsak başka bir hile de çözmüyor.
    /// </para>
    /// <para>
    /// Bölüm SESSİZCE bırakılmıyor: çağıran taraf ilerleme mesajıyla sebebi yazıyor
    /// ve denemelerin hepsi raporda duruyor.
    /// </para>
    /// </remarks>
    public static bool BolumCevapsiz(int ardisikSessiz, int farkliYontem)
        => ardisikSessiz >= SessizlikEsigi && farkliYontem >= SessizlikYontemEsigi;

    /// <summary>
    /// Argüman dizgisindeki <c>--dpi-desync=</c> değeri; yoksa <c>(yok)</c>.
    /// </summary>
    /// <remarks>
    /// Yöntem çeşitliliğini saymak için. "fake" ile "fake,udplen" AYRI sayılıyor:
    /// ikincisi paketi başka türlü biçimlendiriyor, yani gerçekten başka bir hile.
    /// </remarks>
    public static string DesyncYontemi(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return "(yok)";
        }

        const string onek = "--dpi-desync=";

        foreach (var parca in args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (parca.StartsWith(onek, StringComparison.Ordinal))
            {
                return parca[onek.Length..];
            }
        }

        return "(yok)";
    }

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
    /// Bir adayı çalıştırıp engelli hedeflerde deneyerek hangi hedef sınıflarını açtığını döndürür.
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

        // TEK winws örneği, bütün hedefleri kapsayan ipset. Hedef başına ayrı örnek
        // başlatmak winws tarafından reddediliyor ("A copy of winws is already
        // running with the same filter"), çünkü --ipset-ip global WinDivert
        // filtresine girmiyor; iki işçi birebir aynı filtreyi kuruyor.
        // Ayrıntılı gerekçe WinwsCommandBuilder.BuildProbeCommand'da.
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

            // Önce winws'in kendisine doğrulat. Geçersiz bir aday burada ~50 ms'de
            // elenir; yoksa sürücü açılır, ağ isteği zaman aşımına uğrar ve sonuç
            // "zaman aşımı" olarak kaydedilir; yani gerçekte parametre hatası
            // olan bir şey engelleme sanılır.
            var validationError = await runner.ValidateAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (validationError is not null)
            {
                attempts.AddRange(targets
                    .Select(t => Failure(t, "gecersiz parametre: " + validationError))
                    .OrderBy(r => r.TargetHost, StringComparer.Ordinal));
                return opened;
            }

            await runner.StartAsync(arguments, cancellationToken).ConfigureAwait(false);

            // winws'in WinDivert filtresini kurması anlık değil; hemen istek
            // atarsak strateji henüz devrede olmaz ve çalışan bir aday başarısız
            // görünür.
            await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);

            // Artık yalnızca AĞ İSTEKLERİ paralel. Süreç başlatma tek sefer
            // yapıldığı için işçiler arasında paylaşılan hiçbir süreç durumu yok.
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

        // Sonuçlar deterministik sırada eklensin: paralel koşumda tamamlanma
        // sırası değişken ve rapor her seferinde farklı sıralanırsa
        // karşılaştırılamaz hâle gelir.
        attempts.AddRange(results.OrderBy(r => r.TargetHost, StringComparer.Ordinal));

        return opened;
    }

    /// <summary>
    /// Tek bir hedefte tek bir adayı dener.
    /// </summary>
    /// <returns>
    /// Denemenin kaydı, açılan hedef sınıfı (açılmadıysa null) ve engel sayfası
    /// görülüp görülmediği.
    /// </returns>
    /// <remarks>
    /// Paralel çağrılabilmesi için PAYLAŞILAN DURUMA DOKUNMUYOR: sonuçları
    /// listelere kendisi eklemek yerine geri döndürüyor. Çağıran taraf onları
    /// kilit altında topluyor.
    ///
    /// winws SÜRECİNİ BAŞLATMAZ. Süreç, bölümün bütün hedeflerini kapsayan tek bir
    /// örnek olarak çağıran tarafta başlatılıyor; hedef başına ayrı örnek winws
    /// tarafından reddediliyor (ayrıntılı gerekçe
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

            // Tek başarılı deneme yeterli DEĞİL. İlk gerçek koşumda bir aday
            // çalıştı, aynı aday sonraki koşumda çalışmadı; ağ koşulları
            // gürültülü ve tek ölçüm bunu ayırt edemiyor. Kullanıcıya "bulundu"
            // deyip sonra çalışmaması, hiç bulamamaktan kötü.
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
    /// Başarılı bulunan bir denemeyi, winws hâlâ çalışırken bir kez daha doğrular.
    /// </summary>
    /// <remarks>
    /// Ağ koşulları gürültülü; tek bir başarılı istek stratejinin çalıştığını
    /// kanıtlamıyor. Üst üste iki başarı, kullanıcıya "bulundu" demek için
    /// gereken en az kanıt.
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

    /// <summary>Bir bölüm için denenecek adayları tier sırasında üretir.</summary>
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

        // Aile adı tek başına KİMLİK DEĞİL: bir aile eksenlerin Kartezyen çarpımı
        // kadar aday üretiyor, dolayısıyla "ladder/fake-quic-anyproto" adında
        // onlarca farklı aday oluyordu. İlerleme satırlarında aynı ad peş peşe
        // tekrarlıyor ve (asıl sorun) doğrulanan bir aday learned.json'a bu
        // adla yazılınca hangi varyantın çalıştığı kayboluyordu. Aile içinde
        // sıra numarası veriyoruz; asıl kimlik yine argümanlar, ad okunabilirlik için.
        //
        // GroupBy burada sıralamayı bozmuyor: Expand bir ailenin bütün
        // kombinasyonlarını peş peşe üretiyor, yani aileler zaten bitişik geliyor.
        yield return (
            ProbeTier.GenericLadder,
            "Genel arama",
            InterleaveFamilies(profiles.Ladder.Expand(section)));
    }

    /// <summary>
    /// Bölüm için kullanılacak sınama protokolü.
    /// </summary>
    /// <remarks>
    /// Bu eşlemeyi yanlış yapmak sessizce yanlış sonuç üretir; ilk saha koşumunda
    /// tam da bu oldu: düz HTTP sunan bir hedefe HTTPS ile gidilmiş ve hedef
    /// "engelli" sayılmıştı.
    ///
    /// tcp443 için TLS 1.2 kasıtlı seçildi: sertifika DPI'a açık görünür, yani DPI'ın
    /// en çok müdahale ettiği durum. TLS 1.3'te ServerHello şifreli olduğu için bazı
    /// engellemeler devreye bile girmez ve test kolay geçerek yanıltır.
    /// </remarks>
    /// <summary>
    /// Bir bölümü kendi protokolüyle ölçer.
    /// </summary>
    /// <remarks>
    /// Bölüme göre istemci seçimi TEK YERDE tutuluyor. Üç ayrı çağrı yerinde
    /// tekrarlanıyordu ve QUIC istemcisi eklenirken birine yazıp diğerini atlamak,
    /// sessizce yanlış ölçen bir kod yolu bırakırdı; bu bölümde tam olarak bu tür
    /// bir hata zaten bir kez yaşandı.
    ///
    /// public olmasının sebebi teşhis yolu (--engage-check): teşhisin, arama
    /// motorunun ölçtüğü ŞEYİN AYNISINI ölçmesi gerekiyor. Ayrı bir dallanma
    /// yazılmıştı ve discord-voice'u STUN yerine HTTP/3 ile ölçüyordu; yani
    /// teşhis, motorun gördüğünden başka bir şey gösteriyordu.
    ///
    /// QUIC'in ayrı istemcisi olmasının sebebi: <see cref="HttpProbeClient"/> HTTP/3'ü
    /// çözümlenmiş IP'ye SABİTLEYEMİYOR (ConnectCallback yalnızca TCP'de çalışır).
    /// DNS kaçırması olan bir hatta bağlantı engel sunucusuna gidiyor, winws'in
    /// --ipset-ip kontrolü negatif dönüyor ve strateji hiç uygulanmadan paket geçiyor.
    /// </remarks>
    /// <param name="port">
    /// Yalnızca discord-voice bölümünde kullanılır; verilmezse STUN için 19302.
    /// Hedef listesinden gelir, çünkü Google dışındaki STUN sunucuları 3478'i
    /// kullanıyor ve bölümün kontrol hedefi başka bir işletmeciden olmalı.
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
            // Discord ses UDP üzerinden çalışıyor; HTTP istemcisiyle ölçülemez.
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
    /// Merdiven adaylarını AİLELER ARASINDA sırayla dizer: önce her ailenin ilk
    /// varyantı, sonra ikincileri, sonra üçüncüleri...
    /// </summary>
    /// <remarks>
    /// Önceden aileler peş peşe, her aile sonuna kadar deneniyordu. Bütçe sınırsız
    /// olsaydı sıra önemsizdi; ama bütçe var (arayüzde bölüm başına 60) ve sonuç
    /// ölçüldü: BAŞKA bir TTNET hattında 176 aday denendi, 1105 saniye sürdü ve
    /// hiçbiri tutmadı. `tcp443` genel aramasında 7 aile / 136 varyant var ve ilk
    /// aile (`fake-fooling`) tek başına 45 varyant; genel aramaya kalan ~33'lük
    /// bütçenin tamamını yiyor. Yani `multisplit-pure`, `multidisorder-pure`,
    /// `fakedsplit`, `fake-tls-mod` ve `syndata` aileleri HİÇ DENENMEDİ. Oysa bunlar
    /// mekanizma olarak tamamen farklı şeyler; aralarında sahte paket hiç üretmeyenler
    /// bile var.
    ///
    /// Bir ailenin ilk varyantı tutmuyorsa o ailenin TTL/fooling varyasyonlarını
    /// tüketmek, hiç denenmemiş bir mekanizmayı denemekten daha az bilgi veriyor.
    /// Bu yüzden önce genişlik, sonra derinlik.
    ///
    /// Aile İÇİNDEKİ eksen sırası değişmedi; o `generic-ladder.json`'da profil
    /// yazarının kararı ve `GenericLadder.Expand` orada bırakıldı. Burası aramanın
    /// stratejisi, merdivenin içeriği değil.
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
    /// Tekilleştirme anahtarı: bayraklar sıralanmış hâlde. YALNIZCA karşılaştırma
    /// için; winws'e her zaman adayın kendi yazımı veriliyor.
    /// </summary>
    private static string NormalizeArgs(string args)
        => string.Join(' ', args
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(part => part, StringComparer.Ordinal));

    /// <summary>Denenecek tek bir aday: görünür kimliği ve winws argümanları.</summary>
    public readonly record struct ProbeCandidate(string Id, string Args);
}
