using ZapretTr.Core.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>Bir aday stratejinin nereden geldiği. Duyum ile çıkarımı ayırmak için.</summary>
[JsonConverter(typeof(CandidateSourceJsonConverter))]
public enum CandidateSource
{
    /// <summary>TR topluluğunda bildirilmiş ama bizim doğrulamadığımız.</summary>
    CommunityUnverified,

    /// <summary>Belgelenen mekanizmadan türetilmiş; kimse bildirmedi, biz çıkardık.</summary>
    Hypothesis,

    /// <summary>zapret'in kendi örnek preset dosyasından.</summary>
    UpstreamPreset,

    /// <summary>Bizim testimizde gerçekten çalıştı.</summary>
    Verified,
}

/// <summary>
/// Kaynak etiketlerini JSON'daki tireli hâllerine bağlar.
/// (.NET 9'daki JsonStringEnumMemberName bu hedefte yok.)
/// </summary>
public sealed class CandidateSourceJsonConverter : JsonConverter<CandidateSource>
{
    public override CandidateSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "community-unverified" => CandidateSource.CommunityUnverified,
            "hypothesis" => CandidateSource.Hypothesis,
            "upstream-preset" => CandidateSource.UpstreamPreset,
            "verified" => CandidateSource.Verified,
            var other => throw new JsonException(
                $"Bilinmeyen kaynak etiketi: '{other}'. Beklenen: community-unverified, hypothesis, upstream-preset, verified."),
        };

    public override void Write(Utf8JsonWriter writer, CandidateSource value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            CandidateSource.CommunityUnverified => "community-unverified",
            CandidateSource.Hypothesis => "hypothesis",
            CandidateSource.UpstreamPreset => "upstream-preset",
            CandidateSource.Verified => "verified",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        });
}

