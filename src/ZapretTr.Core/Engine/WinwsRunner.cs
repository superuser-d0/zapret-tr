using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretTr.Core.Engine;

/// <summary>winws sürecinin durumu.</summary>
public enum WinwsState
{
    Stopped,
    Running,
    Faulted,
}

/// <summary>winws'in stdout/stderr satırı.</summary>
public sealed record WinwsLogLine(DateTimeOffset Timestamp, string Text, bool IsError);

/// <summary>
/// winws.exe sürecini başlatır, durdurur ve çıktısını yayınlar.
/// </summary>
/// <remarks>
/// Tek bir örneği yönetir. Test motoru aynı anda birden fazla winws çalıştıracağı için
/// her işçi kendi <see cref="WinwsRunner"/> örneğini tutar.
///
/// Önemli: süreç düzgün sonlandırılmazsa WinDivert sürücüsü yüklü kalır. Durdurma
/// yolunda bu yüzden önce nazik kapatma denenir, olmazsa öldürülür; sürücü temizliği
/// ayrı bir işle (<see cref="WinDivertCleanup"/>) yapılır, çünkü aynı sürücüyü başka
/// bir winws örneği hâlâ kullanıyor olabilir.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WinwsRunner : IAsyncDisposable
{
    private readonly VendorPaths _vendor;
    private readonly object _gate = new();
    private Process? _process;

    public WinwsRunner(VendorPaths vendor) => _vendor = vendor;

    /// <summary>winws bir satır yazdığında tetiklenir. Arayüzdeki canlı günlük bunu dinler.</summary>
    public event Action<WinwsLogLine>? LogLineReceived;

    /// <summary>Durum değiştiğinde tetiklenir.</summary>
    public event Action<WinwsState>? StateChanged;

    public WinwsState State { get; private set; } = WinwsState.Stopped;

    /// <summary>Şu an çalışan komutun argümanları. Durmuşken null.</summary>
    public IReadOnlyList<string>? CurrentArguments { get; private set; }

    /// <summary>
    /// winws'in KENDİ bildirdiği sürüm ("v72.12"). Hiç çalışmadıysa null.
    /// </summary>
    /// <remarks>
    /// Sabit bir metin yerine ikilinin kendi söylediği tutuluyor, çünkü ikisi
    /// AYRIŞMIŞ durumdaydı: arayüz "winws v72.13" yazıyordu ama gerçek bir
    /// kullanıcının günlüğünde motor "github version v72.12" diyordu.
    ///
    /// Sebebi tedarik zincirinde: <c>winws.exe</c> <c>zapret-win-bundle</c>
    /// deposundan bir COMMIT ile sabitleniyor (o depoda etiket yok), yalnızca sahte
    /// yük dosyaları ve filtre parçaları <c>zapret</c> deposunun v72.13
    /// etiketinden geliyor. Yani "v72.13" hiçbir zaman winws'in sürümü değildi.
    ///
    /// Yanlış sürüm bildirmek teşhisi doğrudan bozar: gelen bir hata
    /// bildiriminde hangi ikilinin koştuğu bilinmezse, bir seçeneğin var olup
    /// olmadığı bile tartışılamaz.
    /// </remarks>
    public string? ReportedVersion { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>
    /// winws'i verilen argümanlarla başlatır.
    /// </summary>
    /// <exception cref="InvalidOperationException">Zaten çalışıyorsa.</exception>
    /// <exception cref="FileNotFoundException">winws.exe yoksa.</exception>
    /// <exception cref="UnauthorizedAccessException">Yönetici yetkisi yoksa.</exception>
    public void Start(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            throw new ArgumentException("Bos arguman listesiyle winws baslatilamaz.", nameof(arguments));
        }

        ElevationGuard.EnsureElevated();

        // Eksik dosyaları BAŞLATMADAN önce yakala. Eksik bir DLL ile winws.exe
        // başlatılırsa Windows kalıcı (modal) bir hata penceresi açar; süreç ölmediği
        // için biz de hata almayız, sonsuza kadar bekleriz. Bu tam olarak yaşandı:
        // cygwin1.dll indirme listesinden çıkarılmıştı.
        var missing = _vendor.FindMissingFiles();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                VendorPaths.MissingFilesAdvice(
                    string.Join(", ", missing.Select(Path.GetFileName))),
                missing[0]);
        }

        // Çocuk süreç ana sürecin hata modunu miras alır. SEM_FAILCRITICALERRORS
        // ile eksik DLL gibi yükleyici hatalarında pencere açılmaz, süreç doğrudan
        // düşer; yani hata bize görünür bir biçimde ulaşır.
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);

        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("winws zaten calisiyor. Once durdurun.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _vendor.WinwsExe,
                WorkingDirectory = _vendor.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // ArgumentList kullanıyoruz: her argüman ayrı geçer, kabuk tırnaklama
            // kurallarını elle taklit etmemiz gerekmez.
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            // winws'in kendi söyledikleri. Erken ölümde hata mesajına bunlar
            // konuyor: eskiden yalnızca çıkış kodu vardı ve "34 koduyla kapandı,
            // ayrıntılar için günlüğe bakın" deniyordu; ama günlükte ayrıntı
            // YOKTU. Gerçek bir kullanıcıda 304 aday bu mesajla düştü ve
            // sebebini kimse öğrenemedi. Motorun söylediği şey teşhisin
            // kendisiydi ve biz onu atıyorduk.
            var ilkSatirlar = new List<string>();
            _startupLines = ilkSatirlar;
            var yakalama = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureStarted = yakalama;

            void Kaydet(string? satir)
            {
                if (string.IsNullOrWhiteSpace(satir))
                {
                    return;
                }

                if (ParseVersion(satir) is { } surum)
                {
                    ReportedVersion = surum;
                }

                if (satir.Contains(CaptureStartedLine, StringComparison.OrdinalIgnoreCase))
                {
                    yakalama.TrySetResult();
                }

                lock (ilkSatirlar)
                {
                    // İlk birkaç satır yetiyor: winws sebebi hemen başta
                    // yazıyor, sonrası paket günlüğü. Sürücü hatası satırları
                    // sınırdan bağımsız tutuluyor: teşhis onların içinde.
                    if (ilkSatirlar.Count < 5 || WinDivertDriver.IsOpenFailure([satir]))
                    {
                        ilkSatirlar.Add(satir.Trim());
                    }
                }
            }

            // Akışların sonu (e.Data == null). Erken ölümde son satırları beklemek için.
            var ciktiBitti = new ManualResetEventSlim();
            var hataBitti = new ManualResetEventSlim();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    ciktiBitti.Set();
                    return;
                }

                Kaydet(e.Data);
                PublishLine(e.Data, isError: false);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    hataBitti.Set();
                    return;
                }

                Kaydet(e.Data);
                PublishLine(e.Data, isError: true);
            };
            process.Exited += (_, _) =>
            {
                // Beklenmedik çıkış: kullanıcı durdurmadıysa bu bir hatadır ve
                // arayüzde yeşil gözükmeye devam etmesi kabul edilemez.
                // Başlatma penceresindeki ölüm ise StartAsync'in işi: orada
                // sınıflandırılıp (gerekirse sürücü boşaltılıp) yeniden deneniyor.
                // Burada da "Faulted" yayınlamak arayüzde bir anlık "BEKLENMEDİK
                // DURUŞ" gösterirdi, sonra her şey düzelmiş olurdu.
                if (State == WinwsState.Running && !_starting)
                {
                    SetState(WinwsState.Faulted);
                }
            };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
                when (SecurityBlockAdvice.Describe(ex, "winws.exe") is { } engel)
            {
                // Defender ya da Akıllı Uygulama Denetimi winws.exe'yi çalıştırmadı.
                // Windows'un tek başına cümlesi ("Bir Uygulama Denetimi ilkesi bu
                // dosyayı engelledi") kimin engellediğini ve ne yapılacağını söylemiyor.
                process.Dispose();
                throw new InvalidOperationException(engel, ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Erken ölüm kontrolü: winws geçersiz bir argüman aldığında ya da
            // sürücüyü açamadığında hemen çıkıyor. Bunu burada yakalamazsak
            // arayüz "çalışıyor" gösterir ve kullanıcı korunduğunu sanır.
            if (WaitForEarlyExit(process, StartupGraceMilliseconds, ciktiBitti.WaitHandle, hataBitti.WaitHandle))
            {
                var exitCode = process.ExitCode;
                process.Dispose();
                throw BuildEarlyExitException(exitCode, ilkSatirlar);
            }

            _process = process;
            CurrentArguments = arguments;
        }

        SetState(WinwsState.Running);
    }

    /// <summary>Son satırlar için akışların kapanması en fazla bu kadar beklenir.</summary>
    private static readonly TimeSpan OutputDrainLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Süreç başlangıç penceresinde öldü mü. Öldüyse çıktısının son satırlarını
    /// SINIRLI bir süre bekler.
    /// </summary>
    /// <remarks>
    /// KİLİTLENME. Burada önce parametresiz <c>process.WaitForExit()</c> vardı. O
    /// çağrı yönlendirilmiş çıktı akışlarının sonunu bekliyor; akışları okuyan geri
    /// çağrılar ise satırı <see cref="LogLineReceived"/> ile arayüze gönderiyor ve
    /// arayüz satırı <c>Dispatcher.Invoke</c> ile yazıyordu. Start arayüzün
    /// "Başlat"ından ARAYÜZ İŞ PARÇACIĞINDA çağrılıyor. winws 250 ms içinde ölünce
    /// (örneğin çökmüş bir önceki örnekten sahipsiz bir winws kalmışsa) iki taraf
    /// birbirini sonsuza kadar bekliyordu: pencere donuyor, Windows "yanıt vermiyor"
    /// deyip kapatıyordu. Gerçek bir kullanıcıda 2026-09-14'te iki kez ölçüldü (olay
    /// günlüğünde AppHang) ve ayrı bir denemede yeniden üretildi.
    ///
    /// Bekleme artık akışların kapandığı olaylarla ve bir üst sınırla yapılıyor.
    /// Arayüz tarafı da satırı beklemeden gönderiyor (MainViewModel.Append); ikisi
    /// birlikte hem kilitlenmeyi hem de son satırların kaybolmasını önlüyor.
    /// </remarks>
    public static bool WaitForEarlyExit(Process process, int graceMilliseconds, WaitHandle stdoutClosed, WaitHandle stderrClosed)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!process.WaitForExit(graceMilliseconds))
        {
            return false;
        }

        // Sırayla ve ortak bir süreyle. WaitHandle.WaitAll STA iş parçacığında
        // desteklenmiyor (NotSupportedException) ve Start tam da WPF'in STA arayüz
        // iş parçacığında çağrılıyor: ilk düzeltme denemesi donmayı ÇÖKMEYE çeviriyordu,
        // EarlyExitDeadlockTests yakaladı.
        var sure = Stopwatch.StartNew();
        stdoutClosed.WaitOne(OutputDrainLimit);
        var kalan = OutputDrainLimit - sure.Elapsed;
        stderrClosed.WaitOne(kalan > TimeSpan.Zero ? kalan : TimeSpan.Zero);
        return true;
    }

    private List<string> _startupLines = [];
    private TaskCompletionSource _captureStarted = new();

    /// <summary>winws'in yakalamayı başlattığında yazdığı satır.</summary>
    private const string CaptureStartedLine = "capture is started";

    /// <summary>
    /// Sürücü açma hatasının görünebileceği en uzun pencere.
    /// </summary>
    /// <remarks>
    /// <see cref="Start"/> yalnızca ilk 250 ms'deki ölümü yakalıyor. Sürücü
    /// yüklemesi yavaş bir makinede (virüsten koruma sürücüyü tararken) daha uzun
    /// sürebiliyor ve o zaman winws "çalışıyor" sayılıp birkaç yüz milisaniye sonra
    /// sessizce ölüyordu: parametre testi aday adına zaman aşımı yazıyor, yani
    /// motor hatası DPI engeli gibi görünüyordu. Normalde bekleme winws
    /// "capture is started" dediği anda bitiyor.
    /// </remarks>
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// winws'i başlatır; sürücü çekirdekte takılı kaldığı için açılamazsa sürücüyü
    /// boşaltıp BİR KEZ yeniden dener.
    /// </summary>
    /// <remarks>
    /// Bütün başlatma yolları (arayüzün "Başlat"ı, parametre testi, CLI) bunu
    /// çağırmalı. Gerekçesi <see cref="WinDivertDriver"/>'da: eskiden bu durumda
    /// her aday aynı hatayla düşüyor ve tek çıkış yolu bilgisayarı yeniden
    /// başlatmaktı.
    /// </remarks>
    public async Task StartAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            await StartAndWaitForCaptureAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (WinDivertOpenException ex) when (ex.Recoverable)
        {
            PublishLine("WinDivert surucusu acilamadi; cekirdekte takili surucu bosaltilip yeniden deneniyor...", isError: true);

            var unload = await WinDivertDriver
                .TryUnloadIdleAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            PublishLine(unload.Detail, isError: !unload.Cleared);

            if (!unload.Cleared)
            {
                throw new WinDivertOpenException(ex.Win32Error,
                    ex.Message + " " + unload.Detail
                    + " Surucuyu kullanan baska bir araci kapatin; olmazsa bilgisayari bir kez yeniden baslatin.",
                    recoverable: false);
            }

            await StartAndWaitForCaptureAsync(arguments, cancellationToken).ConfigureAwait(false);
            PublishLine("Surucu bosaltildiktan sonra winws basladi.", isError: false);
        }
    }

    private volatile bool _starting;

    private async Task StartAndWaitForCaptureAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Process? process;
        Task done;
        Task exited;

        _starting = true;
        try
        {
            Start(arguments);

            lock (_gate)
            {
                process = _process;
            }

            if (process is null)
            {
                return;
            }

            exited = process.WaitForExitAsync(cancellationToken);
            done = await Task.WhenAny(_captureStarted.Task, exited, Task.Delay(CaptureWindow, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            _starting = false;
        }

        if (done != exited)
        {
            // Pencere kapandıktan hemen sonra ölmüşse Exited olayı "Faulted"ı
            // kaçırmış olabilir; normal yola bırak.
            try
            {
                if (process.HasExited && State == WinwsState.Running)
                {
                    SetState(WinwsState.Faulted);
                }
            }
            catch (InvalidOperationException)
            {
                // Bu arada StopAsync süreci bıraktı.
            }

            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
            {
                // Bu arada biri durdurdu; hata değil.
                return;
            }

            _process = null;
            CurrentArguments = null;
        }

        SetState(WinwsState.Stopped);
        var exitCode = process.ExitCode;
        process.Dispose();
        throw BuildEarlyExitException(exitCode, _startupLines);
    }

    /// <summary>winws'in erken ölümünü, söylediklerine göre sınıflandırılmış bir hataya çevirir.</summary>
    private static InvalidOperationException BuildEarlyExitException(int exitCode, List<string> lines)
    {
        List<string> kopya;
        lock (lines)
        {
            kopya = [.. lines];
        }

        var soyledigi = kopya.Count > 0
            ? " winws: " + string.Join(" | ", kopya)
            : " (winws hicbir sey yazmadan cikti)";

        // winws.exe'ye izin verilip yüklediği imzasız cygwin1.dll ya da WinDivert.dll
        // engellenirse süreç yükleyicinin NTSTATUS koduyla ölüyor. Çıplak
        // "-1073740760 koduyla kapandı" kullanıcıya hiçbir şey söylemiyordu.
        if (SecurityBlockAdvice.DescribeExitCode(exitCode, "winws.exe") is { } engel)
        {
            return new InvalidOperationException(engel + soyledigi);
        }

        if (WinDivertDriver.IsOpenFailure(kopya))
        {
            var kod = WinDivertDriver.ParseWin32Error(kopya);
            return new WinDivertOpenException(
                kod,
                WinDivertDriver.Explain(kod) + $" winws {exitCode} koduyla kapandi." + soyledigi,
                WinDivertDriver.IsRecoverable(kod));
        }

        // "1" neredeyse her zaman TEK bir şeyi anlatıyor: winws aynı
        // filtreyle zaten çalışıyor ve ikinci örneği reddediyor
        // ("A copy of winws is already running with the same filter").
        // En sık sebebi otomatik başlatma servisinin açık olması.
        // Çıplak "1 koduyla kapandı" mesajı kullanıcıya hiçbir şey
        // söylemiyordu; gerçek bir kullanıcı bu duvara tosladı.
        var ipucu = exitCode == 1
            ? " En olasi sebep: winws zaten calisiyor (otomatik baslatma servisi acik" +
              " olabilir ya da onceki bir kosum surmus olabilir). Ayni filtreyle ikinci" +
              " bir ornek baslatilamaz."
            : string.Empty;

        return new InvalidOperationException(
            $"winws baslar baslamaz {exitCode} koduyla kapandi." + ipucu + soyledigi);
    }

    /// <summary>
    /// Argümanları winws'in kendisine doğrulatır; sürücüye DOKUNMAZ.
    /// </summary>
    /// <returns>
    /// Geçerliyse null, değilse winws'in yazdığı hata metni.
    /// </returns>
    /// <remarks>
    /// winws'in <c>--dry-run</c> seçeneği "parametreleri doğrula ve başarılıysa 0
    /// ile çık" diyor. Test motoru için bu büyük kazanç: geçersiz bir aday tam
    /// başlatma + ağ zaman aşımı (~7 sn) yerine bir süreç başlatma (~50 ms)
    /// maliyetiyle eleniyor; üstelik "zaman aşımı" gibi bilgisiz bir sonuç yerine
    /// winws'in kendi hata mesajını alıyoruz.
    /// </remarks>
    public async Task<string?> ValidateAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = _vendor.WinwsExe,
            WorkingDirectory = _vendor.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("--dry-run");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return "winws baslatilamadi.";
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode == 0)
        {
            return null;
        }

        // Güvenlik engeli bir parametre hatası DEĞİL. Metin olarak dönseydi test motoru
        // her adayı "geçersiz parametre" diye eler, kullanıcı da yalnızca "hiçbir
        // strateji çalışmadı" görürdü. İstisna, adayın sonucuna engelin adıyla yazılıyor.
        if (SecurityBlockAdvice.DescribeExitCode(process.ExitCode, "winws.exe") is { } engel)
        {
            throw new InvalidOperationException(engel);
        }

        // Hem \r hem \n ayracı: winws çıktısı CRLF kullanıyor ve yalnızca \n ile
        // bölmek her satırın sonunda görünmez bir \r bırakırdı.
        var message = (stderr + stdout)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !line.StartsWith("github version", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(message)
            ? $"winws parametreleri reddetti (kod {process.ExitCode})"
            : message;
    }

    /// <summary>
    /// winws'in açılışta yazdığı sürüm satırından sürümü çıkarır.
    /// </summary>
    /// <remarks>
    /// Satır şu biçimde geliyor: <c>github version v72.12 (5cc46a98...)</c>.
    /// Eşleşmezse null döner; tahmin etmektense bilmemek daha iyi.
    /// </remarks>
    public static string? ParseVersion(string? line)
    {
        const string onEk = "github version ";

        if (line is null)
        {
            return null;
        }

        var i = line.IndexOf(onEk, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        var kalan = line[(i + onEk.Length)..].TrimStart();
        var bosluk = kalan.IndexOf(' ');
        var surum = (bosluk < 0 ? kalan : kalan[..bosluk]).Trim();

        return string.IsNullOrEmpty(surum) ? null : surum;
    }

    /// <summary>
    /// winws.exe'nin içine gömülü sürümü, motoru ÇALIŞTIRMADAN okur. Bulunamazsa null.
    /// </summary>
    /// <remarks>
    /// <see cref="ReportedVersion"/> ancak motor çalışıp sürüm satırını yazdıktan
    /// sonra dolu. Alt bilgi bu yüzden duruma göre değişiyordu (0.1.21, kullanıcı
    /// bildirdi): hiç başlatılmamışken ve duraklatılmışken "winws (zapret-win-bundle)",
    /// DEVAM ET'ten sonra "winws v72.12". İlk Başlat'ta da çoğu zaman eski metin
    /// kalıyordu: alt bilgi "çalışıyor" bildiriminde tazeleniyor, sürüm satırı ise
    /// çıktıyı okuyan ayrı iş parçacığında ondan SONRA gelebiliyor.
    ///
    /// Sürüm yine ikilinin kendisinden geliyor, sabit bir metinden değil: winws
    /// "github version %s (%s)" biçim metnini sürüm ve commit dizgeleriyle
    /// dolduruyor ve derleyici bu dizgeleri yan yana yazıyor. v72.12'de dosyada
    /// sıra şöyle: commit, NUL, "v72.12", NUL, biçim metni.
    /// </remarks>
    public static string? ReadEmbeddedVersion(string exePath)
    {
        try
        {
            return FindEmbeddedVersion(File.ReadAllBytes(exePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// İkili içinde, sürüm biçim metninden hemen önceki dizge sürüm gibi görünüyorsa onu döndürür.
    /// </summary>
    /// <remarks>
    /// Katı: yalnızca "v72.12" biçimi kabul ediliyor. Derleyici dizge sırasını
    /// değiştirirse null dönüyor ve arayüz eskisi gibi motorun bildireceğini
    /// bekliyor; yanlış bir sürüm yazmaktansa bilmemek.
    /// </remarks>
    public static string? FindEmbeddedVersion(ReadOnlySpan<byte> image)
    {
        var bicim = image.IndexOf("github version %s (%s)"u8);
        if (bicim <= 0)
        {
            return null;
        }

        // Hizalama için birden fazla NUL olabilir.
        var son = bicim;
        while (son > 0 && image[son - 1] == 0)
        {
            son--;
        }

        if (son == bicim)
        {
            return null;
        }

        var bas = son;
        while (bas > 0 && image[bas - 1] != 0 && son - bas <= 32)
        {
            bas--;
        }

        var aday = Encoding.ASCII.GetString(image[bas..son]);
        return SurumBicimi.IsMatch(aday) ? aday : null;
    }

    private static readonly Regex SurumBicimi = new(@"^v\d+(\.\d+)+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Başlatmadan sonra erken ölümü yakalamak için beklenen süre.
    /// </summary>
    /// <remarks>
    /// Kısa tutuldu: test motoru her aday için bir winws başlatıyor, dolayısıyla
    /// buradaki her milisaniye aday sayısıyla çarpılıyor.
    /// </remarks>
    private const int StartupGraceMilliseconds = 250;

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    /// <summary>
    /// winws'i durdurur. Zaten durmuşsa sessizce döner.
    /// </summary>
    /// <param name="gracePeriod">
    /// Nazik kapatma için tanınan süre. Dolarsa süreç öldürülür; winws'in asılı
    /// kalması WinDivert sürücüsünün de asılı kalması demek, bu yüzden beklemeyi
    /// süresiz bırakmıyoruz.
    /// </param>
    public async Task StopAsync(TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            SetState(WinwsState.Stopped);
            return;
        }

        // Durumu önce değiştiriyoruz ki Exited olayı bunu hata sanmasın.
        SetState(WinwsState.Stopped);
        CurrentArguments = null;

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();

                var grace = gracePeriod ?? TimeSpan.FromSeconds(3);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(grace);

                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Nazik yol tutmadı. Konsol uygulaması olduğu için bu beklenen durum.
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Süreç zaten ölmüş; yapacak bir şey yok.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void PublishLine(string? text, bool isError)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        LogLineReceived?.Invoke(new WinwsLogLine(DateTimeOffset.Now, text, isError));
    }

    private void SetState(WinwsState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
