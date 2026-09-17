using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>Bir hedefin hangi protokolle sınanacağı.</summary>
/// <remarks>
/// Bölüme göre seçilir. Yanlış seçim sessizce yanlış sonuç üretir: düz HTTP sunan
/// bir hedefe HTTPS ile gitmek onu "engelli" gösterir, QUIC hedefini TCP üzerinden
/// ölçmek ise QUIC'i hiç ölçmemiş olur.
/// </remarks>
public enum ProbeMode
{
    /// <summary>Düz HTTP, 80 portu.</summary>
    PlainHttp,

    /// <summary>TLS 1.2 zorlanır. Sertifika DPI'a açık görünür; en çok müdahale edilen durum.</summary>
    Tls12,

    /// <summary>TLS 1.3 zorlanır. ServerHello şifreli olduğu için DPI'ın gördüğü şey farklı.</summary>
    Tls13,

    /// <summary>HTTP/3 zorlanır. QUIC'i gerçekten ölçmenin tek yolu.</summary>
    Http3,
}

/// <summary>Test edilecek hedef.</summary>
/// <param name="Host">Alan adı.</param>
/// <param name="Label">Kullanıcıya gösterilen ad.</param>
/// <param name="Category">Hedef sınıfı: genel-web, youtube, discord, discord-voice.</param>
/// <param name="Section">Bu hedefin hangi bölümü test ettiği.</param>
/// <param name="Port">
/// Yalnızca <see cref="StrategySection.DiscordVoice"/> bölümünde anlamlı: STUN
/// sunucusunun portu. Verilmezse Google'ın kullandığı 19302 varsayılır. Alan
/// gerekli oldu, çünkü discord-voice bölümüne KONTROL hedefi eklenebilmesi için
/// başka bir işletmecinin sunucusu gerekiyordu ve Google dışındaki STUN
/// sunucuları 3478'i kullanıyor. tcp80/tcp443/quic bölümlerinde port zaten
/// bölümün kendisinden belli.
/// </param>
public sealed record ProbeTarget(
    string Host, string Label, string Category, StrategySection Section, int? Port = null);

/// <summary>Bir hedefin winws kapalıyken erişilebilirlik durumu.</summary>
public enum BaselineStatus
{
    /// <summary>Zaten açılıyor; strateji testinde kullanılamaz.</summary>
    Accessible,

    /// <summary>Engelli. Test için anlamlı hedef.</summary>
    Blocked,

    /// <summary>
    /// Bağlantı kuruldu ama karşı taraf engel sayfası döndürdü.
    /// </summary>
    /// <remarks>
    /// Bu DPI engellemesi DEĞİL: trafik zaten doğru sunucuya gitmiyor, çoğunlukla
    /// DNS yönlendirmesi yüzünden engel sunucusuna gidiyor. zapret paketleri
    /// kurcalayarak bunu çözemez; çözüm DNS tarafında (DoH/DoT). Bu hedefler
    /// strateji aramasından ÇIKARILIR, yoksa hiçbiri çalışmayacak yüzlerce aday
    /// boşuna denenir.
    /// </remarks>
    DnsRedirected,

    /// <summary>Çözümlenemedi ya da başka bir sebeple karar verilemedi.</summary>
    Inconclusive,
}

/// <summary>Baseline taramasının tek bir hedef için sonucu.</summary>
public sealed record BaselineResult(ProbeTarget Target, BaselineStatus Status, string? Detail, string? ResolvedIp);

/// <summary>Tek bir adayın tek bir hedefteki sonucu.</summary>
public sealed record CandidateResult(
    string CandidateId,
    string Args,
    StrategySection Section,
    string TargetHost,
    string TargetCategory,
    bool Succeeded,
    string? Detail,
    TimeSpan Duration);

/// <summary>Bir bölümün kazananı.</summary>
public sealed record SectionWinner(
    StrategySection Section,
    string CandidateId,
    string Args,
    IReadOnlyList<string> VerifiedCategories);

/// <summary>Aramanın hangi aşamada olduğu.</summary>
public enum ProbeTier
{
    /// <summary>winws kapalıyken hangi hedeflerin gerçekten engelli olduğunu belirleme.</summary>
    Baseline,

    /// <summary>Seçilen İSS'nin kendi profili.</summary>
    IspProfile,

    /// <summary>Komşu TR profilleri.</summary>
    Neighbours,

    /// <summary>Genel kombinatoryal arama.</summary>
    GenericLadder,
}

/// <summary>Arayüzün ilerleme çubuğunu besleyen olay.</summary>
public sealed record ProbeProgress(
    ProbeTier Tier,
    string TierLabel,
    int Completed,
    int Total,
    string CurrentDescription)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}

/// <summary>Testin tamamının sonucu.</summary>
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
    /// <summary>Hiçbir bölümde kazanan bulunamadıysa true.</summary>
    public bool IsEmpty => Winners.Count == 0;

    /// <summary>Kazananları winws komut kurucusunun beklediği sözlüğe çevirir.</summary>
    public IReadOnlyDictionary<StrategySection, string> ToWinnerMap()
        => Winners.ToDictionary(w => w.Section, w => w.Args);
}
