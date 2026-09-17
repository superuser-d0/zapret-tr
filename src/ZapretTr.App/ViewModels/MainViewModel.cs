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

/// <summary>Arayüzün üst bandında gösterilen genel durum.</summary>
public enum AppStatus
{
    NotReady,
    Ready,
    Running,
    Paused,
    Testing,
    Faulted,
}

/// <summary>Strateji seçim kutusundaki tek bir satır.</summary>
public sealed record StrategyChoice(string Id, string Display, string Args, CandidateSource Source)
{
    /// <summary>Bu strateji bizim testimizde doğrulandı mı.</summary>
    public bool IsVerified => Source == CandidateSource.Verified;

    public override string ToString() => Display;
}

/// <summary>İSS seçim kutusundaki tek bir satır. Profili null olan satır "bilmiyorum".</summary>
public sealed record IspChoice(IspProfile? Profile, string Display)
{
    public override string ToString() => Display;
}

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly VendorPaths? _vendor;
    /// <summary>Yüklü profiller (kullanıcının doğrulamaları bindirilmiş).</summary>
    /// <remarks>
    /// readonly DEĞİL: "Tüm Ayarları Sıfırla" öğrenilmiş doğrulamaları siliyor ve
    /// listenin bellekte eski hâliyle kalması, silinmiş bir şeyin ekranda
    /// "doğrulandı" olarak görünmesi demek olurdu.
    /// </remarks>
    private ProfileStore? _profiles;
    private readonly WinwsRunner? _runner;

    /// <summary>winws.exe'nin içinden okunan sürüm; motor hiç çalışmamışken alt bilgi için.</summary>
    private readonly string? _embeddedWinwsVersion;
    private readonly DnsCryptRunner? _dnsRunner;
    private CancellationTokenSource? _testCancellation;

    /// <summary>Otomatik başlatma servisi parametre testi için durduruldu mu.</summary>
    private bool _serviceSuspendedForTest;

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
    private bool _isSecureDnsEnabled = true;
    private bool _isSecureDnsActive;
    private bool _isServiceInstalled;
    private bool _isServicePaused;

    /// <summary>
    /// Kayıtlı ayarlar geri yüklenirken true.
    /// </summary>
    /// <remarks>
    /// Bu bayrak olmadan geri yükleme kendi kendini bozuyordu: ilk atanan özelliğin
    /// setter'ı SaveSelection() çağırıyor, o an İSS ve strateji HENÜZ geri
    /// yüklenmemiş oluyor ve yapılandırma yarım hâliyle üzerine yazılıyordu. Sonuç:
    /// her açılışta ayarların bir kısmı sessizce kayboluyordu.
    /// </remarks>
    private bool _isRestoring;

    /// <summary>Temizlik bir kez koşulduysa tekrar koşmasın.</summary>
    private bool _shutdownCompleted;

    public MainViewModel()
    {
        StartCommand = new RelayCommand(StartAsync, () => CanStart);
        PauseCommand = new RelayCommand(PauseAsync, () => CanPause);
        TestCommand = new RelayCommand(RunTestAsync, () => !IsBusy && IsReady);
        CancelTestCommand = new RelayCommand(CancelTestAsync, () => Status == AppStatus.Testing);
        ResetCommand = new RelayCommand(ResetAsync, () => !IsBusy);
        ExitCommand = new RelayCommand(ExitAsync);
        ServiceCommand = new RelayCommand(ToggleServiceAsync, () => !IsBusy && IsReady);
        SaveReportCommand = new RelayCommand(SaveReportAsync);
        ReportIssueCommand = new RelayCommand(ReportIssueAsync);
        UpdateCommand = new RelayCommand(UpdateAsync, () => !IsBusy);
        ToggleThemeCommand = new RelayCommand(ToggleThemeAsync);

        try
        {
            _vendor = VendorPaths.Locate();

            // Kullanıcının kendi doğruladıkları dağıtım profillerinin üzerine
            // bindiriliyor: parametre testinde bulunan strateji bir sonraki
            // açılışta "doğrulanmış" olarak hazır gelsin.
            _profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
            _runner = new WinwsRunner(_vendor);
            _embeddedWinwsVersion = WinwsRunner.ReadEmbeddedVersion(_vendor.WinwsExe);
            _runner.LogLineReceived += line =>
            {
                Append(line.Text, line.IsError);

                // Sürüm satırı "çalışıyor" bildiriminden SONRA okunabiliyor; o durumda
                // alt bilgi bir sonraki durum değişikliğine kadar eski kalıyordu.
                if (WinwsRunner.ParseVersion(line.Text) is not null)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        Notify(nameof(EngineVersionText));
                        Notify(nameof(FooterText));
                    });
                }
            };
            _runner.StateChanged += OnRunnerStateChanged;

            _dnsRunner = new DnsCryptRunner(_vendor);

            // Çözümleyici dökümü SÜZÜLÜYOR. Süzülmezse açılıştaki 470 satırlık
            // sunucu listesi 500 satırlık günlüğü taşırıp her şeyi dışarı
            // atıyor; gerçek bir kullanıcının raporunda tam olarak bu oldu.
            // Gerekçesi ve neyin geçtiği DnsCryptRunner.IsNoise içinde.
            _dnsRunner.LogLineReceived += line =>
            {
                if (!DnsCryptRunner.IsNoise(line))
                {
                    Append(line);
                }
            };

            // Önceki çalışmada uygulama düzgün kapanmadıysa sistem DNS'i hâlâ
            // 127.0.0.1'i gösteriyor olabilir ve o durumda hiçbir ad çözülmez.
            // Kullanıcıya sormadan düzeltiyoruz: interneti yokken onay beklemek
            // yardım değil, engel.
            _ = RecoverDnsIfNeededAsync();

            LoadIspChoices();
            RestoreSavedSelection();
            _ = RefreshServiceStatusAsync();
            SetIdleStatus();
            Append("Profiller yüklendi: " + _profiles.Profiles.Count + " servis sağlayıcısı.");

            // Beklemiyoruz: ağ yavaşsa uygulamanın açılışını geciktirmesin.
            NotifyIfVersionChanged();


            var missing = _vendor.FindMissingFiles();
            if (missing.Count > 0)
            {
                // Kullanıcıya "fetch-upstream.ps1 çalıştırın" demek, kurulum
                // paketiyle gelen birine elinde olmayan bir depoda
                // kullanamayacağı bir komut vermekti. Kurulu bir makinede bu
                // dosyaların kaybolmasının en olası sebebi virüs programının
                // WinDivert sürücüsünü karantinaya alması.
                SetStatus(AppStatus.NotReady, "KURULUM DOSYALARI EKSİK",
                    $"{missing.Count} dosya eksik. Ayrıntılar ve çözüm günlükte.");

                Append(VendorPaths.MissingFilesAdvice(
                    string.Join(", ", missing.Select(Path.GetFileName))), isError: true);
                IsLogExpanded = true;
            }
        }
        catch (Exception ex)
        {
            // Kurulum eksikse uygulama açılmalı ve NEDEN açılamadığını söylemeli;
            // sessizce çöken bir pencere kullanıcıya hiçbir şey anlatmaz.
            SetStatus(AppStatus.NotReady, "KURULUM EKSİK", ex.Message);
            Append(ex.Message, isError: true);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- Komutlar ---------------------------------------------------------------

    public RelayCommand StartCommand { get; }

    /// <summary>Açık ve koyu tema arasında geçer.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>Tema düğmesinin yazısı: geçilecek temayı söyler.</summary>
    public string ThemeButtonText => ThemeManager.Current == AppTheme.Dark ? "Açık tema" : "Koyu tema";
    public RelayCommand PauseCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    private string? _updateMessage;

    /// <summary>Yeni sürüm varsa gösterilecek satır; yoksa <c>null</c>.</summary>
    public string? UpdateMessage
    {
        get => _updateMessage;
        private set
        {
            if (Set(ref _updateMessage, value))
            {
                Notify(nameof(HasUpdate));
            }
        }
    }

    /// <summary>Güncelleme satırı gösterilsin mi.</summary>
    public bool HasUpdate => !string.IsNullOrEmpty(UpdateMessage);

    /// <summary>Kullanıcı uygulamadan GERÇEKTEN çıkmak istedi.</summary>
    /// <remarks>
    /// Pencereyi kapatmak artık çıkış anlamına gelmiyor (bildirim alanına
    /// iniyor), bu yüzden niyetin ayrıca duyurulması gerekiyor.
    /// </remarks>
    public event EventHandler? ExitRequested;

    public RelayCommand ResetCommand { get; }

    /// <summary>Güncellemeyi denetler; varsa indirip kurar.</summary>
    public RelayCommand UpdateCommand { get; }

    /// <summary>Günlüğü ve ortam özetini bir dosyaya yazar.</summary>
    public RelayCommand SaveReportCommand { get; }

    /// <summary>Doldurulmuş hata bildirimi formunu tarayıcıda açar.</summary>
    public RelayCommand ReportIssueCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand ServiceCommand { get; }

    // --- Bağlanan özellikler ----------------------------------------------------

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

    /// <summary>Durum bandının rengi. XAML bunu kaynak anahtarı olarak kullanır.</summary>
    public string StatusBrushKey => Status switch
    {
        AppStatus.Running => "StatusRunningBrush",
        AppStatus.Paused => "StatusPausedBrush",
        AppStatus.Testing => "StatusTestingBrush",
        AppStatus.Faulted or AppStatus.NotReady => "StatusFaultedBrush",
        _ => "StatusReadyBrush",
    };

    /// <summary>Ana düğmenin yazısı. Duraklatılmış durumdan devam etmek "başlat"tan farklı okunmalı.</summary>
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
                RefreshIdlePresentation();
                RefreshCommands();
            }
        }
    }

    /// <summary>
    /// Seçili stratejinin henüz doğrulanmadığı. Rozet bunu gösterir.
    /// </summary>
    /// <remarks>
    /// Bu ayrımın arayüze çıkması kasıtlı: profil verisinin büyük kısmı toplulukta
    /// bildirilmiş ya da mekanizmadan türetilmiş, bizim test etmediğimiz adaylardan
    /// oluşuyor. Kullanıcıya "bu çalışıyor" demek ile "bu denenmeye değer" demek
    /// arasındaki farkı gizlemek, işe yaramadığında güveni tümden yıkar.
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

    /// <summary>
    /// Elle başlatma mümkün mü.
    /// </summary>
    /// <remarks>
    /// Servis kuruluysa winws ZATEN çalışıyor ve aynı filtreyle ikinci bir örnek
    /// başlatılamıyor; winws "A copy of winws is already running with the same
    /// filter" deyip 1 koduyla çıkıyor. Düğme yine de açıktı ve basan kullanıcı
    /// "winws başlar başlamaz 1 koduyla kapandı" diye anlamsız bir hata alıyordu.
    /// Gerçek bir kullanıcıda yaşandı.
    ///
    /// Koruma zaten açıksa basılacak bir düğme olmamalı.
    /// </remarks>
    private bool _isServiceStopped;

    /// <summary>Servis kurulu ama çalışmıyor: koruma yok.</summary>
    public bool IsServiceStopped
    {
        get => _isServiceStopped;
        private set => Set(ref _isServiceStopped, value);
    }

    public bool CanStart => Status is AppStatus.Ready or AppStatus.Paused
                            && !IsBusy
                            && (IsServicePaused || (SelectedStrategy is not null && !IsServiceInstalled));

    /// <summary>
    /// Duraklatma mümkün mü: uygulamanın kendi koruması çalışıyorsa YA DA otomatik
    /// başlatma servisi çalışıyorsa.
    /// </summary>
    /// <remarks>
    /// Eskiden yalnızca ilki vardı. Servis modunda düğme hep kapalıydı ve korumayı
    /// geçici olarak kapatmanın tek yolu ayarı silen "Otomatik Başlatmayı Kaldır"
    /// ya da "Tüm Ayarları Sıfırla" idi. VPN kullanmak isteyen gerçek bir kullanıcı
    /// tam olarak bu yüzden sıfırlamak zorunda kaldı; gerekçesi
    /// <see cref="ServiceManager.PauseAsync"/> içinde.
    /// </remarks>
    public bool CanPause => !IsBusy
                            && (Status == AppStatus.Running
                                || (Status is AppStatus.Ready or AppStatus.Faulted
                                    && ((IsServiceInstalled && !IsServicePaused) || _hasLeftovers)));

    /// <summary>
    /// Arkada ZapretTR'den kalan çalışan bir şey var mı: winws, dnscrypt ya da DNS
    /// yönlendirmesi. Varsa Duraklat, koruma "çalışıyor" görünmese de açık kalır.
    /// </summary>
    /// <remarks>
    /// Motor çöktüğünde ("BEKLENMEDİK DURUŞ") ya da önceki bir oturumdan kalıntı
    /// varken düğme kapalıydı; kullanıcının elinde yalnızca ayarı silen
    /// sıfırlama kalıyordu.
    /// </remarks>
    private bool _hasLeftovers;

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

    /// <summary>Kullanıcının elle girdiği hedef. Boş bırakılabilir.</summary>
    public string CustomTarget
    {
        get => _customTarget;
        set => Set(ref _customTarget, value);
    }

    /// <summary>Alt bilgideki motor sürümü.</summary>
    /// <remarks>
    /// Burada eskiden sabit olarak "winws v72.13" yazıyordu ve bu YANLIŞTI.
    /// Gerçek bir kullanıcının günlüğü motoru kendi ağzıyla ele verdi:
    /// <c>github version v72.12</c>. Sebebi tedarik zincirinde:
    /// <c>winws.exe</c> <c>zapret-win-bundle</c> deposundan bir COMMIT ile
    /// sabitleniyor (orada etiket yok); v72.13 yalnızca sahte yük dosyalarının ve
    /// filtre parçalarının geldiği <c>zapret</c> etiketi. Yani o numara hiçbir
    /// zaman winws'in sürümü değildi.
    ///
    /// Artık motor bir kez çalıştıysa ONUN söylediği gösteriliyor; hiç
    /// çalışmadıysa uydurmak yerine ikilinin nereden geldiği yazılıyor.
    /// </remarks>
    /// <remarks>
    /// Önce motorun çalışırken kendi bildirdiği sürüm, yoksa ikilinin içinden
    /// okunan. Yalnızca ilkine bakmak alt bilginin duruma göre değişmesine yol
    /// açıyordu; gerekçesi <see cref="WinwsRunner.ReadEmbeddedVersion"/>'da.
    /// </remarks>
    public string EngineVersionText =>
        ((_runner?.ReportedVersion ?? _embeddedWinwsVersion) is { } surum
            ? "winws " + surum
            : "winws (zapret-win-bundle)")
        + " · dnscrypt-proxy 2.1.18";

    /// <summary>Pencere başlığı: uygulamanın sürümüyle.</summary>
    /// <remarks>
    /// Uygulamanın KENDİ sürümü ekranda hiçbir yerde yazmıyordu; alt bilgide
    /// yalnızca motorun sürümü vardı. "Hangi sürümdesiniz" sorusunun cevabı ancak
    /// "Raporu Kaydet" dosyasında bulunuyordu; yani yeni sürümü indirdiğini
    /// söyleyen kullanıcının gerçekten onu çalıştırıp çalıştırmadığı bilinemiyordu.
    /// </remarks>
    public string WindowTitle => "ZapretTR " + ShortVersion(SurumMetni());

    /// <summary>Alt bilgi: uygulama, motor ve çözümleyici sürümleri tek satırda.</summary>
    public string FooterText => "ZapretTR " + ShortVersion(SurumMetni()) + " · " + EngineVersionText;

    /// <summary>
    /// "0.1.20+5cc46a98..." gibi bir sürüm metninden kullanıcıya gösterilecek kısmı alır.
    /// </summary>
    /// <remarks>
    /// Commit özeti rapora giriyor (teşhis için gerekli) ama başlıkta 40 karakterlik
    /// bir özet kullanıcıya bir şey söylemiyor.
    /// </remarks>
    public static string ShortVersion(string informationalVersion)
    {
        var arti = informationalVersion.IndexOf('+');
        return arti < 0 ? informationalVersion : informationalVersion[..arti];
    }

    /// <summary>
    /// Şifreli DNS kullanılsın mı. Varsayılan olarak AÇIK.
    /// </summary>
    /// <remarks>
    /// Varsayılanın açık olması ölçülmüş bir gerekçe taşıyor: bu makinede
    /// discord.com, pornhub.com ve xvideos.com'un üçü de sağlayıcının engel
    /// sunucusuna çözümleniyordu. O katman aşılmadan winws stratejisi hiçbir şey
    /// değiştirmiyor; trafik zaten gerçek sunucuya gitmiyor. Kapalı başlasaydı
    /// kullanıcıların çoğu "çalışmıyor" deyip bırakırdı.
    /// </remarks>
    public bool IsSecureDnsEnabled
    {
        get => _isSecureDnsEnabled;
        set
        {
            if (Set(ref _isSecureDnsEnabled, value))
            {
                RefreshIdlePresentation();
                SaveSelection();
            }
        }
    }

    /// <summary>Otomatik başlatma servisi kurulu mu.</summary>
    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        private set
        {
            if (Set(ref _isServiceInstalled, value))
            {
                Notify(nameof(ServiceButtonText));
                Notify(nameof(CanStart));
                RefreshCommands();

                // Servis kuruluysa koruma açılıştan itibaren zaten çalışıyor.
                // Kullanıcı bunu ekranda görmeli, yoksa "neden Başlat kapalı"
                // diye düşünür. Metnin kendisi SetIdleStatus içinde, tek yerde.
                RefreshIdlePresentation();
            }
        }
    }

    /// <summary>Otomatik başlatma servisi kullanıcı tarafından duraklatıldı mı.</summary>
    /// <remarks>
    /// Duraklatılmış servis <see cref="IsServiceInstalled"/> sayılıyor (kaldırma
    /// düğmesi görünür kalsın), ama koruma kapalı ve ana düğme onu geri açıyor.
    /// </remarks>
    public bool IsServicePaused
    {
        get => _isServicePaused;
        private set
        {
            if (Set(ref _isServicePaused, value))
            {
                RefreshCommands();
                RefreshIdlePresentation();
            }
        }
    }

    public string ServiceButtonText => IsServiceInstalled
        ? "Otomatik Başlatmayı Kaldır"
        : "Servis Olarak Yükle (Otomatik Başlat)";

    /// <summary>Şifreli DNS şu an gerçekten devrede mi.</summary>
    public bool IsSecureDnsActive
    {
        get => _isSecureDnsActive;
        private set => Set(ref _isSecureDnsActive, value);
    }

    // --- Eylemler ---------------------------------------------------------------

    private async Task StartAsync()
    {
        // Duraklatılmış servis KENDİ kayıtlı ayarıyla geri açılıyor; seçili
        // stratejiyle uygulama içinde ikinci bir koruma başlatılmıyor.
        if (IsServicePaused)
        {
            await ResumeServiceAsync().ConfigureAwait(true);
            return;
        }

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

            // Şifreli DNS ÖNCE açılıyor. Ters sırada yapılsaydı winws, hâlâ engel
            // sunucusuna giden bir trafiği kurcalamış olurdu; yani hiçbir şey.
            if (IsSecureDnsEnabled && _dnsRunner is not null && !_dnsRunner.IsRunning)
            {
                Append("Şifreli DNS başlatılıyor...");
                await _dnsRunner.StartAsync().ConfigureAwait(true);
                IsSecureDnsActive = true;
            }

            var winners = BuildRuntimeSelection();
            var builder = new WinwsCommandBuilder(_vendor);
            var alanlar = BuildHostlistDomains(winners);
            var arguments = builder.BuildRuntimeCommand(winners, alanlar);

            Append(alanlar.Count > 0
                ? $"Strateji yalnızca şu adreslere uygulanacak ({alanlar.Count}): {string.Join(", ", alanlar)}"
                : "UYARI: adres listesi boş; strateji BÜTÜN 80/443 trafiğine uygulanacak.");

            Append("Başlatılıyor (" + winners.Count + " bölüm): " + WinwsCommandBuilder.ToDisplayString(arguments));
            await _runner.StartAsync(arguments).ConfigureAwait(true);
            SaveSelection();

            // BU KORUMA YENİDEN BAŞLATMAYI ATLATMAZ VE BUNU SÖYLEMEK ZORUNDAYIZ.
            //
            // "Başlat" yalnızca bu oturumda bir winws süreci açıyor. Kullanıcı
            // bilgisayarı kapatıp açtığında geriye hiçbir şey kalmıyor: uygulama
            // kendiliğinden açılmıyor, winws çalışmıyor, koruma yok. Ekranda
            // bunu anlatan tek satır yoktu.
            //
            // Belirtisi tam olarak sahadan gelen cümle: "kurdum, çalıştı,
            // bilgisayarı yeniden başlattım, olmadı." Kullanıcı uygulamayı bir
            // kez ayarlanıp unutulacak bir şey sandı (ki doğru beklenti bu) ve
            // karşılığı olan düğme ("Servis Olarak Yükle") ekranda duruyordu ama
            // hiçbir yerde ÖNERİLMİYORDU.
            if (!IsServiceInstalled)
            {
                Append("NOT: bu koruma yalnızca şu an için geçerli. Bilgisayarı yeniden");
                Append("başlattığınızda kendiliğinden açılmaz. Kalıcı olması için");
                Append("\"Servis Olarak Yükle (Otomatik Başlat)\" düğmesini kullanın.");
            }

            // Kayıtlı strateji HÂLÂ çalışıyor mu. Beklemiyoruz: başlatma anında
            // bitmiş sayılır, doğrulama arkadan gelir.
            _ = VerifyAfterStartAsync();
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "BAŞLATILAMADI", ex.Message);

            // Yarım kalmış bir başlatma DNS'i bizde bırakmamalı: winws açılmadıysa
            // kullanıcının kazancı yok ama sistem DNS'i değiştirilmiş olur.
            await StopSecureDnsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Bağlantıdan servis sağlayıcıyı tespit edip seçmeyi dener.
    /// </summary>
    /// <remarks>
    /// Tespit BAŞARISIZ olursa test yine de çalışır, sadece genel aramadan başlar.
    /// Kullanıcıyı "önce İSS'ini seç" diye geri çevirmek, bilmeyen kullanıcıyı
    /// tam da yardım etmesi gereken yerde duvara toslatmak olurdu.
    ///
    /// Birden fazla profil eşleşirse (örneğin "vodafone" hem sabit hat hem mobil)
    /// seçim kullanıcıya bırakılıyor; birini sessizce seçmek yanlış profille
    /// dakikalarca test etmek demek olabilir.
    /// </remarks>
    private async Task TryDetectIspAsync()
    {
        if (_profiles is null)
        {
            return;
        }

        try
        {
            Append("Servis sağlayıcı tespit ediliyor...");

            using var detector = new IspDetector();
            var result = await detector.DetectAsync(_profiles).ConfigureAwait(true);

            if (result.Identity is null)
            {
                Append("Tespit edilemedi (bağlantı yok ya da sorgu servisleri erişilemiyor). "
                       + "Test genel aramayla devam edecek.");
                return;
            }

            Append($"Bağlantı: {result.Identity.OrgName ?? "(ad yok)"}"
                   + (result.Identity.Asn is { } asn ? $" · AS{asn}" : string.Empty)
                   + $" ({result.Identity.Source})");

            if (result.BestMatch is null)
            {
                Append("Bu sağlayıcı için profil yok; test genel aramayla devam edecek.");
                return;
            }

            if (result.IsAmbiguous)
            {
                var options = string.Join(
                    Environment.NewLine + "   ",
                    result.Matches.Select(m => m.DisplayName));

                var choice = MessageBox.Show(
                    "Bağlantınız birden fazla profille eşleşti:"
                    + Environment.NewLine + "   " + options
                    + Environment.NewLine + Environment.NewLine
                    + $"En olası olan \"{result.BestMatch.DisplayName}\" ile devam edilsin mi?"
                    + Environment.NewLine + Environment.NewLine
                    + "Hayır derseniz listeden kendiniz seçebilirsiniz.",
                    "Servis sağlayıcı tespiti",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (choice != MessageBoxResult.Yes)
                {
                    Append("Otomatik seçim reddedildi; listeden seçim bekleniyor.");
                    return;
                }
            }

            var match = IspChoices.FirstOrDefault(c => c.Profile?.Id == result.BestMatch.Id);
            if (match is not null)
            {
                SelectedIsp = match;
                SaveSelection();
                Append($"Servis sağlayıcı seçildi: {result.BestMatch.DisplayName}");
            }
        }
        catch (Exception ex)
        {
            // Tespit bir kolaylık; başarısızlığı testi engellememeli.
            Append("Tespit denemesi başarısız: " + ex.Message, isError: true);
        }
    }

    /// <summary>Kayıtlı seçimleri geri yükler.</summary>
    /// <remarks>
    /// Strateji önce kimlikle, bulunamazsa argümanla aranır. Kimlikler profil sürümleri
    /// arasında değişebiliyor; kullanıcının çalışan ayarının sırf kimlik eşleşmedi diye
    /// kaybolması kabul edilemez.
    /// </remarks>
    private void RestoreSavedSelection()
    {
        var config = ConfigStore.Load();

        _isRestoring = true;
        try
        {

            IsSecureDnsEnabled = config.SecureDnsEnabled;
            CustomTarget = config.CustomTarget ?? string.Empty;

            if (config.SelectedIspId is not null)
            {
                var isp = IspChoices.FirstOrDefault(c =>
                    string.Equals(c.Profile?.Id, config.SelectedIspId, StringComparison.OrdinalIgnoreCase));

                if (isp is not null)
                {
                    SelectedIsp = isp;
                }
            }

            var strategy = StrategyChoices.FirstOrDefault(c => c.Id == config.SelectedStrategyId)
                           ?? StrategyChoices.FirstOrDefault(c =>
                               string.Equals(c.Args, config.SelectedStrategyArgs, StringComparison.Ordinal));

            if (strategy is not null)
            {
                SelectedStrategy = strategy;
                Append("Kayıtlı ayarlar geri yüklendi.");
            }
        }
        finally
        {
            _isRestoring = false;
        }
    }

    /// <summary>Seçimleri diske yazar. Her değişiklikte değil, anlamlı anlarda çağrılır.</summary>
    /// <summary>
    /// Günlüğü ve ortam özetini kullanıcının seçtiği bir dosyaya yazar.
    /// </summary>
    /// <remarks>
    /// Bu düğme bir kolaylık değil, eksik bir kanaldı. Türksat Kablonet
    /// kullanıcısı 0.1.6'nın çalıştığını bildirdi ama profil hâlâ
    /// doğrulanmamış durumda: HANGİ adayın kazandığını bilmiyoruz, çünkü
    /// kullanıcının günlüğü bize ulaştırmasının tek yolu pencereden metni elle
    /// seçip kopyalamaktı. Bildirim geldi, veri gelmedi.
    ///
    /// Rapor DIŞARI GÖNDERİLMİYOR: yalnızca diske yazılıyor, neyi paylaşacağına
    /// kullanıcı karar veriyor. Dosyanın başında ne içerdiği yazıyor ki
    /// paylaşmadan önce bilerek baksın.
    /// </remarks>
    /// <summary>Sürüm değiştiyse kullanıcıya durumu bildirir.</summary>
    /// <remarks>
    /// Güncellemeden sonra kayıtlı parametreleri SİLMİYORUZ. Bir sürüm
    /// değişikliği İSS'in DPI yapılandırmasını değiştirmiyor, dolayısıyla
    /// ölçülmüş strateji hâlâ geçerli; silmek kullanıcıyı her güncellemede
    /// dakikalarca sürecek yeni bir teste zorlardı ve güncellemekten
    /// çekinmesine yol açardı.
    ///
    /// Asıl endişe ("ya eski parametre artık çalışmıyorsa") zaten
    /// karşılanmış durumda: Başlat'tan birkaç saniye sonra hedefler
    /// gerçekten açılıyor mu diye ölçülüyor ve açılmıyorsa "ÇALIŞIYOR — AMA
    /// AÇMIYOR" deyip yeni bir test öneriliyor. Yani karar TAHMİNE değil
    /// ÖLÇÜME dayanıyor.
    /// </remarks>
    private void NotifyIfVersionChanged()
    {
        try
        {
            var config = ConfigStore.Load();
            var simdiki = SurumMetni().Split('+')[0];

            if (string.Equals(config.LastRunVersion, simdiki, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(config.LastRunVersion))
            {
                Append($"Sürüm değişti: {config.LastRunVersion} → {simdiki}");

                if (!string.IsNullOrWhiteSpace(config.SelectedStrategyArgs))
                {
                    Append("Kayıtlı stratejiniz korundu. Başlattığınızda gerçekten çalışıp");
                    Append("çalışmadığı ölçülecek; çalışmıyorsa yeni bir test önerilecek.");
                }
            }

            config.LastRunVersion = simdiki;
            ConfigStore.Save(config);
        }
        catch (Exception)
        {
            // Bilgilendirme amaçlı; başarısız olması uygulamayı etkilemez.
        }
    }

    /// <summary>Temayı değiştirir ve tercihi kaydeder.</summary>
    /// <remarks>
    /// Kayıt, SaveSelection gibi mevcut dosyanın ÜSTÜNE yapılıyor: diğer alanlar
    /// korunmalı. Kaydedilemezse tema yine değişiyor; yalnızca bir sonraki açılışta
    /// hatırlanmıyor.
    /// </remarks>
    private Task ToggleThemeAsync()
    {
        var yeni = ThemeManager.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeManager.Apply(yeni);

        // Durum bandının rengi dönüştürücüyle bir kez alınıyor; tema değişince
        // yeniden sorulmazsa bant eski temanın renginde kalır.
        Notify(nameof(StatusBrushKey));
        Notify(nameof(ThemeButtonText));

        try
        {
            var config = ConfigStore.Load();
            config.Theme = ThemeManager.ToConfigValue(yeni);
            ConfigStore.Save(config);
        }
        catch (Exception ex)
        {
            Append("Tema tercihi kaydedilemedi: " + ex.Message, isError: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Önceki güncellemelerden kalan kurulum paketlerini siler.</summary>
    /// <remarks>
    /// Açılışta koşuyor, çünkü güncellemeden hemen sonraki açılış tam da paketin
    /// artık gereksiz olduğu an. Ama o an kurulum hâlâ bitmemiş olabiliyor: uygulamayı
    /// kurulumun son sayfası başlatıyor ve kurulum kendi dosyasını birkaç saniye daha
    /// kilitli tutuyor. Silinemeyen varsa bir kez daha deneniyor.
    ///
    /// Güncelleme o arada başladıysa (IsBusy) dokunulmuyor: indirilmekte ya da
    /// çalıştırılmak üzere olan paketi silmek güncellemeyi bozardı.
    ///
    /// KURUCUDAN ÇAĞRILMIYOR; App.OnStartup çağırıyor. Kurucudan çağrıldığında test
    /// paketi (pencere ve görünüm modelini kuran duman testleri) geliştiricinin
    /// GERÇEK %TEMP%\ZapretTR-guncelleme klasörünü boşalttı; 2026-09-14'te
    /// bu makinede altı paket böyle silindi. Silen bir işlem yalnızca uygulama
    /// gerçekten açıldığında koşmalı.
    /// </remarks>
    public async Task DeleteOldUpdatePackagesAsync()
    {
        try
        {
            for (var deneme = 0; deneme < 2; deneme++)
            {
                if (deneme > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                }

                if (IsBusy)
                {
                    return;
                }

                var sonuc = await Task
                    .Run(() => UpdateDownloader.DeleteOldInstallers(UpdateDownloader.DefaultDirectory))
                    .ConfigureAwait(true);

                if (sonuc.Deleted > 0)
                {
                    Append($"Eski güncelleme paketleri silindi: {sonuc.Deleted} dosya, "
                           + $"{sonuc.Bytes / (1024 * 1024)} MB.");
                }

                if (sonuc.Skipped == 0)
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Temizlik; başarısız olması uygulamayı etkilemez. Paketler bir sonraki
            // açılışta ya da güncellemede yine denenir.
        }
    }

    /// <summary>Yayımlanmış daha yeni bir sürüm var mı diye bakar.</summary>
    /// <remarks>
    /// Günde birkaç sürüm çıkabiliyor ve her seferinde kullanıcılara tek tek
    /// "şunu kur" demek gerekiyordu; kullanıcıya ulaşmayan bir düzeltme işe
    /// yaramıyor. Bu, uygulamanın DIŞARI istek yapan tek yeri: GitHub'a
    /// yalnızca "en son sürüm ne" sorusu gidiyor, başka hiçbir şey değil.
    /// Ayardan kapatılabilir.
    /// </remarks>
    /// <remarks>
    /// KURUCUDAN ÇAĞRILMIYOR; App.OnStartup çağırıyor. Kurucudan çağrıldığında test
    /// paketindeki her pencere ve görünüm modeli kurulumu GitHub'a gerçek bir sorgu
    /// atıyordu; GitHub oturumsuz sorguları IP başına saatte 60 ile sınırlıyor ve
    /// test koşumları aynı makinedeki uygulamanın sınırını da tüketiyordu.
    /// </remarks>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            if (!ConfigStore.Load().UpdateCheckEnabled)
            {
                return;
            }

            var latest = await UpdateChecker
                .GetLatestVersionAsync(TimeSpan.FromSeconds(8))
                .ConfigureAwait(true);

            if (!UpdateChecker.IsNewer(latest, SurumMetni()))
            {
                return;
            }

            UpdateMessage = $"Yeni sürüm var: {latest}";
            Append($"Yeni sürüm yayınlandı: {latest} (kurulu: {SurumMetni().Split('+')[0]})");
            Append("İndirme: " + UpdateChecker.ReleasesPage);
        }
        catch (Exception)
        {
            // Güncelleme kontrolü bir kolaylık, korumanın parçası değil:
            // başarısız olması kullanıcıya hata olarak gösterilmemeli.
        }
    }

    /// <summary>
    /// "Güncellemeleri Denetle": yeni sürüm varsa indirip kurulumu başlatır.
    /// </summary>
    /// <remarks>
    /// Kullanıcılar her sürümde tarayıcı açıp dosyayı bulmak zorundaydı ve bu,
    /// düzeltmenin kullanıcıya ulaşmasındaki en büyük sürtünmeydi.
    ///
    /// İndirilen paket ÇALIŞTIRILACAĞI için SHA256 doğrulaması atlanmıyor
    /// (UpdateDownloader yapıyor) ve kurulum kullanıcı ONAYLAMADAN başlamıyor:
    /// uygulamanın kendi kendine ikili çalıştırması, kullanıcının bilmesi
    /// gereken bir şey.
    /// </remarks>
    private async Task UpdateAsync()
    {
        try
        {
            IsBusy = true;
            Append("Güncellemeler denetleniyor...");

            var sorgu = await UpdateChecker
                .CheckLatestAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(true);

            if (sorgu.Version is not { } latest)
            {
                var sebep = sorgu.Failure ?? "Sürüm bilgisi alınamadı.";
                Append("Güncellemeler denetlenemedi: " + sebep, isError: true);

                // PENCEREYLE SÖYLE. Eskiden yalnızca günlüğe yazılıyordu; Ayrıntılar
                // kapalıyken düğmeye basan kullanıcı ekranda hiçbir değişiklik
                // görmüyor ve düğmenin bozuk olduğunu düşünüyordu (2026-09-14,
                // gerçek kullanıcı). "Güncel" sonucu zaten pencereyle söyleniyordu,
                // başarısızlık söylenmiyordu.
                MessageBox.Show(
                    "Güncellemeler denetlenemedi." + Environment.NewLine + Environment.NewLine +
                    sebep + Environment.NewLine + Environment.NewLine +
                    "Yeni sürüm olup olmadığına tarayıcıdan da bakabilirsiniz:" + Environment.NewLine +
                    UpdateChecker.ReleasesPage,
                    "Güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var kurulu = SurumMetni().Split('+')[0];

            if (!UpdateChecker.IsNewer(latest, kurulu))
            {
                Append($"En güncel sürümü kullanıyorsunuz ({kurulu}).");
                UpdateMessage = null;

                // Sonucu SÖYLEMEK gerekiyor. Eskiden yalnızca günlüğe
                // yazılıyordu ve Ayrıntılar paneli varsayılan olarak kapalı:
                // kullanıcı düğmeye basıyor, ekranda hiçbir şey değişmiyor ve
                // düğmenin çalışıp çalışmadığını bilmiyordu.
                MessageBox.Show(
                    $"Yeni güncelleme bulunamadı." + Environment.NewLine + Environment.NewLine +
                    $"En güncel sürümü kullanıyorsunuz: {kurulu}",
                    "Güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var onay = MessageBox.Show(
                $"Yeni sürüm var: {latest}" + Environment.NewLine +
                $"Kurulu sürüm: {kurulu}" + Environment.NewLine + Environment.NewLine +
                "İndirilip kurulsun mu?" + Environment.NewLine + Environment.NewLine +
                "Paket GitHub'dan indirilir, SHA256 özeti doğrulanır ve kurulum" +
                Environment.NewLine +
                "başlatılır. Kurulum sırasında ZapretTR kapanacak.",
                "Güncelleme",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (onay != MessageBoxResult.Yes)
            {
                Append("Güncelleme iptal edildi.");
                return;
            }

            Append($"{latest} indiriliyor...");

            var klasor = UpdateDownloader.DefaultDirectory;
            var sonOnluk = -1;
            var ilerleme = new Progress<int>(yuzde =>
            {
                // Her bayt için satır yazmak günlüğü kullanılamaz hâle getiriyor;
                // onar onar yeter.
                if (yuzde / 10 > sonOnluk)
                {
                    sonOnluk = yuzde / 10;
                    Append($"  indiriliyor: %{yuzde}");
                }
            });

            var paket = await UpdateDownloader
                .DownloadAsync(latest, klasor, ilerleme)
                .ConfigureAwait(true);

            Append("İndirildi ve SHA256 özeti doğrulandı.");
            Append("Kurulum başlatılıyor: " + paket);

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(paket) { UseShellExecute = true });

            // Kurulum çalışan uygulamayı kapatmak zorunda (dosyalar kilitli).
            // Kendimiz çıkarsak kullanıcı "neden kapandı" diye sormaz.
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Append("Güncelleme başarısız: " + ex.Message, isError: true);
            Append("Yayın sayfasından elle indirebilirsiniz: " + UpdateChecker.ReleasesPage);

            // İndirme ya da özet doğrulaması başarısızsa da kullanıcı bunu görmeli;
            // yoksa "İndir ve Kur"a bastı ve hiçbir şey olmadı.
            MessageBox.Show(
                "Güncelleme yapılamadı." + Environment.NewLine + Environment.NewLine +
                ex.Message + Environment.NewLine + Environment.NewLine +
                "Paketi yayın sayfasından elle indirebilirsiniz:" + Environment.NewLine +
                UpdateChecker.ReleasesPage,
                "Güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveReportAsync()
    {
        try
        {
            var ad = $"zapret-tr-rapor-{DateTime.Now:yyyy-MM-dd-HHmm}.txt";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = ad,
                DefaultExt = ".txt",
                Filter = "Metin dosyasi (*.txt)|*.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Title = "Raporu kaydet",
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, BuildReport()).ConfigureAwait(true);
            Append("Rapor kaydedildi: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            // Kaydetme başarısız olsa bile uygulama çalışmaya devam etmeli:
            // rapor bir teşhis aracı, korumanın parçası değil.
            Append("Rapor kaydedilemedi: " + ex.Message, isError: true);
        }
    }

    /// <summary>Hata bildirimi için ortam özetini toplar.</summary>
    public IssueDetails BuildIssueDetails() => new(
        AppVersion: SurumMetni(),
        EngineVersion: EngineVersionText,
        Windows: Environment.OSVersion.Version.ToString(),
        Isp: SelectedIsp?.Display ?? string.Empty,
        Strategy: SelectedStrategy?.Display ?? string.Empty,
        StrategyArgs: SelectedStrategy?.Args ?? string.Empty,
        SecureDns: IsSecureDnsEnabled,
        ServiceState: IsServiceStopped
            ? "kurulu ama durmuş"
            : IsServiceInstalled ? "kurulu" : "kurulu değil",
        Status: StatusHeadline,
        LogLines: [.. LogLines]);

    /// <summary>Doldurulmuş hata bildirimi formunu tarayıcıda açar.</summary>
    /// <remarks>
    /// Buradan HİÇBİR ŞEY GÖNDERİLMİYOR: tarayıcıda form açılıyor, gönderme
    /// kararı kullanıcının. Önce onay kutusu çıkıyor, çünkü açılan sayfada
    /// kullanıcının hattı ve denediği parametreler yazıyor olacak; bunu
    /// habersiz yapmak, güncelleme denetimindeki tutumumuzla çelişirdi.
    /// </remarks>
    private Task ReportIssueAsync()
    {
        try
        {
            var onay = MessageBox.Show(
                "GitHub'da bir hata bildirimi formu açılacak. Sürüm, servis sağlayıcı, "
                + "seçili parametre ve günlüğün son satırları form için önceden doldurulmuş "
                + "olacak.\n\n"
                + "Hiçbir şey gönderilmez: formu okuyup istemediğiniz satırı silebilir, "
                + "sonra kendiniz gönderebilirsiniz. Göndermek için GitHub hesabı gerekir.\n\n"
                + "Devam edilsin mi?",
                "Hata bildir",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (onay != MessageBoxResult.Yes)
            {
                return Task.CompletedTask;
            }

            var adres = IssueReporter.BuildUrl(BuildIssueDetails());

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(adres) { UseShellExecute = true });

            Append("Hata bildirimi formu tarayıcıda açıldı. Gönderme kararı sizin.");
            Append("Günlüğün tamamı gerekirse \"Raporu Kaydet\" ile kaydedip konuya ekleyin.");
        }
        catch (Exception ex)
        {
            // Tarayıcı açılamadı diye kullanıcıyı bildirimsiz bırakmayalım:
            // konu listesinin adresini günlüğe yazıp elle gitmesini sağlayalım.
            Append("Form açılamadı: " + ex.Message, isError: true);
            Append("Bildirimi elle açabilirsiniz: " + IssueReporter.IssuesPage);
        }

        return Task.CompletedTask;
    }

    /// <summary>Rapor metnini kurar.</summary>
    /// <remarks>
    /// Ortam özeti günlüğün ÖNÜNE konuyor. Günlük tek başına çoğu zaman
    /// yetmiyor: "şu aday çalıştı" satırını okuyup hangi profil ve hangi sürümle
    /// olduğunu bilmeden profile işleyemiyoruz.
    /// </remarks>
    public string BuildReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("ZapretTR raporu");
        sb.AppendLine("===============");
        sb.AppendLine();
        sb.AppendLine("Bu dosya hicbir yere gonderilmedi; yalnizca diske yazildi.");
        sb.AppendLine("Paylasmadan once icerigine bakin. Icinde sunlar var: kullandiginiz");
        sb.AppendLine("servis saglayici, denenen parametreler ve test edilen adresler.");
        sb.AppendLine("Genel IP adresiniz yazilmaz.");
        sb.AppendLine();

        sb.AppendLine("Tarih            : " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("ZapretTR         : " + SurumMetni());
        sb.AppendLine("Motor            : " + EngineVersionText);
        sb.AppendLine("Windows          : " + Environment.OSVersion.Version);
        sb.AppendLine();

        sb.AppendLine("Servis saglayici : " + (SelectedIsp?.Display ?? "(secilmedi)"));
        sb.AppendLine("Strateji         : " + (SelectedStrategy?.Display ?? "(secilmedi)"));
        sb.AppendLine("Parametre        : " + (SelectedStrategy?.Args ?? "-"));
        sb.AppendLine("Sifreli DNS      : " + (IsSecureDnsEnabled ? "acik" : "kapali"));
        sb.AppendLine("Otomatik baslatma: " + (IsServiceInstalled ? "kurulu" : "kurulu degil"));
        sb.AppendLine("Durum            : " + StatusHeadline);
        sb.AppendLine();

        // MAKİNENİN ÖLÇÜLEN DURUMU, görünüm modelinin bildikleri DEĞİL.
        //
        // Eski rapor yalnızca yukarıdaki alanları ve günlüğü taşıyordu. Bir
        // kullanıcı bilgisayarı yeniden başlatıp uygulamayı yeni açtıysa günlük
        // neredeyse boş oluyor ve rapor "çalışmadı" cümlesine hiçbir şey
        // ekleyemiyordu; oysa "olmadı" bildirimlerinde yanlış olan şey
        // genellikle yukarıdaki alanlarda değil, bu bölümde görünüyor: yetki,
        // eksik dosya, ölü servis, DNS'in bizde asılı kalması.
        sb.AppendLine("Makine durumu");
        sb.AppendLine("=============");
        sb.AppendLine();

        foreach (var satir in CollectEnvironmentLines())
        {
            sb.AppendLine(satir);
        }

        sb.AppendLine("Gunluk");
        sb.AppendLine("------");
        foreach (var satir in LogLines)
        {
            sb.AppendLine(satir);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Ortam özetini toplar; toplanamazsa raporu boşa düşürmez.
    /// </summary>
    /// <remarks>
    /// Bilerek eşzamanlı: <c>BuildReport</c> arayüz iş parçacığından çağrılıyor ve
    /// <see cref="EnvironmentReport"/> içindeki her bekleme
    /// <c>ConfigureAwait(false)</c> ile yazıldığı için geri çağrı arayüz kuyruğuna
    /// dönmüyor; yani kilitlenme yok. Süre birkaç sc.exe çağrısı kadar.
    /// </remarks>
    private static IReadOnlyList<string> CollectEnvironmentLines()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            return EnvironmentReport.CollectAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return ["(makine durumu toplanamadi: " + ex.Message + ")", string.Empty];
        }
    }

    private static string SurumMetni()
    {
        var asm = typeof(MainViewModel).Assembly;
        var bilgi = asm
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();

        return bilgi?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "bilinmiyor";
    }
    private void SaveSelection()
    {
        // Geri yükleme sırasında kaydetme: yarım durumu diske yazmak, kaydedilmiş
        // ayarların bir kısmını silmek demek.
        if (_isRestoring)
        {
            return;
        }

        try
        {
            // Mevcut dosyanın ÜSTÜNE yazılıyor, yerine değil. Eskiden her kayıt
            // sıfırdan bir AppConfig yazıyordu ve arayüzde karşılığı olmayan
            // alanlar sessizce siliniyordu: elle kapatılan güncelleme denetimi
            // (updateCheckEnabled) bir sonraki seçimde yeniden açılıyor,
            // lastRunVersion her seferinde null oluyordu; gerçek makinedeki
            // config.json'da öyleydi.
            var config = ConfigStore.Load();
            config.SelectedIspId = SelectedIsp?.Profile?.Id;
            config.SelectedStrategyId = SelectedStrategy?.Id;
            config.SelectedStrategyArgs = SelectedStrategy?.Args;
            config.SecureDnsEnabled = IsSecureDnsEnabled;
            config.CustomTarget = string.IsNullOrWhiteSpace(CustomTarget) ? null : CustomTarget;
            ConfigStore.Save(config);
        }
        catch (Exception ex)
        {
            // Kaydedememek çalışmayı engellememeli; yalnızca bir sonraki açılışta
            // ayarlar geri gelmez.
            Append("Ayarlar kaydedilemedi: " + ex.Message, isError: true);
        }
    }

    private async Task RefreshServiceStatusAsync()
    {
        try
        {
            var status = await ServiceManager.GetStatusAsync().ConfigureAwait(true);

            // "Kurulu" ile "çalışıyor" AYRI sorular. İkisini birbirine
            // karıştırmak kullanıcıyı kilitliyordu: servis kurulu ama durmuşsa
            // arayüz "SERVİS MODU AKTİF" deyip Başlat'ı kapatıyor, koruma yok
            // ve kullanıcının yapabileceği de bir şey yok. Gerçek bir
            // kullanıcıda 0.1.9'dan 0.1.15'e yükseltmeden sonra yaşandı.
            //
            // Bu durumda servisi "kurulu değil" sayıyoruz: böylece Başlat
            // AÇIK kalıyor ve kullanıcı korumasını elle başlatabiliyor.
            _hasLeftovers = HasLeftoverProcessesOrRedirect();
            IsServiceStopped = status.InstalledButStopped;
            IsServicePaused = status.WinwsPaused;
            IsServiceInstalled = status.WinwsInstalled && (status.WinwsRunning || status.WinwsPaused);
            SyncServicePausedSetting(status.WinwsPaused);
            RefreshCommands();

            if (status.InstalledButStopped)
            {
                Append("UYARI: otomatik başlatma servisi kurulu ama ÇALIŞMIYOR.", isError: true);
                Append("  Koruma şu anda kapalı. \"ZAPRET'İ BAŞLAT\" ile elle başlatabilir,");
                Append("  ya da \"Otomatik Başlatmayı Kaldır\" deyip yeniden kurabilirsiniz.");
                Append("  Sorun sürerse bilgisayarı bir kez yeniden başlatın.");
            }

            // Servis duraklatıldıysa ya da duraklatmadan çıktıysa bant da değişmeli.
            // Uygulamanın KENDİ duraklatmasına dokunulmuyor: onda servis kurulu değil.
            if (Status == AppStatus.Ready
                || (Status == AppStatus.Paused && IsServiceInstalled))
            {
                SetIdleStatus();
            }

            await WarnIfSecureDnsServiceIsDeadAsync(status).ConfigureAwait(true);
        }
        catch (Exception)
        {
            IsServiceInstalled = false;
            IsServiceStopped = false;
        }
    }

    private static bool HasLeftoverProcessesOrRedirect()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("winws").Length > 0
                   || System.Diagnostics.Process.GetProcessesByName("dnscrypt-proxy").Length > 0
                   || SystemDnsManager.HasBackup;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Yapılandırmadaki "servis duraklatıldı" bilgisini servisin gerçek durumuna eşitler.</summary>
    /// <remarks>
    /// Bilgi yalnızca yükseltmede okunuyor (bkz. <see cref="AppConfig.ServicePaused"/>).
    /// Her durum okumasında eşitleniyor ki servis başka bir yoldan açıldıysa ya da
    /// kaldırıldıysa güncelleme eski bir niyete göre davranmasın.
    /// </remarks>
    private void SyncServicePausedSetting(bool paused)
    {
        try
        {
            var config = ConfigStore.Load();
            if (config.ServicePaused != paused)
            {
                config.ServicePaused = paused;
                ConfigStore.Save(config);
            }
        }
        catch (Exception ex)
        {
            Append("Duraklatma bilgisi kaydedilemedi: " + ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Şifreli DNS servisi ölmüşse (ya da hiç yoksa ama sistem DNS'i hâlâ
    /// bizdeyse) kullanıcıyı uyarır.
    /// </summary>
    /// <remarks>
    /// Servis durumu bugüne kadar YALNIZCA winws için sorulmuştu. Oysa iki
    /// katmanlı korumada ikinci servis (<c>ZapretTR-DNS</c>) sessizce düşebiliyor
    /// ve sonucu winws'inkinden daha ağır:
    ///
    ///   * winws düşerse engelli siteler geri kapanır; can sıkıcı ama görünür.
    ///   * dnscrypt düşerse sistem DNS'i hâlâ 127.0.0.1'i gösteriyorken orada
    ///     dinleyen kimse kalmaz ve makine HİÇBİR adı çözemez. Kullanıcının
    ///     bunu anlatış biçimi de "internetim gitti" ya da sadece "olmadı".
    ///
    /// Bu durumda arayüz "SERVİS MODU AKTİF" diyordu, çünkü sorulan tek soru
    /// winws'in ayakta olup olmadığıydı ve winws gerçekten ayaktaydı.
    /// </remarks>
    private async Task WarnIfSecureDnsServiceIsDeadAsync(ServiceStatus status)
    {
        if (!SystemDnsManager.HasBackup)
        {
            return;
        }

        if (await DnsCryptRunner.IsLocalResolverRespondingAsync().ConfigureAwait(true))
        {
            return;
        }

        Append("UYARI: sistem DNS'i ZapretTR'ye yönlendirilmiş ama çözümleyici cevap vermiyor.",
            isError: true);
        Append("  Bu haldeyken hiçbir adres çözülemez — internet tamamen gitmiş gibi görünür.",
            isError: true);

        if (status.DnsInstalled)
        {
            Append("  Şifreli DNS servisi kurulu ama çalışmıyor. \"Otomatik Başlatmayı Kaldır\"");
            Append("  deyip yeniden kurmak ya da \"Tüm Ayarları Sıfırla\" bunu düzeltir.");
        }
        else
        {
            Append("  Şifreli DNS servisi kurulu değil; yönlendirme önceki bir oturumdan kalmış.");
            Append("  Uygulama bunu kendiliğinden geri almayı deniyor.");
        }
    }

    /// <summary>Otomatik başlatmayı kurar ya da kaldırır.</summary>
    private async Task ToggleServiceAsync()
    {
        if (_vendor is null)
        {
            return;
        }

        // SESSİZ ÇIKIŞ YOK. Bu koşul eskiden hiçbir şey söylemeden geri
        // dönüyordu: strateji seçilmemiş bir kullanıcı "Servis Olarak Yükle"ye
        // basıyor, ekranda hiçbir şey değişmiyor ve düğmenin bozuk olduğunu
        // düşünüyordu. Kurulum sonrası ilk açılışta strateji listesi ZATEN boş
        // olduğu için bu, en olası yol.
        if (SelectedStrategy is null && !IsServiceInstalled)
        {
            MessageBox.Show(
                "Önce çalışan bir parametre bulunmalı.\n\n"
                + "\"PARAMETRE TESTİ YAP\" düğmesine basın; hattınıza uyan ayar bulunduktan "
                + "sonra otomatik başlatmayı kurabilirsiniz.",
                "Otomatik başlatma",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Append("Otomatik başlatma kurulamadı: seçili bir parametre yok. Önce parametre testi yapın.",
                isError: true);
            return;
        }

        IsBusy = true;
        IsLogExpanded = true;
        var servisYeniKuruldu = false;

        try
        {
            if (IsServiceInstalled)
            {
                var removed = await ServiceManager.UninstallAsync().ConfigureAwait(true);
                foreach (var step in removed)
                {
                    Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
                           + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
                           isError: !step.Succeeded);
                }

                Append("Otomatik başlatma kaldırıldı.");

                // Eskiden burada "bilgisayarı yeniden başlatın" öneriliyordu: sürücü
                // çekirdekte takılı kalırsa sonraki testte motor açılmıyordu. Motor
                // artık bu durumda sürücüyü kendisi boşaltıp yeniden deniyor
                // (WinwsRunner.StartAsync); kullanıcıya yeniden başlatma yükü
                // bindirmek gereksiz.
                SetStatus(AppStatus.Ready, "OTOMATİK BAŞLATMA KAPATILDI",
                    "Koruma şu an kapalı. \"ZAPRET'İ BAŞLAT\" ya da yeni bir parametre testiyle devam edebilirsiniz.");
            }
            else
            {
                var dnsNote = IsSecureDnsEnabled
                    ? """
                      Şifreli DNS de servis olarak kurulacak ve sistem DNS ayarınız kalıcı olarak
                      ona yönlendirilecek. Bu ayar, otomatik başlatmayı kaldırdığınızda geri alınır.


                      """
                    : string.Empty;

                var confirmation = MessageBox.Show(
                    """
                    ZapretTR Windows servisi olarak kurulacak ve bilgisayar her açıldığında
                    kendiliğinden çalışacak.

                    Şu anki ayarınız kullanılacak:

                    """
                    // Bu dala ancak strateji seçilmişken giriliyor; yukarıdaki
                    // kontrol seçimsiz kullanıcıyı mesajla geri çevirdi.
                    + $"   {Describe(SelectedStrategy!.Args)}"
                    + Environment.NewLine + Environment.NewLine
                    + dnsNote
                    + "Devam edilsin mi?",
                    "Otomatik başlatma",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                // UYGULAMANIN KENDİ KORUMASI ÖNCE DURMALI; SERVİS DEVRALACAK.
                //
                // Düğme koruma çalışırken de açık. Eskiden öyle kurulum yapıldığında
                // üç şey birden bozuluyordu: servisin winws'i aynı filtreyle ikinci
                // örnek olarak açılamıyordu; servisin dnscrypt'i 127.0.0.1:53'ü
                // uygulamanın dnscrypt'i tuttuğu için bağlayamıyordu (ama "cevap
                // veriyor mu" kontrolü UYGULAMANINKİNDEN cevap alıp geçiyordu); ve
                // DNS yedeği "uygulama" sahipliğiyle kalıyordu. Uygulama kapanınca
                // yönlendirmeyi geri alıyor, kendi dnscrypt'ini de kapatıyordu:
                // ekranda "SERVİS MODU AKTİF", arkada şifreli DNS yok.
                if (_runner?.IsRunning == true || _dnsRunner?.IsRunning == true)
                {
                    Append("Uygulamanın kendi koruması durduruluyor; servis devralacak...");

                    if (_runner is not null)
                    {
                        await _runner.StopAsync().ConfigureAwait(true);
                    }

                    await StopSecureDnsAsync().ConfigureAwait(true);
                }

                // Servise, arayüzün çalıştırdığı komutun AYNISI veriliyor. Ayrışırsa
                // kullanıcının test edip beğendiği şey ile açılışta çalışan şey
                // farklı olur.
                var winners = BuildRuntimeSelection();
                var builder = new WinwsCommandBuilder(_vendor);
                var arguments = builder.BuildRuntimeCommand(winners, BuildHostlistDomains(winners));

                var installed = await ServiceManager
                    .InstallAsync(_vendor, arguments, IsSecureDnsEnabled).ConfigureAwait(true);

                foreach (var step in installed)
                {
                    Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
                           + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
                           isError: !step.Succeeded);
                }

                Append("Otomatik başlatma kuruldu.");
                Append("Uygulamayı artık kapatabilirsiniz: koruma servis olarak çalışıyor");
                Append("ve bilgisayar her açıldığında kendiliğinden devreye giriyor.");
                Append("Yeniden başlatmanıza gerek yok; servis şu anda çalışıyor.");

                SetStatus(AppStatus.Ready, "SERVİS MODU AKTİF",
                    "Koruma servis olarak çalışıyor. Uygulamayı kapatabilirsiniz; " +
                    "bilgisayar açıldığında kendiliğinden devreye girer.");

                servisYeniKuruldu = true;
            }

            await RefreshServiceStatusAsync().ConfigureAwait(true);
            SaveSelection();

            // Servis gerçekten ayaktaysa hedefleri ölç. Beklemiyoruz: kurulum
            // bitmiş sayılır, doğrulama arkadan gelir; Başlat yolundaki gibi.
            if (servisYeniKuruldu && IsServiceInstalled)
            {
                _ = VerifyAfterStartAsync(viaService: true);
            }
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "SERVİS İŞLEMİ BAŞARISIZ", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Şifreli DNS'i kapatır ve sistem ayarını geri alır. Kapalıysa sessizce döner.</summary>
    private async Task StopSecureDnsAsync()
    {
        if (_dnsRunner is null)
        {
            return;
        }

        if (!_dnsRunner.IsRunning && !SystemDnsManager.HasBackup)
        {
            return;
        }

        await _dnsRunner.StopAsync().ConfigureAwait(true);
        IsSecureDnsActive = false;
    }

    /// <summary>
    /// Önceki çalışmadan kalan DNS yönlendirmesini temizler.
    /// </summary>
    /// <remarks>
    /// Uygulama düzgün kapanmadıysa sistem DNS'i hâlâ 127.0.0.1'i gösteriyor ama
    /// dnscrypt-proxy çalışmıyor olabilir. O durumda makine HİÇBİR adı çözemez.
    /// Açılışta sessizce düzeltiyoruz.
    /// </remarks>
    private async Task RecoverDnsIfNeededAsync()
    {
        try
        {
            if (!SystemDnsManager.HasBackup)
            {
                return;
            }

            var responding = await DnsCryptRunner.IsLocalResolverRespondingAsync().ConfigureAwait(true);

            // YÖNLENDİRMEYİ SERVİS YAPTIYSA HEMEN KARAR VERME.
            //
            // Kullanıcıların büyük kısmı uygulamayı açılıştan hemen sonra açıyor
            // ve o an ZapretTR-DNS servisi HENÜZ ayağa kalkmamış olabiliyor:
            // dnscrypt-proxy önce ağı bekliyor, sonra çözümleyici listesini
            // çekiyor. Tek bir denemeye bakıp "ölmüş" saymak, çalışır durumdaki
            // bir kurulumun şifreli DNS'ini sessizce sökmek demekti; üstelik
            // yedek de silindiği için geri dönüşü yoktu. Kullanıcının gördüğü
            // şey "bir süre sonra engeller geri geldi" oluyordu.
            //
            // Uygulamanın kendi yönlendirmesinde bekleme yok: uygulama yeni
            // açılıyorsa onu yapan önceki oturum zaten kapanmış demektir.
            if (!responding && SystemDnsManager.IsOwnedByService)
            {
                Append("Sistem DNS'i servise yönlendirilmiş; şifreli DNS servisi bekleniyor...");
                responding = await WaitForLocalResolverAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);

                if (!responding)
                {
                    Append("Şifreli DNS servisi 20 saniyede cevap vermedi; yönlendirme geri alınıyor.",
                        isError: true);
                }
            }

            var recovered = await SystemDnsManager.TryRecoverAsync(responding).ConfigureAwait(true);

            if (recovered.Count > 0)
            {
                Append("Önceki oturumdan kalan DNS yönlendirmesi geri alındı: "
                       + string.Join(", ", recovered));
            }
        }
        catch (Exception ex)
        {
            Append("DNS kurtarma denemesi başarısız: " + ex.Message, isError: true);
        }
    }

    /// <summary>127.0.0.1:53 cevap verene kadar bekler; süre dolarsa false.</summary>
    private static async Task<bool> WaitForLocalResolverAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await DnsCryptRunner.IsLocalResolverRespondingAsync().ConfigureAwait(true))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        }

        return false;
    }

    /// <summary>
    /// Çalıştırılacak bölüm -> strateji eşleşmesini kurar.
    /// </summary>
    /// <remarks>
    /// Kural <see cref="RuntimeSelection"/> içinde: sorunu olmayan yere dokunma.
    /// Kullanıcının seçtiği HTTPS stratejisi ve yalnızca DOĞRULANMIŞ diğer bölümler
    /// uygulanır. Denenmemiş bir strateji çalışan trafiğe uygulanmaz; gerçek bir
    /// koşumda bunun bedeli ölçüldü: sorunsuz çalışan QUIC bağlantısı, üzerine
    /// denenmemiş bir QUIC stratejisi uygulanınca bozuldu.
    /// </remarks>
    private Dictionary<StrategySection, string> BuildRuntimeSelection()
        => RuntimeSelection.Build(CurrentProfile(), SelectedStrategy!.Args);

    /// <summary>
    /// Seçili sağlayıcının, en son öğrenilmiş doğrulamalar bindirilmiş profili.
    /// </summary>
    /// <remarks>
    /// <see cref="SelectedIsp"/> listedeki kaydı tutuyor ve o kayıt uygulama açılırken
    /// yüklenmiş profili gösteriyor. Test yeni bir şey doğrulayınca diske yazılıyordu ama
    /// bellekteki profil ESKİ kalıyordu. Ölçüldü (2026-09-16, 0.2.6, Türk Telekom): test
    /// "açılan: discord-guncelleme, discord, roblox" dedi, hemen ardından Başlat
    /// "yalnızca şu adreslere uygulanacak (6): discord…" dedi; roblox.com yoktu, doğrulama
    /// "Açılmayanlar: www.roblox.com" diye uyardı. Uygulama yeniden açılana kadar yeni
    /// doğrulama hiçbir yere yansımıyordu; bu 0.2.2'den beri her kategori için böyleydi.
    /// Çalışma zamanı kararları (hangi bölümler, hangi adresler) bu yüzden buradan okunur.
    /// </remarks>
    private IspProfile? CurrentProfile()
    {
        var secili = SelectedIsp?.Profile;
        if (secili is null || _profiles is null)
        {
            return secili;
        }

        return _profiles.Profiles.FirstOrDefault(p =>
                   string.Equals(p.Id, secili.Id, StringComparison.OrdinalIgnoreCase))
               ?? secili;
    }

    /// <summary>
    /// Stratejinin uygulanacağı alan adları. Boş dönerse strateji bütün trafiğe uygulanır.
    /// </summary>
    /// <remarks>
    /// Issue #1 (Vodafone Net, 2026-09-15): kazanan 443 stratejisi bütün 443 trafiğine
    /// uygulanıyordu ve GitHub çalışmıyordu. Artık yalnızca engelli ölçülmüş
    /// kategorilerin adreslerine ve kullanıcının kendi hedefine dokunuluyor.
    /// </remarks>
    private IReadOnlyList<string> BuildHostlistDomains(
        IReadOnlyDictionary<StrategySection, string> winners)
    {
        if (_profiles is null)
        {
            return [];
        }

        // Test yapmadan doğrudan Başlat'a basan kullanıcı da uyarılmalı: kutudaki
        // adres anlaşılmıyorsa koruma onu KAPSAMIYOR. Test yolundaki uyarı burada
        // görünmez, çünkü test hiç koşmamış olabilir.
        if (HostlistStore.DescribeUnusableTarget(CustomTarget) is { } sorun)
        {
            Append(sorun, isError: true);
        }

        var kategoriler = RuntimeSelection.VerifiedCategories(CurrentProfile(), winners);

        return HostlistStore.Load(_profiles.Root).DomainsFor(kategoriler, CustomTarget);
    }

    /// <summary>
    /// Başlatmadan sonra kayıtlı stratejinin GERÇEKTEN işe yaradığını ölçer.
    /// </summary>
    /// <remarks>
    /// Kullanıcı bir kez test yapıp stratejiyi kaydediyor ve sonraki açılışlarda
    /// doğrudan Başlat'a basıyor; test tekrar koşmuyor. Ama engelleme değişebilir:
    /// İSS'in DPI yapılandırması güncellenir ve dün çalışan parametre bugün çalışmaz.
    /// O durumda arayüz "ÇALIŞIYOR" gösteriyordu ve kullanıcı korunduğunu sanıyordu.
    ///
    /// Burada yalnızca tcp443 hedefleri ölçülüyor: üçü de birkaç saniye sürüyor ve
    /// başlatma akışını bekletmiyor. HİÇBİRİ açılmıyorsa strateji artık işe
    /// yaramıyor demektir ve kullanıcıya yeni bir test önerilir.
    ///
    /// Doğrulama bir KOLAYLIK: kendisi hata verirse başlatma bozulmamalı, çünkü
    /// winws zaten çalışıyor ve ölçümün başarısızlığı korumanın başarısızlığı değil.
    ///
    /// Servis kurulumundan sonra da çağrılıyor. Eskiden yalnızca "Başlat" yolunda
    /// koşuyordu; servis kuran kullanıcının ekranında "SERVİS MODU AKTİF" yazıyordu
    /// ama servisin gerçekten hedefleri açtığı hiç ölçülmemişti. Üstelik servis
    /// yolu en çok kullanılan yol: README kullanıcıya tam olarak onu öneriyor.
    /// </remarks>
    /// <param name="viaService">
    /// Koruma otomatik başlatma servisiyle mi çalışıyor. O yolda durum değeri
    /// <see cref="AppStatus.Ready"/> kalıyor (Başlat düğmesi servis varken kapalı),
    /// yani "hâlâ çalışıyor mu" sorusu farklı soruluyor.
    /// </param>
    private async Task VerifyAfterStartAsync(bool viaService = false)
    {
        if (_profiles is null)
        {
            return;
        }

        // Ölçüm süresince kullanıcı durdurmuş, test başlatmış ya da servisi
        // kaldırmış olabilir: o durumda "açmıyor" yazmak kafa karıştırır.
        bool HalaDevrede() => viaService
            ? Status == AppStatus.Ready && IsServiceInstalled && !IsServicePaused
            : Status == AppStatus.Running;

        var uyariDurumu = viaService ? AppStatus.Ready : AppStatus.Running;

        try
        {
            // Ağ yığınının oturması için kısa bir bekleme; hemen ölçmek yanlış
            // negatif üretiyor.
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

            // Bu arada winws çöktüyse (Faulted) veya kullanıcı durdurduysa ölçecek
            // bir şey yok: hatanın üzerine "açmıyor" yazmak kafa karıştırır.
            if (!HalaDevrede())
            {
                return;
            }

            var targets = ProbeTargetStore.Load(_profiles.Root)
                .Where(t => t.Section == StrategySection.Tcp443
                            && t.Category != StrategyProber.ControlCategory)
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            using var client = new HttpProbeClient();
            var acilan = new List<string>();
            var acilmayan = new List<string>();

            foreach (var target in targets)
            {
                var outcome = await StrategyProber
                    .ProbeAsync(target.Section, target.Host, null, client)
                    .ConfigureAwait(true);

                (outcome.Succeeded ? acilan : acilmayan).Add(target.Host);
            }

            if (!HalaDevrede())
            {
                return;
            }

            if (acilmayan.Count == 0)
            {
                Append($"Doğrulandı: {acilan.Count}/{targets.Count} hedef açılıyor.");
                return;
            }

            // HANGİ HEDEFİN AÇILMADIĞINI YAZ.
            //
            // Eskiden yalnızca sayı yazılıyordu ve yalnızca SIFIR açıldığında
            // uyarılıyordu. Gerçek bir kullanıcının raporunda sonuç şuydu:
            //
            //     Doğrulandı: 1/4 hedef açılıyor.
            //     Durum: KORUMA AKTİF
            //
            // O dört hedefin üçü Discord, biri YouTube; ve YouTube o hatta
            // zaten engelli değil. Yani açılan tek hedef muhtemelen hiçbir şey
            // gerektirmeyen hedefti ve Discord'un üçü de kapalıydı. Ekran yeşil
            // "KORUMA AKTİF" diyordu ve "Doğrulandı" kelimesini kullanıyordu.
            //
            // Bu, bu projedeki en pahalı hata sınıfı: kullanıcı korunduğunu
            // sanıyor. Üstelik hangi hedefin açılmadığı yazılmadığı için gelen
            // rapordan da anlaşılamıyordu; sayının kendisi teşhis vermiyor,
            // ADLAR veriyor.
            if (acilan.Count == 0)
            {
                Append("UYARI: kayıtlı strateji artık işe yaramıyor görünüyor —", isError: true);
                Append("test hedeflerinin hiçbiri açılmadı. Engelleme değişmiş olabilir.", isError: true);
                Append("Açılmayanlar: " + string.Join(", ", acilmayan), isError: true);
                Append("\"PARAMETRE TESTİ YAP\" ile yeni bir strateji aramanız önerilir.");

                SetStatus(uyariDurumu, "ÇALIŞIYOR — AMA AÇMIYOR",
                    "Kayıtlı strateji hedefleri açmadı; yeni bir parametre testi önerilir.");
                return;
            }

            Append($"UYARI: {targets.Count} hedeften yalnızca {acilan.Count} tanesi açılıyor.",
                isError: true);
            Append("Açılmayanlar: " + string.Join(", ", acilmayan), isError: true);
            Append("Koruma çalışıyor ama bu adresleri açmıyor. İlgili uygulama takılabilir");
            Append("(örneğin Discord giriş ya da güncelleme ekranında kalabilir).");
            Append("\"PARAMETRE TESTİ YAP\" ile yeni bir strateji aramanız önerilir.");

            SetStatus(uyariDurumu, "ÇALIŞIYOR — KISMEN AÇIYOR",
                $"{acilmayan.Count} hedef hâlâ açılmıyor: {string.Join(", ", acilmayan)}");
        }
        catch (Exception)
        {
            // Doğrulama yapılamadı. Sessiz geçiyoruz: winws çalışıyor ve ölçümün
            // kendi hatasını korumanın hatası gibi göstermek yanlış olur.
        }
    }
    /// <summary>
    /// "Duraklat": ZapretTR'nin arkada çalışan HER ŞEYİNİ durdurur, ayarı silmez.
    /// </summary>
    /// <remarks>
    /// Eskiden iki ayrı ve eksik yol vardı. Elle başlatılan korumada yalnızca
    /// uygulamanın kendi winws'i ile şifreli DNS'i kapanıyordu; gerçek makinede
    /// ölçüldü (2026-09-13) ki WinDivert sürücüsü Duraklat'tan 20 saniye sonra bile
    /// çekirdekte RUNNING kalıyordu, arkada kurulu bir servis varsa ona hiç
    /// dokunulmuyordu. Aynı kullanıcıda VPN koruma kapatıldıktan sonra da bağlanmadı
    /// ve ancak sıfırlamayla bağlandı. Artık Duraklat sıfırlamanın durdurduğu her şeyi
    /// durduruyor (<see cref="WinDivertCleanup.StopEverythingAsync"/>), yalnızca
    /// hiçbir şeyi silmiyor; ardından gerçekten bir şey kalıp kalmadığını ÖLÇÜP
    /// yazıyor.
    /// </remarks>
    private async Task PauseAsync()
    {
        IsBusy = true;
        IsLogExpanded = true;

        try
        {
            Append("Duraklatılıyor: koruma, şifreli DNS, servisler ve ağ sürücüsü kapatılıyor...");

            if (_runner is not null)
            {
                await _runner.StopAsync().ConfigureAwait(true);
            }

            await StopSecureDnsAsync().ConfigureAwait(true);
            AppendSteps(await WinDivertCleanup.StopEverythingAsync().ConfigureAwait(true));
            await RefreshServiceStatusAsync().ConfigureAwait(true);

            var kalan = await WinDivertCleanup.FindLeftoversAsync().ConfigureAwait(true);
            if (kalan.Count > 0)
            {
                Append("UYARI: duraklatmadan sonra hâlâ duranlar: " + string.Join("; ", kalan), isError: true);
                SetStatus(AppStatus.Faulted, "TAM DURAKLATILAMADI",
                    "Bazı parçalar durdurulamadı; ayrıntılar günlükte. Olmazsa \"Tüm Ayarları Sıfırla\".");
                return;
            }

            Append("Duraklatıldı. Arkada hiçbir şey kalmadı: winws ve dnscrypt çalışmıyor,");
            Append("ağ sürücüsü çekirdekte değil, sistem DNS'i eski hâlinde. Ayarlarınız silinmedi.");

            if (!IsServicePaused && SelectedStrategy is null)
            {
                // Devam ettirilecek bir koruma yok; yalnızca kalıntılar temizlendi.
                SetIdleStatus("Arkada kalan her şey durduruldu.");
                return;
            }

            SetStatus(AppStatus.Paused, "DURAKLATILDI",
                "Koruma, şifreli DNS ve ağ sürücüsü kapalı; DNS ayarınız eski hâlinde, VPN kullanabilirsiniz. "
                + "\"DEVAM ET\" ile kaldığı yerden sürer.");
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "DURAKLATILAMADI", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Duraklatılmış servisi kayıtlı ayarıyla geri açar.</summary>
    private async Task ResumeServiceAsync()
    {
        IsBusy = true;
        IsLogExpanded = true;

        try
        {
            Append("Otomatik başlatma servisi kaldığı yerden sürdürülüyor...");
            AppendSteps(await ServiceManager.ResumeAsync().ConfigureAwait(true));
            await RefreshServiceStatusAsync().ConfigureAwait(true);

            if (IsServiceInstalled && !IsServicePaused && !IsServiceStopped)
            {
                Append("Koruma yeniden açıldı; servis kurulduğu andaki ayarla çalışıyor.");
                _ = VerifyAfterStartAsync(viaService: true);
            }
            else
            {
                SetStatus(AppStatus.Faulted, "DEVAM EDİLEMEDİ",
                    "Servis yeniden başlatılamadı; ayrıntılar günlükte.");
            }
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "DEVAM EDİLEMEDİ", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendSteps(IEnumerable<CleanupStep> steps)
    {
        foreach (var step in steps)
        {
            Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
                   + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
                   isError: !step.Succeeded);
        }
    }

    private async Task RunTestAsync()
    {
        if (_vendor is null || _profiles is null)
        {
            return;
        }

        // Test sırasında winws kapalı olmalı: baseline taraması mevcut durumu
        // ölçecek, açık bir strateji ölçümü kirletir.
        if (_runner is { IsRunning: true })
        {
            Append("Test için winws geçici olarak durduruluyor.");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        // BAŞKA BİR DPI ARACI AÇIKSA ÖNCE ONU SÖYLE. WinDivert'i aynı anda iki araç
        // kullanamıyor; GoodbyeDPI açıkken winws paketleri göremiyor ve bütün adaylar
        // aynı şekilde düşüyor. Kullanıcının gördüğü şey "N aday denendi, hiçbiri
        // çalışmadı" oluyor; yani stratejiler kötü sanılıyor, oysa ölçüm hiç
        // yapılamamış. Testi engellemiyoruz; karar kullanıcının, ama körlemesine
        // 15 dakika beklemesin.
        await ScanAndOfferCleanupAsync().ConfigureAwait(true);

        // "Bilmiyorum" seçildiyse önce İSS'i tespit etmeyi dene. Profil bilinmeden
        // yapılan test Tier 1'i tamamen atlar ve doğrudan genel aramaya düşer;
        // yani kullanıcı tam da bu aracı hızlandıran şeyden mahrum kalır.
        if (SelectedIsp?.Profile is null)
        {
            await TryDetectIspAsync().ConfigureAwait(true);
        }

        _testCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressVisible = true;
        IsLogExpanded = true;
        SetStatus(AppStatus.Testing, "PARAMETRE TESTİ", "Mevcut durum ölçülüyor...");

        var yeniStratejiBulundu = false;
        try
        {
            await SuspendServiceForTestAsync().ConfigureAwait(true);

            var targets = ProbeTargetStore.Load(_profiles.Root).ToList();

            var custom = ProbeTargetStore.TryParseUserTarget(CustomTarget);
            if (custom is not null)
            {
                targets.Insert(0, custom);
                Append("Kendi hedefiniz eklendi: " + custom.Host);
            }
            else if (HostlistStore.DescribeUnusableTarget(CustomTarget) is { } sorun)
            {
                // Eskiden burada HİÇBİR ŞEY yoktu: anlaşılmayan girdi sessizce
                // düşüyordu ve kullanıcı testin kendi sitesini denediğini sanıyordu
                // (issue #1, KeremKuyucu). Sessiz reddetmek, çalışmayan bir özellikten
                // daha kötü: kullanıcı yanlış bir şey yaptığını bile bilmiyor.
                Append(sorun, isError: true);
            }

            // Şifreli DNS seçimi ÖLÇÜME de geçmeli. Geçmediği sürece hedefler sistem
            // DNS'iyle çözülüyordu ve Türkiye'de o katman çoğu zaman kaçırılmış
            // durumda: discord.com engel sunucusuna çözülüyor, ölçüm "engel sayfası"
            // görüyor, bölüm DnsRedirected işaretleniyor ve strateji aranmıyor.
            //
            // Gerçek makinede ölçüldü (TTNET, kurulum paketiyle): arayüz
            // "ENGEL BULUNAMADI" diyordu ve kullanıcıya "kendi hedefinizi girin"
            // öneriyordu; aynı hatta aynı anda CLI --doh ile 22 çalışan strateji
            // buluyordu. Yani ürünün ana yüzeyi, DNS kaçırması olan her hatta
            // (ki bu Türkiye'de olağan durum) kullanılamaz hâldeydi.
            var prober = new StrategyProber(_vendor, _profiles, targets, IsSecureDnsEnabled);
            var progress = new Progress<ProbeProgress>(OnProbeProgress);

            var report = await prober
                .RunAsync(
                    SelectedIsp?.Profile,
                    progress,
                    stopAtFirstSuccess: true,
                    // Arayüzden çalışan testte bölüm başına bütçe koyuyoruz: Tier 3'ün
                    // 180 adayını sonuna kadar denemek kullanıcıyı belirsiz süre
                    // bekletir. Sınıra takılırsa "daha geniş ara" ayrı bir eylem olmalı.
                    maxCandidatesPerSection: 60,
                    cancellationToken: _testCancellation.Token)
                .ConfigureAwait(true);

            ReportResult(report);
            yeniStratejiBulundu = report.Winners.Count > 0;
        }
        catch (OperationCanceledException)
        {
            Append("Test iptal edildi.");
            SetIdleStatus("Test iptal edildi.");
        }
        catch (ProbeEngineException ex)
        {
            // "Strateji bulunamadı" DEMEK DEĞİL. Motor hiç başlamadığı için
            // hiçbir aday ölçülemedi; ikisini aynı ekranda göstermek kullanıcıyı
            // yanlış yöne gönderiyordu ("demek bu hatta işe yaramıyor").
            Append("ÖLÇÜM YAPILAMADI: motor art arda hiç başlamadı.", isError: true);
            Append(ex.Message, isError: true);
            Append("Bu bir strateji sorunu değil; makinede motoru engelleyen bir şey var.");
            Append("Sırayla deneyin:");
            Append("  1. Bilgisayarı yeniden başlatın (takılı kalmış sürücü en sık sebep).");
            Append("  2. GoodbyeDPI gibi başka bir DPI aracı açıksa kapatın.");
            Append("  3. Testi yeniden çalıştırın.");

            SetStatus(AppStatus.Faulted, "ÖLÇÜM YAPILAMADI",
                "Motor başlamadığı için hiçbir strateji denenemedi. Ayrıntılar günlükte.");
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "TEST BAŞARISIZ", ex.Message);
        }
        finally
        {
            await ResumeServiceAfterTestAsync(yeniStratejiBulundu).ConfigureAwait(true);

            IsBusy = false;
            IsProgressVisible = false;
            _testCancellation?.Dispose();
            _testCancellation = null;
        }
    }

    /// <summary>
    /// Otomatik başlatma servisi çalışıyorsa test süresince durdurur.
    /// </summary>
    /// <remarks>
    /// Test yalnızca uygulamanın KENDİ başlattığı winws'i durduruyordu. Servis
    /// kuruluyken arkada ikinci bir winws çalışmaya devam ediyor ve ölçümü iki
    /// yerden bozuyordu: mevcut durum taraması servisin stratejisi açıkken
    /// yapıldığı için engel görünmüyordu ("ENGEL BULUNAMADI"), adaylar ise aynı
    /// filtreyle ikinci örnek olarak başlayamıyordu ("ÖLÇÜM YAPILAMADI").
    ///
    /// Servisin durumu burada YENİDEN soruluyor; açılıştaki önbelleğe
    /// güvenilmiyor, çünkü servis o zamandan beri kurulmuş ya da düşmüş olabilir.
    /// Kurulu ama zaten durmuş bir servise dokunulmuyor: geri başlatılacak bir
    /// şey yok ve test sonunda onu başlatmak kullanıcının görmediği bir değişiklik
    /// olurdu.
    /// </remarks>
    private async Task SuspendServiceForTestAsync()
    {
        ServiceStatus status;
        try
        {
            status = await ServiceManager.GetStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append("Servis durumu okunamadı: " + ex.Message, isError: true);
            return;
        }

        if (!status.WinwsRunning)
        {
            return;
        }

        Append("Otomatik başlatma servisi test boyunca durduruluyor; test bitince geri açılacak.");

        // Bayrak DURDURMADAN ÖNCE kaldırılıyor: durdurma yarıda kalsa bile servis
        // artık bizim elimizde ve test sonunda geri başlatılmalı.
        _serviceSuspendedForTest = true;

        var stopped = await ServiceManager
            .StopWinwsServiceAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);

        if (!stopped)
        {
            Append("UYARI: servis 15 saniyede durmadı ya da başka bir winws çalışıyor.", isError: true);
            Append("  Test sonucu güvenilir olmayabilir. Olmazsa \"Otomatik Başlatmayı Kaldır\"");
            Append("  deyip bilgisayarı yeniden başlatın ve testi öyle çalıştırın.");
        }
    }

    /// <summary>
    /// <see cref="SuspendServiceForTestAsync"/> ile durdurulan servisi geri açar.
    /// </summary>
    /// <remarks>
    /// Birden fazla yoldan çağrılabilir (testin sonu, uygulamadan çıkış); bayrak
    /// beklemeden ÖNCE indiriliyor ki servis iki kez başlatılmasın.
    ///
    /// Servis KURULDUĞU ANDAKİ ayarla geri gelir. Test yeni bir strateji bulduysa
    /// bu kendiliğinden servise geçmez; bunu kullanıcıya söylemek zorundayız, yoksa
    /// "test buldu ama hiçbir şey değişmedi" durumu yeniden ortaya çıkar.
    /// </remarks>
    /// <param name="newStrategyFound">Test çalışan bir parametre buldu mu.</param>
    private async Task ResumeServiceAfterTestAsync(bool newStrategyFound)
    {
        if (!_serviceSuspendedForTest)
        {
            return;
        }

        _serviceSuspendedForTest = false;

        var step = await ServiceManager.StartWinwsServiceAsync().ConfigureAwait(true);
        Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
               + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
               isError: !step.Succeeded);

        if (!step.Succeeded)
        {
            Append("Koruma şu anda kapalı. Bilgisayarı yeniden başlatınca servis kendiliğinden açılır.",
                isError: true);

            // Yalnızca başarısızlıkta tazeleniyor: başarılı yolda durum bandı
            // değişmemeli, yoksa test sonucu ("STRATEJİ BULUNDU") ekrandan
            // silinir ve yerine "SERVİS MODU AKTİF" yazar.
            await RefreshServiceStatusAsync().ConfigureAwait(true);
            return;
        }

        if (newStrategyFound)
        {
            Append("NOT: servis kurulduğu andaki ayarla çalışmaya devam ediyor. Yeni bulunan");
            Append("parametreyi kullanmak için \"Otomatik Başlatmayı Kaldır\", ardından");
            Append("\"Servis Olarak Yükle\" deyin.");
        }
    }

    /// <summary>
    /// Testten ÖNCE başka araçların kalıntılarını arar ve silinebilir olanlar
    /// için kullanıcıdan onay ister.
    /// </summary>
    /// <remarks>
    /// Burası eskiden yalnızca ÇALIŞAN sürece bakıyordu. O kontrol en sık
    /// karşılaşılan hâli (kapalı ama kurulu kalıntıyı) hiç görmüyordu:
    /// kullanıcı eski aracı kapatıyor, "kapattım" diyor, ama geride kalan servis
    /// kaydı açılışta geri geliyor ve WinDivert'i kapıyor. Sonuç, dışarıdan
    /// "hiçbir strateji çalışmadı" gibi görünen bir ölçüm. Gerçek bir
    /// kullanıcıda ölçüldü: 176 aday, 1105 saniye, sonuç yok.
    ///
    /// Silme AYRI bir karar ve kullanıcının: ne silineceği tek tek yazılıyor ve
    /// onaysız hiçbir şey silinmiyor. Silinebilir sayılan tek şey KAYIT:
    /// öksüz servisler ve sahipsiz sürücü kayıtları. Başka bir ürünün
    /// dosyalarına dokunulmuyor; bizi engelleyen şey dosyalar değil kayıt ve
    /// çalışan bir kurulumu bozmanın geri dönüşü yok.
    /// </remarks>
    private async Task ScanAndOfferCleanupAsync()
    {
        IReadOnlyList<ConflictFinding> bulgular;

        try
        {
            // hosts kontrolü için test hedeflerimizin adları veriliyor: DNS
            // zehirlenmesi tam olarak o adları başka bir adrese çevirir.
            string[] hedefler = _profiles is null
                ? []
                : ProbeTargetStore.Load(_profiles.Root)
                    .Select(t => t.Host)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            bulgular = await ConflictScanner.ScanAsync(hedefler).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Append("Çakışma taraması yapılamadı: " + ex.Message, isError: true);
            return;
        }

        if (bulgular.Count == 0)
        {
            Append("Çakışma taraması: temiz (başka DPI aracı, kalıntı servis ya da hosts kaydı yok).");
            return;
        }

        Append($"ÇAKIŞMA TARAMASI: {bulgular.Count} bulgu.", isError: true);
        foreach (var bulgu in bulgular)
        {
            Append("  • " + bulgu.Description, isError: true);
            if (!string.IsNullOrWhiteSpace(bulgu.Detail))
            {
                Append("    " + bulgu.Detail);
            }
        }

        var silinebilir = bulgular.Where(b => b.Removable).ToList();
        if (silinebilir.Count == 0)
        {
            Append("Bunların hiçbirini uygulama kendisi kaldıramaz; yukarıdaki açıklamalara bakın.");
            return;
        }

        var liste = string.Join(Environment.NewLine,
            silinebilir.Select(b => "   • " + b.Name + " — " + b.Description));

        var onay = MessageBox.Show(
            "Başka DPI atlatma araçlarından kalmış kayıtlar bulundu. Bunlar açılışta "
            + "ayağa kalkıp ağ sürücüsünü kapabiliyor ve ölçümün hiç yapılamamasına yol "
            + "açıyor.\n\n"
            + "Şunlar KALDIRILACAK:\n\n" + liste + "\n\n"
            + "Yalnızca Windows servis kayıtları siliniyor. Başka bir programın "
            + "dosyalarına dokunulmaz.\n\n"
            + "Kaldırılsın mı?",
            "Kalıntı temizliği",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (onay != MessageBoxResult.Yes)
        {
            Append("Kalıntı temizliği reddedildi; test mevcut haliyle devam ediyor.");
            return;
        }

        try
        {
            foreach (var step in await ConflictScanner.RemoveAsync(silinebilir).ConfigureAwait(true))
            {
                Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
                       + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
                       isError: !step.Succeeded);
            }

            Append("ÖNERİ: bilgisayarı bir kez yeniden başlatın, sonra testi çalıştırın.");
            Append("  Ağ sürücüsü çekirdekten hemen düşmüyor; kayıt silinse bile sürücü");
            Append("  görüntüsü yeniden başlatmaya kadar yüklü kalabiliyor.");
        }
        catch (Exception ex)
        {
            Append("Kalıntı temizliği başarısız: " + ex.Message, isError: true);
        }
    }

    private Task CancelTestAsync()
    {
        _testCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ResetAsync()
    {
        // Yıkıcı işlem: onaysız çalıştırılmamalı ve tam olarak ne yapacağı
        // önceden söylenmeli.
        //
        // Metin eskiden "DNS ayarlarınıza dokunulmaz" diyordu; aynı anda günlük
        // "Sistem DNS ayari geri alindi" yazıyordu. Davranış doğru (sıfırlama DNS'i
        // bizde bırakmamalı), yanlış olan metindi: kullanıcının KENDİ ayarına
        // dokunulmuyor, ZapretTR'nin yaptığı yönlendirme geri alınıyor.
        var confirmation = MessageBox.Show(
            "Bu işlem şunları yapacak:\n\n" +
            "  • Çalışan winws ve şifreli DNS süreçlerini durdurur\n" +
            "  • ZapretTR'nin Windows servislerini (otomatik başlatma ve şifreli DNS) siler\n" +
            "  • WinDivert sürücüsünü kaldırır\n" +
            "  • Kaydedilmiş yapılandırmayı ve öğrenilmiş sonuçları siler\n" +
            "  • DNS önbelleğini temizler\n\n" +
            "ZapretTR sistem DNS ayarınızı değiştirdiyse ayar, ZapretTR'den önceki hâline " +
            "döner. Kendi yaptığınız DNS ayarına dokunulmaz.\n\nDevam edilsin mi?",
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

            await StopSecureDnsAsync().ConfigureAwait(true);

            var steps = await WinDivertCleanup.RunAsync().ConfigureAwait(true);
            foreach (var step in steps)
            {
                var mark = step.Succeeded ? "[+]" : "[!]";
                var detail = string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " -- " + step.Detail;
                Append($"{mark} {step.Description}{detail}", isError: !step.Succeeded);
            }

            // Diski temizlemek YETMİYOR: profiller, seçili sağlayıcı ve
            // strateji BELLEKTE duruyordu. Kullanıcı "sıfırla" dedikten sonra
            // ekranda hâlâ eski İSS'i ve "✓ doğrulanmış" stratejiyi görüyordu;
            // silinmiş bir şeyin adı ekranda kalıyordu. Daha kötüsü: o hâliyle
            // Başlat'a basmak, artık diskte karşılığı olmayan bir seçimi
            // yeniden kaydediyordu.
            _profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());

            IsSecureDnsEnabled = true;
            CustomTarget = string.Empty;
            IsServiceInstalled = false;
            IsServiceStopped = false;
            IsServicePaused = false;
            UpdateMessage = null;

            LoadIspChoices();
            LoadStrategyChoices();

            Append("Yapılandırma, öğrenilmiş doğrulamalar ve seçimler silindi.");
            Append("Uygulama ilk kurulum durumuna döndü; yeni bir parametre testi gerekiyor.");

            SetStatus(AppStatus.Ready, "SIFIRLANDI",
                "İlk kurulum durumuna dönüldü. \"PARAMETRE TESTİ YAP\" ile yeniden başlayın.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// winws'i durdurur ve şifreli DNS'i geri alır. BÜTÜN çıkış yollarının ortak adımı.
    /// </summary>
    /// <remarks>
    /// Ayrı bir metot olmasının sebebi ölçülmüş bir hata: temizlik yalnızca "Çıkış"
    /// düğmesinin içindeydi, pencereyi X ile kapatmanın hiçbir işlevi yoktu. Gerçek
    /// makinede koruma açıkken pencere kapatıldığında winws ve dnscrypt-proxy öksüz
    /// kaldı ve sistem DNS'i 127.0.0.1'i göstermeye devam etti. Bu, projedeki en kötü
    /// sonuca açılan yol: dnscrypt sonradan ölürse (yeniden başlatma, Görev Yöneticisi,
    /// çökme) makine hiçbir adı çözemez. Kullanıcıların çoğu pencereyi X ile kapatır.
    ///
    /// Birden fazla kez çağrılabilir; ikinci çağrı hiçbir şey yapmaz.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        if (_shutdownCompleted)
        {
            return;
        }

        _shutdownCompleted = true;

        // Test sürerken çıkılıyorsa servis durdurulmuş hâlde kalmamalı: kaydı
        // "auto" olduğu için yeniden başlatmada geri gelirdi ama o zamana kadar
        // kullanıcı korumasız kalırdı.
        _testCancellation?.Cancel();
        await ResumeServiceAfterTestAsync(newStrategyFound: false).ConfigureAwait(true);

        if (_runner is not null)
        {
            Append("Kapatılıyor, winws durduruluyor...");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        // DNS geri alınmadan çıkmak, kullanıcıyı ad çözemez bir makineyle
        // bırakmak demek. Çıkış yolunda atlanabilecek bir adım değil.
        await StopSecureDnsAsync().ConfigureAwait(true);

        // SÜRÜCÜ DE ÇEKİRDEKTEN DÜŞMELİ.
        //
        // winws kapanınca WinDivert sürücüsü kendiliğinden düşmüyor; ölçüldü:
        // Duraklat'tan 20 saniye sonra hâlâ RUNNING. Uygulamayı kapatan kullanıcı
        // arkada hiçbir şey kalmadığını düşünüyor. Otomatik başlatma servisi
        // çalışıyorsa sürücü onun; dokunulmuyor.
        try
        {
            var status = await ServiceManager.GetStatusAsync().ConfigureAwait(true);
            if (!status.WinwsRunning)
            {
                await WinDivertDriver.TryUnloadIdleAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Çıkış bir sürücü hatası yüzünden takılmamalı.
        }
    }

    /// <summary>
    /// "Çıkış" düğmesi. Temizliği KENDİSİ yapmıyor: çıkış niyetini duyuruyor,
    /// pencere kapanıyor ve temizlik pencerenin kapanma yolunda çalışıyor.
    /// </summary>
    /// <remarks>
    /// Pencereyi burada KAPATMIYORUZ. X ile kapatmak artık uygulamayı bildirim
    /// alanına indiriyor; "gerçek çıkış" ile "gizle" ayrımını yalnızca pencere
    /// bilebilir, çünkü WPF ikisini de aynı Closing olayıyla bildiriyor.
    /// Görünüm modeli niyeti duyuruyor, kararı pencere veriyor.
    /// </remarks>
    private Task ExitAsync()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    // --- Yardımcılar ------------------------------------------------------------

    private void LoadIspChoices()
    {
        if (_profiles is null)
        {
            return;
        }

        IspChoices.Clear();

        // "Bilmiyorum" birinci sınıf seçenek: kullanıcıların çoğu İSS'ini teknik
        // adıyla bilmiyor ve bilmiyor olmak testi engellememelidir.
        IspChoices.Add(new IspChoice(null, "Bilmiyorum / otomatik tespit et"));

        foreach (var profile in _profiles.Profiles)
        {
            IspChoices.Add(new IspChoice(profile, profile.DisplayName));
        }

        // İlk açılışta "Bilmiyorum" seçili gelir, listedeki ilk profil DEĞİL.
        //
        // Önceden ilk gerçek profil seçiliyordu ve bu, kurulum paketiyle gerçek bir
        // makinede denendiğinde görüldü: Türk Telekom hattında uygulama açıldığında
        // "Turkcell Superonline" seçili geliyordu (priority'si en küçük profil).
        // Kullanıcının doğrudan Başlat'a basması, kendi hattında HİÇ denenmemiş bir
        // stratejiyi trafiğe uygulaması demekti. Uygulama, tespit etmediği bir
        // sağlayıcıyı seçilmiş gibi göstermemeli.
        //
        // Tespit burada kendiliğinden ÇALIŞTIRILMIYOR: ASN sorgusu kullanıcının
        // IP'sini üçüncü bir servise gönderiyor ve bu, kullanıcı hiçbir şey
        // istemeden açılışta yapılacak bir şey değil. "Bilmiyorum" seçili hâldeyken
        // parametre testi başlatıldığında tespit zaten devreye giriyor.
        SelectedIsp = IspChoices.FirstOrDefault(c => c.Profile is null)
                      ?? IspChoices.FirstOrDefault();
    }

    private void LoadStrategyChoices()
    {
        StrategyChoices.Clear();

        var profile = SelectedIsp?.Profile;
        if (profile is null)
        {
            SelectedStrategy = null;

            // RefreshIdlePresentation, UpdateStatusDetail DEĞİL. İkincisi
            // ayrıntı satırını "servis sağlayıcısı seçilmedi · strateji yok"
            // ile ezip sıradaki adımı siliyordu; yani kullanıcının tam da bu
            // durumda görmesi gereken tek cümleyi. Üst üste iki hata vardı:
            // yukarıdaki atama zaten null'sa setter hiç koşmuyor, dolayısıyla
            // tazeleme burada AÇIKÇA çağrılmak zorunda.
            RefreshIdlePresentation();
            return;
        }

        // Yalnızca HTTPS adayları listeleniyor. Kullanıcının "strateji" derken
        // kastettiği şey bu; diğer bölümler (HTTP, QUIC, Discord ses) profilin en
        // yüksek ağırlıklı adaylarıyla otomatik dolduruluyor. Dört bölümün adaylarını
        // tek bir listede karıştırmak, kullanıcının farkında olmadan yalnızca 80
        // portunu koruyan bir seçim yapmasına yol açıyordu.
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

            // NEDEN olmadığını da söyle. Önceden yalnızca "hiçbiri açmadı" yazıyordu
            // ve bu iki çok farklı durumu aynı gösteriyordu: (a) stratejiler gerçekten
            // tutmadı, (b) winws hiç çalışmadı; örneğin WinDivert sürücüsü önceki
            // koşumdan çekirdekte asılı kaldığı için. Gerçek bir kullanıcıda (b)
            // yaşandı ve ekranda ayırt edilemedi: 176 aday, 1105 saniye, tek satır
            // "sonuç yok".
            //
            // Bütün denemeler AYNI sebeple düştüyse bu neredeyse her zaman ortamla
            // ilgilidir, stratejiyle değil.
            var reasons = report.Attempts
                .GroupBy(a => a.Detail ?? "(sebep belirtilmedi)", StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            if (reasons.Count > 0)
            {
                Append("En sık görülen sebepler:");
                foreach (var reason in reasons)
                {
                    Append($"   {reason.Count()}x  {reason.Key}");
                }

                if (reasons.Count == 1 && report.Attempts.Count > 5)
                {
                    Append("Bütün denemeler aynı sebeple düştü. Bu genellikle stratejiyle değil");
                    Append("ortamla ilgilidir: winws çalışamamış ya da paketleri hiç görememiş olabilir.");
                    Append("Uygulamayı kaldırıp yeniden kurmak, gerekirse bilgisayarı yeniden");
                    Append("başlatmak bu durumu çözer (ağ sürücüsü önceki koşumdan asılı kalmış olabilir).");
                }
            }

            return;
        }

        Append($"Sonuç ({report.Duration.TotalSeconds:F0} sn):");
        foreach (var winner in report.Winners)
        {
            Append($"   [+] {SectionLabel(winner.Section)}: {winner.Args}");
            Append($"       açılan: {string.Join(", ", winner.VerifiedCategories)}");

            if (winner.Section == StrategySection.DiscordVoice)
            {
                Append("       not: bu, UDP yolunun açıldığını gösterir; Discord sesli görüşmenin");
                Append("       çalıştığını doğrulamaz. Sesi ancak Discord'da bir görüşmeyle deneyebilirsiniz.");
            }

            // YALNIZCA tcp443 kazananı seçim listesine girer. Bu liste HTTPS
            // strateji listesi; diğer bölümlerin kazananları kullanıcının seçtiği
            // şey değil, RuntimeSelection'ın profilden otomatik eklediği şey.
            //
            // Önceden döngü her kazanan için SelectedStrategy'yi eziyordu ve
            // bölümler tcp80 -> tcp443 -> quic sırasında geldiği için SONUNCUSU,
            // yani QUIC stratejisi, HTTPS stratejisi olarak seçili kalıyordu.
            // Gerçek makinede ölçüldü: winws "--filter-tcp=443 --dpi-desync=fake
            // --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n2
            // --dpi-desync-fake-quic=..." ile çalışıyordu; TCP bölümüne QUIC
            // komutu. Sonuç: test "3 bölüm için çalışan parametre bulundu" diyor,
            // Başlat'a basılıyor, tcp80 açılıyor ama discord.com HTTPS'te RST
            // almaya devam ediyor. Yani kullanıcının gördüğü şey ile uygulanan
            // şey birbirinden ayrılmıştı.
            if (winner.Section != StrategySection.Tcp443)
            {
                continue;
            }

            var choice = new StrategyChoice(
                winner.CandidateId,
                $"✓ {SectionLabel(winner.Section)} · {Describe(winner.Args)} (test edildi)",
                winner.Args,
                CandidateSource.Verified);

            StrategyChoices.Insert(0, choice);
            SelectedStrategy = choice;
        }

        PersistLearned(report);

        // EKSİK KALAN KATEGORİLERİ SÖYLE. Bir bölümde birden fazla hedef sınıfı
        // olabiliyor ve kazanan aday hepsini açmak zorunda değil: arama ilk
        // başarıda duruyor, "başarı" ise en az bir sınıfın açılması.
        //
        // Gerçek bir kullanıcıda bunun bedeli görüldü: test çalışan strateji
        // buldu, Zapret başlatıldı, ama Discord istemcisi GÜNCELLEME ekranında
        // takılı kaldı. Sebep, istemcinin güncelleme için ayrı bir sunucuya
        // gitmesi ve o sunucunun açılmamasıydı. Ekranda "strateji bulundu"
        // yazıyordu ve eksik olan şey hiçbir yerde görünmüyordu.
        var eksikler = new List<string>();
        foreach (var winner in report.Winners)
        {
            var beklenen = report.Baseline
                .Where(b => b.Target.Section == winner.Section
                            && b.Status == BaselineStatus.Blocked
                            && b.Target.Category != StrategyProber.ControlCategory)
                .Select(b => b.Target.Category)
                .Distinct(StringComparer.Ordinal);

            eksikler.AddRange(beklenen.Except(winner.VerifiedCategories, StringComparer.Ordinal));
        }

        eksikler = eksikler.Distinct(StringComparer.Ordinal).ToList();

        if (eksikler.Count > 0)
        {
            Append("DİKKAT: şu hedefler hâlâ açılmıyor: " + string.Join(", ", eksikler));
            Append("Bulunan strateji bunları açmadı. İlgili uygulama yine takılabilir");
            Append("(örneğin Discord güncellemede kalabilir). Testi tekrar çalıştırmak");
            Append("ya da açılmayan adresi \"Açılmayan site\" kutusuna yazmak işe yarayabilir.");

            SetStatus(AppStatus.Ready, "KISMEN ÇALIŞIYOR",
                $"{report.Winners.Count} bölüm açıldı, {eksikler.Count} hedef hâlâ kapalı.");
            SuggestNextStep();
            return;
        }

        SetStatus(AppStatus.Ready, "STRATEJİ BULUNDU",
            $"{report.Winners.Count} bölüm için çalışan parametre bulundu. " +
            "Sıradaki adım: \"ZAPRET'İ BAŞLAT\".");

        SuggestNextStep();
    }

    /// <summary>
    /// Test bittikten sonra SIRADAKİ ADIMI yazar.
    /// </summary>
    /// <remarks>
    /// Test biten ekranda "STRATEJİ BULUNDU" yazıyordu ve orada kalıyordu. Bulunan
    /// strateji KENDİLİĞİNDEN uygulanmıyor; kullanıcının ayrıca Başlat'a basması,
    /// kalıcı olmasını istiyorsa da servisi kurması gerekiyor. Bu iki adım hiçbir
    /// yerde söylenmediği için "test yaptım, buldu, ama hiçbir şey değişmedi"
    /// tamamen makul bir kullanıcı deneyimiydi.
    /// </remarks>
    private void SuggestNextStep()
    {
        Append("Sıradaki adım: \"ZAPRET'İ BAŞLAT\" düğmesine basın — bulunan parametre");
        Append("ancak o zaman trafiğe uygulanır.");

        if (!IsServiceInstalled)
        {
            Append("Ardından, bilgisayar her açıldığında kendiliğinden çalışması için");
            Append("\"Servis Olarak Yükle (Otomatik Başlat)\" düğmesini kullanın. Bu");
            Append("yapılmazsa koruma yeniden başlatmadan sonra kapalı gelir.");
        }
    }

    /// <summary>
    /// Testte doğrulanan stratejileri diske yazar.
    /// </summary>
    /// <remarks>
    /// Bu olmadan test her açılışta baştan koşulmak zorundaydı: kullanıcı 2-3
    /// dakika bekleyip çalışan bir strateji buluyor, uygulamayı kapatıyor ve
    /// bulunan her şey kayboluyordu.
    /// </remarks>
    private void PersistLearned(ProbeReport report)
    {
        var ispId = SelectedIsp?.Profile?.Id;
        if (ispId is null || report.Winners.Count == 0)
        {
            return;
        }

        try
        {
            ConfigStore.AddLearned(report.Winners.Select(w => new LearnedCandidate
            {
                IspId = ispId,
                CandidateId = w.CandidateId,
                Section = w.Section.ToJsonName(),
                Args = w.Args,
                VerifiedFor = w.VerifiedCategories,
                LastVerified = DateTime.Now.ToString("yyyy-MM-dd"),
            }));

            SaveSelection();
            Append("Sonuçlar kaydedildi; bir sonraki açılışta hazır olacak.");
        }
        catch (Exception ex)
        {
            Append("Sonuçlar kaydedilemedi: " + ex.Message, isError: true);
            return;
        }

        // Bellekteki profiller de tazelensin; yoksa hemen ardından basılan Başlat
        // yeni doğrulamayı görmüyor (bkz. CurrentProfile). Listeyi yeniden kurmuyoruz:
        // seçimi ve "test edildi" satırını silerdi.
        try
        {
            _profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
        }
        catch (Exception ex)
        {
            Append("Yeni doğrulamalar uygulamayı yeniden açınca devreye girecek: " + ex.Message, isError: true);
        }
    }

    private void OnRunnerStateChanged(WinwsState state)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case WinwsState.Running:
                    // Motor artık kendi sürümünü bildirdi; alt bilgi tazelensin.
                    // Burada yapılıyor, çünkü bu geri çağrı zaten arayüz iş
                    // parçacığında koşuyor; sürüm, çıktıyı okuyan AYRI bir
                    // iş parçacığında yakalanıyor ve oradan bildirim göndermek
                    // WPF bağlamasını patlatırdı.
                    Notify(nameof(EngineVersionText));
                    Notify(nameof(FooterText));
                    SetStatus(AppStatus.Running, "KORUMA AKTİF");
                    break;
                case WinwsState.Faulted:
                    SetStatus(AppStatus.Faulted, "BEKLENMEDİK DURUŞ",
                        "winws kendiliğinden kapandı. Ayrıntılar günlükte.");

                    // Motor ölse de şifreli DNS ve yönlendirme ayakta olabilir;
                    // Duraklat onları kapatabilsin.
                    _hasLeftovers = HasLeftoverProcessesOrRedirect();
                    RefreshCommands();
                    break;
                case WinwsState.Stopped:
                    if (Status == AppStatus.Running)
                    {
                        SetIdleStatus();
                    }

                    break;
            }
        });
    }

    /// <summary>
    /// Koruma çalışmıyorken durum bandını yazar.
    /// </summary>
    /// <remarks>
    /// Burası eskiden koşulsuz "SİSTEM HAZIR" diyordu ve bu, teknik olmayan bir
    /// kullanıcı için YANLIŞ bir cümleydi. Yeni kurulmuş bir makinede tablo
    /// şuydu: servis sağlayıcı "Bilmiyorum", strateji listesi boş, Başlat düğmesi
    /// kapalı, winws çalışmıyor, koruma yok; ve ekranın en üstünde, en büyük
    /// puntoyla "SİSTEM HAZIR". Kullanıcının yapması gereken tek şey (parametre
    /// testi) hiçbir yerde söylenmiyordu. "Kurdum, çalışmadı" bildirimlerinin en
    /// ucuz açıklaması bu: uygulama hazır olduğunu söylüyor, kullanıcı da
    /// inanıyor.
    ///
    /// Başlık artık iki soruyu birden cevaplıyor: koruma açık mı ve açık değilse
    /// SIRADAKİ ADIM ne. Durum değeri <see cref="AppStatus.Ready"/> olarak
    /// kalıyor; değiştirmek düğmelerin etkinliğini bozardı. Değişen yalnızca
    /// kullanıcıya söylenen şey.
    /// </remarks>
    /// <param name="note">Varsa başa eklenecek tek cümlelik bağlam.</param>
    private void SetIdleStatus(string? note = null)
    {
        string headline;
        string guidance;

        if (IsServicePaused)
        {
            // Durum değeri Paused: ana düğme "DEVAM ET" okunsun ve servisi geri açsın.
            SetStatus(AppStatus.Paused, "DURAKLATILDI",
                (string.IsNullOrWhiteSpace(note) ? string.Empty : note + " ")
                + "Koruma ve şifreli DNS kapalı, DNS ayarınız eski hâlinde; VPN kullanabilirsiniz. "
                + "\"DEVAM ET\" ile kaldığı yerden sürer.");
            return;
        }

        if (IsServiceStopped)
        {
            headline = "SERVİS DURMUŞ";
            guidance = "Otomatik başlatma servisi kurulu ama çalışmıyor; koruma kapalı. " +
                       "\"ZAPRET'İ BAŞLAT\" ile elle açabilirsiniz.";
        }
        else if (IsServiceInstalled)
        {
            headline = "SERVİS MODU AKTİF";
            guidance = "Koruma otomatik başlatma servisiyle çalışıyor; elle başlatmaya gerek yok.";
        }
        else if (SelectedStrategy is null)
        {
            headline = "KORUMA KAPALI — KURULUM YARIM";
            guidance = "Henüz bir parametre bulunmadı. \"PARAMETRE TESTİ YAP\" düğmesine basın; " +
                       "hattınıza uyan ayar aranacak (birkaç dakika sürer).";
        }
        else
        {
            headline = "KORUMA KAPALI";
            guidance = "Ayar hazır. \"ZAPRET'İ BAŞLAT\" düğmesiyle korumayı açın.";
        }

        SetStatus(AppStatus.Ready, headline,
            string.IsNullOrWhiteSpace(note) ? guidance : note + " " + guidance);
    }

    /// <summary>
    /// Seçim değiştiğinde durum bandını tazeler.
    /// </summary>
    /// <remarks>
    /// Yalnızca ayrıntıyı güncellemek yetmiyordu: kullanıcı listeden bir strateji
    /// seçtiğinde ayrıntı satırı değişiyor ama BAŞLIK "KURULUM YARIM" olarak
    /// kalıyordu; yani artık doğru olmayan bir cümle ekranda asılı duruyordu.
    /// </remarks>
    private void RefreshIdlePresentation()
    {
        if (Status == AppStatus.Ready)
        {
            SetIdleStatus();
        }
        else
        {
            UpdateStatusDetail();
        }
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
        var dns = IsSecureDnsEnabled ? " · şifreli DNS" : string.Empty;
        StatusDetail = $"{isp} · {strategy}{dns}";
    }

    private void RefreshCommands()
    {
        Notify(nameof(CanStart));
        Notify(nameof(CanPause));
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

            // Günlük sınırsız büyümemeli: uzun bir test binlerce satır üretir ve
            // arayüz yavaşlamaya başlar.
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
            // BEKLEMEDEN. Invoke idi ve bir kilitlenmenin yarısıydı: arayüz iş
            // parçacığı bir sürecin çıktısının bitmesini beklerken çıktıyı okuyan
            // iş parçacığı da burada arayüzü bekliyordu. Gerekçesi
            // WinwsRunner.WaitForEarlyExit'te. Sıra korunuyor: BeginInvoke aynı
            // öncelikteki işleri geldiği sırayla çalıştırıyor.
            Application.Current.Dispatcher.BeginInvoke(Add);
        }
    }

    /// <summary>Bölümün kullanıcıya gösterilen adı.</summary>
    /// <remarks>
    /// discord-voice için "Discord ses" yazıyordu. Ölçülen şey ise STUN cevabı, yani
    /// UDP yolu; ad, ölçümün kanıtlamadığı bir şeyi vaat ediyordu. Gerekçesi
    /// <see cref="IspProfile.LearnedNote"/>'ta.
    /// </remarks>
    public static string SectionLabel(StrategySection section) => section switch
    {
        StrategySection.Tcp80 => "HTTP",
        StrategySection.Tcp443 => "HTTPS",
        StrategySection.Quic => "QUIC",
        StrategySection.DiscordVoice => "UDP (STUN)",
        _ => section.ToString(),
    };

    /// <summary>Uzun argüman dizgisini seçim kutusuna sığacak kısa bir ada çevirir.</summary>
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
