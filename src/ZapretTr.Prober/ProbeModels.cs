using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>Bir hedefin hangi protokolle sinanacagi.</summary>
/// <remarks>
/// Bolume gore secilir. Yanlis secim sessizce yanlis sonuc uretir: duz HTTP sunan
/// bir hedefe HTTPS ile gitmek onu "engelli" gosterir, QUIC hedefini TCP uzerinden
/// olcmek ise QUIC'i hic olcmemis olur.
/// </remarks>
public enum ProbeMode
{
    /// <summary>Duz HTTP, 80 portu.</summary>
    PlainHttp,

    /// <summary>TLS 1.2 zorlanir. Sertifika DPI'a acik gorunur; en cok mudahale edilen durum.</summary>
    Tls12,

    /// <summary>TLS 1.3 zorlanir. ServerHello sifreli oldugu icin DPI'in gordugu sey farkli.</summary>
    Tls13,

    /// <summary>HTTP/3 zorlanir. QUIC'i gercekten olcmenin tek yolu.</summary>
    Http3,
}

/// <summary>Test edilecek hedef.</summary>
/// <param name="Host">Alan adi.</param>
/// <param name="Label">Kullaniciya gosterilen ad.</param>
/// <param name="Category">Hedef sinifi: genel-web, youtube, discord, discord-voice.</param>
/// <param name="Section">Bu hedefin hangi bolumu test ettigi.</param>
/// <param name="Port">
/// Yalnizca <see cref="StrategySection.DiscordVoice"/> bolumunde anlamli: STUN
/// sunucusunun portu. Verilmezse Google'in kullandigi 19302 varsayilir. Alan
/// gerekli oldu cunku discord-voice bolumune KONTROL hedefi eklenebilmesi icin
/// baska bir isletmecinin sunucusu gerekiyordu ve Google disindaki STUN
/// sunuculari 3478'i kullaniyor. tcp80/tcp443/quic bolumlerinde port zaten
/// bolumun kendisinden belli.
/// </param>
public sealed record ProbeTarget(
    string Host, string Label, string Category, StrategySection Section, int? Port = null);

/// <summary>Bir hedefin winws kapaliyken erisilebilirlik durumu.</summary>
public enum BaselineStatus
{
    /// <summary>Zaten aciliyor -- strateji testinde kullanilamaz.</summary>
    Accessible,

    /// <summary>Engelli. Test icin anlamli hedef.</summary>
    Blocked,

    /// <summary>
    /// Baglanti kuruldu ama karsi taraf engel sayfasi dondurdu.
    /// </summary>
    /// <remarks>
    /// Bu DPI engellemesi DEGIL: trafik zaten dogru sunucuya gitmiyor, cogunlukla
    /// DNS yonlendirmesi yuzunden engel sunucusuna gidiyor. zapret paketleri
    /// kurcalayarak bunu cozemez -- cozum DNS tarafinda (DoH/DoT). Bu hedefler
    /// strateji aramasindan CIKARILIR, yoksa hicbiri calismayacak yuzlerce aday
    /// bosuna denenir.
    /// </remarks>
    DnsRedirected,

    /// <summary>Cozumlenemedi ya da baska bir sebeple karar verilemedi.</summary>
    Inconclusive,
}

/// <summary>Baseline taramasinin tek bir hedef icin sonucu.</summary>
public sealed record BaselineResult(ProbeTarget Target, BaselineStatus Status, string? Detail, string? ResolvedIp);

/// <summary>Tek bir adayin tek bir hedefteki sonucu.</summary>
public sealed record CandidateResult(
    string CandidateId,
    string Args,
    StrategySection Section,
    string TargetHost,
    string TargetCategory,
    bool Succeeded,
    string? Detail,
    TimeSpan Duration);

/// <summary>Bir bolumun kazanani.</summary>
public sealed record SectionWinner(
    StrategySection Section,
    string CandidateId,
    string Args,
    IReadOnlyList<string> VerifiedCategories);

/// <summary>Aramanin hangi asamada oldugu.</summary>
public enum ProbeTier
{
    /// <summary>winws kapaliyken hangi hedeflerin gercekten engelli oldugunu belirleme.</summary>
    Baseline,

    /// <summary>Secilen ISP'nin kendi profili.</summary>
    IspProfile,

    /// <summary>Komsu TR profilleri.</summary>
    Neighbours,

    /// <summary>Genel kombinatoryal arama.</summary>
    GenericLadder,
}

/// <summary>Arayuzun ilerleme cubugunu besleyen olay.</summary>
public sealed record ProbeProgress(
    ProbeTier Tier,
    string TierLabel,
    int Completed,
    int Total,
    string CurrentDescription)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}

/// <summary>Testin tamaminin sonucu.</summary>
public sealed record ProbeReport(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    string? IspProfileId,
    int? Asn,
    string? OrgName,
    IReadOnlyList<BaselineResult> Baseline,
    IReadOnlyList<CandidateResult> Attempts,
    IReadOnlyList<SectionWinner> Winners)
{
    /// <summary>Hicbir bolumde kazanan bulunamadiysa true.</summary>
    public bool IsEmpty => Winners.Count == 0;

    /// <summary>Kazananlari winws komut kurucusunun bekledigi sozluge cevirir.</summary>
    public IReadOnlyDictionary<StrategySection, string> ToWinnerMap()
        => Winners.ToDictionary(w => w.Section, w => w.Args);
}
