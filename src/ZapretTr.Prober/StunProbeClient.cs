using System.Net;
using System.Net.Sockets;

namespace ZapretTr.Prober;

/// <summary>
/// UDP tabanlı trafiğin dışarı çıkıp çıkmadığını STUN ile ölçer.
/// </summary>
/// <remarks>
/// Discord sesli görüşme UDP kullanıyor ve HTTP istemcisiyle ölçülemez. Bu bölüm
/// bugüne kadar hiç sınanmıyordu: probe-targets.json'da discord-voice hedefi yoktu,
/// dolayısıyla araç "ses çalışıyor mu" sorusuna sessiz kalıyordu. Kullanıcı
/// "Discord açıldı" diye rapor görürken sesli görüşmenin durumu hakkında hiçbir
/// şey söylenmemiş oluyordu.
///
/// STUN seçilmesinin sebebi: Discord'un ses altyapısı zaten STUN kullanıyor ve
/// sunucular kamuya açık, kimlik doğrulama istemiyor. Basit bir Binding Request
/// gönderip cevap gelip gelmediğine bakmak, UDP yolunun açık olup olmadığını
/// doğrudan ölçüyor.
///
/// Bir DNS/STUN kütüphanesi eklemek yerine paket elle kuruluyor; bağımlılık
/// yüzeyini büyütmemek için.
/// </remarks>
public sealed class StunProbeClient
{
    /// <summary>STUN'un sabit sihirli sayısı (RFC 5389).</summary>
    private static readonly byte[] MagicCookie = [0x21, 0x12, 0xA4, 0x42];

    private readonly TimeSpan _timeout;

    public StunProbeClient(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(4);

    /// <summary>
    /// STUN sunucusuna Binding Request gönderip cevap bekler.
    /// </summary>
    /// <returns>
    /// Cevap geldiyse başarılı. UDP yolu kapalıysa ya da DPI paketleri düşürüyorsa
    /// zaman aşımı döner.
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

            // Cevabın BİZİM sorgumuza ait olduğunu doğrula. İşlem kimliği
            // eşleşmezse gelen paket başka bir şeydir ve "çalışıyor" saymak
            // yanıltıcı olur.
            if (!IsMatchingResponse(response.Buffer, transactionId))
            {
                return new ProbeOutcome(false, "STUN: cevap islem kimligiyle eslesmedi", resolvedIp);
            }

            return new ProbeOutcome(true, $"STUN cevabi alindi ({response.Buffer.Length} bayt)", resolvedIp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // UDP'de "bağlantı reddedildi" yok; engellemenin belirtisi sessizliktir.
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
        packet[0] = 0x00;   // Binding Request, üst bayt
        packet[1] = 0x01;   // Binding Request, alt bayt
        packet[2] = 0x00;   // gövde uzunluğu: 0
        packet[3] = 0x00;

        MagicCookie.CopyTo(packet, 4);
        transactionId.CopyTo(packet, 8);

        return packet;
    }

    private static bool IsMatchingResponse(byte[] buffer, byte[] transactionId)
    {
        // 20 bayt başlık + sihirli sayı + işlem kimliği.
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
