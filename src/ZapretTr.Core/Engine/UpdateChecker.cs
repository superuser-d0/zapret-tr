using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Yayinlanmis en yeni surumu sorar.</summary>
/// <remarks>
/// Bu, uygulamanin KENDILIGINDEN disari istek yapan tek yeri ve bunu gizlemek dogru olmaz:
/// GitHub'a bir GET gidiyor, dolayisiyla kullanicinin IP adresi GitHub'in
/// gunluklerine dusuyor. Gonderilen baska hicbir sey yok -- ne hat bilgisi, ne
/// secili strateji, ne olcum sonucu. Yalnizca "en son surum ne" sorusu.
///
/// Neden var: gunde bir kac surum cikabiliyor ve her seferinde test
/// kullanicilarina tek tek "sunu kur" demek gerekiyordu. Duzeltmeyi kullaniciya
/// ulastiramayan bir surum ise yaramiyor.
///
/// Kapatilabilir olmasi sart (<c>updateCheckEnabled</c>): engellemenin konu
/// oldugu bir arac, kullanicinin haberi olmadan ag istegi yapmamali.
/// </remarks>
public static class UpdateChecker
{
    private const string LatestUrl =
        "https://api.github.com/repos/superuser-d0/zapret-tr/releases/latest";

    /// <summary>Yayin sayfasi; kullaniciya gosterilecek adres.</summary>
    public const string ReleasesPage =
        "https://github.com/superuser-d0/zapret-tr/releases/latest";

    /// <summary>
    /// En yeni surumu sorar. Ulasilamazsa <c>null</c> doner.
    /// </summary>
    /// <remarks>
    /// Hata YUTULUYOR: guncelleme kontrolu bir kolaylik, korumanin parcasi
    /// degil. Ag yoksa ya da GitHub cevap vermiyorsa kullaniciya hata
    /// gostermenin bir anlami olmaz -- uygulamanin isi bundan etkilenmiyor.
    /// </remarks>
    public static async Task<string?> GetLatestVersionAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await CheckLatestAsync(timeout, cancellationToken).ConfigureAwait(false)).Version;

    /// <summary>En yeni surumu sorar; alinamazsa SEBEBINI kullaniciya soylenecek bicimde doner.</summary>
    /// <remarks>
    /// "Güncellemeleri Denetle" basarisiz olunca yalnizca gunluge "Sürüm bilgisi
    /// alınamadı" yaziyordu; Ayrintilar kapaliyken kullanici icin dugme hicbir sey
    /// yapmiyordu. Sebep de tek bir cumleye siniyordu. En olasi sebeplerden biri
    /// GitHub'in oturumsuz API sinirinin (IP basina saatte 60) dolmasi ve bu
    /// kullanicinin yapabilecegi tek sey beklemek -- ne kadar bekleyecegini
    /// soylemek gerekiyor.
    /// </remarks>
    public static async Task<LatestVersionCheck> CheckLatestAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };

            // GitHub API User-Agent olmadan 403 donuyor.
            client.DefaultRequestHeaders.Add("User-Agent", "ZapretTR");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            using var response = await client.GetAsync(LatestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new LatestVersionCheck(null, DescribeHttpFailure(
                    (int)response.StatusCode,
                    Header(response, "X-RateLimit-Remaining"),
                    Header(response, "X-RateLimit-Reset"),
                    DateTimeOffset.Now));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var surum = ParseTagName(json);

            return surum is null
                ? new LatestVersionCheck(null, "GitHub'ın cevabında sürüm numarası bulunamadı.")
                : new LatestVersionCheck(surum, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LatestVersionCheck(null,
                $"GitHub {Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds))} saniyede cevap vermedi (zaman aşımı).");
        }
        catch (HttpRequestException ex)
        {
            return new LatestVersionCheck(null, "GitHub'a ulaşılamadı: " + ex.Message);
        }
    }

    /// <summary>Yayin cevabindaki tag_name'den surumu cikarir ("v0.1.22" -> "0.1.22").</summary>
    /// <remarks>
    /// Duz metin ayristirma: System.Text.Json'in yansimali yolu kirpma analizorunu
    /// patlatiyor ve tek bir alan icin kaynak uretimli bir baglam tanimlamak fazla.
    /// </remarks>
    public static string? ParseTagName(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var match = Regex.Match(
            json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+\\.[0-9]+\\.[0-9]+)\"");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Basarisiz HTTP cevabini kullaniciya soylenecek bir cumleye cevirir.</summary>
    /// <param name="statusCode">HTTP durum kodu.</param>
    /// <param name="rateLimitRemaining">X-RateLimit-Remaining basligi.</param>
    /// <param name="rateLimitReset">X-RateLimit-Reset basligi (Unix saniyesi).</param>
    /// <param name="now">Simdiki zaman; "kac dakika" hesabi icin.</param>
    public static string DescribeHttpFailure(int statusCode, string? rateLimitRemaining, string? rateLimitReset, DateTimeOffset now)
    {
        var sinirDoldu = statusCode is 403 or 429 && rateLimitRemaining?.Trim() == "0";

        if (sinirDoldu)
        {
            if (long.TryParse(rateLimitReset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            {
                var sifirlanma = DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(now.Offset);
                var dakika = Math.Max(1, (int)Math.Ceiling((sifirlanma - now).TotalMinutes));

                return "GitHub'ın saatlik sorgu sınırı doldu (bu internet bağlantısından saatte 60 sorgu). "
                       + $"Yaklaşık {dakika} dakika sonra, {sifirlanma:HH:mm}'den sonra yeniden deneyin.";
            }

            return "GitHub'ın saatlik sorgu sınırı doldu (bu internet bağlantısından saatte 60 sorgu). "
                   + "Bir saat içinde yeniden deneyin.";
        }

        return statusCode == (int)HttpStatusCode.NotFound
            ? "GitHub'da yayınlanmış bir sürüm bulunamadı (HTTP 404)."
            : $"GitHub beklenmeyen bir cevap verdi (HTTP {statusCode}).";
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// <paramref name="latest"/> surumu <paramref name="current"/>'dan yeni mi.
    /// </summary>
    /// <remarks>
    /// Metin karsilastirmasi YANLIS sonuc verir: "0.1.9" ile "0.1.14"
    /// karsilastirildiginda metin sirasi 0.1.9'u sonraya koyar ve guncelleme
    /// hic gorunmez. Parcalar sayi olarak karsilastiriliyor.
    /// </remarks>
    public static bool IsNewer(string? latest, string? current)
    {
        if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        // Kurulu surum "0.1.14+abc123" biciminde gelebiliyor (commit damgasi).
        var currentCore = current.Split('+')[0];

        return Version.TryParse(latest, out var l)
               && Version.TryParse(currentCore, out var c)
               && l > c;
    }
}

/// <summary>Surum sorgusunun sonucu: surum ya da neden alinamadigi.</summary>
/// <param name="Version">En yeni surum ("0.1.22"); alinamadiysa null.</param>
/// <param name="Failure">Alinamadiysa kullaniciya gosterilecek sebep.</param>
public sealed record LatestVersionCheck(string? Version, string? Failure);
