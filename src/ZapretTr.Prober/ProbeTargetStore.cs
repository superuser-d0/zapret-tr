using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>
/// Test hedeflerini profiles/probe-targets.json dosyasından yükler.
/// </summary>
/// <remarks>
/// Hedefler kodda değil veride: kimin neye erişemediği zamanla ve kişiye göre
/// değişiyor, dolayısıyla bunu değiştirmek için yeniden derleme gerekmemeli.
/// Kullanıcı arayüzden kendi hedefini de ekleyebilir.
/// </remarks>
public static class ProbeTargetStore
{
    public static IReadOnlyList<ProbeTarget> Load(string profilesDirectory)
    {
        var path = Path.Combine(profilesDirectory, "probe-targets.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Hedef listesi bulunamadi: {path}", path);
        }

        var document = JsonSerializer.Deserialize(File.ReadAllText(path), ProberJsonContext.Default.TargetDocument)
                       ?? throw new InvalidDataException("probe-targets.json bos cozumlendi.");

        return document.Targets
            .Select(t => new ProbeTarget(t.Host, t.Label, t.Category, t.Section, t.Port))
            .ToList();
    }

    /// <summary>
    /// Kullanıcının elle girdiği bir alan adını hedefe çevirir.
    /// </summary>
    /// <remarks>
    /// Kullanıcılar adres çubuğundan kopyalayıp yapıştırıyor, yani "https://site.com/yol"
    /// gelmesi normal. Alan adını ayıklamak arayüzün değil burasının işi.
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

    internal sealed class TargetDocument
    {
        [JsonPropertyName("targets")]
        public IReadOnlyList<TargetEntry> Targets { get; init; } = Array.Empty<TargetEntry>();
    }

    internal sealed class TargetEntry
    {
        [JsonPropertyName("host")]
        public required string Host { get; init; }

        [JsonPropertyName("label")]
        public required string Label { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }

        [JsonPropertyName("section")]
        public StrategySection Section { get; init; }

        /// <summary>Yalnızca discord-voice bölümünde anlamlı; verilmezse 19302 varsayılır.</summary>
        [JsonPropertyName("port")]
        public int? Port { get; init; }
    }
}
