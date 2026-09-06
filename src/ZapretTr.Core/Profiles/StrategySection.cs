using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// winws komutunun bagimsiz bolumleri. Her bolum kendi <c>--filter-*</c> ifadesine
/// sahiptir ve nihai komutta <c>--new</c> ile ayrilir.
/// </summary>
/// <remarks>
/// Bolumlerin ayri olmasi kozmetik degil: calisan strateji hem ISP'nin hem hedef
/// sunucunun fonksiyonu. Ornegin <c>--dpi-desync-fooling=md5sig</c> yalnizca sunucu
/// TCP MD5 secenegini reddettiginde ise yarar, yani ayni ISP'de bir hedefte calisip
/// digerinde calismayabilir. Bu yuzden her bolum bagimsiz test edilir ve bagimsiz
/// kazanani olur.
/// </remarks>
[JsonConverter(typeof(StrategySectionJsonConverter))]
public enum StrategySection
{
    /// <summary>Duz HTTP. SNI yok, Host basligi acikta -- 443'ten farkli strateji gerektirir.</summary>
    Tcp80,

    /// <summary>TLS uzerinden HTTPS. Asil savas alani.</summary>
    Tcp443,

    /// <summary>QUIC / HTTP3. UDP tasima katmaninda parcalanamaz, bu yuzden secenekler dar.</summary>
    Quic,

    /// <summary>Discord sesli gorusme (discord + STUN). TCP tarafindan tamamen bagimsiz.</summary>
    DiscordVoice,
}

public static class StrategySectionExtensions
{
    /// <summary>JSON profillerinde kullanilan kanonik ad.</summary>
    public static string ToJsonName(this StrategySection section) => section switch
    {
        StrategySection.Tcp80 => "tcp80",
        StrategySection.Tcp443 => "tcp443",
        StrategySection.Quic => "quic",
        StrategySection.DiscordVoice => "discord-voice",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    /// <summary>
    /// Bu bolumun winws komutundaki filtre ifadesi. Nihai komutta stratejinin
    /// hemen onune yazilir.
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
/// Bolum adlarini JSON'daki kanonik hallerine baglar. Varsayilan enum
/// donusturucusu "discord-voice" gibi tireli adlari karsilayamadigi icin elle yazildi.
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
