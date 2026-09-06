using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ZapretTr.Prober;

/// <summary>Tek bir erisim denemesinin sonucu.</summary>
public sealed record ProbeOutcome(bool Succeeded, string Detail, string? ResolvedIp);

/// <summary>
/// Bir hedefe erisilip erisilemedigini olcer.
/// </summary>
/// <remarks>
/// blockcheck.sh'in curl ile yaptigi isin native karsiligi. Uc onemli fark var:
///
/// 1. Baglanti cozumlenmis IP'ye SABITLENIR. Hem her denemenin ayni sunucuya gitmesini
///    garanti eder (CDN'lerde bu onemsiz degil), hem de winws'e verecegimiz
///    --ipset-ip degerini elde etmis oluruz.
///
/// 2. TLS surumu acikca zorlanir. TLS 1.2'de sertifika DPI'a acik gorunur, TLS 1.3'te
///    ServerHello sifreli -- DPI'in gorebildigi sey degistigi icin ikisi farkli
///    strateji gerektirebilir ve ayri puanlanmalari gerekir.
///
/// 3. Turkiye'ye ozel engel sayfasi tespiti var: BTK engellemesi cogu zaman baglantiyi
///    kesmez, 200 ile bir bilgilendirme sayfasi dondurur. Yalnizca durum koduna bakan
///    bir kontrol bunu "calisiyor" sanar.
/// </remarks>
public sealed class HttpProbeClient : IDisposable
{
    /// <summary>
    /// BTK/erisim engeli sayfalarinda gecen ifadeler. Sayfa 200 dondurdugu icin
    /// durum kodu yetmiyor, icerige bakmak gerekiyor.
    /// </summary>
    private static readonly string[] BlockPageMarkers =
    [
        "btk.gov.tr",
        "bilgi teknolojileri ve i", // "Bilgi Teknolojileri ve Iletisim Kurumu" (kodlama farklarina karsi kisa tutuldu)
        "internet sitesine erisim",
        "koruma tedbiri",
        "5651 say",
        "erisime engellenmi",
        "tib.gov.tr",
    ];

    private readonly TimeSpan _timeout;

    public HttpProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(6);

