using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>Takili surucuyu bosaltma denemesinin sonucu.</summary>
/// <param name="Cleared">Denemeden sonra cekirdekte WinDivert surucusu kalmadi mi.</param>
/// <param name="Detail">Kullaniciya ve gunluge yazilacak aciklama.</param>
public sealed record DriverUnloadResult(bool Cleared, string Detail);

/// <summary>winws, WinDivert surucusunu acamadigi icin baslayamadi.</summary>
public sealed class WinDivertOpenException : InvalidOperationException
{
    public WinDivertOpenException(int? win32Error, string message, bool recoverable)
        : base(message)
    {
        Win32Error = win32Error;
        Recoverable = recoverable;
    }

    /// <summary>winws'in bildirdigi Windows hata kodu; yazmadiysa null.</summary>
    public int? Win32Error { get; }

    /// <summary>Takili surucuyu bosaltmak bu hatayi duzeltebilir mi.</summary>
    public bool Recoverable { get; }
}

/// <summary>
/// WinDivert surucusunun cekirdekteki durumunu teshis eder ve takili kalmissa bosaltir.
/// </summary>
/// <remarks>
/// SAHADAN GELEN BELIRTI: "yeni surumu indirip parametre testi yaptim, motor
/// calismiyor; bilgisayari yeniden baslatinca aciliyor." Yeniden baslatmanin
/// duzelttigi tek sey cekirdekte yuklu kalmis bir surucu.
///
/// WinDivert'in surucusu winws kapaninca cekirdekten DUSMUYOR: servis kaydi
/// baslatildigi anda "silinmek uzere" isaretleniyor ve surucu ancak servis
/// DURDURULUNCA ya da makine yeniden baslayinca gidiyor. Bu genelde zararsiz --
/// sonraki winws yuklu surucuyu kullanir. Zararli oldugu iki durum var:
///
///   1. Cekirdekteki surucu BASKA bir kopyadan geliyor (GoodbyeDPI, baska bir
///      zapret dagitimi, WinDivert'in farkli bir surumu). Yeni surucu
///      yuklenemiyor: 654 "onceki surucu hala bellekte".
///   2. Surucu yarim birakilmis bir durdurmada kaldi: yukseltme/kaldirma winws'i
///      oldururken hemen ardindan <c>sc stop</c> + <c>sc delete</c> calisiyor ve
///      surec henuz tutamaclarini kapatmamis oluyor. Kayit "silinmek uzere
///      isaretli" (1072) kaliyor, yeni kayit acilamiyor.
///
/// Iki durumda da yeniden baslatmaya gerek yok: surucuyu kullanan hicbir surec
/// yoksa <c>sc stop</c> onu cekirdekten dusuruyor. Eskiden bunu yalnizca "Tum
/// Ayarlari Sifirla" yapiyordu ve kullaniciya "bilgisayari yeniden baslatin"
/// deniyordu.
/// </remarks>
[SupportedOSPlatform("windows")]
public static partial class WinDivertDriver
{
    /// <summary>winws ciktisindaki satirlar surucu acma hatasini mi anlatiyor.</summary>
    public static bool IsOpenFailure(IEnumerable<string> lines)
        => lines.Any(l => l.Contains("windivert: error opening filter", StringComparison.OrdinalIgnoreCase)
                          || l.Contains("win_dark_init failed", StringComparison.OrdinalIgnoreCase));

    /// <summary>winws'in yazdigi "win32 error N" kodunu cikarir; yoksa null.</summary>
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
    /// Surucuyu bosaltmak bu hatayi duzeltebilir mi.
    /// </summary>
    /// <remarks>
    /// Imza reddi, guvenlik yazilimi engeli, kapali BFE servisi ve eksik dosya
    /// surucuyu bosaltmakla gecmez; o durumlarda denemek yalnizca kullaniciyi
    /// bekletir ve "yeniden denendi" diyerek yanlis umut verir. Kodu bilinmeyen
    /// hata deneniyor: takili surucu en sik sebep.
    /// </remarks>
    public static bool IsRecoverable(int? win32Error)
        => win32Error is not (577 or 1275 or 1753 or 5 or 2 or 3);

    /// <summary>Hata kodunun kullaniciya anlatimi.</summary>
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
    /// Surucuyu kullanabilecek bir surec calisiyor mu: winws ya da bilinen bir DPI araci.
    /// </summary>
    /// <remarks>
    /// Okunamiyorsa CALISIYOR sayiliyor: kullanilan bir surucuyu durdurmaya
    /// kalkmaktansa bosaltmayi atlamak yeglenir.
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
    /// Bosaltma basarisiz olduktan sonra yeniden denenmeden once beklenen sure.
    /// </summary>
    /// <remarks>
    /// Parametre testi yuzlerce aday deniyor. Surucu gercekten bosaltilamiyorsa
    /// her aday icin 20 saniye beklemek, "motor calismiyor" sonucunu dakikalarca
    /// geciktirirdi.
    /// </remarks>
    private static readonly TimeSpan FailedUnloadCooldown = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Kimse kullanmiyorsa WinDivert surucusunu cekirdekten dusurur ve dustugunu bekler.
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

        // Surucu bir SUREC tarafindan tutuluyorsa durdurma istegi onu dusurmez,
        // yalnizca "durduruluyor" durumunda asili birakir ve durumu kotulestirir.
        // Yarim olmekte olan bir winws icin kisa bir bekleme taniniyor:
        // yukseltmedeki taskkill tam olarak boyle bir an birakiyor.
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
    /// <c>sc query</c> ciktisi surucunun gittigini ya da durdugunu mu soyluyor.
    /// </summary>
    /// <remarks>
    /// STOP_PENDING durmus SAYILMAZ: tam da takili kalmanin gorunumu o. 1060 "servis
    /// yok" demek: WinDivert kaydi silinmek uzere isaretli oldugu icin surucu dusunce
    /// kayit da kayboluyor.
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
