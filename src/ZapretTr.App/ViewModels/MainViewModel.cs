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
    private string _statusHeadline = "BAŞLATILIYOR";
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
            SetStatus(AppStatus.Ready, "SİSTEM HAZIR");
            Append("Profiller yüklendi: " + _profiles.Profiles.Count + " servis sağlayıcısı.");

            var missing = _vendor.FindMissingFiles();
            if (missing.Count > 0)
            {
                SetStatus(AppStatus.NotReady, "EKSİK DOSYA",
                    $"{missing.Count} upstream dosyası eksik — tools/fetch-upstream.ps1 çalıştırın.");
            }
        }
        catch (Exception ex)
        {
            // Kurulum eksikse uygulama acilmali ve NEDEN acilamadigini soylemeli;
            // sessizce coken bir pencere kullaniciya hicbir sey anlatmaz.
            SetStatus(AppStatus.NotReady, "KURULUM EKSİK", ex.Message);
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
        AppStatus.Running => "ZAPRET ÇALIŞIYOR",
        AppStatus.Paused => "DEVAM ET",
        _ => "ZAPRET'İ BAŞLAT",
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
        CandidateSource.Verified => "Bu makinede doğrulandı.",
        CandidateSource.CommunityUnverified => "Toplulukta bildirildi, henüz doğrulanmadı.",
        CandidateSource.UpstreamPreset => "zapret örnek yapılandırmasından, henüz doğrulanmadı.",
        CandidateSource.Hypothesis => "Mekanizmadan türetildi, henüz denenmedi.",
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

            var winners = BuildRuntimeSelection();
            var builder = new WinwsCommandBuilder(_vendor);
            var arguments = builder.BuildRuntimeCommand(winners);

            Append("Başlatılıyor (" + winners.Count + " bölüm): " + WinwsCommandBuilder.ToDisplayString(arguments));
            _runner.Start(arguments);
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "BAŞLATILAMADI", ex.Message);
        }
    }

    /// <summary>
    /// Calistirilacak bolum -> strateji esleimesini kurar.
    /// </summary>
    /// <remarks>
    /// Kullanici listeden tek bir strateji seciyor ama yalnizca onu uygulamak dogru
    /// olmazdi: HTTPS stratejisiyle baslatmak QUIC ve Discord ses trafigini korumasiz
    /// birakir, ve tarayicilar QUIC'e kendiliginden dustugu icin kullanici "acilmadi"
    /// der. Bu yuzden secilen HTTPS stratejisinin yanina diger bolumlerin profildeki
    /// en yuksek agirlikli adaylari da ekleniyor -- upstream'in kendi preset'i de
    /// tam olarak boyle cok bolumlu.
    /// </remarks>
    private Dictionary<StrategySection, string> BuildRuntimeSelection()
    {
        var winners = new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = SelectedStrategy!.Args,
        };

        var profile = SelectedIsp?.Profile;
        if (profile is null)
        {
            return winners;
        }

        foreach (var section in new[] { StrategySection.Tcp80, StrategySection.Quic, StrategySection.DiscordVoice })
        {
            var best = profile.CandidatesFor(section).FirstOrDefault();
            if (best is not null)
            {
                winners[section] = best.Args;
            }
        }

        return winners;
    }

    private async Task PauseAsync()
    {
        if (_runner is null)
        {
            return;
        }

        await _runner.StopAsync().ConfigureAwait(true);
        SetStatus(AppStatus.Paused, "DURAKLATILDI", "Yapılandırma korundu.");
        Append("Duraklatıldı. Ayarlar korundu.");
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
            Append("Test için winws geçici olarak durduruluyor.");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        _testCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressVisible = true;
        IsLogExpanded = true;
        SetStatus(AppStatus.Testing, "PARAMETRE TESTİ", "Mevcut durum ölçülüyor...");

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
                .RunAsync(
                    SelectedIsp?.Profile,
                    progress,
                    stopAtFirstSuccess: true,
                    // Arayuzden calisan testte bolum basina butce koyuyoruz: Tier 3'un
                    // 180 adayini sonuna kadar denemek kullaniciyi belirsiz sure
                    // bekletir. Sinira takilirsa "daha genis ara" ayri bir eylem olmali.
                    maxCandidatesPerSection: 60,
                    cancellationToken: _testCancellation.Token)
                .ConfigureAwait(true);

            ReportResult(report);
        }
        catch (OperationCanceledException)
        {
            Append("Test iptal edildi.");
            SetStatus(AppStatus.Ready, "SİSTEM HAZIR", "Test iptal edildi.");
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "TEST BAŞARISIZ", ex.Message);
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
            "Bu işlem şunları yapacak:\n\n" +
            "  • Çalışan winws sürecini durdurur\n" +
            "  • ZapretTR Windows servisini siler\n" +
            "  • WinDivert sürücüsünü kaldırır\n" +
            "  • Kaydedilmiş yapılandırmayı ve öğrenilmiş sonuçları siler\n" +
            "  • DNS önbelleğini temizler\n\n" +
            "DNS ayarlarınıza dokunulmaz.\n\nDevam edilsin mi?",
            "Tüm ayarları sıfırla",
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

            SetStatus(AppStatus.Ready, "SIFIRLANDI", "İlk kurulum durumuna dönüldü.");
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
            Append("Kapatılıyor, winws durduruluyor...");
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

        // Yalnizca HTTPS adaylari listeleniyor. Kullanicinin "strateji" derken
        // kastettigi sey bu; diger bolumler (HTTP, QUIC, Discord ses) profilin en
        // yuksek agirlikli adaylariyla otomatik dolduruluyor. Dort bolumun adaylarini
        // tek bir listede karistirmak, kullanicinin farkinda olmadan yalnizca 80
        // portunu koruyan bir secim yapmasina yol aciyordu.
        foreach (var candidate in profile.CandidatesFor(StrategySection.Tcp443))
        {
            var badge = candidate.Source == CandidateSource.Verified ? "✓ " : string.Empty;
            StrategyChoices.Add(new StrategyChoice(
                candidate.Id, badge + Describe(candidate.Args), candidate.Args, candidate.Source));
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
        Append($"Mevcut durum: {blocked}/{report.Baseline.Count} hedef engelli.");

        foreach (var item in report.Baseline)
        {
            Append($"   {item.Target.Label}: {item.Status} ({item.Detail})");
        }

        if (blocked == 0)
        {
            SetStatus(AppStatus.Ready, "ENGEL BULUNAMADI",
                "Test hedeflerinin hepsi zaten açılıyor. Kendi hedefinizi girip tekrar deneyin.");
            Append("Hiçbir hedef engelli değil. Bu durumda strateji testi anlamsız olurdu — " +
                   "açılmayan bir adres girip tekrar çalıştırın.");
            return;
        }

        if (report.IsEmpty)
        {
            SetStatus(AppStatus.Ready, "ÇALIŞAN STRATEJİ YOK",
                $"{report.Attempts.Count} aday denendi, hiçbiri açmadı.");
            Append($"{report.Attempts.Count} aday denendi, {report.Duration.TotalSeconds:F0} sn sürdü. Sonuç yok.");
            return;
        }

        Append($"Sonuç ({report.Duration.TotalSeconds:F0} sn):");
        foreach (var winner in report.Winners)
        {
            Append($"   [+] {SectionLabel(winner.Section)}: {winner.Args}");
            Append($"       açılan: {string.Join(", ", winner.VerifiedCategories)}");

            // Kazanani secim listesine dogrulanmis olarak ekle.
            var choice = new StrategyChoice(
                winner.CandidateId,
                $"✓ {SectionLabel(winner.Section)} · {Describe(winner.Args)} (test edildi)",
                winner.Args,
                CandidateSource.Verified);

            StrategyChoices.Insert(0, choice);
            SelectedStrategy = choice;
        }

        SetStatus(AppStatus.Ready, "STRATEJİ BULUNDU",
            $"{report.Winners.Count} bölüm için çalışan parametre bulundu.");
    }

    private void OnRunnerStateChanged(WinwsState state)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case WinwsState.Running:
                    SetStatus(AppStatus.Running, "KORUMA AKTİF");
                    break;
                case WinwsState.Faulted:
                    SetStatus(AppStatus.Faulted, "BEKLENMEDİK DURUŞ",
                        "winws kendiliğinden kapandı. Ayrıntılar günlükte.");
                    break;
                case WinwsState.Stopped:
                    if (Status == AppStatus.Running)
                    {
                        SetStatus(AppStatus.Ready, "SİSTEM HAZIR");
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

        var isp = SelectedIsp?.Profile?.DisplayName ?? "servis sağlayıcısı seçilmedi";
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
