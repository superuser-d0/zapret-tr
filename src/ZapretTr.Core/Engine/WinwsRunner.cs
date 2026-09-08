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
                "winws calistirilamaz, eksik dosya(lar): " +
                string.Join(", ", missing.Select(Path.GetFileName)) +
                ". Depo kokunden 'tools/fetch-upstream.ps1' calistirin.",
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

            void Kaydet(string? satir)
            {
                if (string.IsNullOrWhiteSpace(satir))
                {
                    return;
                }

                lock (ilkSatirlar)
                {
                    // Ilk birkac satir yetiyor: winws sebebi hemen basta
                    // yaziyor, sonrasi paket gunlugu.
                    if (ilkSatirlar.Count < 5)
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
                if (State == WinwsState.Running)
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
                var exitCode = process.ExitCode;
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

                // Cikis kodundan SONRA kisa bir bekleme: cikti okuma geri
                // cagrilari ayri bir is parcaciginda geliyor ve surec olduktan
                // hemen sonra bakarsak son satirlari kacirabiliyoruz.
                process.WaitForExit();

                string soyledigi;
                lock (ilkSatirlar)
                {
                    soyledigi = ilkSatirlar.Count > 0
                        ? " winws: " + string.Join(" | ", ilkSatirlar)
                        : " (winws hicbir sey yazmadan cikti)";
                }

                throw new InvalidOperationException(
                    $"winws baslar baslamaz {exitCode} koduyla kapandi." + ipucu + soyledigi);
            }

            _process = process;
            CurrentArguments = arguments;
        }

        SetState(WinwsState.Running);
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
