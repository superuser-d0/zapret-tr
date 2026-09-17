// System.IO açıkça yazılıyor: bu projede örtülü using değil (MainViewModel.cs de
// aynı sebeple yazıyor). Çökme günlüğünü diske yazan yol Directory/Path/File
// kullanıyor.
using System.IO;
using System.Windows;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.App;

/// <summary>Uygulama giriş noktası.</summary>
public partial class App : Application
{
    /// <summary>
    /// Kaldırma sırasında servisleri söküp DNS'i geri almak için kullanılan bayrak.
    /// </summary>
    /// <remarks>
    /// Kaldırıcı bu bayrakla çağırıyor. Adım atlanırsa kullanıcının sistem DNS'i
    /// 127.0.0.1'de kalır, dnscrypt-proxy de silinmiş olur ve makine hiçbir adı
    /// çözemez; kaldırma sırasında yapılabilecek en kötü şey bu.
    /// </remarks>
    private const string UninstallServicesFlag = "--uninstall-services";

    /// <summary>
    /// Servisleri kayıtlı yapılandırmayla kurar ve çıkar.
    /// </summary>
    /// <remarks>
    /// Arayüz açmadan kurulum yapabilmek için: sessiz dağıtımda ve otomatik
    /// testlerde düğmeye tıklanamıyor. Kayıtlı yapılandırma yoksa hiçbir şey
    /// yapmadan çıkar; hangi stratejinin kurulacağını tahmin etmek yanlış
    /// olurdu.
    /// </remarks>
    private const string InstallServicesFlag = "--install-services";

    /// <summary>DNS bekçisinin zamanlanmış görevini kuran bayrak (kurulum paketi çağırıyor).</summary>
    private const string RegisterDnsGuardFlag = "--register-dns-guard";

    /// <summary>DNS bekçisinin zamanlanmış görevini silen bayrak (kaldırıcı çağırıyor).</summary>
    private const string UnregisterDnsGuardFlag = "--unregister-dns-guard";

    /// <summary>
    /// Aynı anda yalnızca bir arayüz örneği çalışsın diye tutulan kilit.
    /// </summary>
    /// <remarks>
    /// Alan olarak duruyor, çünkü Mutex toplanırsa kilit de bırakılır; değişkeni
    /// yerelde tutmak, ikinci örneğin ilkini görmemesine yol açardı.
    /// </remarks>
    private static Mutex? _instanceLock;

    /// <summary>Kısayola ikinci kez tıklanma isteklerini dinleyen; yalnızca gerçek açılışta kurulur.</summary>
    private IDisposable? _activationListener;

    /// <summary>Çalışan örneğin penceresi öne getirildi; "zaten çalışıyor" uyarısı gereksiz.</summary>
    private bool _suppressAlreadyRunningMessage;

    /// <remarks>
    /// Her komut satırı yolu sonucunu <c>Shutdown(kod)</c> ile veriyor. Biçim tercihi;
    /// <c>Environment.ExitCode</c> atamak da ÇALIŞIR.
    ///
    /// ÖLÇÜLDÜ (2026-09-16, tek kullanımlık bir WPF uygulamasıyla): bu yapıda
    /// (StartupUri yok, pencere yok, karar OnStartup'ta, üretilen <c>Main</c> void)
    /// iki yol da çıkış kodunu dosdoğru döndürüyor: <c>Shutdown(42)</c> 42,
    /// <c>Environment.ExitCode = 43; Shutdown();</c> 43.
    ///
    /// Bu not, yanlış bir teşhisin tekrarlanmaması için duruyor. Aynı gün
    /// <c>--register-dns-guard</c> yetkisiz çalıştırılıp 2 yerine 0 döndürüldüğü
    /// sanıldı ve "çıkış kodları hep 0" diye bir hata uyduruldu. Ölçüm GEÇERSİZDİ:
    /// app.manifest <c>requireAdministrator</c> olduğu için ShellExecute süreci UAC
    /// ile YÜKSELTİYOR, yani "yetkisiz" sanılan çalıştırma aslında yetkiliydi ve 0
    /// doğru cevaptı. Bu uygulama yetkisiz HİÇ çalışamaz; öyle bir test kurulamaz.
    /// </remarks>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, UninstallServicesFlag, StringComparison.OrdinalIgnoreCase)))
        {
            // Arayüz hiç açılmadan iş yapılıp çıkılıyor: kaldırıcı bunu sessiz
            // çalıştırıyor ve pencere açılması kullanıcıyı şaşkına çevirirdi.
            Shutdown(RunServiceCleanup());
            return;
        }

