using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>winws surecinin durumu.</summary>
public enum WinwsState
{
    Stopped,
    Running,
    Faulted,
}

/// <summary>winws'in stdout/stderr satiri.</summary>
public sealed record WinwsLogLine(DateTimeOffset Timestamp, string Text, bool IsError);

/// <summary>
/// winws.exe surecini baslatir, durdurur ve ciktisini yayinlar.
/// </summary>
/// <remarks>
/// Tek bir ornegi yonetir. Test motoru ayni anda birden fazla winws calistiracagi icin
/// her isci kendi <see cref="WinwsRunner"/> ornegini tutar.
///
/// Onemli: surec duzgun sonlandirilmazsa WinDivert surucusu yuklu kalir. Durdurma
/// yolunda bu yuzden once nazik kapatma denenir, olmazsa oldurulur; surucu temizligi
/// ayri bir isle (<see cref="WinDivertCleanup"/>) yapilir cunku ayni surucuyu baska
/// bir winws ornegi hala kullaniyor olabilir.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WinwsRunner : IAsyncDisposable
{
    private readonly VendorPaths _vendor;
    private readonly object _gate = new();
    private Process? _process;

    public WinwsRunner(VendorPaths vendor) => _vendor = vendor;

    /// <summary>winws bir satir yazdiginda tetiklenir. Arayuzdeki canli gunluk bunu dinler.</summary>
    public event Action<WinwsLogLine>? LogLineReceived;

    /// <summary>Durum degistiginde tetiklenir.</summary>
    public event Action<WinwsState>? StateChanged;

    public WinwsState State { get; private set; } = WinwsState.Stopped;

    /// <summary>Su an calisan komutun argumanlari. Durmusken null.</summary>
    public IReadOnlyList<string>? CurrentArguments { get; private set; }

    /// <summary>
    /// winws'in KENDI bildirdigi surum ("v72.12"). Hic calismadiysa null.
    /// </summary>
    /// <remarks>
    /// Sabit bir metin yerine ikilinin kendi soyledigi tutuluyor, cunku ikisi
    /// AYRISMIS durumdaydi: arayuz "winws v72.13" yaziyordu ama gercek bir
    /// kullanicinin gunlugunde motor "github version v72.12" diyordu.
    ///
    /// Sebebi tedarik zincirinde: <c>winws.exe</c> <c>zapret-win-bundle</c>
    /// deposundan bir COMMIT ile sabitleniyor (o depoda tag yok), yalnizca sahte
    /// yuk dosyalari ve filtre parcalari <c>zapret</c> deposunun v72.13
    /// tag'inden geliyor. Yani "v72.13" hicbir zaman winws'in surumu degildi.
    ///
    /// Yanlis surum bildirmek teshisi dogrudan bozar: gelen bir hata
    /// bildiriminde hangi ikilinin kostugu bilinmezse, bir secenegin var olup
    /// olmadigi bile tartisilamaz.
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
    /// winws'i verilen argumanlarla baslatir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Zaten calisiyorsa.</exception>
    /// <exception cref="FileNotFoundException">winws.exe yoksa.</exception>
    /// <exception cref="UnauthorizedAccessException">Yonetici yetkisi yoksa.</exception>
    public void Start(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            throw new ArgumentException("Bos arguman listesiyle winws baslatilamaz.", nameof(arguments));
        }

        ElevationGuard.EnsureElevated();

        // Eksik dosyalari BASLATMADAN once yakala. Eksik bir DLL ile winws.exe
        // baslatilirsa Windows modal bir hata penceresi acar; surec olmedigi icin
        // biz de hata almayiz, sonsuza kadar bekleriz. Bu tam olarak yasandi:
        // cygwin1.dll indirme listesinden cikarilmisti.
        var missing = _vendor.FindMissingFiles();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                VendorPaths.MissingFilesAdvice(
                    string.Join(", ", missing.Select(Path.GetFileName))),
                missing[0]);
        }

        // Cocuk surec ana surecin hata modunu miras alir. SEM_FAILCRITICALERRORS
        // ile eksik DLL gibi yukleyici hatalarinda pencere acilmaz, surec dogrudan
        // duser -- yani hata bize gorunur bir bicimde ulasir.
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

            // ArgumentList kullaniyoruz: her arguman ayri gecer, kabuk tirnaklama
            // kurallarini elle taklit etmemiz gerekmez.
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            // winws'in kendi soyledikleri. Erken olumde hata mesajina bunlar
            // konuyor: eskiden yalnizca cikis kodu vardi ve "34 koduyla kapandi,
            // ayrintilar icin gunluge bakin" deniyordu -- ama gunlukte ayrinti
            // YOKTU. Gercek bir kullanicida 304 aday bu mesajla dustu ve
            // sebebini kimse ogrenemedi. Motorun soyledigi sey teshisin
            // kendisiydi ve biz onu atiyorduk.
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
                    // Ilk birkac satir yetiyor: winws sebebi hemen basta
                    // yaziyor, sonrasi paket gunlugu. Surucu hatasi satirlari
                    // sinirdan bagimsiz tutuluyor: teshis onlarin icinde.
                    if (ilkSatirlar.Count < 5 || WinDivertDriver.IsOpenFailure([satir]))
                    {
                        ilkSatirlar.Add(satir.Trim());
                    }
                }
            }

            process.OutputDataReceived += (_, e) =>
            {
                Kaydet(e.Data);
                PublishLine(e.Data, isError: false);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                Kaydet(e.Data);
                PublishLine(e.Data, isError: true);
            };
            process.Exited += (_, _) =>
            {
                // Beklenmedik cikis: kullanici durdurmadiysa bu bir hatadir ve
                // arayuzde yesil gozukmeye devam etmesi kabul edilemez.
                // Baslatma penceresindeki olum ise StartAsync'in isi: orada
                // siniflandirilip (gerekirse surucu bosaltilip) yeniden deneniyor.
                // Burada da "Faulted" yayinlamak arayuzde bir anlik "BEKLENMEDIK
                // DURUS" gosterirdi, sonra her sey duzelmis olurdu.
                if (State == WinwsState.Running && !_starting)
                {
                    SetState(WinwsState.Faulted);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Erken olum kontrolu: winws gecersiz bir arguman aldiginda ya da
            // surucuyu acamadiginda hemen cikiyor. Bunu burada yakalamazsak
            // arayuz "calisiyor" gosterir ve kullanici korundugunu saniir.
            if (process.WaitForExit(StartupGraceMilliseconds))
            {
                // Cikis kodundan SONRA kisa bir bekleme: cikti okuma geri
                // cagrilari ayri bir is parcaciginda geliyor ve surec olduktan
                // hemen sonra bakarsak son satirlari kacirabiliyoruz.
                process.WaitForExit();

                var exitCode = process.ExitCode;
                process.Dispose();
                throw BuildEarlyExitException(exitCode, ilkSatirlar);
            }

            _process = process;
            CurrentArguments = arguments;
        }

        SetState(WinwsState.Running);
    }

    private List<string> _startupLines = [];
    private TaskCompletionSource _captureStarted = new();

    /// <summary>winws'in yakalamayi baslattiginda yazdigi satir.</summary>
    private const string CaptureStartedLine = "capture is started";

    /// <summary>
    /// Surucu acma hatasinin gorunebilecegi en uzun pencere.
    /// </summary>
    /// <remarks>
    /// <see cref="Start"/> yalnizca ilk 250 ms'deki olumu yakaliyor. Surucu
    /// yuklemesi yavas bir makinede (virusten koruma surucuyu tararken) daha uzun
    /// surebiliyor ve o zaman winws "calisiyor" sayilip birkac yuz milisaniye sonra
    /// sessizce oluyordu: parametre testi aday adina zaman asimi yaziyor, yani
    /// motor hatasi DPI engeli gibi gorunuyordu. Normalde bekleme winws
    /// "capture is started" dedigi anda bitiyor.
    /// </remarks>
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// winws'i baslatir; surucu cekirdekte takili kaldigi icin acilamazsa surucuyu
    /// bosaltip BIR KEZ yeniden dener.
    /// </summary>
    /// <remarks>
    /// Butun baslatma yollari (arayuzun "Baslat"i, parametre testi, CLI) bunu
    /// cagirmali. Gerekcesi <see cref="WinDivertDriver"/>'da: eskiden bu durumda
    /// her aday ayni hatayla dusuyor ve tek cikis yolu bilgisayari yeniden
    /// baslatmakti.
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
            // Pencere kapandiktan hemen sonra olmusse Exited olayi "Faulted"i
            // kacirmis olabilir; normal yola birak.
            try
            {
                if (process.HasExited && State == WinwsState.Running)
                {
                    SetState(WinwsState.Faulted);
                }
            }
            catch (InvalidOperationException)
            {
                // Bu arada StopAsync sureci birakti.
            }

            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
            {
                // Bu arada biri durdurdu; hata degil.
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

    /// <summary>winws'in erken olumunu, soylediklerine gore siniflandirilmis bir hataya cevirir.</summary>
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

        if (WinDivertDriver.IsOpenFailure(kopya))
        {
            var kod = WinDivertDriver.ParseWin32Error(kopya);
            return new WinDivertOpenException(
                kod,
                WinDivertDriver.Explain(kod) + $" winws {exitCode} koduyla kapandi." + soyledigi,
                WinDivertDriver.IsRecoverable(kod));
        }

        // "1" neredeyse her zaman TEK bir seyi anlatiyor: winws ayni
        // filtreyle zaten calisiyor ve ikinci ornegi reddediyor
        // ("A copy of winws is already running with the same filter").
        // En sik sebebi otomatik baslatma servisinin acik olmasi.
        // Ciplak "1 koduyla kapandi" mesaji kullaniciya hicbir sey
        // soylemiyordu; gercek bir kullanici bu duvara tosladi.
        var ipucu = exitCode == 1
            ? " En olasi sebep: winws zaten calisiyor (otomatik baslatma servisi acik" +
              " olabilir ya da onceki bir kosum surmus olabilir). Ayni filtreyle ikinci" +
              " bir ornek baslatilamaz."
            : string.Empty;

        return new InvalidOperationException(
            $"winws baslar baslamaz {exitCode} koduyla kapandi." + ipucu + soyledigi);
    }

    /// <summary>
    /// Argumanlari winws'in kendisine dogrulatir; surucuye DOKUNMAZ.
    /// </summary>
    /// <returns>
    /// Gecerliyse null, degilse winws'in yazdigi hata metni.
    /// </returns>
    /// <remarks>
    /// winws'in <c>--dry-run</c> secenegi "parametreleri dogrula ve basariliysa 0
    /// ile cik" diyor. Test motoru icin bu buyuk kazanc: gecersiz bir aday tam
    /// baslatma + ag zaman asimi (~7 sn) yerine bir surec baslatma (~50 ms)
    /// maliyetiyle eleniyor, ustelik "zaman asimi" gibi bilgisiz bir sonuc yerine
    /// winws'in kendi hata mesajini aliyoruz.
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

        // Hem \r hem \n ayraci: winws ciktisi CRLF kullaniyor ve yalnizca \n ile
        // bolmek her satirin sonunda gorunmez bir \r birakirdi.
        var message = (stderr + stdout)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !line.StartsWith("github version", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(message)
            ? $"winws parametreleri reddetti (kod {process.ExitCode})"
            : message;
    }

    /// <summary>
    /// winws'in acilista yazdigi surum satirindan surumu cikarir.
    /// </summary>
    /// <remarks>
    /// Satir su bicimde geliyor: <c>github version v72.12 (5cc46a98...)</c>.
    /// Eslesmezse null doner -- tahmin etmektense bilmemek daha iyi.
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
    /// Baslatmadan sonra erken olumu yakalamak icin beklenen sure.
    /// </summary>
    /// <remarks>
    /// Kisa tutuldu: test motoru her aday icin bir winws baslatiyor, dolayisiyla
    /// buradaki her milisaniye aday sayisiyla carpiliyor.
    /// </remarks>
    private const int StartupGraceMilliseconds = 250;

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    /// <summary>
    /// winws'i durdurur. Zaten durmussa sessizce doner.
    /// </summary>
    /// <param name="gracePeriod">
    /// Nazik kapatma icin taninan sure. Dolarsa surec oldurulur -- winws'in asili
    /// kalmasi WinDivert surucusunun da asili kalmasi demek, bu yuzden beklemeyi
    /// suresiz birakmiyoruz.
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

        // Durumu once degistiriyoruz ki Exited olayi bunu hata sanmasin.
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
                    // Nazik yol tutmadi. Konsol uygulamasi oldugu icin bu beklenen durum.
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Surec zaten olmus; yapacak bir sey yok.
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
