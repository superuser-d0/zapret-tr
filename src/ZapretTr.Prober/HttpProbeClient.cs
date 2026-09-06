using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ZapretTr.Prober;

/// <summary>Tek bir erisim denemesinin sonucu.</summary>
/// <param name="IsBlockPage">
/// Erisilen sunucu bir engel sayfasi dondurdu. Bu, DPI engellemesinden FARKLI bir
/// durum: baglanti kuruldu ama yanlis sunucuya. Cogunlukla DNS yonlendirmesi demek
/// ve zapret bunu cozemez.
/// </param>
public sealed record ProbeOutcome(
    bool Succeeded,
    string Detail,
    string? ResolvedIp,
    string? BodyPreview = null,
    bool IsBlockPage = false);

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
    /// <summary>
    /// Tek basina engel sayfasi kaniti sayilan ifadeler. Normal bir sayfada
    /// bulunmalari pratikte imkansiz: sinif adlari ve kurum alan adlari.
    /// </summary>
    private static readonly string[] StrongBlockMarkers =
    [
        // Turk Telekom / TTNET engel sayfasindan DOGRUDAN alindi (--diagnose ile
        // 195.175.254.2 uzerinden gozlemlendi). Onceki listede bu ifade "erisime
        // engellenmi" olarak, yani BOSLUKLA yaziliydi ve gercek sayfadaki alt
        // cizgili sinif adiyla hic eslesmiyordu.
        "erisime_engellenmis",
        "btk.gov.tr",
        "tib.gov.tr",
    ];

    /// <summary>
    /// Engel sayfalarinda sik gecen ama normal iceriklerde de gecebilen ifadeler.
    /// </summary>
    /// <remarks>
    /// Bunlar TEK BASINA yeterli sayilmaz. "koruma tedbiri" ya da "5651 say" gibi
    /// ifadeler sansuru anlatan bir haber sayfasinda da gecer; tek eslesmeyi kanit
    /// saymak, acilan bir siteyi engelli gostermek demek olur. Bu yanlis yon daha
    /// tehlikeli: hedefi DNS yonlendirmesi sanip strateji aramasindan cikaririz ve
    /// gercekten asilabilir bir engeli hic denemeyiz. En az iki eslesme aranir.
    /// </remarks>
    private static readonly string[] WeakBlockMarkers =
    [
        "bilgi teknolojileri ve i",
        "internet sitesine erisim",
        "koruma tedbiri",
        "5651 say",
        "erisime engellenmi",
    ];
    private readonly TimeSpan _timeout;

    public HttpProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(6);

    /// <summary>
    /// Hedefe erisilip erisilemedigini dener.
    /// </summary>
    /// <param name="host">Alan adi.</param>
    /// <param name="mode">
    /// Hangi protokolle denenecegi. Bolume gore secilir; duz HTTP hedefine HTTPS ile
    /// gitmek ya da QUIC hedefini TCP uzerinden olcmek yanlis sonuc uretir.
    /// </param>
    /// <param name="pinnedIp">
    /// Baglanilacak IP. null verilirse cozumleme burada yapilir. Ayni testin farkli
    /// adaylarinda ayni IP'yi kullanmak icin cagiran taraf bunu sabitler.
    /// </param>
    public async Task<ProbeOutcome> TryReachAsync(
        string host,
        ProbeMode mode,
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

            var scheme = mode == ProbeMode.PlainHttp ? "http" : "https";

            using var handler = CreateHandler(resolvedIp, mode);
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = _timeout };

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{host}/");

            if (mode == ProbeMode.Http3)
            {
                // QUIC'i gercekten olcmek icin HTTP/3 zorunlu. RequestVersionExact
                // olmadan .NET sessizce HTTP/2'ye duser ve QUIC hic test edilmemis
                // olur -- olculdugu sanilan ama olculmeyen bir sey, hic olcmemekten
                // daha kotu.
                request.Version = HttpVersion.Version30;
                request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            }
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
        catch (Exception ex)
        {
            return new ProbeOutcome(false, DescribeFailure(ex), resolvedIp);
        }
    }

    /// <summary>
    /// Istisna zincirinden anlamli bir sebep cikarir.
    /// </summary>
    /// <remarks>
    /// Tek seviye bakmak yetmiyor: .NET'te TLS hatalari
    /// HttpRequestException -> IOException -> AuthenticationException -> Win32Exception
    /// seklinde sarmalanip disariya "see inner exception" gibi hicbir sey anlatmayan
    /// bir mesaj birakiyor. Bu metin kullanicinin gunlukte gorecegi sey ve saha
    /// raporunda bize geri donecek tek ipucu, dolayisiyla zinciri sonuna kadar geziyoruz.
    /// </remarks>
    private static string DescribeFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SocketException socket:
                    return socket.SocketErrorCode == SocketError.ConnectionReset
                        ? "baglanti sifirlandi (RST)"
                        : $"soket hatasi: {socket.SocketErrorCode}";

                case AuthenticationException:
                    // Strateji uygulanirken bu, sahte paketin gercek baglantiyi da
                    // bozdugu anlamina gelir -- md5sig'in yanlis sunucuda
                    // kullanilmasinin tipik belirtisi.
                    return "TLS el sikismasi basarisiz";
            }
        }

        // Zincirin en icindeki mesaj, en distekinden neredeyse her zaman daha bilgilendirici.
        var innermost = exception;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        return innermost == exception
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{innermost.GetType().Name}: {innermost.Message}";
    }

    /// <summary>
    /// Gelen cevabin "gercek sunucuya ulastik" anlamina gelip gelmedigine karar verir.
    /// </summary>
    /// <remarks>
    /// Olctugumuz sey sayfanin ICERIGI degil, DPI'in baglantiyi oldurup oldurmedigi.
    /// Bu ayrimi kacirmak pahaliya mal oldu: ilk surumde yalnizca 2xx basari
    /// sayiliyordu ve gercek bir kosumda su sonuclar "basarisiz" yazildi --
    ///
    ///   xvideos.com        HTTP 301 -> https://www.xvideos.com/
    ///   pornhub.com        HTTP 301 -> https://www.pornhub.com/
    ///   gateway.discord.gg HTTP 404
    ///
    /// Ucu de aslinda calisiyordu: ilk ikisi siradan bir www yonlendirmesi, ucuncusu
    /// o adresin duz GET'e verdigi normal cevap. Strateji dordunu de acmisti ama arac
    /// yalnizca birini saydi ve daha iyi bir aday aramaya devam etti.
    ///
    /// Dogru olcut: sunucudan HERHANGI bir gecerli HTTP cevabi geldiyse TLS el
    /// sikismasi tamamlanmis ve DPI baglantiyi oldurmemis demektir. Iki istisna var:
    /// engel sayfasi (yanlis sunucuya ulastik) ve HTTP 400 (sunucu bozuk istek aldi,
    /// yani stratejinin kendisi paketi bozmus).
    /// </remarks>
    private async Task<ProbeOutcome> EvaluateResponseAsync(
        HttpResponseMessage response, string resolvedIp, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;

        // 400: sunucu bozuk paket aldi. Strateji baglantiyi bozmus demektir.
        if (status == 400)
        {
            return new ProbeOutcome(false, "HTTP 400 -- sunucu bozuk istek aldi", resolvedIp);
        }

        // Yonlendirme hedefi engel sayfasiysa, ulastigimiz yer gercek sunucu degil.
        if (status is >= 300 and < 400)
        {
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            var locationMarker = FindBlockMarker(location);
            if (locationMarker is not null)
            {
                return new ProbeOutcome(
                    false, $"engel sayfasina yonlendirme ({locationMarker})", resolvedIp, IsBlockPage: true);
            }

            return new ProbeOutcome(true, $"HTTP {status} -> {location}", resolvedIp);
        }

        // Govde engel sayfasi mi? 200 de donebilecegi icin durum koduna guvenilmez.
        var body = await ReadPrefixAsync(response, cancellationToken).ConfigureAwait(false);
        var marker = FindBlockMarker(body);
        if (marker is not null)
        {
            return new ProbeOutcome(false, $"engel sayfasi ({marker})", resolvedIp, Preview(body), IsBlockPage: true);
        }

        // Buraya gelen her cevap gercek sunucudan geldi: 200 de, 403 de, 404 de.
        // Hepsi ayni seyi kanitliyor -- DPI baglantiyi kesmedi.
        return new ProbeOutcome(true, $"HTTP {status}", resolvedIp, Preview(body));
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

    /// <summary>Govdenin tek satirlik, kisaltilmis hali. Yalnizca teshis ciktisi icin.</summary>
    private static string? Preview(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var flattened = System.Text.RegularExpressions.Regex.Replace(body, @"\s+", " ").Trim();
        return flattened.Length <= 400 ? flattened : flattened[..400];
    }

    /// <summary>
    /// Metnin engel sayfasi olup olmadigina karar verir; eslesen isareti dondurur.
    /// </summary>
    /// <returns>Eslesen isaret, yoksa null.</returns>
    public static string? FindBlockMarker(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var haystack = text.ToLowerInvariant();

        var strong = StrongBlockMarkers.FirstOrDefault(m => haystack.Contains(m, StringComparison.Ordinal));
        if (strong is not null)
        {
            return strong;
        }

        var weak = WeakBlockMarkers.Where(m => haystack.Contains(m, StringComparison.Ordinal)).ToList();
        return weak.Count >= 2 ? string.Join(" + ", weak) : null;
    }

    /// <summary>
    /// Baglantiyi belirli bir IP'ye sabitleyen ve TLS surumunu zorlayan handler kurar.
    /// </summary>
    private SocketsHttpHandler CreateHandler(string pinnedIp, ProbeMode mode)
    {
        var tlsProtocol = mode switch
        {
            // TLS 1.2 kasitli: sertifika DPI'a acik gorunur, yani DPI'in en cok
            // mudahale ettigi durum.
            ProbeMode.Tls12 => SslProtocols.Tls12,
            ProbeMode.Tls13 => SslProtocols.Tls13,
            _ => SslProtocols.None,
        };

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

        // ConnectCallback yalnizca TCP tabanli baglantilarda cagriliyor; HTTP/3
        // kendi QUIC yolunu kullandigi icin orada IP sabitleme YAPILAMIYOR. QUIC
        // testlerinde hedef IP yine de ayrica cozumleniyor -- winws'e verilecek
        // --ipset-ip degeri icin gerekli.
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            // Soketi cozumlenmis adresin ailesiyle aciyoruz. Parametresiz kurucu
            // cift yiginli (IPv6 + eslenmis IPv4) bir soket uretir; IPv4 hedefe
            // giderken bu gereksiz ve bu makinede baglantinin sessizce zaman
            // asimina ugramasina yol aciyordu.
            var address = IPAddress.Parse(pinnedIp);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket
                    .ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken)
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
