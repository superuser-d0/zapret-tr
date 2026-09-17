using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ZapretTr.Prober;

/// <summary>Tek bir erişim denemesinin sonucu.</summary>
/// <param name="IsBlockPage">
/// Erişilen sunucu bir engel sayfası döndürdü. Bu, DPI engellemesinden FARKLI bir
/// durum: bağlantı kuruldu ama yanlış sunucuya. Çoğunlukla DNS yönlendirmesi demek
/// ve zapret bunu çözemez.
/// </param>
public sealed record ProbeOutcome(
    bool Succeeded,
    string Detail,
    string? ResolvedIp,
    string? BodyPreview = null,
    bool IsBlockPage = false);

/// <summary>
/// Bir hedefe erişilip erişilemediğini ölçer.
/// </summary>
/// <remarks>
/// blockcheck.sh'ın curl ile yaptığı işin yerel karşılığı. Üç önemli fark var:
///
/// 1. Bağlantı çözümlenmiş IP'ye SABİTLENİR. Hem her denemenin aynı sunucuya gitmesini
///    garanti eder (CDN'lerde bu önemsiz değil), hem de winws'e vereceğimiz
///    --ipset-ip değerini elde etmiş oluruz.
///
/// 2. TLS sürümü açıkça zorlanır. TLS 1.2'de sertifika DPI'a açık görünür, TLS 1.3'te
///    ServerHello şifreli; DPI'ın görebildiği şey değiştiği için ikisi farklı
///    strateji gerektirebilir ve ayrı puanlanmaları gerekir.
///
/// 3. Türkiye'ye özel engel sayfası tespiti var: BTK engellemesi çoğu zaman bağlantıyı
///    kesmez, 200 ile bir bilgilendirme sayfası döndürür. Yalnızca durum koduna bakan
///    bir kontrol bunu "çalışıyor" sanar.
/// </remarks>
public sealed class HttpProbeClient : IDisposable
{
    /// <summary>
    /// BTK/erişim engeli sayfalarında geçen ifadeler. Sayfa 200 döndürdüğü için
    /// durum kodu yetmiyor, içeriğe bakmak gerekiyor.
    /// </summary>
    /// <summary>
    /// Tek başına engel sayfası kanıtı sayılan ifadeler. Normal bir sayfada
    /// bulunmaları pratikte imkânsız: sınıf adları ve kurum alan adları.
    /// </summary>
    private static readonly string[] StrongBlockMarkers =
    [
        // Türk Telekom / TTNET engel sayfasından DOĞRUDAN alındı (--diagnose ile
        // 195.175.254.2 üzerinden gözlemlendi). Önceki listede bu ifade "erisime
        // engellenmi" olarak, yani BOŞLUKLA yazılıydı ve gerçek sayfadaki alt
        // çizgili sınıf adıyla hiç eşleşmiyordu.
        "erisime_engellenmis",
        "btk.gov.tr",
        "tib.gov.tr",
    ];

