using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// Tier 3 genel arama uzayi. ISP profili ve komsu profiller tukendiginde kullanilir.
/// </summary>
/// <remarks>
/// Yuzlerce aday tek tek JSON'a yazilmak yerine kombinatoryal tarif olarak duruyor:
/// her aile bir taban arguman ve birkac eksen. Bu hem dosyayi kucuk tutuyor hem de
/// arama uzayini ayarlamayi kolaylastiriyor -- bir eksene deger eklemek, elle yazilmis
/// onlarca satiri elde guncellemekten cok daha az hataya acik.
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
    /// Bir bolumun tum adaylarini deneme sirasinda uretir.
    /// </summary>
    /// <remarks>
    /// Aileler dosyadaki sirayla gelir; aile icinde ilk eksen en yavas degisir.
    /// Yani ayni fooling'in TTL varyantlari pes pese denenir. Bu kasitli: bir fooling
    /// tamamen ise yaramiyorsa onun butun TTL varyantlarini denemek yerine hizlica
    /// bir sonraki aileye gecmek daha iyi olurdu -- ama ekseni tersine cevirmek de
    /// TTL'i sabit tutup fooling gezdirmek anlamina gelir ki DPI davranisi cogunlukla
    /// fooling'e degil TTL'e duyarli. Bu sira, hangi ekseni once tuketmek istedigini
    /// profil yazarina birakiyor: eksen sirasi JSON'da degistirilebilir.
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

    /// <summary>Bir bolumun toplam aday sayisi. Ilerleme cubugu icin.</summary>
    public int CountFor(StrategySection section)
        => Sections.TryGetValue(section.ToJsonName(), out var families)
            ? families.Sum(f => f.Axes.Aggregate(1, (acc, axis) => acc * Math.Max(axis.Values.Count, 1)))
            : 0;

    /// <summary>
    /// Eksenlerin kartezyen carpimi. Ilk eksen en yavas degisecek sekilde uretir.
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

            // Son eksenden basa dogru tasima yap: son eksen en hizli degisir.
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

    /// <summary>Ailenin degismeyen kismi, ornegin "--dpi-desync=fake".</summary>
    [JsonPropertyName("base")]
    public required string Base { get; init; }

    [JsonPropertyName("axes")]
    public IReadOnlyList<LadderAxis> Axes { get; init; } = Array.Empty<LadderAxis>();
}

public sealed class LadderAxis
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Bos dizgi gecerli bir deger: "bu ekseni hic kullanma" demek.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}

/// <summary>Merdivenden acilmis tek bir aday.</summary>
public readonly record struct LadderCandidate(string Family, string Args, string? Note);
