using System.Net.Http;
using System.Net.Http.Json;

namespace ZapretTr.Prober;

/// <summary>
/// Alan adlarını şifreli DNS (DNS-over-HTTPS) üzerinden çözer.
/// </summary>
/// <remarks>
/// Neden gerekli: Türkiye'de engelleme çoğu zaman İKİ KATMANLI. Önce DNS
/// kaçırılır (sistem DNS'i engel sunucusunun IP'sini döner), altta da SNI'ye bakan
/// bir DPI durur. Sistem DNS'iyle test yapıldığında ikinci katman hiç görünmez:
/// bağlantı zaten engel sunucusuna gittiği için her strateji başarısız olur ve
/// araç "hiçbir şey işe yaramıyor" der.
///
/// Gerçek IP ile test edildiğinde alttaki DPI ortaya çıkıyor ve işte O katman
/// zapret'in çözebildiği şey. Yani DoH burada bir "ekstra özellik" değil, DPI
/// stratejisini bulabilmenin ön koşulu.
///
/// Bu, kullanıcının sistem DNS ayarlarına DOKUNMAZ. Yalnızca testin doğru IP'ye
/// bakmasını sağlar. Bulunan strateji gerçekten işe yarasın diye kullanıcının
/// da şifreli DNS kullanması gerekir; araç bunu ayrıca söylüyor.
/// </remarks>
public sealed class DohResolver : IDisposable
{
    /// <summary>
    /// Varsayılan çözümleyiciler. Birden fazla var, çünkü bunlardan biri de
    /// engellenmiş olabilir; ilki cevap vermezse diğerine geçilir.
    /// </summary>
    private static readonly string[] DefaultEndpoints =
    [
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/resolve",
    ];

    private readonly HttpClient _client;
    private readonly string[] _endpoints;

    /// <summary>Tarama boyunca çözülmüş adlar. Aynı ad birden fazla bölümde geçiyor.</summary>
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DohResolver(TimeSpan? timeout = null, string[]? endpoints = null)
    {
        _endpoints = endpoints ?? DefaultEndpoints;
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    }

    /// <summary>
    /// Alan adının IPv4 adresini döner; çözülemezse null.
    /// </summary>
    /// <remarks>
    /// Sonuçlar bu örneğin ömrü boyunca önbelleğe alınıyor. Sebebi ölçüldü: hedef
    /// listesinde 10 girdi var ama yalnızca 7 benzersiz adres; discord.com üç
    /// bölümde (tcp80, tcp443, quic), www.youtube.com iki bölümde geçiyor.
    /// Önbelleksiz hâli aynı adı tekrar tekrar soruyordu ve baseline taraması
    /// SIRALI olduğu için bu, gecikmeye doğrudan çarpım olarak biniyordu: hızlı
    /// hatta ~300 ms, yavaş hatta 3-6 saniye boşa gidiyordu.
    ///
    /// Önbellek kasıtlı olarak örnek ömrü kadar: bir tarama boyunca aynı adın
    /// aynı adrese çözülmesi zaten istediğimiz şey; aksi hâlde aynı alan adının
    /// iki bölümü farklı sunuculara gidip sonuçlar kıyaslanamaz hâle gelirdi.
    /// TTL takibi gerekmiyor, çünkü nesne tek bir taramadan uzun yaşamıyor.
    /// </remarks>
    public async Task<string?> ResolveIPv4Async(string host, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(host, out var cached))
        {
            return cached;
        }

        var resolved = await ResolveUncachedAsync(host, cancellationToken).ConfigureAwait(false);

        // Başarısızlık da önbelleğe giriyor: çözülemeyen bir ad aynı tarama
        // içinde yeniden sorulursa yine çözülemeyecek, ama her denemede bir
        // zaman aşımı daha yenirdi.
        _cache[host] = resolved;
        return resolved;
    }

    private async Task<string?> ResolveUncachedAsync(string host, CancellationToken cancellationToken)
    {
        foreach (var endpoint in _endpoints)
        {
            try
            {
                var url = $"{endpoint}?name={Uri.EscapeDataString(host)}&type=A";
                var response = await _client.GetFromJsonAsync(url, ProberJsonContext.Default.DohResponse, cancellationToken)
                    .ConfigureAwait(false);

                // type 1 = A kaydı. CNAME zincirleri de dönebildiği için filtreleniyor.
                var address = response?.Answer?
                    .FirstOrDefault(a => a.Type == 1 && !string.IsNullOrWhiteSpace(a.Data))?.Data;

                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Bu çözümleyici olmadı, diğerini dene. Hepsi olmazsa null dönüyoruz
                // ve çağıran taraf sistem DNS'ine düşer.
            }
        }

        return null;
    }

    public void Dispose() => _client.Dispose();

    internal sealed class DohResponse
    {
        public List<DohAnswer>? Answer { get; set; }
    }

    internal sealed class DohAnswer
    {
        public int Type { get; set; }

        public string? Data { get; set; }
    }
}
