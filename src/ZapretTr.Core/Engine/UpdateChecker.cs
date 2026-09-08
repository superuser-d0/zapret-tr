using System.Net.Http;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Yayinlanmis en yeni surumu sorar.</summary>
/// <remarks>
/// Bu, uygulamanin DISARI istek yapan TEK yeri ve bunu gizlemek dogru olmaz:
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
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };

            // GitHub API User-Agent olmadan 403 donuyor.
            client.DefaultRequestHeaders.Add("User-Agent", "ZapretTR");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            var json = await client.GetStringAsync(LatestUrl, cancellationToken)
                .ConfigureAwait(false);

            // Duz metin ayristirma: System.Text.Json'in yansimali yolu kirpma
            // analizorunu patlatiyor ve tek bir alan icin kaynak uretimli bir
            // baglam tanimlamak fazla.
            var match = Regex.Match(
                json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+\\.[0-9]+\\.[0-9]+)\"");

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
