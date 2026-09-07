using System.Net;
using System.Net.Sockets;

namespace ZapretTr.Prober;

/// <summary>
/// UDP tabanli trafigin disari cikip cikmadigini STUN ile olcer.
/// </summary>
/// <remarks>
/// Discord sesli gorusme UDP kullaniyor ve HTTP istemcisiyle olculemez. Bu bolum
/// bugune kadar hic sinanmiyordu: probe-targets.json'da discord-voice hedefi yoktu,
/// dolayisiyla arac "ses calisiyor mu" sorusuna sessiz kaliyordu. Kullanici
/// "Discord acildi" diye rapor gorurken sesli gorusmenin durumu hakkinda hicbir
/// sey soylenmemis oluyordu.
///
/// STUN secilmesinin sebebi: Discord'un ses altyapisi zaten STUN kullaniyor ve
/// sunucular kamuya acik, kimlik dogrulama istemiyor. Basit bir Binding Request
/// gonderip cevap gelip gelmedigine bakmak, UDP yolunun acik olup olmadigini
/// dogrudan olcuyor.
///
/// Bir DNS/STUN kutuphanesi eklemek yerine paket elle kuruluyor; bagimlilik
/// yuzeyini buyutmemek icin.
/// </remarks>
public sealed class StunProbeClient
{
    /// <summary>STUN'un sabit sihirli sayisi (RFC 5389).</summary>
    private static readonly byte[] MagicCookie = [0x21, 0x12, 0xA4, 0x42];

    private readonly TimeSpan _timeout;

    public StunProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(4);

    /// <summary>
    /// STUN sunucusuna Binding Request gonderip cevap bekler.
    /// </summary>
    /// <returns>
    /// Cevap geldiyse basarili. UDP yolu kapaliysa ya da DPI paketleri dusuruyorsa
    /// zaman asimi doner.
    /// </returns>
    public async Task<ProbeOutcome> TryReachAsync(
        string host, int port = 19302, string? pinnedIp = null, CancellationToken cancellationToken = default)
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

            var request = BuildBindingRequest(out var transactionId);

            using var udp = new UdpClient();
            await udp.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Parse(resolvedIp), port))
                .ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var response = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);

            // Cevabin BIZIM sorgumuza ait oldugunu dogrula. Islem kimligi
            // eslesmezse gelen paket baska bir seydir ve "calisiyor" saymak
            // yaniltici olur.
            if (!IsMatchingResponse(response.Buffer, transactionId))
            {
                return new ProbeOutcome(false, "STUN: cevap islem kimligiyle eslesmedi", resolvedIp);
            }

            return new ProbeOutcome(true, $"STUN cevabi alindi ({response.Buffer.Length} bayt)", resolvedIp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // UDP'de "baglanti reddedildi" yok; engellemenin belirtisi sessizliktir.
            return new ProbeOutcome(false, "zaman asimi (UDP cevabi yok)", resolvedIp);
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, ex.GetType().Name + ": " + ex.Message, resolvedIp);
        }
    }

    /// <summary>RFC 5389 Binding Request paketi kurar.</summary>
    private static byte[] BuildBindingRequest(out byte[] transactionId)
    {
        transactionId = new byte[12];
        Random.Shared.NextBytes(transactionId);

        var packet = new byte[20];
        packet[0] = 0x00;   // Binding Request, ust bayt
        packet[1] = 0x01;   // Binding Request, alt bayt
        packet[2] = 0x00;   // govde uzunlugu: 0
        packet[3] = 0x00;

        MagicCookie.CopyTo(packet, 4);
        transactionId.CopyTo(packet, 8);

        return packet;
    }

    private static bool IsMatchingResponse(byte[] buffer, byte[] transactionId)
    {
        // 20 bayt baslik + sihirli sayi + islem kimligi.
        if (buffer.Length < 20)
        {
            return false;
        }

        for (var i = 0; i < MagicCookie.Length; i++)
        {
            if (buffer[4 + i] != MagicCookie[i])
            {
                return false;
            }
        }

        for (var i = 0; i < transactionId.Length; i++)
        {
            if (buffer[8 + i] != transactionId[i])
            {
                return false;
            }
        }

        return true;
    }
}
