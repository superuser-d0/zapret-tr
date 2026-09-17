using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;

namespace ZapretTr.Prober;

// System.Net.Quic .NET 8'de [RequiresPreviewFeatures] ile işaretli (CA2252). Uyarı
// projenin tamamına <EnablePreviewFeatures> açılarak da susturulabilirdi, ama o bayrak
// ÜRETİLEN DERLEMEYİ de "önizleme gerektirir" diye işaretliyor ve bu, derlemeyi
// kullanan her projeye bulaşıyor. Bastırma bu dosyaya sınırlı tutuldu: API yüzeyi
// kararlı, yalnızca msquic'e bağımlılığı yüzünden önizlemede tutuluyor ve
// QuicConnection.IsSupported ile çalışma zamanında zaten kontrol ediliyor.
#pragma warning disable CA2252

/// <summary>
/// QUIC el sıkışmasının tamamlanıp tamamlanmadığını, HEDEF IP'YE SABİTLENEREK ölçer.
/// </summary>
/// <remarks>
/// Neden HttpClient/HTTP/3 yerine ham QUIC:
///
/// <see cref="HttpProbeClient"/> bağlantıyı <c>SocketsHttpHandler.ConnectCallback</c>
/// ile çözümlenmiş IP'ye sabitliyor, ama o geri çağrı YALNIZCA TCP tabanlı
/// bağlantılarda çalışıyor. HTTP/3 kendi QUIC yolunu kullandığı için orada sabitleme
/// yok: .NET adresi kendisi, SİSTEM DNS'i ile çözüyor.
///
/// Türkiye'de bu sessiz bir ölçüm hatasına yol açıyordu. Engelleme iki katmanlı:
/// önce DNS kaçırma, altta SNI'ye bakan DPI. <c>--doh</c> ile çalışıldığında gerçek
/// IP bulunup winws'e <c>--ipset-ip</c> olarak veriliyordu, ama QUIC bağlantısı yine
/// kaçırılmış sistem DNS'inin verdiği engel sunucusuna gidiyordu. Sonuç: winws'in
/// ipset kontrolü her pakette NEGATİF dönüyor, strateji hiç uygulanmıyor ve paket
/// değiştirilmeden geçiyordu. Dışarıdan bakıldığında bu, "strateji işe yaramadı" ile
/// birebir aynı görüntü; bu yüzden QUIC bölümündeki adayların tamamı birbirinin
/// aynısı, işlemsiz koşumlar olarak "başarısız" raporlanmıştı.
///
/// Ölçülen şey bilerek dar tutuldu: HTTP isteği değil, yalnızca QUIC el sıkışması.
/// DPI müdahalesi zaten Initial paketinde oluyor; el sıkışması tamamlanıyorsa DPI
/// aşılmış demektir. Daha dar ölçüm, daha az yanlış sinyal.
/// </remarks>
public sealed class QuicProbeClient
{
    private readonly TimeSpan _timeout;

    public QuicProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(8);

    /// <summary>
    /// Bu makinede ham QUIC kullanılabilir mi. Windows'ta msquic gerekiyor; yoksa
    /// <see cref="QuicConnection.ConnectAsync(QuicClientConnectionOptions, CancellationToken)"/>
    /// <see cref="PlatformNotSupportedException"/> fırlatır.
    /// </summary>
    public static bool IsSupported => QuicConnection.IsSupported;

    /// <summary>
    /// Verilen IP'ye QUIC ile bağlanır; SNI olarak <paramref name="host"/> gönderilir.
    /// </summary>
    /// <param name="pinnedIp">
    /// Bağlanılacak adres. <c>null</c> ise sistem DNS'i kullanılır; DNS kaçırması
    /// olan bir hatta bu, DPI katmanını değil DNS katmanını ölçer.
    /// </param>
    /// <remarks>
    /// IP ile SNI'nin AYRI verilmesi bu ölçümün bütün amacı: paket gerçek sunucuya
    /// gider ama içinde DPI'ın aradığı alan adı durur. İkisi ayrılmazsa ya yanlış
    /// sunucu ölçülür ya da DPI tetiklenmez.
    /// </remarks>
    public async Task<ProbeOutcome> TryReachAsync(
        string host,
        string? pinnedIp = null,
        int port = 443,
        CancellationToken cancellationToken = default)
    {
        if (!QuicConnection.IsSupported)
        {
            return new ProbeOutcome(false, "QUIC bu makinede desteklenmiyor (msquic yok)", pinnedIp);
        }

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

            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Parse(resolvedIp), port),
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,

                // HTTP/3 sunucusu el sıkışmasından hemen sonra ÜÇ tek yönlü akış
                // açmak zorunda: kontrol akışı, QPACK encoder ve QPACK decoder.
                // Bu sınır varsayılan olarak 0 geliyor; o zaman sunucu akışlarını
                // açamıyor ve bağlantıyı ANINDA taşıma hatasıyla kapatıyor.
                //
                // Belirtisi yanıltıcı: ~35 ms'de "TransportError", yani DPI
                // engeliyle KARIŞTIRILABİLİR bir hata. Gerçek DPI engeli bu hatta
                // 10 saniyelik zaman aşımı olarak görünüyor. Google uçları bu
                // kuralı sıkıca uyguluyor, Cloudflare uygulamıyordu; sonuç olarak
                // engelli OLMAYAN www.google.com bile "engelli" ölçülüyordu.
                MaxInboundUnidirectionalStreams = 3,
                MaxInboundBidirectionalStreams = 0,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    // SNI. DPI'ın gördüğü ve üzerinden karar verdiği alan.
                    TargetHost = host,
                    ApplicationProtocols = [new SslApplicationProtocol("h3")],

                    // Sertifika geçerliliği ölçtüğümüz şey değil; el sıkışmasının
                    // tamamlanıp tamamlanmadığını ölçüyoruz. Güvenli bir kanal
                    // kurmuyoruz, bir davranışı ölçüyoruz.
                    RemoteCertificateValidationCallback = static (_, _, _, _) => true,
                },
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            var connection = await QuicConnection
                .ConnectAsync(options, timeoutCts.Token)
                .ConfigureAwait(false);

            var alpn = connection.NegotiatedApplicationProtocol.ToString();
            await connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);

            return new ProbeOutcome(true, $"QUIC el sikismasi tamamlandi (alpn={alpn})", resolvedIp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // DPI müdahalesinin tipik görünümü: Initial paketi düşürülüyor, cevap hiç gelmiyor.
            return new ProbeOutcome(false, "zaman asimi", resolvedIp);
        }
        catch (QuicException ex)
        {
            // Yalnızca QuicError yazmak yetmiyor: "TransportError" hem DPI
            // müdahalesini hem de sunucunun ALPN/sürüm reddini aynı şekilde
            // gösteriyor ve ikisi tamamen farklı sonuçlar. Alt kod ve msquic'in
            // kendi metni ayrımı yapabilmek için gerekli.
            var detail = $"QUIC hatasi: {ex.QuicError}";

            if (ex.ApplicationErrorCode is { } appCode)
            {
                detail += $" (uygulama kodu 0x{appCode:X})";
            }

            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                detail += $" — {ex.Message.Trim()}";
            }

            return new ProbeOutcome(false, detail, resolvedIp);
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, $"{ex.GetType().Name}: {ex.Message}", resolvedIp);
        }
    }
}

#pragma warning restore CA2252
