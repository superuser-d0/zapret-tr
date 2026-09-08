using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;

namespace ZapretTr.Core.Engine;

/// <summary>Yeni surumun kurulum paketini indirir ve ozetini dogrular.</summary>
/// <remarks>
/// Kullanicilar her surumde tarayici acip, dosyayi bulup, indirip, SmartScreen
/// uyarisini gecmek zorundaydi. Bu, duzeltmenin kullaniciya ulasmasindaki en
/// buyuk surtunmeydi -- ve ulasmayan duzeltme ise yaramiyor.
///
/// OZET DOGRULAMASI ATLANAMAZ. Uygulama indirdigi dosyayi CALISTIRACAK;
/// dogrulanmamis bir ikiliyi calistirmak, projenin kullaniciya "indirdiginizi
/// SHA256 ile dogrulayin" demesiyle celisirdi. Yarim inen ya da bozulan bir
/// paket de ayni kontrole takilir.
///
/// Bunun kod imzalamanin yerine gecmedigini bilerek yaziyoruz: ozet de paketle
/// ayni kaynaktan geliyor, dolayisiyla GitHub'a ve HTTPS'e duyulan guveni
/// asmiyor. Sagladigi sey butunluk, koken degil.
/// </remarks>
public static class UpdateDownloader
{
    private const string DownloadBase =
        "https://github.com/superuser-d0/zapret-tr/releases/download";

    /// <summary>Kurulum paketini indirir; ozet tutmazsa hata verir.</summary>
    /// <returns>Indirilen dosyanin tam yolu.</returns>
    public static async Task<string> DownloadAsync(
        string version,
        string targetDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var fileName = $"ZapretTR-Setup-{version}.exe";
        var baseUrl = $"{DownloadBase}/v{version}";

        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, fileName);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "ZapretTR");

        var beklenen = await FetchExpectedHashAsync(client, baseUrl, fileName, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{fileName} icin SHA256 ozeti bulunamadi; indirme dogrulanamaz.");

        await DownloadFileAsync(client, $"{baseUrl}/{fileName}", target, progress, cancellationToken)
            .ConfigureAwait(false);

        var gercek = await ComputeHashAsync(target, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(gercek, beklenen, StringComparison.OrdinalIgnoreCase))
        {
            // Bozuk dosya DISKTE BIRAKILMAZ: kullanici sonradan bulup elle
            // calistirabilir ve o dosyanin dogrulanmadigini bilmez.
            TryDelete(target);

            throw new InvalidOperationException(
                "Indirilen paketin SHA256 ozeti tutmuyor; dosya silindi. " +
                $"Beklenen {beklenen}, bulunan {gercek}.");
        }

        return target;
    }

    private static async Task<string?> FetchExpectedHashAsync(
        HttpClient client, string baseUrl, string fileName, CancellationToken cancellationToken)
    {
        var sums = await client.GetStringAsync($"{baseUrl}/SHA256SUMS.txt", cancellationToken)
            .ConfigureAwait(false);

        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split("  ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                string.Equals(parts[1].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].Trim();
            }
        }

        return null;
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        string target,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0L;
        var okunan = 0L;
        var buffer = new byte[81920];

        using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = File.Create(target);

        while (true)
        {
            var n = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, n), cancellationToken)
                .ConfigureAwait(false);

            okunan += n;

            if (total > 0)
            {
                progress?.Report((int)(okunan * 100 / total));
            }
        }
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Silinemedi. Cagiran zaten hata firlatiyor; burada ikinci bir
            // hatayla o mesaji gizlemenin anlami yok.
        }
    }
}