    /// <summary>
    /// Engel sayfalarında sık geçen ama normal içeriklerde de geçebilen ifadeler.
    /// </summary>
    /// <remarks>
    /// Bunlar TEK BAŞINA yeterli sayılmaz. "koruma tedbiri" ya da "5651 say" gibi
    /// ifadeler sansürü anlatan bir haber sayfasında da geçer; tek eşleşmeyi kanıt
    /// saymak, açılan bir siteyi engelli göstermek demek olur. Bu yanlış yön daha
    /// tehlikeli: hedefi DNS yönlendirmesi sanıp strateji aramasından çıkarırız ve
    /// gerçekten aşılabilir bir engeli hiç denemeyiz. En az iki eşleşme aranır.
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
    /// Hedefe erişilip erişilemediğini dener.
    /// </summary>
    /// <param name="host">Alan adı.</param>
    /// <param name="mode">
    /// Hangi protokolle deneneceği. Bölüme göre seçilir; düz HTTP hedefine HTTPS ile
    /// gitmek ya da QUIC hedefini TCP üzerinden ölçmek yanlış sonuç üretir.
    /// </param>
    /// <param name="pinnedIp">
    /// Bağlanılacak IP. null verilirse çözümleme burada yapılır. Aynı testin farklı
    /// adaylarında aynı IP'yi kullanmak için çağıran taraf bunu sabitler.
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
                // QUIC'i gerçekten ölçmek için HTTP/3 zorunlu. RequestVersionExact
                // olmadan .NET sessizce HTTP/2'ye düşer ve QUIC hiç test edilmemiş
                // olur; ölçüldüğü sanılan ama ölçülmeyen bir şey, hiç ölçmemekten
                // daha kötü.
                request.Version = HttpVersion.Version30;
                request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            }
            // Kimlik doğrulama gerektirmeyen, çerezsiz, sade bir istek: amaç içerik
            // almak değil, DPI'ın TLS el sıkışmasına karışıp karışmadığını ölçmek.
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
            // Zaman aşımı engellemenin en yaygın belirtisi: DPI paketi sessizce düşürüyor.
            return new ProbeOutcome(false, "zaman asimi", resolvedIp);
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, DescribeFailure(ex), resolvedIp);
        }
    }

    /// <summary>
    /// İstisna zincirinden anlamlı bir sebep çıkarır.
    /// </summary>
    /// <remarks>
    /// Tek seviye bakmak yetmiyor: .NET'te TLS hataları
    /// HttpRequestException -> IOException -> AuthenticationException -> Win32Exception
    /// şeklinde sarmalanıp dışarıya "see inner exception" gibi hiçbir şey anlatmayan
    /// bir mesaj bırakıyor. Bu metin kullanıcının günlükte göreceği şey ve saha
    /// raporunda bize geri dönecek tek ipucu, dolayısıyla zinciri sonuna kadar geziyoruz.
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
                    // Strateji uygulanırken bu, sahte paketin gerçek bağlantıyı da
                    // bozduğu anlamına gelir; md5sig'in yanlış sunucuda
                    // kullanılmasının tipik belirtisi.
                    return "TLS el sikismasi basarisiz";
            }
        }

        // Zincirin en içindeki mesaj, en dıştakinden neredeyse her zaman daha bilgilendirici.
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
    /// Gelen cevabın "gerçek sunucuya ulaştık" anlamına gelip gelmediğine karar verir.
    /// </summary>
    /// <remarks>
    /// Ölçtüğümüz şey sayfanın İÇERİĞİ değil, DPI'ın bağlantıyı öldürüp öldürmediği.
    /// Bu ayrımı kaçırmak pahalıya mal oldu: ilk sürümde yalnızca 2xx başarı
    /// sayılıyordu ve gerçek bir koşumda şu sonuçlar "başarısız" yazıldı:
    ///
    ///   xvideos.com        HTTP 301 -> https://www.xvideos.com/
    ///   pornhub.com        HTTP 301 -> https://www.pornhub.com/
    ///   gateway.discord.gg HTTP 404
    ///
    /// Üçü de aslında çalışıyordu: ilk ikisi sıradan bir www yönlendirmesi, üçüncüsü
    /// o adresin düz GET'e verdiği normal cevap. Strateji dördünü de açmıştı ama araç
    /// yalnızca birini saydı ve daha iyi bir aday aramaya devam etti.
    ///
    /// Doğru ölçüt: sunucudan HERHANGİ bir geçerli HTTP cevabı geldiyse TLS el
    /// sıkışması tamamlanmış ve DPI bağlantıyı öldürmemiş demektir. İki istisna var:
    /// engel sayfası (yanlış sunucuya ulaştık) ve HTTP 400 (sunucu bozuk istek aldı,
    /// yani stratejinin kendisi paketi bozmuş).
    /// </remarks>
    private async Task<ProbeOutcome> EvaluateResponseAsync(
        HttpResponseMessage response, string resolvedIp, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;

        // 400: sunucu bozuk paket aldı. Strateji bağlantıyı bozmuş demektir.
        if (status == 400)
        {
            return new ProbeOutcome(false, "HTTP 400 -- sunucu bozuk istek aldi", resolvedIp);
        }

        // Yönlendirme hedefi engel sayfasıysa, ulaştığımız yer gerçek sunucu değil.
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

        // Gövde engel sayfası mı? 200 de dönebileceği için durum koduna güvenilmez.
        var body = await ReadPrefixAsync(response, cancellationToken).ConfigureAwait(false);
        var marker = FindBlockMarker(body);
        if (marker is not null)
        {
            return new ProbeOutcome(false, $"engel sayfasi ({marker})", resolvedIp, Preview(body), IsBlockPage: true);
        }

        // Buraya gelen her cevap gerçek sunucudan geldi: 200 de, 403 de, 404 de.
        // Hepsi aynı şeyi kanıtlıyor: DPI bağlantıyı kesmedi.
        return new ProbeOutcome(true, $"HTTP {status}", resolvedIp, Preview(body));
    }

    /// <summary>Gövdenin başından bir parça okur. Tamamını indirmek gereksiz ve yavaş.</summary>
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
            // Gövdenin okunamaması engel tespiti için belirleyici değil; durum koduyla devam.
            return string.Empty;
        }
    }

    /// <summary>Gövdenin tek satırlık, kısaltılmış hâli. Yalnızca teşhis çıktısı için.</summary>
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
    /// Metnin engel sayfası olup olmadığına karar verir; eşleşen işareti döndürür.
    /// </summary>
    /// <returns>Eşleşen işaret, yoksa null.</returns>
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
    /// Bağlantıyı belirli bir IP'ye sabitleyen ve TLS sürümünü zorlayan işleyici kurar.
    /// </summary>
    private SocketsHttpHandler CreateHandler(string pinnedIp, ProbeMode mode)
    {
        var tlsProtocol = mode switch
        {
            // TLS 1.2 kasıtlı: sertifika DPI'a açık görünür, yani DPI'ın en çok
            // müdahale ettiği durum.
            ProbeMode.Tls12 => SslProtocols.Tls12,
            ProbeMode.Tls13 => SslProtocols.Tls13,
            _ => SslProtocols.None,
        };

        var handler = new SocketsHttpHandler
        {
            // Yönlendirmeyi elle değerlendiriyoruz: engel sayfasına yönlendirme
            // otomatik takip edilirse tespit edilemez hâle gelir.
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = _timeout,
            PooledConnectionLifetime = TimeSpan.Zero,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = tlsProtocol,
                // Sertifika geçerliliği bizim ölçtüğümüz şey değil; DPI müdahalesi
                // sertifikayı bozabilir ve bu da ölçmek istediğimiz sinyalin parçası.
                // Güvenli bir kanal kurmuyoruz, bir davranışı ölçüyoruz.
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            },
        };

        // ConnectCallback yalnızca TCP tabanlı bağlantılarda çağrılıyor; HTTP/3
        // kendi QUIC yolunu kullandığı için orada IP sabitleme YAPILAMIYOR. QUIC
        // testlerinde hedef IP yine de ayrıca çözümleniyor; winws'e verilecek
        // --ipset-ip değeri için gerekli.
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            // Soketi çözümlenmiş adresin ailesiyle açıyoruz. Parametresiz kurucu
            // çift yığınlı (IPv6 + eşlenmiş IPv4) bir soket üretir; IPv4 hedefe
            // giderken bu gereksiz ve bu makinede bağlantının sessizce zaman
            // aşımına uğramasına yol açıyordu.
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
        // İşleyici ve istemci her denemede yeniden kuruluyor (bağlantı havuzunun
        // önceki stratejinin açık bağlantısını yeniden kullanması sonuçları
        // kirletirdi), bu yüzden burada tutulan bir kaynak yok.
    }
}
