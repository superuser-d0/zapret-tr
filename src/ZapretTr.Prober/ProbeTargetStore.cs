using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>
/// Test hedeflerini profiles/probe-targets.json dosyasindan yukler.
/// </summary>
/// <remarks>
/// Hedefler kodda degil veride: kimin neye erisemedigi zamanla ve kisiye gore
/// degisiyor, dolayisiyla bunu degistirmek icin yeniden derleme gerekmemeli.
/// Kullanici arayuzden kendi hedefini de ekleyebilir.
/// </remarks>
public static class ProbeTargetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<ProbeTarget> Load(string profilesDirectory)
    {
        var path = Path.Combine(profilesDirectory, "probe-targets.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Hedef listesi bulunamadi: {path}", path);
        }

        var document = JsonSerializer.Deserialize<TargetDocument>(File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidDataException("probe-targets.json bos cozumlendi.");

        return document.Targets
            .Select(t => new ProbeTarget(t.Host, t.Label, t.Category, t.Section))
            .ToList();
    }

    /// <summary>
    /// Kullanicinin elle girdigi bir alan adini hedefe cevirir.
    /// </summary>
    /// <remarks>
    /// Kullanicilar adres cubugundan kopyalayip yapistiriyor, yani "https://site.com/yol"
    /// gelmesi normal. Alan adini ayiklamak arayuzun degil burasinin isi.
    /// </remarks>
    public static ProbeTarget? TryParseUserTarget(string input, StrategySection section = StrategySection.Tcp443)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return new ProbeTarget(uri.Host, uri.Host, "kullanici", section);
    }

    private sealed class TargetDocument
    {
        [JsonPropertyName("targets")]
        public IReadOnlyList<TargetEntry> Targets { get; init; } = Array.Empty<TargetEntry>();
    }

    private sealed class TargetEntry
    {
        [JsonPropertyName("host")]
        public required string Host { get; init; }

        [JsonPropertyName("label")]
        public required string Label { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }

        [JsonPropertyName("section")]
        public StrategySection Section { get; init; }
    }
}