    /// <summary>
    /// Hedefe TCP/TLS uzerinden erisilip erisilemedigini dener.
    /// </summary>
    /// <param name="host">Alan adi.</param>
    /// <param name="tlsProtocol">
    /// Zorlanacak TLS surumu. <see cref="SslProtocols.None"/> verilirse isletim sistemi secer.
    /// </param>
    /// <param name="pinnedIp">
    /// Baglanilacak IP. null verilirse cozumleme burada yapilir. Ayni testin farkli
    /// adaylarinda ayni IP'yi kullanmak icin cagiran taraf bunu sabitler.
    /// </param>
    public async Task<ProbeOutcome> TryReachAsync(
        string host,
        SslProtocols tlsProtocol = SslProtocols.None,
        string? pinnedIp = null,
        CancellationToken cancellationToken = default)
    {
        string? resolvedIp = pinnedIp;

        try
        {
            if (resolvedIp is null)
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                var v4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (v4 is null)
                {
                    return new ProbeOutcome(false, "DNS: IPv4 adresi cozumlenemedi", null);
                }

                resolvedIp = v4.ToString();
            }

            using var handler = CreateHandler(resolvedIp, tlsProtocol);
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = _timeout };

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/");
            // Kimlik dogrulama gerektirmeyen, cerezsiz, sade bir istek: amac icerik
            // almak degil, DPI'in TLS el sikismasina karisip karismadigini olcmek.
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            request.Headers.AcceptEncoding.ParseAdd("identity");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            return await EvaluateResponseAsync(response, resolvedIp, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Zaman asimi engellemenin en yaygin belirtisi: DPI paketi sessizce dusuruyor.
            return new ProbeOutcome(false, "zaman asimi", resolvedIp);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx)
        {
            // RST cogunlukla aktif engelleme demek.
            var reason = socketEx.SocketErrorCode == SocketError.ConnectionReset
                ? "baglanti sifirlandi (RST)"
                : $"soket hatasi: {socketEx.SocketErrorCode}";
            return new ProbeOutcome(false, reason, resolvedIp);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            // TLS el sikismasi bozuldu. Strateji uygulanirken bu, sahte paketin gercek
            // baglantiyi da bozdugu anlamina gelir -- md5sig'in yanlis sunucuda
            // kullanilmasinin tipik belirtisi.
            return new ProbeOutcome(false, "TLS el sikismasi basarisiz", resolvedIp);
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, ex.GetType().Name + ": " + ex.Message, resolvedIp);
        }
    }

    private async Task<ProbeOutcome> EvaluateResponseAsync(
        HttpResponseMessage response, string resolvedIp, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;

        // 400: sunucu bozuk paket aldi. Strateji baglantiyi bozmus demektir.
        if (status == 400)
        {
            return new ProbeOutcome(false, "HTTP 400 -- sunucu bozuk istek aldi", resolvedIp);
        }

        // Engel sayfasi 200 de donebilir, yonlendirme de yapabilir. Ikisinde de icerige bakiyoruz.
        var body = await ReadPrefixAsync(response, cancellationToken).ConfigureAwait(false);
        var marker = FindBlockMarker(body);
        if (marker is not null)
        {
            return new ProbeOutcome(false, $"engel sayfasi ({marker})", resolvedIp);
        }

        if (status is >= 300 and < 400)
        {
            var location = response.Headers.Location?.ToString() ?? "(bos)";
            var locationMarker = FindBlockMarker(location);
            if (locationMarker is not null)
            {
                return new ProbeOutcome(false, $"engel sayfasina yonlendirme ({locationMarker})", resolvedIp);
            }

            // Alakasiz bir yonlendirme suphelidir ama kesin degil; basarisiz sayiyoruz
            // ki yanlis pozitif uretmeyelim.
            return new ProbeOutcome(false, $"HTTP {status} -> {location}", resolvedIp);
        }

        if (status is >= 200 and < 300)
        {
            return new ProbeOutcome(true, $"HTTP {status}", resolvedIp);
        }

        return new ProbeOutcome(false, $"HTTP {status}", resolvedIp);
    }

    /// <summary>Govdenin basindan bir parca okur. Tamamini indirmek gereksiz ve yavas.</summary>
    private static async Task<string> ReadPrefixAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch
        {
            // Govde okunamamasi engel tespiti icin belirleyici degil; durum koduyla devam.
            return string.Empty;
        }
    }

    private static string? FindBlockMarker(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var haystack = text.ToLowerInvariant();
        return BlockPageMarkers.FirstOrDefault(m => haystack.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Baglantiyi belirli bir IP'ye sabitleyen ve TLS surumunu zorlayan handler kurar.
    /// </summary>
    private SocketsHttpHandler CreateHandler(string pinnedIp, SslProtocols tlsProtocol)
    {
        var handler = new SocketsHttpHandler
        {
            // Yonlendirmeyi elle degerlendiriyoruz: engel sayfasina yonlendirme
            // otomatik takip edilirse tespit edilemez hale gelir.
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = _timeout,
            PooledConnectionLifetime = TimeSpan.Zero,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = tlsProtocol,
                // Sertifika gecerliligi bizim olctugumuz sey degil; DPI mudahalesi
                // sertifikayi bozabilir ve bu da olcmek istedigimiz sinyalin parcasi.
                // Guvenli bir kanal kurmuyoruz, bir davranisi olcuyoruz.
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            },
        };

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket
                    .ConnectAsync(IPAddress.Parse(pinnedIp), context.DnsEndPoint.Port, cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

        return handler;
    }

    public void Dispose()
    {
        // Handler ve client her denemede yeniden kuruluyor (baglanti havuzunun
        // onceki stratejinin acik baglantisini yeniden kullanmasi sonuclari
        // kirletirdi), bu yuzden burada tutulan bir kaynak yok.
    }
}
