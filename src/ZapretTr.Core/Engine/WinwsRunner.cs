using System.Diagnostics;
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

        if (!File.Exists(_vendor.WinwsExe))
        {
            throw new FileNotFoundException(
                $"winws.exe bulunamadi: {_vendor.WinwsExe}. tools/fetch-upstream.ps1 calistirildi mi?",
                _vendor.WinwsExe);
        }

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

            process.OutputDataReceived += (_, e) => PublishLine(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => PublishLine(e.Data, isError: true);
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

            _process = process;
            CurrentArguments = arguments;
        }

        SetState(WinwsState.Running);
    }

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
