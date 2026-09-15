using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Engine;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// Kategori -> alan adi eslesmesi: stratejinin UYGULANACAGI adresler.
/// </summary>
/// <remarks>
/// <para>
/// 0.2.1'e kadar kazanan strateji butun 80/443 trafigine uygulaniyordu. Bunun bedeli
/// gercek bir kullanicida olculdu (issue #1, Vodafone Net, 2026-09-15): kazanan
/// <c>--dpi-desync-fooling=badseq</c> stratejisi acikken GitHub calismiyordu.
/// Engellenmemis bir siteye dokunmanin kazanci yok, riski var.
/// </para>
/// <para>
/// winws bunu <c>--hostlist-domains=</c> ile cozuyor (bundle'daki v72.13'te var;
/// yardim metni: "comma separated fixed domain list" ve "subdomains auto apply").
/// Liste bos kalirsa bayrak HIC eklenmiyor ve davranis eskisi gibi genel oluyor:
/// bu bilincli bir tercih, cunku bos listeyle bayragi eklemek korumayi SESSIZCE
/// tamamen kapatirdi -- bozuk bir veri dosyasinin en kotu sonucu, korumasiz kalip
/// bunu bilmemek olurdu.
/// </para>
/// </remarks>
public sealed class HostlistStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _categories;

    private HostlistStore(IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
        => _categories = categories;

    /// <summary>Hicbir alan adi tanimadan calisir; bayrak eklenmez (genel davranis).</summary>
    public static HostlistStore Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Taninan kategoriler.</summary>
    public IReadOnlyCollection<string> Categories => (IReadOnlyCollection<string>)_categories.Keys;

    /// <summary>
    /// <paramref name="profilesRoot"/> icindeki <c>hostlist-domains.json</c> dosyasini okur.
    /// </summary>
    /// <remarks>
    /// Dosya yoksa ya da bozuksa FIRLATMAZ, <see cref="Empty"/> doner. Gerekce yukarida:
    /// veri dosyasindaki bir sorun korumayi kapatmamali, yalnizca daraltmayi iptal etmeli.
    /// </remarks>
    public static HostlistStore Load(string profilesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRoot);

        var path = Path.Combine(profilesRoot, "hostlist-domains.json");

        try
        {
            if (!File.Exists(path))
            {
                return Empty;
            }

            var document = JsonSerializer.Deserialize(
                File.ReadAllText(path), CoreJsonContext.Default.HostlistDocument);

            if (document?.Categories is not { Count: > 0 } categories)
            {
                return Empty;
            }

            var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, domains) in categories)
            {
                map[category] = domains
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => d.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            return new HostlistStore(map);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Verilen kategorilerin alan adlari, kullanicinin kendi hedefiyle birlikte.
    /// </summary>
    /// <param name="categories">
    /// O baglantida ENGELLI oldugu olculmus kategoriler (kazanan adaylarin
    /// <c>verifiedFor</c> degeri). Bos verilirse taninan butun kategoriler kullanilir:
    /// olcum yoksa daraltmayi tamamen birakmak yerine, aracin hedefledigi adreslere
    /// inmek daha az zararli. Bunun bedeli, o hatta engelli OLMAYAN bir hedefe de
    /// dokunulmasi olabilir; bedeli olmayan secenek yok, cunku hangi kategorinin
    /// engelli oldugu ancak parametre testiyle biliniyor.
    /// </param>
    /// <param name="customTarget">
    /// Arayuzdeki "Acilmayan site" girdisi. Ham metin verilebilir ("discord.com",
    /// "https://x.com/abc"); alan adi cikarilir. Bu OLMAZSA kullanicinin kendi
    /// ekledigi site test edilir, dogrulanir ama calisma zamaninda korunmaz.
    /// </param>
    public IReadOnlyList<string> DomainsFor(
        IEnumerable<string>? categories = null, string? customTarget = null)
    {
        var sonuc = new SortedSet<string>(StringComparer.Ordinal);

        var secilen = categories?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        var anahtarlar = secilen is { Count: > 0 } ? secilen : _categories.Keys.ToList();

        foreach (var category in anahtarlar)
        {
            if (_categories.TryGetValue(category, out var domains))
            {
                foreach (var domain in domains)
                {
                    sonuc.Add(domain);
                }
            }
        }

        if (TryParseDomain(customTarget) is { } custom)
        {
            sonuc.Add(custom);
        }

        return [.. sonuc];
    }

    /// <summary>
    /// Kullanicinin yazdigi metinden alan adini cikarir. Cikaramazsa null.
    /// </summary>
    /// <remarks>
    /// Kullanici "discord.com", "https://discord.com/app" ya da "discord.com/app"
    /// yazabiliyor; uctunde de winws'e gitmesi gereken sey yalnizca ana bilgisayar adi.
    /// Virgul BILEREK eleniyor: deger <c>--hostlist-domains=</c> listesine giriyor ve
    /// virgul orada ayrac, yani tek bir girdinin listeye iki ad eklemesi anlamina gelirdi.
    /// </remarks>
    public static string? TryParseDomain(string? input)
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

        var host = uri.Host.Trim().ToLowerInvariant();

        return host.Contains(',', StringComparison.Ordinal) ? null : host;
    }
}

/// <summary>hostlist-domains.json'un govdesi.</summary>
public sealed class HostlistDocument
{
    [JsonPropertyName("categories")]
    public Dictionary<string, List<string>> Categories { get; init; } = [];
}
