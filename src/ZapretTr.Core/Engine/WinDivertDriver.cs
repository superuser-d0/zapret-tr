using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Takılı sürücüyü boşaltma denemesinin sonucu.</summary>
/// <param name="Cleared">Denemeden sonra çekirdekte WinDivert sürücüsü kalmadı mı.</param>
/// <param name="Detail">Kullanıcıya ve günlüğe yazılacak açıklama.</param>
public sealed record DriverUnloadResult(bool Cleared, string Detail);

/// <summary>winws, WinDivert sürücüsünü açamadığı için başlayamadı.</summary>
public sealed class WinDivertOpenException : InvalidOperationException
{
    public WinDivertOpenException(int? win32Error, string message, bool recoverable)
        : base(message)
    {
        Win32Error = win32Error;
        Recoverable = recoverable;
    }

    /// <summary>winws'in bildirdiği Windows hata kodu; yazmadıysa null.</summary>
    public int? Win32Error { get; }

    /// <summary>Takılı sürücüyü boşaltmak bu hatayı düzeltebilir mi.</summary>
    public bool Recoverable { get; }
}

/// <summary>
/// WinDivert sürücüsünün çekirdekteki durumunu teşhis eder ve takılı kalmışsa boşaltır.
/// </summary>
/// <remarks>
/// SAHADAN GELEN BELİRTİ: "yeni sürümü indirip parametre testi yaptım, motor
/// çalışmıyor; bilgisayarı yeniden başlatınca açılıyor." Yeniden başlatmanın
/// düzelttiği tek şey çekirdekte yüklü kalmış bir sürücü.
///
/// WinDivert'in sürücüsü winws kapanınca çekirdekten DÜŞMÜYOR: servis kaydı
/// başlatıldığı anda "silinmek üzere" işaretleniyor ve sürücü ancak servis
/// DURDURULUNCA ya da makine yeniden başlayınca gidiyor. Bu genelde zararsız;
/// sonraki winws yüklü sürücüyü kullanır. Zararlı olduğu iki durum var:
///
///   1. Çekirdekteki sürücü BAŞKA bir kopyadan geliyor (GoodbyeDPI, başka bir
///      zapret dağıtımı, WinDivert'in farklı bir sürümü). Yeni sürücü
///      yüklenemiyor: 654 "önceki sürücü hâlâ bellekte".
///   2. Sürücü yarım bırakılmış bir durdurmada kaldı: yükseltme/kaldırma winws'i
///      öldürürken hemen ardından <c>sc stop</c> + <c>sc delete</c> çalışıyor ve
///      süreç henüz tutamaçlarını kapatmamış oluyor. Kayıt "silinmek üzere
///      işaretli" (1072) kalıyor, yeni kayıt açılamıyor.
///
/// İki durumda da yeniden başlatmaya gerek yok: sürücüyü kullanan hiçbir süreç
/// yoksa <c>sc stop</c> onu çekirdekten düşürüyor. Eskiden bunu yalnızca "Tüm
/// Ayarları Sıfırla" yapıyordu ve kullanıcıya "bilgisayarı yeniden başlatın"
/// deniyordu.
/// </remarks>
[SupportedOSPlatform("windows")]
public static partial class WinDivertDriver
{
    /// <summary>winws çıktısındaki satırlar sürücü açma hatasını mı anlatıyor.</summary>
    public static bool IsOpenFailure(IEnumerable<string> lines)
        => lines.Any(l => l.Contains("windivert: error opening filter", StringComparison.OrdinalIgnoreCase)
                          || l.Contains("win_dark_init failed", StringComparison.OrdinalIgnoreCase));

