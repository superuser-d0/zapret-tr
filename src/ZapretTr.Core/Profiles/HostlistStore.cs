using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Engine;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// Kategori -> alan adı eşleşmesi: stratejinin UYGULANACAĞI adresler.
/// </summary>
/// <remarks>
/// <para>
/// 0.2.1'e kadar kazanan strateji bütün 80/443 trafiğine uygulanıyordu. Bunun bedeli
/// gerçek bir kullanıcıda ölçüldü (issue #1, Vodafone Net, 2026-09-15): kazanan
/// <c>--dpi-desync-fooling=badseq</c> stratejisi açıkken GitHub çalışmıyordu.
/// Engellenmemiş bir siteye dokunmanın kazancı yok, riski var.
/// </para>
/// <para>
/// winws bunu <c>--hostlist-domains=</c> ile çözüyor (bundle'daki v72.13'te var;
/// yardım metni: "comma separated fixed domain list" ve "subdomains auto apply").
/// Liste boş kalırsa bayrak HİÇ eklenmiyor ve davranış eskisi gibi genel oluyor:
/// bu bilinçli bir tercih, çünkü boş listeyle bayrağı eklemek korumayı SESSİZCE
/// tamamen kapatırdı. Bozuk bir veri dosyasının en kötü sonucu, korumasız kalıp
/// bunu bilmemek olurdu.
/// </para>
/// </remarks>
public sealed class HostlistStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _categories;

    private HostlistStore(IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
        => _categories = categories;

    /// <summary>Hiçbir alan adı tanımadan çalışır; bayrak eklenmez (genel davranış).</summary>
    public static HostlistStore Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Tanınan kategoriler.</summary>
    public IReadOnlyCollection<string> Categories => (IReadOnlyCollection<string>)_categories.Keys;

    /// <summary>
    /// <paramref name="profilesRoot"/> içindeki <c>hostlist-domains.json</c> dosyasını okur.
    /// </summary>
    /// <remarks>
    /// Dosya yoksa ya da bozuksa FIRLATMAZ, <see cref="Empty"/> döner. Gerekçe yukarıda:
    /// veri dosyasındaki bir sorun korumayı kapatmamalı, yalnızca daraltmayı iptal etmeli.
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
    /// Verilen kategorilerin alan adları, kullanıcının kendi hedefiyle birlikte.
    /// </summary>
    /// <param name="categories">
    /// O bağlantıda ENGELLİ olduğu ölçülmüş kategoriler (kazanan adayların
    /// <c>verifiedFor</c> değeri). Boş verilirse tanınan bütün kategoriler kullanılır:
    /// ölçüm yoksa daraltmayı tamamen bırakmak yerine, aracın hedeflediği adreslere
    /// inmek daha az zararlı. Bunun bedeli, o hatta engelli OLMAYAN bir hedefe de
    /// dokunulması olabilir; bedeli olmayan seçenek yok, çünkü hangi kategorinin
    /// engelli olduğu ancak parametre testiyle biliniyor.
    /// </param>
    /// <param name="customTarget">
    /// Arayüzdeki "Açılmayan site" girdisi. Ham metin verilebilir ("discord.com",
    /// "https://x.com/abc"); alan adı çıkarılır. Bu OLMAZSA kullanıcının kendi
    /// eklediği site test edilir, doğrulanır ama çalışma zamanında korunmaz.
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
    /// Kullanıcının yazdığı metinden alan adını çıkarır. Çıkaramazsa null.
    /// </summary>
    /// <remarks>
    /// Kullanıcı "discord.com", "https://discord.com/app" ya da "discord.com/app"
    /// yazabiliyor; üçünde de winws'e gitmesi gereken şey yalnızca ana bilgisayar adı.
    /// Virgül BİLEREK eleniyor: değer <c>--hostlist-domains=</c> listesine giriyor ve
    /// virgül orada ayraç, yani tek bir girdinin listeye iki ad eklemesi anlamına gelirdi.
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

    /// <summary>
    /// Kullanıcının girdisi kullanılamıyorsa SEBEBİNİ Türkçe anlatır; kullanılabiliyorsa null.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ (issue #1, KeremKuyucu, 2026-09-15): kullanıcı "Açılmayan site" kutusuna
    /// Roblox'u eklemeye çalıştı, olmayınca virgülle iki adres yazdı, yine olmadı ve
    /// "ya çalışmıyor ya da bir şeyi yanlış yapıyorum" dedi. İkisi de doğru değildi:
    /// girdisi <see cref="TryParseDomain"/> tarafından REDDEDİLİYORDU ve bunu ona
    /// söyleyen hiçbir şey yoktu. Başarı yolunda "Kendi hedefiniz eklendi: ..." satırı
    /// var, başarısızlık yolunda hiçbir şey yoktu; yani kullanıcı kendi girdisinin
    /// yok sayıldığını görebilecek durumda değildi.
    ///
    /// Virgül .NET'in <see cref="Uri"/> ayrıştırıcısı tarafından zaten reddediliyor
    /// (ölçüldü: "a.com,b.com" ve "a.com, b.com" için TryCreate false dönüyor), yani
    /// burada yapılan iş yalnızca SEBEBİ söylemek.
    /// </remarks>
    public static string? DescribeUnusableTarget(string? input)
    {
        // Boş bırakmak geçerli bir seçim: varsayılan hedefler kullanılır.
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (TryParseDomain(input) is not null)
        {
            return null;
        }

        var text = input.Trim();

        // Kullanıcının kendi bulacağı ilk çare virgül; sebebi ayrıca söylenmezse
        // "yazdım, olmadı" döngüsüne giriyor.
        if (text.Contains(',', StringComparison.Ordinal) || text.Contains(' ', StringComparison.Ordinal))
        {
            return $"\"{text}\" anlaşılamadı: bu kutuya TEK bir adres yazın (örnek: roblox.com). "
                   + "Şimdilik birden fazla adres desteklenmiyor.";
        }

        return $"\"{text}\" bir adres olarak anlaşılamadı. Örnek: roblox.com";
    }
}

/// <summary>hostlist-domains.json'un gövdesi.</summary>
public sealed class HostlistDocument
{
    [JsonPropertyName("categories")]
    public Dictionary<string, List<string>> Categories { get; init; } = [];
}
