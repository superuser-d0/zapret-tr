using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>Bir aday stratejinin nereden geldigi. Duyum ile cikarimi ayirmak icin.</summary>
[JsonConverter(typeof(CandidateSourceJsonConverter))]
public enum CandidateSource
{
    /// <summary>TR toplulugunda bildirilmis ama bizim dogrulamadigimiz.</summary>
    CommunityUnverified,

    /// <summary>Belgelenen mekanizmadan turetilmis; kimse bildirmedi, biz cikardik.</summary>
    Hypothesis,

    /// <summary>zapret'in kendi ornek preset dosyasindan.</summary>
    UpstreamPreset,

    /// <summary>Bizim testimizde gercekten calisti.</summary>
    Verified,
}

/// <summary>
/// Kaynak etiketlerini JSON'daki tireli hallerine baglar.
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
    /// winws argumanlari. <c>{FAKE_QUIC_GOOGLE}</c> gibi yer tutucular
    /// <see cref="VendorPaths"/> tarafindan cozulur -- profiller makineye ozel
    /// mutlak yol icermesin diye.
    /// </summary>
    [JsonPropertyName("args")]
    public required string Args { get; init; }

    [JsonPropertyName("protocols")]
    public IReadOnlyList<string> Protocols { get; init; } = Array.Empty<string>();

    /// <summary>Deneme sirasi; buyuk once denenir. Kullanicinin kendi sonuclari bunu yerelde gunceller.</summary>
    [JsonPropertyName("weight")]
    public int Weight { get; init; }

    [JsonPropertyName("source")]
    public CandidateSource Source { get; init; } = CandidateSource.Hypothesis;

    /// <summary>Hangi hedef siniflarinda dogrulandigi: genel-web, youtube, discord, discord-voice.</summary>
    [JsonPropertyName("verifiedFor")]
    public IReadOnlyList<string> VerifiedFor { get; init; } = Array.Empty<string>();

    [JsonPropertyName("lastVerified")]
    public string? LastVerified { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>Bir servis saglayicisi icin siralanmis aday listesi.</summary>
public sealed class IspProfile
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>Otomatik tespit icin ASN listesi. Bos olabilir.</summary>
    [JsonPropertyName("asns")]
    public IReadOnlyList<int> Asns { get; init; } = Array.Empty<int>();

    /// <summary>
    /// ASN kayit adinda aranacak kucuk harfli anahtar kelimeler.
    /// ASN listeleri eskidigi ve her ISP'nin ASN'sini dogrulayamadigimiz icin yedek eslesme yolu.
    /// </summary>
    [JsonPropertyName("orgKeywords")]
    public IReadOnlyList<string> OrgKeywords { get; init; } = Array.Empty<string>();

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "winws";

    /// <summary>Tier 2 genislemede komsu profillerin denenme sirasi. Kucuk = once.</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 100;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("candidates")]
    public IReadOnlyList<StrategyCandidate> Candidates { get; init; } = Array.Empty<StrategyCandidate>();

    /// <summary>
    /// Bir bolumun adaylarini deneme sirasina gore verir (agirligi buyuk once).
    /// </summary>
    public IEnumerable<StrategyCandidate> CandidatesFor(StrategySection section)
        => Candidates.Where(c => c.Section == section).OrderByDescending(c => c.Weight);

    /// <summary>
    /// Bu profilin verilen ASN ve kurulus adiyla eslesip esmedigi.
    /// ASN kesin eslesme; kurulus adi ise anahtar kelime iceriyor mu diye bakilir.
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