    /// <summary>winws'in yazdığı "win32 error N" kodunu çıkarır; yoksa null.</summary>
    public static int? ParseWin32Error(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var m = Win32ErrorPattern().Match(line);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var code))
            {
                return code;
            }
        }

        return null;
    }

    [GeneratedRegex(@"win32 error (\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Win32ErrorPattern();

    /// <summary>
    /// Sürücüyü boşaltmak bu hatayı düzeltebilir mi.
    /// </summary>
    /// <remarks>
    /// İmza reddi, güvenlik yazılımı engeli, kapalı BFE servisi ve eksik dosya
    /// sürücüyü boşaltmakla geçmez; o durumlarda denemek yalnızca kullanıcıyı
    /// bekletir ve "yeniden denendi" diyerek yanlış umut verir. Kodu bilinmeyen
    /// hata deneniyor: takılı sürücü en sık sebep.
    /// </remarks>
    public static bool IsRecoverable(int? win32Error)
        => win32Error is not (577 or 1275 or 1753 or 5 or 2 or 3);

    /// <summary>Hata kodunun kullanıcıya anlatımı.</summary>
    public static string Explain(int? win32Error) => win32Error switch
    {
        654 => "Cekirdekte baska ya da eski bir WinDivert surucusu yuklu kalmis (hata 654).",
        1072 => "WinDivert surucu kaydi silinmek uzere isaretli ama cekirdekten dusmemis (hata 1072).",
        577 => "Windows surucunun imzasini reddetti (hata 577). Guvenli Onyukleme ayarlarina ve Windows guncellemelerine bakin.",
        1275 => "Bir guvenlik yazilimi WinDivert surucusunu engelledi (hata 1275). Virusten koruma istisnasina ekleyin.",
        1753 => "Windows'un 'Temel Filtreleme Altyapisi' (BFE) servisi kapali (hata 1753).",
        5 => "Surucu acilirken erisim reddedildi (hata 5): uygulama yonetici olarak calismiyor olabilir.",
        2 or 3 => $"WinDivert surucu dosyasi bulunamadi (hata {win32Error}).",
        null => "WinDivert surucusu acilamadi.",
        _ => $"WinDivert surucusu acilamadi (hata {win32Error}).",
    };

    /// <summary>
    /// Sürücüyü kullanabilecek bir süreç çalışıyor mu: winws ya da bilinen bir DPI aracı.
    /// </summary>
    /// <remarks>
    /// Okunamıyorsa ÇALIŞIYOR sayılıyor: kullanılan bir sürücüyü durdurmaya
    /// kalkmaktansa boşaltmayı atlamak yeğlenir.
    /// </remarks>
    public static bool IsAnyUserRunning()
    {
        try
        {
            return Process.GetProcessesByName("winws").Length > 0 || ConflictScanner.RunningTools().Count > 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static readonly object CooldownGate = new();
    private static DateTimeOffset _lastFailedUnload = DateTimeOffset.MinValue;

    /// <summary>
    /// Boşaltma başarısız olduktan sonra yeniden denenmeden önce beklenen süre.
    /// </summary>
    /// <remarks>
    /// Parametre testi yüzlerce aday deniyor. Sürücü gerçekten boşaltılamıyorsa
    /// her aday için 20 saniye beklemek, "motor çalışmıyor" sonucunu dakikalarca
    /// geciktirirdi.
    /// </remarks>
    private static readonly TimeSpan FailedUnloadCooldown = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Kimse kullanmıyorsa WinDivert sürücüsünü çekirdekten düşürür ve düştüğünü bekler.
    /// </summary>
    public static async Task<DriverUnloadResult> TryUnloadIdleAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        lock (CooldownGate)
        {
            if (DateTimeOffset.UtcNow - _lastFailedUnload < FailedUnloadCooldown)
            {
                return new DriverUnloadResult(false,
                    "Takili surucu kisa sure once bosaltilamamisti; yeniden denenmedi.");
            }
        }

        // Sürücü bir SÜREÇ tarafından tutuluyorsa durdurma isteği onu düşürmez,
        // yalnızca "durduruluyor" durumunda asılı bırakır ve durumu kötüleştirir.
        // Yarı ölmekte olan bir winws için kısa bir bekleme tanınıyor:
        // yükseltmedeki taskkill tam olarak böyle bir an bırakıyor.
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (IsAnyUserRunning())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                var kullanan = string.Join(", ", ConflictScanner.RunningTools().Append("winws").Distinct());
                MarkFailed();
                return new DriverUnloadResult(false,
                    "Surucu hala bir surec tarafindan kullaniliyor (" + kullanan + "); bosaltilmadi.");
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        foreach (var name in WinDivertCleanup.DriverServiceNames)
        {
            await RunScAsync(["stop", name], cancellationToken).ConfigureAwait(false);
        }

        var kalan = new List<string>();
        while (true)
        {
            kalan.Clear();
            foreach (var name in WinDivertCleanup.DriverServiceNames)
            {
                var (_, output) = await RunScAsync(["query", name], cancellationToken).ConfigureAwait(false);
                if (!IsGoneOrStopped(output))
                {
                    kalan.Add(name);
                }
            }

            if (kalan.Count == 0)
            {
                return new DriverUnloadResult(true, "Takili WinDivert surucusu cekirdekten bosaltildi.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                MarkFailed();
                return new DriverUnloadResult(false,
                    $"WinDivert surucusu {timeout.TotalSeconds:F0} sn icinde cekirdekten dusmedi ("
                    + string.Join(", ", kalan) + ").");
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <c>sc query</c> çıktısı sürücünün gittiğini ya da durduğunu mu söylüyor.
    /// </summary>
    /// <remarks>
    /// STOP_PENDING durmuş SAYILMAZ: tam da takılı kalmanın görünümü o. 1060 "servis
    /// yok" demek: WinDivert kaydı silinmek üzere işaretli olduğu için sürücü düşünce
    /// kayıt da kayboluyor.
    /// </remarks>
    public static bool IsGoneOrStopped(string scQueryOutput)
        => scQueryOutput.Contains("1060", StringComparison.Ordinal)
           || scQueryOutput.Contains("STOPPED", StringComparison.Ordinal);

    private static void MarkFailed()
    {
        lock (CooldownGate)
        {
            _lastFailedUnload = DateTimeOffset.UtcNow;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunScAsync(
        string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, "sc.exe baslatilamadi.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
