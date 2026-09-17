using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>Bağlantının hangi servis sağlayıcıya ait olduğu.</summary>
/// <param name="Asn">Otonom sistem numarası. Bulunamadıysa null.</param>
/// <param name="OrgName">Kuruluş adı. Bulunamadıysa null.</param>
/// <param name="Source">Bilgiyi hangi servisten aldığımız.</param>
public sealed record IspIdentity(int? Asn, string? OrgName, string Source)
{
    public bool IsKnown => Asn is not null || !string.IsNullOrWhiteSpace(OrgName);
}

/// <summary>Tespit sonucu ve eşleşen profiller.</summary>
/// <param name="Identity">Tespit edilen kimlik.</param>
/// <param name="Matches">Eşleşen profiller, en iyi eşleşme önce.</param>
public sealed record IspDetectionResult(IspIdentity? Identity, IReadOnlyList<IspProfile> Matches)
{
    /// <summary>Tek ve net bir eşleşme varsa o profil; yoksa null.</summary>
    public IspProfile? BestMatch => Matches.Count > 0 ? Matches[0] : null;

    /// <summary>Birden fazla profil eşleşti; kullanıcıya seçim sunulmalı.</summary>
    public bool IsAmbiguous => Matches.Count > 1;
}

/// <summary>
/// Bağlantının servis sağlayıcısını tespit eder.
/// </summary>
/// <remarks>
/// Arayüzdeki "Bilmiyorum / otomatik tespit et" seçeneğini çalışır hâle getiren
/// parça. Kullanıcıların çoğu servis sağlayıcısını teknik adıyla bilmiyor
/// ("Türk Telekom" mu "TTNET" mi, "Superonline" mı "Turkcell" mi) ve bilmemek
/// testi engellememeli.
///
/// İki kaynak sırayla deneniyor. Tek bir servise güvenmek kırılgan olurdu:
/// bunların herhangi biri kapalı olabilir ya da, Türkiye bağlamında daha
/// önemlisi, engellenmiş olabilir.
///
/// Gönderilen tek bilgi bağlantının kendi genel IP adresi; zaten bağlanılan
/// her sunucu onu görüyor. Başka hiçbir şey gönderilmiyor.
///
/// SIRA ÖNEMLİ: önce HTTPS olan ipinfo.io. Eskiden önce ip-api.com soruluyordu ve
/// o istek ŞİFRESİZ gidiyor (ücretsiz planında HTTPS yok): servis sağlayıcı,
/// kullanıcının "hangi servis sağlayıcıdayım" sorgusunu düz metin olarak görüyordu.
/// ip-api yalnızca yedek; README-DETAYLI "Sık sorulanlar"da bu yazılı. Kullanıcı
/// servis sağlayıcısını listeden seçerse bu sınıf hiç çalışmıyor.
/// </remarks>
public sealed class IspDetector : IDisposable
{
    /// <summary>Sorgu kaynakları, sorulma sırasıyla. İlki şifreli olmalı.</summary>
    public static IReadOnlyList<string> QueryOrder { get; } =
    [
        "https://ipinfo.io/json",
        "http://ip-api.com/json/?fields=status,isp,org,as,asname",
    ];

    private readonly HttpClient _client;

    public IspDetector(TimeSpan? timeout = null)
    {
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretTR");
    }

    /// <summary>Kimliği tespit edip eşleşen profilleri döner.</summary>
    public async Task<IspDetectionResult> DetectAsync(
        ProfileStore profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var identity = await IdentifyAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
        {
            return new IspDetectionResult(null, []);
        }

        return new IspDetectionResult(identity, profiles.Match(identity.Asn, identity.OrgName));
    }

    /// <summary>Bağlantının ASN ve kuruluş adını bulur; hiçbir kaynak cevap vermezse null.</summary>
    public async Task<IspIdentity?> IdentifyAsync(CancellationToken cancellationToken = default)
    {
        var identity = await TryIpInfoAsync(cancellationToken).ConfigureAwait(false);
        if (identity is { IsKnown: true })
        {
            return identity;
        }

        return await TryIpApiAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IspIdentity?> TryIpApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetFromJsonAsync(
                QueryOrder[1],
                ProberJsonContext.Default.IpApiResponse,
                cancellationToken).ConfigureAwait(false);

            if (response is null || !string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // "as" alanı "AS9121 Turk Telekomunikasyon Anonim Sirketi" şeklinde geliyor.
            var asn = ParseAsn(response.As);

            // Kuruluş adı için birkaç alan var; en dolgun olanı seç, çünkü profil
            // eşleşmesi anahtar kelime aramasıyla yapılıyor.
            var org = new[] { response.Isp, response.Org, response.AsName, response.As }
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return new IspIdentity(asn, org, "ip-api.com");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<IspIdentity?> TryIpInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetFromJsonAsync(
                QueryOrder[0], ProberJsonContext.Default.IpInfoResponse, cancellationToken).ConfigureAwait(false);

            if (response is null)
            {
                return null;
            }

            // ipinfo "org" alanını "AS9121 Turk Telekomunikasyon" şeklinde veriyor.
            return new IspIdentity(ParseAsn(response.Org), response.Org, "ipinfo.io");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"AS9121 Bir Şirket" gibi bir dizgiden 9121 çıkarır.</summary>
    public static int? ParseAsn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"\bAS(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups[1].Value, out var asn) ? asn : null;
    }

    public void Dispose() => _client.Dispose();

    internal sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("isp")]
        public string? Isp { get; set; }

        [JsonPropertyName("org")]
        public string? Org { get; set; }

        [JsonPropertyName("as")]
        public string? As { get; set; }

        [JsonPropertyName("asname")]
        public string? AsName { get; set; }
    }

    internal sealed class IpInfoResponse
    {
        [JsonPropertyName("org")]
        public string? Org { get; set; }
    }
}
