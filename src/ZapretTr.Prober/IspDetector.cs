using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Prober;

/// <summary>Baglantinin hangi servis saglayiciya ait oldugu.</summary>
/// <param name="Asn">Otonom sistem numarasi. Bulunamadiysa null.</param>
/// <param name="OrgName">Kurulus adi. Bulunamadiysa null.</param>
/// <param name="Source">Bilgiyi hangi servisten aldigimiz.</param>
public sealed record IspIdentity(int? Asn, string? OrgName, string Source)
{
    public bool IsKnown => Asn is not null || !string.IsNullOrWhiteSpace(OrgName);
}

/// <summary>Tespit sonucu ve eslesen profiller.</summary>
/// <param name="Identity">Tespit edilen kimlik.</param>
/// <param name="Matches">Eslesen profiller, en iyi eslesme once.</param>
public sealed record IspDetectionResult(IspIdentity? Identity, IReadOnlyList<IspProfile> Matches)
{
    /// <summary>Tek ve net bir eslesme varsa o profil; yoksa null.</summary>
    public IspProfile? BestMatch => Matches.Count > 0 ? Matches[0] : null;

    /// <summary>Birden fazla profil eslesti; kullaniciya secim sunulmali.</summary>
    public bool IsAmbiguous => Matches.Count > 1;
}

/// <summary>
/// Baglantinin servis saglayicisini tespit eder.
/// </summary>
/// <remarks>
/// Arayuzdeki "Bilmiyorum / otomatik tespit et" secenegini calisir hale getiren
/// parca. Kullanicilarin cogu servis saglayicisini teknik adiyla bilmiyor
/// ("Turk Telekom" mu "TTNET" mi, "Superonline" mu "Turkcell" mi) ve bilmemek
/// testi engellememeli.
///
/// Iki kaynak sirayla deneniyor. Tek bir servise guvenmek kirilgan olurdu:
/// bunlarin herhangi biri kapali olabilir ya da -- Turkiye baglaminda daha
/// onemlisi -- engellenmis olabilir.
///
/// Gonderilen tek bilgi baglantinin kendi genel IP adresi, ki zaten baglanilan
/// her sunucu onu goruyor. Baska hicbir sey gonderilmiyor.
/// </remarks>
public sealed class IspDetector : IDisposable
{
    private readonly HttpClient _client;

    public IspDetector(TimeSpan? timeout = null)
    {
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretTR");
    }

    /// <summary>Kimligi tespit edip eslesen profilleri doner.</summary>
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

    /// <summary>Baglantinin ASN ve kurulus adini bulur; hicbir kaynak cevap vermezse null.</summary>
    public async Task<IspIdentity?> IdentifyAsync(CancellationToken cancellationToken = default)
    {
        var identity = await TryIpApiAsync(cancellationToken).ConfigureAwait(false);
        if (identity is { IsKnown: true })
        {
            return identity;
        }

        return await TryIpInfoAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IspIdentity?> TryIpApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetFromJsonAsync(
                "http://ip-api.com/json/?fields=status,isp,org,as,asname",
                ProberJsonContext.Default.IpApiResponse,
                cancellationToken).ConfigureAwait(false);

            if (response is null || !string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // "as" alani "AS9121 Turk Telekomunikasyon Anonim Sirketi" seklinde geliyor.
            var asn = ParseAsn(response.As);

            // Kurulus adi icin birkac alan var; en dolgun olani sec cunku profil
            // eslesmesi anahtar kelime aramasiyla yapiliyor.
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
                "https://ipinfo.io/json", ProberJsonContext.Default.IpInfoResponse, cancellationToken).ConfigureAwait(false);

            if (response is null)
            {
                return null;
            }

            // ipinfo "org" alanini "AS9121 Turk Telekomunikasyon" seklinde veriyor.
            return new IspIdentity(ParseAsn(response.Org), response.Org, "ipinfo.io");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"AS9121 Bir Sirket" gibi bir dizgiden 9121 cikarir.</summary>
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