/// <summary>Tek bir aday strateji.</summary>
public sealed class StrategyCandidate
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("section")]
    public required StrategySection Section { get; init; }

    /// <summary>
    /// winws argümanları. <c>{FAKE_QUIC_GOOGLE}</c> gibi yer tutucular
    /// <see cref="VendorPaths"/> tarafından çözülür; profiller makineye özel
    /// mutlak yol içermesin diye.
    /// </summary>
    [JsonPropertyName("args")]
    public required string Args { get; init; }

    [JsonPropertyName("protocols")]
    public IReadOnlyList<string> Protocols { get; init; } = Array.Empty<string>();

    /// <summary>Deneme sırası; büyük önce denenir. Kullanıcının kendi sonuçları bunu yerelde günceller.</summary>
    [JsonPropertyName("weight")]
    public int Weight { get; init; }

    [JsonPropertyName("source")]
    public CandidateSource Source { get; init; } = CandidateSource.Hypothesis;

    /// <summary>Hangi hedef sınıflarında doğrulandığı: genel-web, youtube, discord, discord-voice.</summary>
    [JsonPropertyName("verifiedFor")]
    public IReadOnlyList<string> VerifiedFor { get; init; } = Array.Empty<string>();

    [JsonPropertyName("lastVerified")]
    public string? LastVerified { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>Bir servis sağlayıcısı için sıralanmış aday listesi.</summary>
public sealed class IspProfile
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>Otomatik tespit için ASN listesi. Boş olabilir.</summary>
    [JsonPropertyName("asns")]
    public IReadOnlyList<int> Asns { get; init; } = Array.Empty<int>();

    /// <summary>
    /// ASN kayıt adında aranacak küçük harfli anahtar kelimeler.
    /// ASN listeleri eskidiği ve her İSS'nin ASN'sini doğrulayamadığımız için yedek eşleşme yolu.
    /// </summary>
    [JsonPropertyName("orgKeywords")]
    public IReadOnlyList<string> OrgKeywords { get; init; } = Array.Empty<string>();

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "winws";

    /// <summary>Tier 2 genişlemede komşu profillerin denenme sırası. Küçük = önce.</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 100;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("candidates")]
    public IReadOnlyList<StrategyCandidate> Candidates { get; init; } = Array.Empty<StrategyCandidate>();

    /// <summary>
    /// Bu profilin adaylarını, kullanıcının kendi doğruladıklarıyla birleştirilmiş
    /// yeni bir profil döner.
    /// </summary>
    /// <remarks>
    /// Doğrulanmış adaylar listenin BAŞINA geçer (ağırlık 200), çünkü "bu makinede
    /// gerçekten çalıştı" bilgisi, toplulukta bildirilmiş ya da mekanizmadan
    /// türetilmiş her şeyden daha güçlü bir kanıt. Merdivende zaten var olan bir
    /// aday doğrulanmışsa yerine geçer, yoksa yeni aday olarak eklenir; genel
    /// aramada bulunan kazananlar bu ikinci yoldan giriyor.
    /// </remarks>
    /// <summary>Öğrenilmiş adayın notu: ölçümün neyi gösterdiğini abartmadan söyler.</summary>
    /// <remarks>
    /// discord-voice bölümü STUN ile ölçülüyor. STUN cevabı UDP yolunun açık olduğunu
    /// gösteriyor, Discord sesli görüşmenin çalıştığını DEĞİL: ses sunucusunun adresi
    /// ancak kimliği doğrulanmış bir ses oturumundan alınıyor ve dışarıdan ölçülemiyor.
    /// Eskiden bu bölümün kazananı da "parametre testiyle doğrulandı" diye
    /// kaydediliyordu. Kaynak yine Verified kalıyor, çünkü ölçümü geçti ve çalışma
    /// zamanında ölçüm geçmemiş adaylardan önce gelmeli; abartan yalnızca metindi.
    /// </remarks>
    public static string LearnedNote(StrategySection section) => section == StrategySection.DiscordVoice
        ? "Bu baglantida STUN ile olculdu: UDP yolu acik. Discord sesli gorusmenin calistigi ayrica dogrulanmadi."
        : "Bu baglantida parametre testiyle dogrulandi.";

    public IspProfile WithLearned(IEnumerable<LearnedCandidate> learned)
    {
        var mine = learned
            .Where(l => string.Equals(l.IspId, Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mine.Count == 0)
        {
            return this;
        }

        var candidates = Candidates.ToList();

        foreach (var entry in mine)
        {
            if (!StrategySectionExtensions.TryParseJsonName(entry.Section, out var section))
            {
                continue;
            }

            var verified = new StrategyCandidate
            {
                Id = entry.CandidateId,
                Section = section,
                Args = entry.Args,
                Protocols = [],
                Weight = 200,
                Source = CandidateSource.Verified,
                VerifiedFor = entry.VerifiedFor,
                LastVerified = entry.LastVerified,
                Note = LearnedNote(section),
            };

            var existingIndex = candidates.FindIndex(c =>
                c.Section == section && string.Equals(c.Args, entry.Args, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                candidates[existingIndex] = verified;
            }
            else
            {
                candidates.Add(verified);
            }
        }

        return new IspProfile
        {
            Id = Id,
            DisplayName = DisplayName,
            Asns = Asns,
            OrgKeywords = OrgKeywords,
            Engine = Engine,
            Priority = Priority,
            Notes = Notes,
            Candidates = candidates,
        };
    }

    /// <summary>
    /// Bir bölümün adaylarını deneme sırasına göre verir (ağırlığı büyük önce).
    /// </summary>
    public IEnumerable<StrategyCandidate> CandidatesFor(StrategySection section)
        => Candidates.Where(c => c.Section == section).OrderByDescending(c => c.Weight);

    /// <summary>
    /// Bu profilin verilen ASN ve kuruluş adıyla eşleşip eşleşmediği.
    /// ASN kesin eşleşme; kuruluş adında ise anahtar kelime geçiyor mu diye bakılır.
    /// </summary>
    public bool Matches(int? asn, string? orgName)
    {
        if (asn is not null && Asns.Contains(asn.Value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(orgName))
        {
            return false;
        }

        var haystack = orgName.ToLowerInvariant();
        return OrgKeywords.Any(k => haystack.Contains(k, StringComparison.Ordinal));
    }
}
