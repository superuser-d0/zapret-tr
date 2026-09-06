using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.App.ViewModels;

/// <summary>Arayuzun ust bandinda gosterilen genel durum.</summary>
public enum AppStatus
{
    NotReady,
    Ready,
    Running,
    Paused,
    Testing,
    Faulted,
}

/// <summary>Strateji secim kutusundaki tek bir satir.</summary>
public sealed record StrategyChoice(string Id, string Display, string Args, CandidateSource Source)
{
    /// <summary>Bu strateji bizim testimizde dogrulandi mi.</summary>
    public bool IsVerified => Source == CandidateSource.Verified;

    public override string ToString() => Display;
}

/// <summary>ISP secim kutusundaki tek bir satir. Profili null olan satir "bilmiyorum".</summary>
public sealed record IspChoice(IspProfile? Profile, string Display)
{
    public override string ToString() => Display;
}

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly VendorPaths? _vendor;
    private readonly ProfileStore? _profiles;
    private readonly WinwsRunner? _runner;
    private CancellationTokenSource? _testCancellation;

    private AppStatus _status = AppStatus.NotReady;
    private string _statusHeadline = "BASLATILIYOR";
    private string _statusDetail = string.Empty;
    private IspChoice? _selectedIsp;
    private StrategyChoice? _selectedStrategy;
    private bool _isBusy;
    private double _progressFraction;
    private string _progressText = string.Empty;
    private bool _isProgressVisible;
    private bool _isLogExpanded;
    private string _customTarget = string.Empty;

    public MainViewModel()
    {
        StartCommand = new RelayCommand(StartAsync, () => CanStart);
        PauseCommand = new RelayCommand(PauseAsync, () => Status == AppStatus.Running);
        TestCommand = new RelayCommand(RunTestAsync, () => !IsBusy && IsReady);
        CancelTestCommand = new RelayCommand(CancelTestAsync, () => Status == AppStatus.Testing);
        ResetCommand = new RelayCommand(ResetAsync, () => !IsBusy);
        ExitCommand = new RelayCommand(ExitAsync);

        try
        {
            _vendor = VendorPaths.Locate();
            _profiles = ProfileStore.Load();
            _runner = new WinwsRunner(_vendor);
            _runner.LogLineReceived += line => Append(line.Text, line.IsError);
            _runner.StateChanged += OnRunnerStateChanged;

            LoadIspChoices();
            SetStatus(AppStatus.Ready, "SISTEM HAZIR");
            Append("Profiller yuklendi: " + _profiles.Profiles.Count + " servis saglayicisi.");

            var missing = _vendor.FindMissingFiles();
            if (missing.Count > 0)
            {
                SetStatus(AppStatus.NotReady, "EKSIK DOSYA",
                    $"{missing.Count} upstream dosyasi eksik -- tools/fetch-upstream.ps1 calistirin.");
            }
        }
        catch (Exception ex)
        {
            // Kurulum eksikse uygulama acilmali ve NEDEN acilamadigini soylemeli;
            // sessizce coken bir pencere kullaniciya hicbir sey anlatmaz.
            SetStatus(AppStatus.NotReady, "KURULUM EKSIK", ex.Message);
            Append(ex.Message, isError: true);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- Komutlar ---------------------------------------------------------------

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ExitCommand { get; }

    // --- Baglanan ozellikler ----------------------------------------------------

    public ObservableCollection<IspChoice> IspChoices { get; } = [];

    public ObservableCollection<StrategyChoice> StrategyChoices { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public AppStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                Notify(nameof(IsReady));
                Notify(nameof(CanStart));
                Notify(nameof(StatusBrushKey));
                Notify(nameof(PrimaryButtonText));
                RefreshCommands();
            }
        }
    }

    public string StatusHeadline
    {
        get => _statusHeadline;
        private set => Set(ref _statusHeadline, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => Set(ref _statusDetail, value);
    }

    /// <summary>Durum bandinin rengi. XAML bunu kaynak anahtari olarak kullanir.</summary>
    public string StatusBrushKey => Status switch
    {
        AppStatus.Running => "StatusRunningBrush",
        AppStatus.Paused => "StatusPausedBrush",
        AppStatus.Testing => "StatusTestingBrush",
        AppStatus.Faulted or AppStatus.NotReady => "StatusFaultedBrush",
        _ => "StatusReadyBrush",
    };

    /// <summary>Ana dugmenin yazisi. Duraklatilmis durumdan devam etmek "baslat"tan farkli okunmali.</summary>
    public string PrimaryButtonText => Status switch
    {
        AppStatus.Running => "ZAPRET CALISIYOR",
        AppStatus.Paused => "DEVAM ET",
        _ => "ZAPRET'I BASLAT",
    };

    public IspChoice? SelectedIsp
    {
        get => _selectedIsp;
        set
        {
            if (Set(ref _selectedIsp, value))
            {
                LoadStrategyChoices();
            }
        }
    }

    public StrategyChoice? SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (Set(ref _selectedStrategy, value))
            {
                Notify(nameof(CanStart));
                Notify(nameof(IsSelectedStrategyUnverified));
                Notify(nameof(VerificationNote));
                UpdateStatusDetail();
                RefreshCommands();
            }
        }
    }

    /// <summary>
    /// Secili stratejinin henuz dogrulanmadigi. Rozet bunu gosterir.
    /// </summary>
    /// <remarks>
    /// Bu ayrimin arayuze cikmasi kasitli: profil verisinin buyuk kismi toplulukta
    /// bildirilmis ya da mekanizmadan turetilmis, bizim test etmedigimiz adaylardan
    /// olusuyor. Kullaniciya "bu calisiyor" demek ile "bu denenmeye deger" demek
    /// arasindaki farki gizlemek, ise yaramadiginda guveni tumden yikar.
    /// </remarks>
    public bool IsSelectedStrategyUnverified => SelectedStrategy is { IsVerified: false };

    public string VerificationNote => SelectedStrategy?.Source switch
    {
        CandidateSource.Verified => "Bu makinede dogrulandi.",
        CandidateSource.CommunityUnverified => "Toplulukta bildirildi, henuz dogrulanmadi.",
        CandidateSource.UpstreamPreset => "zapret ornek yapilandirmasindan, henuz dogrulanmadi.",
        CandidateSource.Hypothesis => "Mekanizmadan turetildi, henuz denenmedi.",
        _ => string.Empty,
    };

    public bool IsReady => Status is AppStatus.Ready or AppStatus.Running or AppStatus.Paused;

    public bool CanStart => Status is AppStatus.Ready or AppStatus.Paused && SelectedStrategy is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        private set => Set(ref _progressFraction, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => Set(ref _isProgressVisible, value);
    }

    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set => Set(ref _isLogExpanded, value);
    }

    /// <summary>Kullanicinin elle girdigi hedef. Bos birakilabilir.</summary>
    public string CustomTarget
    {
        get => _customTarget;
        set => Set(ref _customTarget, value);
    }

    public string EngineVersionText => "winws v72.13 · zapret-win-bundle";

    // --- Eylemler ---------------------------------------------------------------

    private async Task StartAsync()
    {
        if (_runner is null || _vendor is null || SelectedStrategy is null)
        {
            return;
        }

        try
        {
            if (_runner.IsRunning)
            {
                await _runner.StopAsync().ConfigureAwait(true);
            }

            var section = SectionOf(SelectedStrategy);
            var builder = new WinwsCommandBuilder(_vendor);
            var arguments = builder.BuildRuntimeCommand(
                new Dictionary<StrategySection, string> { [section] = SelectedStrategy.Args });

            Append("Baslatiliyor: " + WinwsCommandBuilder.ToDisplayString(arguments));
            _runner.Start(arguments);
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "BASLATILAMADI", ex.Message);
        }
    }

    private async Task PauseAsync()
    {
        if (_runner is null)
        {
            return;
        }

        await _runner.StopAsync().ConfigureAwait(true);
        SetStatus(AppStatus.Paused, "DURAKLATILDI", "Yapilandirma korundu.");
        Append("Duraklatildi. Ayarlar korundu.");
    }

    private async Task RunTestAsync()
    {
        if (_vendor is null || _profiles is null)
        {
            return;
        }

        // Test sirasinda winws kapali olmali: baseline taramasi mevcut durumu
        // olcecek, acik bir strateji olcumu kirletir.
        if (_runner is { IsRunning: true })
        {
            Append("Test icin winws gecici olarak durduruluyor.");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        _testCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressVisible = true;
        IsLogExpanded = true;
        SetStatus(AppStatus.Testing, "PARAMETRE TESTI", "Mevcut durum olculuyor...");

        try
        {
            var targets = ProbeTargetStore.Load(_profiles.Root).ToList();

            var custom = ProbeTargetStore.TryParseUserTarget(CustomTarget);
            if (custom is not null)
            {
                targets.Insert(0, custom);
                Append("Kendi hedefiniz eklendi: " + custom.Host);
            }

            var prober = new StrategyProber(_vendor, _profiles, targets);
            var progress = new Progress<ProbeProgress>(OnProbeProgress);

            var report = await prober
                .RunAsync(SelectedIsp?.Profile, progress, stopAtFirstSuccess: true, _testCancellation.Token)
                .ConfigureAwait(true);

            ReportResult(report);
        }
        catch (OperationCanceledException)
        {
            Append("Test iptal edildi.");
            SetStatus(AppStatus.Ready, "SISTEM HAZIR", "Test iptal edildi.");
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "TEST BASARISIZ", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
            _testCancellation?.Dispose();
            _testCancellation = null;
        }
    }

    private Task CancelTestAsync()
    {
        _testCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ResetAsync()
    {
        // Yikici islem: onaysiz calistirilmamali ve tam olarak ne yapacagi
        // onceden soylenmeli.
        var confirmation = MessageBox.Show(
            "Bu islem sunlari yapacak:\n\n" +
            "  • Calisan winws surecini durdurur\n" +
            "  • ZapretTR Windows servisini siler\n" +
            "  • WinDivert surucusunu kaldirir\n" +
            "  • Kaydedilmis yapilandirmayi ve ogrenilmis sonuclari siler\n" +
            "  • DNS onbellegini temizler\n\n" +
            "DNS ayarlariniza dokunulmaz.\n\nDevam edilsin mi?",
            "Tum ayarlari sifirla",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        IsLogExpanded = true;

        try
        {
            if (_runner is not null)
            {
                await _runner.StopAsync().ConfigureAwait(true);
            }

            var steps = await WinDivertCleanup.RunAsync().ConfigureAwait(true);
            foreach (var step in steps)
            {
                var mark = step.Succeeded ? "[+]" : "[!]";
                var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " -- " + step.Detail;
                Append($"{mark} {step.Description}{detail}", isError: !step.Succeeded);
            }

            SetStatus(AppStatus.Ready, "SIFIRLANDI", "Ilk kurulum durumuna donuldu.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExitAsync()
    {
        if (_runner is not null)
        {
            Append("Kapatiliyor, winws durduruluyor...");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        Application.Current?.Shutdown();
    }

    // --- Yardimcilar ------------------------------------------------------------

    private void LoadIspChoices()
    {
        if (_profiles is null)
        {
            return;
        }

        IspChoices.Clear();

        // "Bilmiyorum" birinci sinif secenek: kullanicilarin cogu ISS'ini teknik
        // adiyla bilmiyor, ve bilmiyor olmak testi engellememelidir.
        IspChoices.Add(new IspChoice(null, "Bilmiyorum / otomatik tespit et"));

        foreach (var profile in _profiles.Profiles)
        {
            IspChoices.Add(new IspChoice(profile, profile.DisplayName));
        }

        SelectedIsp = IspChoices.FirstOrDefault(c => c.Profile is not null);
    }

    private void LoadStrategyChoices()
    {
        StrategyChoices.Clear();

        var profile = SelectedIsp?.Profile;
        if (profile is null)
        {
            SelectedStrategy = null;
            UpdateStatusDetail();
            return;
        }

        foreach (var candidate in profile.Candidates.OrderBy(c => c.Section).ThenByDescending(c => c.Weight))
        {
            var badge = candidate.Source == CandidateSource.Verified ? "✓ " : string.Empty;
            var display = $"{badge}{SectionLabel(candidate.Section)} · {Describe(candidate.Args)}";
            StrategyChoices.Add(new StrategyChoice(candidate.Id, display, candidate.Args, candidate.Source));
        }

        SelectedStrategy = StrategyChoices.FirstOrDefault();
    }

    private void OnProbeProgress(ProbeProgress progress)
    {
        ProgressFraction = progress.Fraction;
        ProgressText = $"{progress.TierLabel} · {progress.Completed}/{progress.Total} · {progress.CurrentDescription}";
        StatusDetail = ProgressText;
    }

    private void ReportResult(ProbeReport report)
    {
        var blocked = report.Baseline.Count(b => b.Status == BaselineStatus.Blocked);
        Append($"Baseline: {blocked}/{report.Baseline.Count} hedef engelli.");

        foreach (var item in report.Baseline)
        {
            Append($"   {item.Target.Label}: {item.Status} ({item.Detail})");
        }

        if (blocked == 0)
        {
            SetStatus(AppStatus.Ready, "ENGEL BULUNAMADI",
                "Test hedeflerinin hepsi zaten aciliyor. Kendi hedefinizi girip tekrar deneyin.");
            Append("Hicbir hedef engelli degil. Bu durumda strateji testi anlamsiz olurdu -- " +
                   "acilmayan bir adres girip tekrar calistirin.");
            return;
        }

        if (report.IsEmpty)
        {
            SetStatus(AppStatus.Ready, "CALISAN STRATEJI YOK",
                $"{report.Attempts.Count} aday denendi, hicbiri acmadi.");
            Append($"{report.Attempts.Count} aday denendi, {report.Duration.TotalSeconds:F0} sn surdu. Sonuc yok.");
            return;
        }

        Append($"Sonuc ({report.Duration.TotalSeconds:F0} sn):");
        foreach (var winner in report.Winners)
        {
            Append($"   [+] {SectionLabel(winner.Section)}: {winner.Args}");
            Append($"       acilan: {string.Join(", ", winner.VerifiedCategories)}");

            // Kazanani secim listesine dogrulanmis olarak ekle.
            var choice = new StrategyChoice(
                winner.CandidateId,
                $"✓ {SectionLabel(winner.Section)} · {Describe(winner.Args)} (test edildi)",
                winner.Args,
                CandidateSource.Verified);

            StrategyChoices.Insert(0, choice);
            SelectedStrategy = choice;
        }

        SetStatus(AppStatus.Ready, "STRATEJI BULUNDU",
            $"{report.Winners.Count} bolum icin calisan parametre bulundu.");
    }

    private void OnRunnerStateChanged(WinwsState state)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case WinwsState.Running:
                    SetStatus(AppStatus.Running, "KORUMA AKTIF");
                    break;
                case WinwsState.Faulted:
                    SetStatus(AppStatus.Faulted, "BEKLENMEDIK DURUS",
                        "winws kendiliginden kapandi. Ayrintilar gunlukte.");
                    break;
                case WinwsState.Stopped:
                    if (Status == AppStatus.Running)
                    {
                        SetStatus(AppStatus.Ready, "SISTEM HAZIR");
                    }

                    break;
            }
        });
    }

    private void SetStatus(AppStatus status, string headline, string? detail = null)
    {
        Status = status;
        StatusHeadline = headline;

        if (detail is not null)
        {
            StatusDetail = detail;
        }
        else
        {
            UpdateStatusDetail();
        }
    }

    private void UpdateStatusDetail()
    {
        if (Status is AppStatus.Testing or AppStatus.NotReady or AppStatus.Faulted)
        {
            return;
        }

        var isp = SelectedIsp?.Profile?.DisplayName ?? "servis saglayicisi secilmedi";
        var strategy = SelectedStrategy is null ? "strateji yok" : Describe(SelectedStrategy.Args);
        StatusDetail = $"{isp} · {strategy}";
    }

    private void RefreshCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        TestCommand.RaiseCanExecuteChanged();
        CancelTestCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
    }

    private void Append(string text, bool isError = false)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {(isError ? "! " : string.Empty)}{text}";

        void Add()
        {
            LogLines.Add(line);

            // Gunluk sinirsiz buyumemeli: uzun bir test binlerce satir uretir ve
            // arayuz yavaslamaya baslar.
            while (LogLines.Count > 500)
            {
                LogLines.RemoveAt(0);
            }
        }

        if (Application.Current?.Dispatcher.CheckAccess() ?? true)
        {
            Add();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(Add);
        }
    }

    private StrategySection SectionOf(StrategyChoice choice)
    {
        var profile = SelectedIsp?.Profile;
        var match = profile?.Candidates.FirstOrDefault(c => c.Id == choice.Id);
        return match?.Section ?? StrategySection.Tcp443;
    }

    private static string SectionLabel(StrategySection section) => section switch
    {
        StrategySection.Tcp80 => "HTTP",
        StrategySection.Tcp443 => "HTTPS",
        StrategySection.Quic => "QUIC",
        StrategySection.DiscordVoice => "Discord ses",
        _ => section.ToString(),
    };

    /// <summary>Uzun arguman dizgisini secim kutusuna sigacak kisa bir ada cevirir.</summary>
    private static string Describe(string args)
    {
        var method = args.Split(' ')
            .FirstOrDefault(a => a.StartsWith("--dpi-desync=", StringComparison.Ordinal))
            ?.Replace("--dpi-desync=", string.Empty, StringComparison.Ordinal) ?? "?";

        var extras = new List<string>();
        if (args.Contains("md5sig", StringComparison.Ordinal))
        {
            extras.Add("md5sig");
        }

        if (args.Contains("badseq", StringComparison.Ordinal))
        {
            extras.Add("badseq");
        }

        var ttl = args.Split(' ').FirstOrDefault(a => a.StartsWith("--dpi-desync-ttl=", StringComparison.Ordinal));
        if (ttl is not null)
        {
            extras.Add("ttl" + ttl.Split('=')[1]);
        }

        return extras.Count == 0 ? method : $"{method} + {string.Join('+', extras)}";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Notify(propertyName);
        return true;
    }

    private void Notify([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
