using System.Net.Http;
using System.Net.Http.Json;

namespace ZapretTr.Prober;

/// <summary>
/// Alan adlarini sifreli DNS (DNS-over-HTTPS) uzerinden cozer.
/// </summary>
/// <remarks>
/// Neden gerekli: Turkiye'de engelleme cogu zaman IKI KATMANLI. Once DNS
/// kacirilir (sistem DNS'i engel sunucusunun IP'sini doner), altta da SNI'ye bakan
/// bir DPI durur. Sistem DNS'iyle test yapildiginda ikinci katman hic gorunmez --
/// baglanti zaten engel sunucusuna gittigi icin her strateji basarisiz olur ve
/// arac "hicbir sey ise yaramiyor" der.
///
/// Gercek IP ile test edildiginde alttaki DPI ortaya cikiyor ve iste O katman
/// zapret'in cozebildigi sey. Yani DoH burada bir "ekstra ozellik" degil, DPI
/// stratejisini bulabilmenin on kosulu.
///
/// Bu, kullanicinin sistem DNS ayarlarina DOKUNMAZ. Yalnizca testin dogru IP'ye
/// bakmasini saglar. Bulunan strateji gercekten ise yarasin diye kullanicinin
/// da sifreli DNS kullanmasi gerekir; arac bunu ayrica soyluyor.
/// </remarks>
public sealed class DohResolver : IDisposable
{
    /// <summary>
    /// Varsayilan cozumleyiciler. Birden fazla var cunku bunlardan biri de
    /// engellenmis olabilir; ilki cevap vermezse digerine gecilir.
    /// </summary>
    private static readonly string[] DefaultEndpoints =
    [
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/resolve",
    ];

    private readonly HttpClient _client;
    private readonly string[] _endpoints;

    /// <summary>Tarama boyunca cozulmus adlar. Ayni ad birden fazla bolumde geciyor.</summary>
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DohResolver(TimeSpan? timeout = null, string[]? endpoints = null)
    {
        _endpoints = endpoints ?? DefaultEndpoints;
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    }

    /// <summary>
    /// Alan adinin IPv4 adresini doner; cozulemezse null.
    /// </summary>
    /// <remarks>
    /// Sonuclar bu ornegin omru boyunca onbellekleniyor. Sebebi olculdu: hedef
    /// listesinde 10 girdi var ama yalnizca 7 benzersiz adres -- discord.com uc
    /// bolumde (tcp80, tcp443, quic), www.youtube.com iki bolumde geciyor.
    /// Onbelleksiz hali ayni adi tekrar tekrar soruyordu ve baseline taramasi
    /// SIRALI oldugu icin bu, gecikmeye dogrudan carpim olarak biniyordu: hizli
    /// hatta ~300 ms, yavas hatta 3-6 saniye bosa gidiyordu.
    ///
    /// Onbellek kasitli olarak ornek omru kadar: bir tarama boyunca ayni adin
    /// ayni adrese cozulmesi zaten istedigimiz sey -- aksi halde ayni alan adinin
    /// iki bolumu farkli sunuculara gidip sonuclar kiyaslanamaz hale gelirdi.
    /// TTL takibi gerekmiyor, cunku nesne tek bir taramadan uzun yasamiyor.
    /// </remarks>
    public async Task<string?> ResolveIPv4Async(string host, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(host, out var cached))
        {
            return cached;
        }

        var resolved = await ResolveUncachedAsync(host, cancellationToken).ConfigureAwait(false);

        // Basarisizlik da onbellege giriyor: cozulemeyen bir ad ayni tarama
        // icinde yeniden sorulursa yine cozulemeyecek, ama her denemede bir
        // zaman asimi daha yenirdi.
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

                // type 1 = A kaydi. CNAME zincirleri de donebildigi icin filtreleniyor.
                var address = response?.Answer?
                    .FirstOrDefault(a => a.Type == 1 && !string.IsNullOrWhiteSpace(a.Data))?.Data;

                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Bu cozumleyici olmadi, digerini dene. Hepsi olmazsa null donuyoruz
                // ve cagiran taraf sistem DNS'ine duser.
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
