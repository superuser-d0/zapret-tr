using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// Tier 3 genel arama uzayı. İSS profili ve komşu profiller tükendiğinde kullanılır.
/// </summary>
/// <remarks>
/// Yüzlerce aday tek tek JSON'a yazılmak yerine kombinatoryal tarif olarak duruyor:
/// her aile bir taban argüman ve birkaç eksen. Bu hem dosyayı küçük tutuyor hem de
/// arama uzayını ayarlamayı kolaylaştırıyor; bir eksene değer eklemek, elle yazılmış
/// onlarca satırı elde güncellemekten çok daha az hataya açık.
/// </remarks>
public sealed class GenericLadder
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("sections")]
    public IReadOnlyDictionary<string, IReadOnlyList<LadderFamily>> Sections { get; init; }
        = new Dictionary<string, IReadOnlyList<LadderFamily>>();

    /// <summary>
    /// Bir bölümün tüm adaylarını deneme sırasında üretir.
    /// </summary>
    /// <remarks>
    /// Aileler dosyadaki sırayla gelir; aile içinde ilk eksen en yavaş değişir.
    /// Yani aynı fooling'in TTL varyantları peş peşe denenir. Bu kasıtlı: bir fooling
    /// tamamen işe yaramıyorsa onun bütün TTL varyantlarını denemek yerine hızlıca
    /// bir sonraki aileye geçmek daha iyi olurdu; ama ekseni tersine çevirmek de
    /// TTL'i sabit tutup fooling gezdirmek anlamına gelir ki DPI davranışı çoğunlukla
    /// fooling'e değil TTL'e duyarlı. Bu sıra, hangi ekseni önce tüketmek istediğini
    /// profil yazarına bırakıyor: eksen sırası JSON'da değiştirilebilir.
    /// </remarks>
    public IEnumerable<LadderCandidate> Expand(StrategySection section)
    {
        if (!Sections.TryGetValue(section.ToJsonName(), out var families))
        {
            yield break;
        }

        foreach (var family in families)
        {
            foreach (var combination in CartesianProduct(family.Axes))
            {
                var parts = new List<string> { family.Base };
                parts.AddRange(combination.Where(v => !string.IsNullOrWhiteSpace(v)));

                yield return new LadderCandidate(
                    Family: family.Family,
                    Args: string.Join(' ', parts),
                    Note: family.Note);
            }
        }
    }

    /// <summary>Bir bölümün toplam aday sayısı. İlerleme çubuğu için.</summary>
    public int CountFor(StrategySection section)
        => Sections.TryGetValue(section.ToJsonName(), out var families)
            ? families.Sum(f => f.Axes.Aggregate(1, (acc, axis) => acc * Math.Max(axis.Values.Count, 1)))
            : 0;

    /// <summary>
    /// Eksenlerin Kartezyen çarpımı. İlk eksen en yavaş değişecek şekilde üretir.
    /// </summary>
    private static IEnumerable<string[]> CartesianProduct(IReadOnlyList<LadderAxis> axes)
    {
        if (axes.Count == 0)
        {
            yield return [];
            yield break;
        }

        var indices = new int[axes.Count];
        while (true)
        {
            var current = new string[axes.Count];
            for (var i = 0; i < axes.Count; i++)
            {
                current[i] = axes[i].Values[indices[i]];
            }

            yield return current;

            // Son eksenden başa doğru taşıma yap: son eksen en hızlı değişir.
            var position = axes.Count - 1;
            while (position >= 0)
            {
                indices[position]++;
                if (indices[position] < axes[position].Values.Count)
                {
                    break;
                }

                indices[position] = 0;
                position--;
            }

            if (position < 0)
            {
                yield break;
            }
        }
    }
}

public sealed class LadderFamily
{
    [JsonPropertyName("family")]
    public required string Family { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>Ailenin değişmeyen kısmı, örneğin "--dpi-desync=fake".</summary>
    [JsonPropertyName("base")]
    public required string Base { get; init; }

    [JsonPropertyName("axes")]
    public IReadOnlyList<LadderAxis> Axes { get; init; } = Array.Empty<LadderAxis>();
}

public sealed class LadderAxis
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Boş dizgi geçerli bir değer: "bu ekseni hiç kullanma" demek.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}

/// <summary>Merdivenden açılmış tek bir aday.</summary>
public readonly record struct LadderCandidate(string Family, string Args, string? Note);