        if (e.Args.Any(a => string.Equals(a, InstallServicesFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunServiceInstall());
            return;
        }

        // Bekçi tek örnek kilidinden ÖNCE: arayüz açıkken de çalışabilmeli ve
        // kararı "arayüz açık mı" sorusuna kilidin kendisine bakarak veriyor.
        // Kilidi burada almak, açık arayüzü kapalı gösterirdi.
        if (e.Args.Any(a => string.Equals(a, DnsGuardTask.GuardFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunDnsGuard());
            return;
        }

        if (e.Args.Any(a => string.Equals(a, RegisterDnsGuardFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunRegisterDnsGuard());
            return;
        }

        if (e.Args.Any(a => string.Equals(a, UnregisterDnsGuardFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunUnregisterDnsGuard());
            return;
        }

        // Beklenmedik hatada SESSİZCE KAYBOLMA. Bu kanca olmadan, arayüz
        // kurulurken çıkan bir hata Windows'un kendi çökme penceresiyle
        // sonuçlanıyor ve kullanıcının elinde "açılmıyor"dan başka bir şey
        // kalmıyordu; teşhis edilemeyen bildirimlerin en kötü sınıfı.
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception);

            // İşaretleniyor ki uygulama ayakta kalsın: yarım çalışan bir pencere,
            // kaybolan bir pencereden iyidir; kullanıcı en azından "Raporu
            // Kaydet" düğmesine ulaşabilir.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash(args.ExceptionObject as Exception);

        if (!TryClaimSingleInstance() && !HandOverToRunningInstance())
        {
            // İKİNCİ ÖRNEK ÇALIŞMAMALI.
            //
            // Pencereyi X ile kapatmak uygulamayı bildirim alanına indiriyor, yani
            // "kapattım" sanan kullanıcı masaüstü kısayoluna tekrar tıklayabiliyor.
            // O anda iki ZapretTR birden açık oluyor ve ikincisinde her şey
            // bozuluyor: winws aynı filtreyle ikinci kez açılamadığı için "1
            // koduyla kapandı" veriyor (kullanıcıya göre "Başlat çalışmıyor") ve
            // daha kötüsü, ikinci örnek kapanırken sistem DNS yedeğini geri alıp
            // SİLİYOR; birinci örneğin şifreli DNS'i sessizce devre dışı
            // kalıyor, geri dönüş kaydı da kalmıyor.
            if (_suppressAlreadyRunningMessage)
            {
                Shutdown();
                return;
            }

            MessageBox.Show(
                "ZapretTR zaten çalışıyor.\n\n"
                + "Pencere kapalıysa saatin yanındaki bildirim alanındadır: "
                + "simgeye çift tıklayarak geri getirebilirsiniz.\n\n"
                + "Uygulamayı tamamen kapatmak için o simgeye sağ tıklayıp \"Çıkış\" deyin.",
                "ZapretTR",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        base.OnStartup(e);

        // PENCERE YALNIZCA BURADA KURULUR, App.xaml'daki StartupUri ile DEĞİL.
        //
        // StartupUri, OnStartup Shutdown() çağırıp dönse BİLE ana pencereyi
        // kuruyordu; Shutdown yalnızca sıraya konuyor. Pencereyle birlikte
        // görünüm modeli de kuruluyor ve kurucusu iş yapıyor: servis durumunu
        // okuyup yapılandırmaya yazıyor, DNS kurtarması deniyor, güncelleme
        // sorusu gönderiyor. Yani --uninstall-services, --install-services,
        // 10 dakikada bir koşan --dns-guard ve "zaten çalışıyor" diyen ikinci
        // örnek, hepsi arkada görünmez bir arayüz çalıştırıyordu. Ölçüldü
        // (2026-09-13): kurulum paketinin çağırdığı --uninstall-services,
        // servisleri sildikten sonra bu yoldan config.json'a
        // "servicePaused": false yazdı ve VPN için duraklatılmış servis
        // yükseltmeden sonra çalışır hâlde geri geldi.
        //
        // Tema pencereden ÖNCE uygulanıyor: sonra uygulanırsa pencere bir an açık
        // renkte görünüp koyuya dönüyor.
        try
        {
            ThemeManager.Apply(ThemeManager.Resolve(ConfigStore.Load().Theme));
        }
        catch (Exception)
        {
            // Yapılandırma okunamadı; açık temayla devam. Pencere yine açılmalı.
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Kısayola ikinci kez tıklanırsa uyarı yerine bu pencere öne gelsin.
        _activationListener = InstanceActivation.StartListening(Dispatcher, window.BringToFront);

        // Dışarıya dokunan açılış işleri burada, görünüm modelinin kurucusunda DEĞİL:
        // kurucu testlerde de koşuyor. Oradayken temizlik gerçek güncelleme klasörünü
        // siliyor, güncelleme sorgusu da her test koşumunda GitHub'a gidiyordu.
        if (window.DataContext is ViewModels.MainViewModel viewModel)
        {
            _ = viewModel.CheckForUpdateAsync();
            _ = viewModel.DeleteOldUpdatePackagesAsync();
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _activationListener?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Başka bir örnek çalışıyorken: onun penceresini öne getirir ya da kapanmasını bekler.
    /// </summary>
    /// <returns>
    /// true: bu örnek normal açılışa devam etmeli (önceki örnek kapandı ve kilit alındı).
    /// false: bu örnek çıkmalı; ya pencere öne getirildi ya da eski uyarı gösterilecek.
    /// </returns>
    /// <remarks>
    /// Pencere öne getirildiyse uyarı göstermeden çıkıyoruz. Gerekçesi
    /// InstanceActivation'da. <see cref="_suppressAlreadyRunningMessage"/> uyarının
    /// gösterilmemesi gerektiğini çağırana bildiriyor.
    /// </remarks>
    private bool HandOverToRunningInstance()
    {
        switch (InstanceActivation.TryActivateExisting(TimeSpan.FromSeconds(3)))
        {
            case ActivationResult.Shown:
                _suppressAlreadyRunningMessage = true;
                return false;

            case ActivationResult.Closing:
                // "Çıkış"a basılmış ve temizlik sürüyor (winws durduruluyor, DNS geri
                // alınıyor). Eskiden burada "zaten çalışıyor" deniyordu; bu yanlıştı.
                // Önceki örnek çıkınca kilit bize geçiyor ve normal açılıyoruz.
                return WaitForInstanceLock(TimeSpan.FromSeconds(20));

            default:
                // Dinleyen yok (başka oturum) ya da cevap gelmedi (donmuş olabilir):
                // eski uyarı.
                return false;
        }
    }

    /// <summary>Önceki örneğin bıraktığı tek örnek kilidini bekler.</summary>
    private static bool WaitForInstanceLock(TimeSpan timeout)
    {
        if (_instanceLock is null)
        {
            return false;
        }

        try
        {
            return _instanceLock.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // Önceki örnek kilidi bırakmadan öldü; kilit yine de artık bizde.
            return true;
        }
    }

    /// <summary>Tek örnek kilidini alır. Başka bir örnek varsa false.</summary>
    private static bool TryClaimSingleInstance()
    {
        try
        {
            // Global: uygulama her zaman yükseltilmiş çalışıyor ve yükseltilmiş
            // süreç farklı bir oturumda açılabiliyor. Yerel ad alanı o durumda
            // iki örneği birbirinden habersiz bırakırdı.
            //
            // Ad DnsGuard'dan geliyor: bekçi "arayüz açık mı" sorusunu bu kilide
            // bakarak cevaplıyor. İki yerde ayrı yazılıp ayrışırsa bekçi açık bir
            // arayüzün DNS yönlendirmesini geri alır.
            _instanceLock = new Mutex(initiallyOwned: true, DnsGuard.AppInstanceMutexName, out var yeni);
            return yeni;
        }
        catch (Exception)
        {
            // Kilit kurulamadı. Tek örnek güvencesi bir kolaylık; uygulamanın
            // hiç açılmamasına sebep olmamalı.
            return true;
        }
    }

    /// <summary>
    /// Beklenmedik hatayı diske yazar ve kullanıcıya söyler.
    /// </summary>
    /// <remarks>
    /// Dosyaya yazmak şart: çökme uygulama kapanırken olursa pencere gösterecek
    /// zaman kalmıyor, ama kullanıcının bize gönderebileceği bir dosya kalıyor.
    /// </remarks>
    private static void ReportCrash(Exception? exception)
    {
        var metin = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}"
                    + (exception?.ToString() ?? "(ayrinti yok)")
                    + Environment.NewLine + new string('-', 60) + Environment.NewLine;

        string? yol = null;
        try
        {
            Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
            yol = Path.Combine(WinDivertCleanup.ConfigDirectory, "cokme.log");
            File.AppendAllText(yol, metin);
        }
        catch (Exception)
        {
            // Diske yazamadıysak en azından pencerede gösterelim.
            yol = null;
        }

        try
        {
            MessageBox.Show(
                "ZapretTR beklenmedik bir hatayla karşılaştı.\n\n"
                + (exception?.Message ?? "(ayrıntı yok)")
                + (yol is null ? string.Empty : "\n\nAyrıntılar: " + yol),
                "ZapretTR",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Pencere de açılamıyorsa yapılabilecek bir şey kalmadı.
        }
    }

    /// <summary>Kayıtlı yapılandırmayla servisleri kurar. Çıkış kodu döner.</summary>
    private static int RunServiceInstall()
    {
        try
        {
            if (!ElevationGuard.IsElevated())
            {
                return 2;
            }

            var config = ConfigStore.Load();
            if (string.IsNullOrWhiteSpace(config.SelectedStrategyArgs))
            {
                // Kayıtlı bir seçim yoksa hangi stratejinin kurulacağını tahmin
                // etmek yanlış olur; kullanıcı önce uygulamayı açıp seçmeli.
                return 3;
            }

            var vendor = VendorPaths.Locate();
            var profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
            var profile = config.SelectedIspId is null ? null : profiles.FindById(config.SelectedIspId);

            var winners = RuntimeSelection.Build(profile, config.SelectedStrategyArgs);

            // Arayüzün kurduğu komutla BİREBİR aynı daraltma: servis, kullanıcının
            // denediği şeyden farklı davranmamalı (issue #1).
            var alanlar = HostlistStore
                .Load(profiles.Root)
                .DomainsFor(RuntimeSelection.VerifiedCategories(profile, winners), config.CustomTarget);

            var arguments = new WinwsCommandBuilder(vendor).BuildRuntimeCommand(winners, alanlar);

            // Duraklatılmış servis duraklatılmış olarak geri kurulur: güncelleme,
            // kullanıcının VPN için kapattığı korumayı habersizce açmamalı.
            var steps = ServiceManager
                .InstallAsync(vendor, arguments, config.SecureDnsEnabled, startPaused: config.ServicePaused)
                .GetAwaiter().GetResult();

            return steps.All(s => s.Succeeded) ? 0 : 1;
        }
        catch (Exception)
        {
            return 4;
        }
    }

    /// <summary>Bir DNS bekçisi turu koşar. Çıkış kodu: 0 normal, 2 yetki yok, 4 hata.</summary>
    private static int RunDnsGuard()
    {
        try
        {
            if (!ElevationGuard.IsElevated())
            {
                return 2;
            }

            // Çözümleyiciye tanınan süre görevin 5 dakikalık sınırının çok altında:
            // açılışta dnscrypt önce ağı (en çok 60 sn) sonra listeyi bekliyor.
            var lines = DnsGuard.RunAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
            DnsGuard.AppendLog(lines);
            return 0;
        }
        catch (Exception ex)
        {
            DnsGuard.AppendLog(["Bekci turu hatayla bitti: " + ex.Message]);
            return 4;
        }
    }

    private static int RunRegisterDnsGuard()
    {
        try
        {
            if (!ElevationGuard.IsElevated())
            {
                return 2;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return 4;
            }

            var (succeeded, _) = DnsGuardTask.RegisterAsync(exe).GetAwaiter().GetResult();
            return succeeded ? 0 : 1;
        }
        catch (Exception)
        {
            return 4;
        }
    }

    private static int RunUnregisterDnsGuard()
    {
        try
        {
            if (!ElevationGuard.IsElevated())
            {
                return 2;
            }

            return DnsGuardTask.UnregisterAsync().GetAwaiter().GetResult() ? 0 : 1;
        }
        catch (Exception)
        {
            return 4;
        }
    }

    /// <summary>
    /// Servisleri ve sürücüleri söker. Kaldırmayı ASLA durdurmaz; yalnızca sonucu
    /// çıkış koduyla BİLDİRİR (0 başarılı, 2 yetki yok, 4 istisna).
    /// </summary>
    /// <remarks>
    /// Eskiden <c>void</c>'di: çağıran taraf HER ZAMAN 0 görüyordu, çünkü dönecek bir
    /// şey yoktu. Kaldırmayı bir hata yüzünden durdurmamak doğru bir karar (gerekçesi
    /// aşağıda), ama SESSİZ KALMAK ayrı bir şey; bu yol başarısız olursa kullanıcının
    /// sistem DNS'i 127.0.0.1'de, çözümleyicisiz kalabilir. Projenin en kötü senaryosu
    /// tam olarak bu.
    ///
    /// Kod döndürmek kaldırmayı hâlâ durdurmuyor (Inno <c>[UninstallRun]</c> dönüş
    /// kodunu zaten kullanmıyor); kazanılan şey, CI'daki
    /// <c>if ($p.ExitCode -ne 0) { throw }</c> denetiminin artık GERÇEKTEN bir şey
    /// ölçmesi. Çıkış kodunun bu yapıda doğru döndüğü ölçüldü (bkz. OnStartup).
    /// </remarks>
    private static int RunServiceCleanup()
    {
        try
        {
            // Kaldırıcı zaten yönetici olarak çalışıyor; yine de yetki yoksa
            // sessizce geçmek yerine hiçbir şey yapmamak doğru: yarım kalmış bir
            // temizlik, hiç yapılmamış olandan daha kötü durumlar bırakabilir.
            if (!ElevationGuard.IsElevated())
            {
                return 2;
            }

            ServiceManager.UninstallAsync().GetAwaiter().GetResult();

            // SÜRÜCÜYÜ DE ÇEKİRDEKTEN KALDIR. ServiceManager yalnızca ZapretTR
            // servislerini söker; "windivert" sürücüsüne dokunmaz. Sonucu gerçek bir
            // kullanıcıda görüldü: bir test koşumundan sonra sürücü çekirdekte asılı
            // kalıyor, WinDivert64.sys kilitleniyor ve
            //
            //   - YÜKSELTME dosyayı değiştiremiyor: "DeleteFile tamamlanamadı; kod 5.
            //     Erişim engellendi." Kullanıcıya "bu dosya atlansın" demekten başka
            //     seçenek kalmıyor.
            //   - KALDIRMA klasörü boşaltamıyor; geriye kalıntı dosyalar kalıyor ve
            //     bir sonraki kurulum aynı duvara tosluyor.
            //
            // removeConfig: false. Bu yol yükseltme sırasında da çalışıyor ve
            // kullanıcının profil seçimini, öğrenilmiş doğrulamalarını silmek
            // yanlış olurdu. Kaldırma zaten [UninstallDelete] ile klasörü temizliyor.
            WinDivertCleanup.RunAsync(removeConfig: false).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception)
        {
            // Kaldırmayı bir istisna yüzünden durdurmuyoruz. Servis zaten yoksa
            // ya da başka bir şey ters gittiyse kullanıcının kaldırma işlemi
            // yine de tamamlanmalı. Kod dönüyor ki sessiz kalmasın.
            return 4;
        }
    }
}
