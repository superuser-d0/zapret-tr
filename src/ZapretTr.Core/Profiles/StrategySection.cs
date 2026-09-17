using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// winws komutunun bağımsız bölümleri. Her bölüm kendi <c>--filter-*</c> ifadesine
/// sahiptir ve nihai komutta <c>--new</c> ile ayrılır.
/// </summary>
/// <remarks>
/// Bölümlerin ayrı olması kozmetik değil: çalışan strateji hem İSS'nin hem hedef
/// sunucunun işlevi. Örneğin <c>--dpi-desync-fooling=md5sig</c> yalnızca sunucu
/// TCP MD5 seçeneğini reddettiğinde işe yarar, yani aynı İSS'de bir hedefte çalışıp
/// diğerinde çalışmayabilir. Bu yüzden her bölüm bağımsız test edilir ve bağımsız
/// kazananı olur.
/// </remarks>
[JsonConverter(typeof(StrategySectionJsonConverter))]
public enum StrategySection
{
    /// <summary>Düz HTTP. SNI yok, Host başlığı açıkta; 443'ten farklı strateji gerektirir.</summary>
    Tcp80,

    /// <summary>TLS üzerinden HTTPS. Asıl savaş alanı.</summary>
    Tcp443,

    /// <summary>QUIC / HTTP3. UDP taşıma katmanında parçalanamaz, bu yüzden seçenekler dar.</summary>
    Quic,

    /// <summary>Discord sesli görüşme (discord + STUN). TCP tarafından tamamen bağımsız.</summary>
    DiscordVoice,
}

public static class StrategySectionExtensions
{
    /// <summary>JSON profillerinde kullanılan kanonik ad.</summary>
    public static string ToJsonName(this StrategySection section) => section switch
    {
        StrategySection.Tcp80 => "tcp80",
        StrategySection.Tcp443 => "tcp443",
        StrategySection.Quic => "quic",
        StrategySection.DiscordVoice => "discord-voice",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    /// <summary>
    /// Bu bölümün winws komutundaki filtre ifadesi. Nihai komutta stratejinin
    /// hemen önüne yazılır.
    /// </summary>
    public static string ToWinwsFilter(this StrategySection section) => section switch
    {
        StrategySection.Tcp80 => "--filter-tcp=80",
        StrategySection.Tcp443 => "--filter-tcp=443",
        StrategySection.Quic => "--filter-l7=quic",
        StrategySection.DiscordVoice => "--filter-l7=discord,stun",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    public static bool TryParseJsonName(string? value, out StrategySection section)
    {
        switch (value)
        {
            case "tcp80": section = StrategySection.Tcp80; return true;
            case "tcp443": section = StrategySection.Tcp443; return true;
            case "quic": section = StrategySection.Quic; return true;
            case "discord-voice": section = StrategySection.DiscordVoice; return true;
            default: section = default; return false;
        }
    }
}

/// <summary>
/// Bölüm adlarını JSON'daki kanonik hâllerine bağlar. Varsayılan enum
/// dönüştürücüsü "discord-voice" gibi tireli adları karşılayamadığı için elle yazıldı.
/// </summary>
public sealed class StrategySectionJsonConverter : JsonConverter<StrategySection>
{
    public override StrategySection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (!StrategySectionExtensions.TryParseJsonName(raw, out var section))
        {
            throw new JsonException(
                $"Bilinmeyen bolum adi: '{raw}'. Beklenen: tcp80, tcp443, quic, discord-voice.");
        }

        return section;
    }

    public override void Write(Utf8JsonWriter writer, StrategySection value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToJsonName());
}
