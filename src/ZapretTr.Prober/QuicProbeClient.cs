using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;

namespace ZapretTr.Prober;

// System.Net.Quic .NET 8'de [RequiresPreviewFeatures] ile isaretli (CA2252). Uyari
// projenin tamamina <EnablePreviewFeatures> acilarak da susturulabilirdi, ama o bayrak
// URETILEN DERLEMEYI de "onizleme gerektirir" diye isaretliyor ve bu, derlemeyi
// kullanan her projeye buluasiyor. Bastirma bu dosyaya sinirli tutuldu: API yuzeyi
// kararli, yalnizca msquic'e bagimliligi yuzunden onizlemede tutuluyor ve
// QuicConnection.IsSupported ile calisma zamaninda zaten kontrol ediliyor.
#pragma warning disable CA2252

/// <summary>
/// QUIC el sikismasinin tamamlanip tamamlanmadigini, HEDEF IP'YE SABITLENEREK olcer.
/// </summary>
/// <remarks>
/// Neden HttpClient/HTTP/3 yerine ham QUIC:
///
/// <see cref="HttpProbeClient"/> baglantiyi <c>SocketsHttpHandler.ConnectCallback</c>
/// ile cozumlenmis IP'ye sabitliyor, ama o geri cagri YALNIZCA TCP tabanli
/// baglantilarda calisiyor. HTTP/3 kendi QUIC yolunu kullandigi icin orada sabitleme
/// yok: .NET adresi kendisi, SISTEM DNS'i ile cozuyor.
///
/// Turkiye'de bu sessiz bir olcum hatasina yol aciyordu. Engelleme iki katmanli:
/// once DNS kacirma, altta SNI'ye bakan DPI. <c>--doh</c> ile calisildiginda gercek
/// IP bulunup winws'e <c>--ipset-ip</c> olarak veriliyordu, ama QUIC baglantisi yine
/// kacirilmis sistem DNS'inin verdigi engel sunucusuna gidiyordu. Sonuc: winws'in
/// ipset kontrolu her pakette NEGATIF donuyor, strateji hic uygulanmiyor ve paket
/// degistirilmeden geciyordu. Disaridan bakildiginda bu "strateji ise yaramadi" ile
/// birebir ayni goruntu -- bu yuzden QUIC bolumundeki adaylarin tamami birbirinin
/// ayni islemsiz kosum olarak "basarisiz" raporlanmisti.
///
/// Olculen sey bilerek dar tutuldu: HTTP istegi degil, yalnizca QUIC el sikismasi.
/// DPI mudahalesi zaten Initial paketinde oluyor; el sikismasi tamamlaniyorsa DPI
/// asilmis demektir. Daha dar olcum, daha az yanlis sinyal.
/// </remarks>
public sealed class QuicProbeClient
{
    private readonly TimeSpan _timeout;

    public QuicProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(8);

    /// <summary>
    /// Bu makinede ham QUIC kullanilabilir mi. Windows'ta msquic gerekiyor; yoksa
    /// <see cref="QuicConnection.ConnectAsync(QuicClientConnectionOptions, CancellationToken)"/>
    /// <see cref="PlatformNotSupportedException"/> firlatir.
    /// </summary>
    public static bool IsSupported => QuicConnection.IsSupported;

    /// <summary>
    /// Verilen IP'ye QUIC ile baglanir; SNI olarak <paramref name="host"/> gonderilir.
    /// </summary>
    /// <param name="pinnedIp">
    /// Baglanilacak adres. <c>null</c> ise sistem DNS'i kullanilir -- DNS kacirmasi
    /// olan bir hatta bu, DPI katmanini degil DNS katmanini olcer.
    /// </param>
    /// <remarks>
    /// IP ile SNI'nin AYRI verilmesi bu olcumun butun amaci: paket gercek sunucuya
    /// gider ama icinde DPI'in aradigi alan adi durur. Ikisi ayrilmazsa ya yanlis
    /// sunucu olculur ya da DPI tetiklenmez.
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
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    // SNI. DPI'in gordugu ve uzerinden karar verdigi alan.
                    TargetHost = host,
                    ApplicationProtocols = [new SslApplicationProtocol("h3")],

                    // Sertifika gecerliligi olctugumuz sey degil; el sikismasinin
                    // tamamlanip tamamlanmadigini olcuyoruz. Guvenli bir kanal
                    // kurmuyoruz, bir davranisi olcuyoruz.
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
            // DPI mudahalesinin tipik gorunumu: Initial paketi dusuruluyor, cevap hic gelmiyor.
            return new ProbeOutcome(false, "zaman asimi", resolvedIp);
        }
        catch (QuicException ex)
        {
            return new ProbeOutcome(false, $"QUIC hatasi: {ex.QuicError}", resolvedIp);
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, $"{ex.GetType().Name}: {ex.Message}", resolvedIp);
        }
    }
}

#pragma warning restore CA2252
