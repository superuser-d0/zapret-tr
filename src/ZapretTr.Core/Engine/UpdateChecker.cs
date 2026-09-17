using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Yayımlanmış en yeni sürümü sorar.</summary>
/// <remarks>
/// Bu, uygulamanın KENDİLİĞİNDEN dışarı istek yapan tek yeri ve bunu gizlemek doğru olmaz:
/// GitHub'a bir GET gidiyor, dolayısıyla kullanıcının IP adresi GitHub'ın
/// günlüklerine düşüyor. Gönderilen başka hiçbir şey yok: ne hat bilgisi, ne
/// seçili strateji, ne ölçüm sonucu. Yalnızca "en son sürüm ne" sorusu.
///
/// Neden var: günde birkaç sürüm çıkabiliyor ve her seferinde test
/// kullanıcılarına tek tek "şunu kur" demek gerekiyordu. Düzeltmeyi kullanıcıya
/// ulaştıramayan bir sürüm işe yaramıyor.
///
/// Kapatılabilir olması şart (<c>updateCheckEnabled</c>): engellemenin konu
/// olduğu bir araç, kullanıcının haberi olmadan ağ isteği yapmamalı.
/// </remarks>
public static class UpdateChecker
{
    private const string LatestUrl =
        "https://api.github.com/repos/superuser-d0/zapret-tr/releases/latest";

    /// <summary>Yayın sayfası; kullanıcıya gösterilecek adres.</summary>
    public const string ReleasesPage =
        "https://github.com/superuser-d0/zapret-tr/releases/latest";

    /// <summary>
    /// En yeni sürümü sorar. Ulaşılamazsa <c>null</c> döner.
    /// </summary>
    /// <remarks>
    /// Hata YUTULUYOR: güncelleme kontrolü bir kolaylık, korumanın parçası
    /// değil. Ağ yoksa ya da GitHub cevap vermiyorsa kullanıcıya hata
    /// göstermenin bir anlamı olmaz; uygulamanın işi bundan etkilenmiyor.
    /// </remarks>
    public static async Task<string?> GetLatestVersionAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
        => (await CheckLatestAsync(timeout, cancellationToken).ConfigureAwait(false)).Version;

    /// <summary>En yeni sürümü sorar; alınamazsa SEBEBİNİ kullanıcıya söylenecek biçimde döner.</summary>
    /// <remarks>
    /// "Güncellemeleri Denetle" başarısız olunca yalnızca günlüğe "Sürüm bilgisi
    /// alınamadı" yazıyordu; Ayrıntılar kapalıyken kullanıcı için düğme hiçbir şey
    /// yapmıyordu. Sebep de tek bir cümleye sığıyordu. En olası sebeplerden biri
    /// GitHub'ın oturumsuz API sınırının (IP başına saatte 60) dolması ve bu
    /// durumda kullanıcının yapabileceği tek şey beklemek; ne kadar bekleyeceğini
    /// söylemek gerekiyor.
    /// </remarks>
    public static async Task<LatestVersionCheck> CheckLatestAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };

            // GitHub API, User-Agent olmadan 403 dönüyor.
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

    /// <summary>Yayın cevabındaki tag_name'den sürümü çıkarır ("v0.1.22" -> "0.1.22").</summary>
    /// <remarks>
    /// Düz metin ayrıştırma: System.Text.Json'ın yansımalı yolu kırpma çözümleyicisini
    /// patlatıyor ve tek bir alan için kaynak üretimli bir bağlam tanımlamak fazla.
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

    /// <summary>Başarısız HTTP cevabını kullanıcıya söylenecek bir cümleye çevirir.</summary>
    /// <param name="statusCode">HTTP durum kodu.</param>
    /// <param name="rateLimitRemaining">X-RateLimit-Remaining başlığı.</param>
    /// <param name="rateLimitReset">X-RateLimit-Reset başlığı (Unix saniyesi).</param>
    /// <param name="now">Şimdiki zaman; "kaç dakika" hesabı için.</param>
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
    /// <paramref name="latest"/> sürümü <paramref name="current"/>'dan yeni mi.
    /// </summary>
    /// <remarks>
    /// Metin karşılaştırması YANLIŞ sonuç verir: "0.1.9" ile "0.1.14"
    /// karşılaştırıldığında metin sırası 0.1.9'u sonraya koyar ve güncelleme
    /// hiç görünmez. Parçalar sayı olarak karşılaştırılıyor.
    /// </remarks>
    public static bool IsNewer(string? latest, string? current)
    {
        if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        // Kurulu sürüm "0.1.14+abc123" biçiminde gelebiliyor (commit damgası).
        var currentCore = current.Split('+')[0];

        return Version.TryParse(latest, out var l)
               && Version.TryParse(currentCore, out var c)
               && l > c;
    }
}

/// <summary>Sürüm sorgusunun sonucu: sürüm ya da neden alınamadığı.</summary>
/// <param name="Version">En yeni sürüm ("0.1.22"); alınamadıysa null.</param>
/// <param name="Failure">Alınamadıysa kullanıcıya gösterilecek sebep.</param>
public sealed record LatestVersionCheck(string? Version, string? Failure);
