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

    public DohResolver(TimeSpan? timeout = null, string[]? endpoints = null)
    {
        _endpoints = endpoints ?? DefaultEndpoints;
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    }

    /// <summary>
    /// Alan adinin IPv4 adresini doner; cozulemezse null.
    /// </summary>
    public async Task<string?> ResolveIPv4Async(string host, CancellationToken cancellationToken = default)
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
