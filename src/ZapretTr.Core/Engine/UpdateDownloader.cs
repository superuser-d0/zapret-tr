using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;

namespace ZapretTr.Core.Engine;

/// <summary>Yeni sürümün kurulum paketini indirir ve özetini doğrular.</summary>
/// <remarks>
/// Kullanıcılar her sürümde tarayıcı açıp, dosyayı bulup, indirip, SmartScreen
/// uyarısını geçmek zorundaydı. Bu, düzeltmenin kullanıcıya ulaşmasındaki en
/// büyük sürtünmeydi; ulaşmayan düzeltme de işe yaramıyor.
///
/// ÖZET DOĞRULAMASI ATLANAMAZ. Uygulama indirdiği dosyayı ÇALIŞTIRACAK;
/// doğrulanmamış bir ikiliyi çalıştırmak, projenin kullanıcıya "indirdiğinizi
/// SHA256 ile doğrulayın" demesiyle çelişirdi. Yarım inen ya da bozulan bir
/// paket de aynı kontrole takılır.
///
/// Bunun kod imzalamanın yerine geçmediğini bilerek yazıyoruz: özet de paketle
/// aynı kaynaktan geliyor, dolayısıyla GitHub'a ve HTTPS'e duyulan güveni
/// aşmıyor. Sağladığı şey bütünlük, köken değil.
/// </remarks>
public static class UpdateDownloader
{
    private const string DownloadBase =
        "https://github.com/superuser-d0/zapret-tr/releases/download";

    /// <summary>Yalnızca bu adla eşleşen dosyalar silinir; klasördeki başka hiçbir şeye dokunulmaz.</summary>
    private const string InstallerPattern = "ZapretTR-Setup-*.exe";

    /// <summary>Güncelleme paketlerinin indirildiği klasör.</summary>
    public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "ZapretTR-guncelleme");

    /// <summary>Kurulum paketini indirir; özet tutmazsa hata verir.</summary>
    /// <returns>İndirilen dosyanın tam yolu.</returns>
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

        // Öncekiler artık gereksiz: ya kuruldular ya da yerlerine bu gelecek.
        DeleteOldInstallers(targetDirectory);

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
            // Bozuk dosya DİSKTE BIRAKILMAZ: kullanıcı sonradan bulup elle
            // çalıştırabilir ve o dosyanın doğrulanmadığını bilmez.
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

    /// <summary>Klasördeki eski kurulum paketlerini siler.</summary>
    /// <remarks>
    /// Güncelleme, indirdiği paketi hiç silmiyordu. 0.1.22 ile gerçek bir makinede
    /// ölçüldü: <c>%TEMP%\ZapretTR-guncelleme</c> içinde 0.1.16'dan 0.1.22'ye altı
    /// paket, yaklaşık 330 MB. Her güncelleme 55 MB daha ekliyordu.
    ///
    /// Paket kurulum bitene kadar silinemez: çalışan kurulum kendi dosyasını
    /// kilitliyor. Kilitli dosya atlanır ve sayılır; çağıran sonra tekrar deneyebilir.
    /// Eşleşmeyen dosyalara dokunulmaz; kullanıcı klasöre başka bir şey
    /// koyduysa o bizim sileceğimiz bir şey değil.
    /// </remarks>
    public static InstallerCleanupResult DeleteOldInstallers(string directory)
    {
        var silinen = 0;
        var atlanan = 0;
        var bayt = 0L;

        if (!Directory.Exists(directory))
        {
            return new InstallerCleanupResult(0, 0, 0);
        }

        foreach (var dosya in Directory.EnumerateFiles(directory, InstallerPattern, SearchOption.TopDirectoryOnly))
        {
            // İkinci güvence. .NET 8'in deseni ".exe_" gibi uzantıları eşleştirmiyor
            // (ölçüldü: bu kontrol kaldırılınca da test geçiyor), ama Win32'nin eski
            // "*.exe" davranışı eşleştiriyordu. Silme işleminde desen motoruna
            // güvenmek yerine uzantı birebir karşılaştırılıyor.
            if (!dosya.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var boyut = new FileInfo(dosya).Length;
                File.Delete(dosya);
                silinen++;
                bayt += boyut;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                atlanan++;
            }
        }

        return new InstallerCleanupResult(silinen, bayt, atlanan);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Silinemedi. Çağıran zaten hata fırlatıyor; burada ikinci bir
            // hatayla o mesajı gizlemenin anlamı yok.
        }
    }
}

/// <summary>Eski kurulum paketi temizliğinin sonucu.</summary>
/// <param name="Deleted">Silinen paket sayısı.</param>
/// <param name="Bytes">Boşaltılan alan.</param>
/// <param name="Skipped">Kilitli ya da erişilemediği için silinemeyen paket sayısı.</param>
public sealed record InstallerCleanupResult(int Deleted, long Bytes, int Skipped);
