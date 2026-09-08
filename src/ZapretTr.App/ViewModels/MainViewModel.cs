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
    private readonly DnsCryptRunner? _dnsRunner;
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
    private bool _isSecureDnsEnabled = true;
    private bool _isSecureDnsActive;
    private bool _isServiceInstalled;

    /// <summary>
    /// Kayitli ayarlar geri yuklenirken true.
    /// </summary>
    /// <remarks>
    /// Bu bayrak olmadan geri yukleme kendi kendini bozuyordu: ilk atanan ozelligin
    /// setter'i SaveSelection() cagiriyor, o an ISS ve strateji HENUZ geri
    /// yuklenmemis oluyor ve yapilandirma yarim haliyle uzerine yaziliyordu. Sonuc:
    /// her acilista ayarlarin bir kismi sessizce kayboluyordu.
    /// </remarks>
    private bool _isRestoring;

    /// <summary>Temizlik bir kez kosulduysa tekrar kosmasin.</summary>
    private bool _shutdownCompleted;

    public MainViewModel()
    {
        StartCommand = new RelayCommand(StartAsync, () => CanStart);
        PauseCommand = new RelayCommand(PauseAsync, () => Status == AppStatus.Running);
        TestCommand = new RelayCommand(RunTestAsync, () => !IsBusy && IsReady);
        CancelTestCommand = new RelayCommand(CancelTestAsync, () => Status == AppStatus.Testing);
        ResetCommand = new RelayCommand(ResetAsync, () => !IsBusy);
        ExitCommand = new RelayCommand(ExitAsync);
        ServiceCommand = new RelayCommand(ToggleServiceAsync, () => !IsBusy && IsReady);
        SaveReportCommand = new RelayCommand(SaveReportAsync);

        try
        {
            _vendor = VendorPaths.Locate();

            // Kullanicinin kendi dogruladiklari dagitim profillerinin uzerine
            // bindiriliyor: parametre testinde bulunan strateji bir sonraki
            // acilista "dogrulanmis" olarak hazir gelsin.
            _profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
            _runner = new WinwsRunner(_vendor);
            _runner.LogLineReceived += line => Append(line.Text, line.IsError);
            _runner.StateChanged += OnRunnerStateChanged;

            _dnsRunner = new DnsCryptRunner(_vendor);
            _dnsRunner.LogLineReceived += line => Append(line);

            // Onceki calismada uygulama duzgun kapanmadiysa sistem DNS'i hala
            // 127.0.0.1'i gosteriyor olabilir ve o durumda hicbir ad cozulmez.
            // Kullaniciya sormadan duzeltiyoruz: internetin yokken onay beklemek
            // yardim degil, engel.
            _ = RecoverDnsIfNeededAsync();

            LoadIspChoices();
            RestoreSavedSelection();
            _ = RefreshServiceStatusAsync();
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

    /// <summary>Gunlugu ve ortam ozetini bir dosyaya yazar.</summary>
    public RelayCommand SaveReportCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand ServiceCommand { get; }

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

    /// <summary>
    /// Manuel baslatma mumkun mu.
    /// </summary>
    /// <remarks>
    /// Servis kuruluysa winws ZATEN calisiyor ve ayni filtreyle ikinci bir ornek
    /// baslatilamiyor -- winws "A copy of winws is already running with the same
    /// filter" deyip 1 koduyla cikiyor. Dugme yine de aciktı ve basan kullanici
    /// "winws baslar baslamaz 1 koduyla kapandi" diye anlamsiz bir hata aliyordu.
    /// Gercek bir kullanicida yasandi.
    ///
    /// Koruma zaten aciksa basilacak bir dugme olmamali.
    /// </remarks>
    public bool CanStart => Status is AppStatus.Ready or AppStatus.Paused
                            && SelectedStrategy is not null
                            && !IsServiceInstalled;

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

    public string EngineVersionText => "winws v72.13 · dnscrypt-proxy 2.1.18";

    /// <summary>
    /// Sifreli DNS kullanilsin mi. Varsayilan olarak ACIK.
    /// </summary>
    /// <remarks>
    /// Varsayilanin acik olmasi olculmus bir gerekce tasiyor: bu makinede
    /// discord.com, pornhub.com ve xvideos.com'un ucu de saglayicinin engel
    /// sunucusuna cozumleniyordu. O katman asilmadan winws stratejisi hicbir sey
    /// degistirmiyor -- trafik zaten gercek sunucuya gitmiyor. Kapali baslasaydi
    /// kullanicilarin cogu "calismiyor" deyip birakirdi.
    /// </remarks>
    public bool IsSecureDnsEnabled
    {
        get => _isSecureDnsEnabled;
        set
        {
            if (Set(ref _isSecureDnsEnabled, value))
            {
                UpdateStatusDetail();
                SaveSelection();
            }
        }
    }

    /// <summary>Otomatik baslatma servisi kurulu mu.</summary>
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
                UpdateStatusDetail();

                // Servis kuruluysa koruma acilistan itibaren zaten calisiyor.
                // Kullanici bunu ekranda gormeli, yoksa "neden Baslat kapali"
                // diye dusunur.
                if (value && Status == AppStatus.Ready)
                {
                    SetStatus(AppStatus.Ready, "SERVİS MODU AKTİF",
                        "Koruma otomatik başlatma servisiyle çalışıyor; elle başlatmaya gerek yok.");
                }
            }
        }
    }

    public string ServiceButtonText => IsServiceInstalled
        ? "Otomatik Başlatmayı Kaldır"
        : "Servis Olarak Yükle (Otomatik Başlat)";

    /// <summary>Sifreli DNS su an gercekten devrede mi.</summary>
    public bool IsSecureDnsActive
    {
        get => _isSecureDnsActive;
        private set => Set(ref _isSecureDnsActive, value);
    }

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

            // Sifreli DNS ONCE aciliyor. Ters sirada yapilsaydi winws, hala engel
            // sunucusuna giden bir trafigi kurcalamis olurdu -- yani hicbir sey.
            if (IsSecureDnsEnabled && _dnsRunner is not null && !_dnsRunner.IsRunning)
            {
                Append("Şifreli DNS başlatılıyor...");
                await _dnsRunner.StartAsync().ConfigureAwait(true);
                IsSecureDnsActive = true;
            }

            var winners = BuildRuntimeSelection();
            var builder = new WinwsCommandBuilder(_vendor);
            var arguments = builder.BuildRuntimeCommand(winners);

            Append("Başlatılıyor (" + winners.Count + " bölüm): " + WinwsCommandBuilder.ToDisplayString(arguments));
            _runner.Start(arguments);
            SaveSelection();

            // Kayitli strateji HALA calisiyor mu. Beklemiyoruz: baslatma aninda
            // bitmis sayilir, dogrulama arkadan gelir.
            _ = VerifyAfterStartAsync();
        }
        catch (Exception ex)
        {
            Append(ex.Message, isError: true);
            SetStatus(AppStatus.Faulted, "BAŞLATILAMADI", ex.Message);

            // Yarim kalmis bir baslatma DNS'i bizde birakmamali: winws acilmadiysa
            // kullanicinin kazanci yok ama sistem DNS'i degistirilmis olur.
            await StopSecureDnsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Baglantidan servis saglayiciyi tespit edip secmeyi dener.
    /// </summary>
    /// <remarks>
    /// Tespit BASARISIZ olursa test yine de calisir, sadece genel aramadan baslar.
    /// Kullaniciyi "once ISS'ini sec" diye geri cevirmek, bilmeyen kullaniciyi
    /// tam da yardim etmesi gereken yerde duvara toslatmak olurdu.
    ///
    /// Birden fazla profil eslesirse (ornegin "vodafone" hem sabit hat hem mobil)
    /// secim kullaniciya birakiliyor; birini sessizce secmek yanlis profille
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
            // Tespit bir kolaylik; basarisizligi testi engellememeli.
            Append("Tespit denemesi başarısız: " + ex.Message, isError: true);
        }
    }

    /// <summary>Kayitli secimleri geri yukler.</summary>
    /// <remarks>
    /// Strateji once id ile, bulunamazsa argumanla aranir. Id'ler profil surumleri
    /// arasinda degisebiliyor; kullanicinin calisan ayarinin sirf id eslesmedi diye
    /// kaybolmasi kabul edilemez.
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

    /// <summary>Secimleri diske yazar. Her degisiklikte degil, anlamli anlarda cagrilir.</summary>
    /// <summary>
    /// Gunlugu ve ortam ozetini kullanicinin sectigi bir dosyaya yazar.
    /// </summary>
    /// <remarks>
    /// Bu dugme bir kolaylik degil, eksik bir kanaldi. Turksat Kablonet
    /// kullanicisi 0.1.6'nin calistigini bildirdi ama profil hala
    /// dogrulanmamis durumda: HANGI adayin kazandigini bilmiyoruz, cunku
    /// kullanicinin gunlugu bize ulastirmasinin tek yolu pencereden metni elle
    /// secip kopyalamakti. Bildirim geldi, veri gelmedi.
    ///
    /// Rapor DISARI GONDERILMIYOR: yalnizca diske yaziliyor, neyi paylasacagina
    /// kullanici karar veriyor. Dosyanin basinda ne icerdigi yaziyor ki
    /// paylasmadan once bilerek baksin.
    /// </remarks>
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
            // Kaydetme basarisiz olsa bile uygulama calismaya devam etmeli:
            // rapor bir teshis araci, korumanin parcasi degil.
            Append("Rapor kaydedilemedi: " + ex.Message, isError: true);
        }
    }

    /// <summary>Rapor metnini kurar.</summary>
    /// <remarks>
    /// Ortam ozeti gunlugun ONUNE konuyor. Gunluk tek basina cogu zaman
    /// yetmiyor: "su aday calisti" satirini okuyup hangi profil ve hangi surumle
    /// oldugunu bilmeden profile isleyemiyoruz.
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

        sb.AppendLine("Gunluk");
        sb.AppendLine("------");
        foreach (var satir in LogLines)
        {
            sb.AppendLine(satir);
        }

        return sb.ToString();
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
        // Geri yukleme sirasinda kaydetme: yarim durumu diske yazmak, kaydedilmis
        // ayarlarin bir kismini silmek demek.
        if (_isRestoring)
        {
            return;
        }

        try
        {
            ConfigStore.Save(new AppConfig
            {
                SelectedIspId = SelectedIsp?.Profile?.Id,
                SelectedStrategyId = SelectedStrategy?.Id,
                SelectedStrategyArgs = SelectedStrategy?.Args,
                SecureDnsEnabled = IsSecureDnsEnabled,
                CustomTarget = string.IsNullOrWhiteSpace(CustomTarget) ? null : CustomTarget,
            });
        }
        catch (Exception ex)
        {
            // Kaydedememek calismayi engellememeli; yalnizca bir sonraki acilista
            // ayarlar geri gelmez.
            Append("Ayarlar kaydedilemedi: " + ex.Message, isError: true);
        }
    }

    private async Task RefreshServiceStatusAsync()
    {
        try
        {
            var status = await ServiceManager.GetStatusAsync().ConfigureAwait(true);
            IsServiceInstalled = status.AnyInstalled;
        }
        catch (Exception)
        {
            IsServiceInstalled = false;
        }
    }

    /// <summary>Otomatik baslatmayi kurar ya da kaldirir.</summary>
    private async Task ToggleServiceAsync()
    {
        if (_vendor is null || SelectedStrategy is null)
        {
            return;
        }

        IsBusy = true;
        IsLogExpanded = true;

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

                SetStatus(AppStatus.Ready, "OTOMATİK BAŞLATMA KAPATILDI");
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
                    + $"   {Describe(SelectedStrategy.Args)}"
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

                // Servise, arayuzun calistirdigi komutun AYNISI veriliyor. Ayrisirsa
                // kullanicinin test edip begendigi sey ile acilista calisan sey
                // farkli olur.
                var winners = BuildRuntimeSelection();
                var builder = new WinwsCommandBuilder(_vendor);
                var arguments = builder.BuildRuntimeCommand(winners);

                var installed = await ServiceManager
                    .InstallAsync(_vendor, arguments, IsSecureDnsEnabled).ConfigureAwait(true);

                foreach (var step in installed)
                {
                    Append((step.Succeeded ? "[+] " : "[!] ") + step.Description
                           + (string.IsNullOrWhiteSpace(step.Detail) ? string.Empty : " — " + step.Detail),
                           isError: !step.Succeeded);
                }

                SetStatus(AppStatus.Ready, "OTOMATİK BAŞLATMA KURULDU",
                    "Bilgisayar açıldığında kendiliğinden çalışacak.");
            }

            await RefreshServiceStatusAsync().ConfigureAwait(true);
            SaveSelection();
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

    /// <summary>Sifreli DNS'i kapatir ve sistem ayarini geri alir. Kapaliysa sessizce doner.</summary>
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
    /// Onceki calismadan kalan DNS yonlendirmesini temizler.
    /// </summary>
    /// <remarks>
    /// Uygulama duzgun kapanmadiysa sistem DNS'i hala 127.0.0.1'i gosteriyor ama
    /// dnscrypt-proxy calismiyor olabilir. O durumda makine HICBIR adi cozemez.
    /// Acilista sessizce duzeltiyoruz.
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

    /// <summary>
    /// Calistirilacak bolum -> strateji eslesmesini kurar.
    /// </summary>
    /// <remarks>
    /// Kural <see cref="RuntimeSelection"/> icinde: sorunu olmayan yere dokunma.
    /// Kullanicinin sectigi HTTPS stratejisi ve yalnizca DOGRULANMIS diger bolumler
    /// uygulanir. Denenmemis bir strateji calisan trafige uygulanmaz -- gercek bir
    /// kosumda bunun bedeli olculdu: sorunsuz calisan QUIC baglantisi, uzerine
    /// denenmemis bir QUIC stratejisi uygulanınca bozuldu.
    /// </remarks>
    private Dictionary<StrategySection, string> BuildRuntimeSelection()
        => RuntimeSelection.Build(SelectedIsp?.Profile, SelectedStrategy!.Args);

    /// <summary>
    /// Baslatmadan sonra kayitli stratejinin GERCEKTEN ise yaradigini olcer.
    /// </summary>
    /// <remarks>
    /// Kullanici bir kez test yapip stratejiyi kaydediyor ve sonraki acilislarda
    /// dogrudan Baslat'a basiyor -- test tekrar kosmuyor. Ama engelleme degisebilir:
    /// ISS'in DPI yapilandirmasi guncellenir ve dun calisan parametre bugun calismaz.
    /// O durumda arayuz "CALISIYOR" gosteriyordu ve kullanici korundugunu saniyordu.
    ///
    /// Burada yalnizca tcp443 hedefleri olculuyor: ucu de birkac saniye suruyor ve
    /// baslatma akisini bekletmiyor. HICBIRI acilmiyorsa strateji artik ise
    /// yaramiyor demektir ve kullaniciya yeni bir test onerilir.
    ///
    /// Dogrulama bir KOLAYLIK: kendisi hata verirse baslatma bozulmamali, cunku
    /// winws zaten calisiyor ve olcumun basarisizligi korumanin basarisizligi degil.
    /// </remarks>
    private async Task VerifyAfterStartAsync()
    {
        if (_profiles is null)
        {
            return;
        }

        try
        {
            // Ag yiginin oturmasi icin kisa bir bekleme; hemen olcmek yanlis
            // negatif uretiyor.
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

            // Bu arada winws coktuyse (Faulted) veya kullanici durdurduysa olcecek
            // bir sey yok: hatanin uzerine "acmiyor" yazmak kafa karistirir.
            if (Status != AppStatus.Running)
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
            var opened = 0;
            foreach (var target in targets)
            {
                var outcome = await StrategyProber
                    .ProbeAsync(target.Section, target.Host, null, client)
                    .ConfigureAwait(true);

                if (outcome.Succeeded)
                {
                    opened++;
                }
            }

            if (Status != AppStatus.Running)
            {
                return;
            }

            if (opened > 0)
            {
                Append($"Doğrulandı: {opened}/{targets.Count} hedef açılıyor.");
                return;
            }

            Append("UYARI: kayıtlı strateji artık işe yaramıyor görünüyor —", isError: true);
            Append("test hedeflerinin hiçbiri açılmadı. Engelleme değişmiş olabilir.", isError: true);
            Append("\"PARAMETRE TESTİ YAP\" ile yeni bir strateji aramanız önerilir.");

            SetStatus(AppStatus.Running, "ÇALIŞIYOR — AMA AÇMIYOR",
                "Kayıtlı strateji hedefleri açmadı; yeni bir parametre testi önerilir.");
        }
        catch (Exception)
        {
            // Dogrulama yapilamadi. Sessiz geciyoruz: winws calisiyor ve olcumun
            // kendi hatasini korumanin hatasi gibi gostermek yanlis olur.
        }
    }
    private async Task PauseAsync()
    {
        if (_runner is null)
        {
            return;
        }

        await _runner.StopAsync().ConfigureAwait(true);

        // Duraklatmak "korumayi gecici olarak kaldir" demek; DNS yonlendirmesi de
        // korumanin parcasi. Onu acik birakmak, kullanicinin kapattigini sandigi
        // bir seyin sistem ayarlarinda durmaya devam etmesi olurdu.
        await StopSecureDnsAsync().ConfigureAwait(true);

        SetStatus(AppStatus.Paused, "DURAKLATILDI", "Yapılandırma korundu.");
        Append("Duraklatıldı. Ayarlar korundu, sistem DNS'i geri alındı.");
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

        // BASKA BIR DPI ARACI ACIKSA ONCE ONU SOYLE. WinDivert'i ayni anda iki arac
        // kullanamiyor; GoodbyeDPI acikken winws paketleri goremiyor ve butun adaylar
        // ayni sekilde dusuyor. Kullanicinin gordugu sey "N aday denendi, hicbiri
        // calismadi" oluyor -- yani stratejiler kotu saniliyor, oysa olcum hic
        // yapilamamis. Testi engellemiyoruz; karar kullanicinin, ama korlemesine
        // 15 dakika beklemesin.
        var conflicts = WinDivertCleanup.DetectConflictingTools();
        if (conflicts.Count > 0)
        {
            Append("DİKKAT: başka bir DPI atlatma aracı çalışıyor: " + string.Join(", ", conflicts));
            Append("WinDivert sürücüsünü aynı anda iki araç kullanamaz. Bu açıkken test");
            Append("hiçbir strateji bulamayabilir — sebebi stratejiler değil, ölçümün");
            Append("hiç yapılamaması olur. Önce o aracı kapatmanız önerilir.");
        }

        // "Bilmiyorum" secildiyse once ISS'i tespit etmeyi dene. Profil bilinmeden
        // yapilan test Tier 1'i tamamen atlar ve dogrudan genel aramaya duser --
        // yani kullanici tam da bu araci hizlandiran seyden mahrum kalir.
        if (SelectedIsp?.Profile is null)
        {
            await TryDetectIspAsync().ConfigureAwait(true);
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

            // Sifreli DNS secimi OLCUME de gecmeli. Gecmedigi surece hedefler sistem
            // DNS'iyle cozuluyordu ve Turkiye'de o katman cogu zaman kacirilmis
            // durumda: discord.com engel sunucusuna cozuluyor, olcum "engel sayfasi"
            // goruyor, bolum DnsRedirected isaretleniyor ve strateji aranmiyor.
            //
            // Gercek makinede olculdu (TTNET, kurulum paketiyle): arayuz
            // "ENGEL BULUNAMADI" diyordu ve kullaniciya "kendi hedefinizi girin"
            // oneriyordu; ayni hatta ayni anda CLI --doh ile 22 calisan strateji
            // buluyordu. Yani urunun ana yuzeyi, DNS kacirmasi olan her hatta
            // -- ki bu Turkiye'de olagan durum -- kullanilamaz haldeydi.
            var prober = new StrategyProber(_vendor, _profiles, targets, IsSecureDnsEnabled);
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
        catch (ProbeEngineException ex)
        {
            // "Strateji bulunamadi" DEMEK DEGIL. Motor hic baslamadigi icin
            // hicbir aday olculemedi; ikisini ayni ekranda gostermek kullaniciyi
            // yanlis yone gonderiyordu ("demek bu hatta ise yaramiyor").
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

            await StopSecureDnsAsync().ConfigureAwait(true);

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

    /// <summary>
    /// winws'i durdurur ve sifreli DNS'i geri alir. BUTUN cikis yollarinin ortak adimi.
    /// </summary>
    /// <remarks>
    /// Ayri bir metot olmasinin sebebi olculmus bir hata: temizlik yalnizca "Çıkış"
    /// dugmesinin icindeydi, pencereyi X ile kapatmanin hicbir islevi yoktu. Gercek
    /// makinede koruma acikken pencere kapatildiginda winws ve dnscrypt-proxy oksuz
    /// kaldi ve sistem DNS'i 127.0.0.1'i gostermeye devam etti. Bu, projedeki en kotu
    /// sonuca acilan yol: dnscrypt sonradan olurse (yeniden baslatma, gorev yoneticisi,
    /// cokme) makine hicbir adi cozemez. Kullanicilarin cogu pencereyi X ile kapatir.
    ///
    /// Birden fazla kez cagrilabilir; ikinci cagri hicbir sey yapmaz.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        if (_shutdownCompleted)
        {
            return;
        }

        _shutdownCompleted = true;

        if (_runner is not null)
        {
            Append("Kapatılıyor, winws durduruluyor...");
            await _runner.StopAsync().ConfigureAwait(true);
        }

        // DNS geri alinmadan cikmak, kullaniciyi ad cozemez bir makineyle
        // birakmak demek. Cikis yolunda atlanabilecek bir adim degil.
        await StopSecureDnsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// "Çıkış" dugmesi. Temizligi KENDISI yapmiyor: pencereyi kapatiyor ve temizlik
    /// pencerenin kapanma yolunda calisiyor. Boylece dugme ile X ayni yoldan gecer --
    /// ikisi ayri olunca biri temizlerken digeri temizlemiyordu.
    /// </summary>
    private Task ExitAsync()
    {
        var window = Application.Current?.MainWindow;
        if (window is not null)
        {
            window.Close();
        }
        else
        {
            Application.Current?.Shutdown();
        }

        return Task.CompletedTask;
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

        // Ilk acilista "Bilmiyorum" secili gelir, listedeki ilk profil DEGIL.
        //
        // Onceden ilk gercek profil seciliyordu ve bu, kurulum paketiyle gercek bir
        // makinede denendiginde goruldu: Turk Telekom hattinda uygulama acildiginda
        // "Turkcell Superonline" secili geliyordu (priority'si en kucuk profil).
        // Kullanicinin dogrudan Baslat'a basmasi, kendi hattinda HIC denenmemis bir
        // stratejiyi trafige uygulamasi demekti. Uygulama, tespit etmedigi bir
        // saglayiciyi secilmis gibi gostermemeli.
        //
        // Tespit burada kendiliginden CALISTIRILMIYOR: ASN sorgusu kullanicinin
        // IP'sini ucuncu bir servise gonderiyor ve bu, kullanici hicbir sey
        // istemeden acilista yapilacak bir sey degil. "Bilmiyorum" secili haldeyken
        // parametre testi baslatildiginda tespit zaten devreye giriyor.
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

            // NEDEN olmadigini da soyle. Onceden yalnizca "hicbiri acmadi" yaziyordu
            // ve bu iki cok farkli durumu ayni gosteriyordu: (a) stratejiler gercekten
            // tutmadi, (b) winws hic calismadi -- ornegin WinDivert surucusu onceki
            // kosumdan cekirdekte asili kaldigi icin. Gercek bir kullanicida (b)
            // yasandi ve ekranda ayirt edilemedi: 176 aday, 1105 saniye, tek satir
            // "sonuc yok".
            //
            // Butun denemeler AYNI sebeple dustuyse bu neredeyse her zaman ortamla
            // ilgilidir, stratejiyle degil.
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

            // YALNIZCA tcp443 kazanani secim listesine girer. Bu liste HTTPS
            // strateji listesi; diger bolumlerin kazananlari kullanicinin sectigi
            // sey degil, RuntimeSelection'in profilden otomatik ekledigi sey.
            //
            // Onceden dongu her kazanan icin SelectedStrategy'yi eziyordu ve
            // bolumler tcp80 -> tcp443 -> quic sirasinda geldigi icin SONUNCUSU,
            // yani QUIC stratejisi, HTTPS stratejisi olarak secili kaliyordu.
            // Gercek makinede olculdu: winws "--filter-tcp=443 --dpi-desync=fake
            // --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n2
            // --dpi-desync-fake-quic=..." ile calisiyordu -- TCP bolumune QUIC
            // komutu. Sonuc: test "3 bolum icin calisan parametre bulundu" diyor,
            // Baslat'a basiliyor, tcp80 aciliyor ama discord.com HTTPS'te RST
            // almaya devam ediyor. Yani kullanicinin gordugu sey ile uygulanan
            // sey birbirinden ayrilmisti.
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

        // EKSIK KALAN KATEGORILERI SOYLE. Bir bolumde birden fazla hedef sinifi
        // olabiliyor ve kazanan aday hepsini acmak zorunda degil: arama ilk
        // basarida duruyor, "basari" ise en az bir sinifin acilmasi.
        //
        // Gercek bir kullanicida bunun bedeli goruldu: test calisan strateji
        // buldu, Zapret baslatildi, ama Discord istemcisi GUNCELLEME ekraninda
        // takili kaldi. Sebep, istemcinin guncelleme icin ayri bir sunucuya
        // gitmesi ve o sunucunun acilmamasiydi. Ekranda "strateji bulundu"
        // yaziyordu ve eksik olan sey hicbir yerde gorunmuyordu.
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
            return;
        }

        SetStatus(AppStatus.Ready, "STRATEJİ BULUNDU",
            $"{report.Winners.Count} bölüm için çalışan parametre bulundu.");
    }

    /// <summary>
    /// Testte dogrulanan stratejileri diske yazar.
    /// </summary>
    /// <remarks>
    /// Bu olmadan test her acilista bastan kosulmak zorundaydi: kullanici 2-3
    /// dakika bekleyip calisan bir strateji buluyor, uygulamayi kapatiyor ve
    /// bulunan her sey kayboluyordu.
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
        }
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
        var dns = IsSecureDnsEnabled ? " · şifreli DNS" : string.Empty;
        StatusDetail = $"{isp} · {strategy}{dns}";
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
