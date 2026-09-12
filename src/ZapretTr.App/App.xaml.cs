using System.Windows;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.App;

/// <summary>Uygulama giris noktasi.</summary>
public partial class App : Application
{
    /// <summary>
    /// Kaldirma sirasinda servisleri sokup DNS'i geri almak icin kullanilan bayrak.
    /// </summary>
    /// <remarks>
    /// Kaldirici bu bayrakla cagiriyor. Adim atlanirsa kullanicinin sistem DNS'i
    /// 127.0.0.1'de kalir, dnscrypt-proxy de silinmis olur ve makine hicbir adi
    /// cozemez -- kaldirma sirasinda yapilabilecek en kotu sey bu.
    /// </remarks>
    private const string UninstallServicesFlag = "--uninstall-services";

    /// <summary>
    /// Servisleri kayitli yapilandirmayla kurar ve cikar.
    /// </summary>
    /// <remarks>
    /// Arayuz acmadan kurulum yapabilmek icin: sessiz dagitimda ve otomatik
    /// testlerde dugmeye tiklanamiyor. Kayitli yapilandirma yoksa hicbir sey
    /// yapmadan cikar -- hangi stratejinin kurulacagini tahmin etmek yanlis
    /// olurdu.
    /// </remarks>
    private const string InstallServicesFlag = "--install-services";

    /// <summary>
    /// Ayni anda yalnizca bir arayuz ornegi calissin diye tutulan kilit.
    /// </summary>
    /// <remarks>
    /// Alan olarak duruyor cunku Mutex toplanirsa kilit de birakilir; degiskeni
    /// yerelde tutmak, ikinci ornegin ilkini gormemesine yol acardi.
    /// </remarks>
    private static Mutex? _instanceLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, UninstallServicesFlag, StringComparison.OrdinalIgnoreCase)))
        {
            // Arayuz hic acilmadan is yapilip cikiliyor: kaldirici bunu sessiz
            // calistiriyor ve pencere acilmasi kullaniciyi saskina cevirirdi.
            RunServiceCleanup();
            Shutdown();
            return;
        }

        if (e.Args.Any(a => string.Equals(a, InstallServicesFlag, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = RunServiceInstall();
            Shutdown();
            return;
        }

        // Beklenmedik hatada SESSIZCE KAYBOLMA. Bu kanca olmadan, arayuz
        // kurulurken cikan bir hata Windows'un kendi cokme penceresiyle
        // sonuclaniyor ve kullanicinin elinde "acilmiyor"dan baska bir sey
        // kalmiyordu -- teshis edilemeyen bildirimlerin en kotu sinifi.
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception);

            // Isaretleniyor ki uygulama ayakta kalsin: yarim calisan bir pencere,
            // kaybolan bir pencereden iyidir -- kullanici en azindan "Raporu
            // Kaydet" dugmesine ulasabilir.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash(args.ExceptionObject as Exception);

        if (!TryClaimSingleInstance())
        {
            // IKINCI ORNEK CALISMAMALI.
            //
            // Pencereyi X ile kapatmak uygulamayi bildirim alanina indiriyor, yani
            // "kapattim" sanan kullanici masaustu kisayoluna tekrar tiklayabiliyor.
            // O anda iki ZapretTR birden acik oluyor ve ikincisinde her sey
            // bozuluyor: winws ayni filtreyle ikinci kez acilamadigi icin "1
            // koduyla kapandi" veriyor (kullaniciya gore "Baslat calismiyor"), ve
            // daha kotusu, ikinci ornek kapanirken sistem DNS yedegini geri alip
            // SILIYOR -- birinci ornegin sifreli DNS'i sessizce devre disi
            // kaliyor, geri donus kaydi da kalmiyor.
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
    }

    /// <summary>Tek ornek kilidini alir. Baska bir ornek varsa false.</summary>
    private static bool TryClaimSingleInstance()
    {
        try
        {
            // Global: uygulama her zaman yukseltilmis calisiyor ve yukseltilmis
            // surec farkli bir oturumda acilabiliyor. Yerel ad alani o durumda
            // iki ornegi birbirinden habersiz birakirdi.
            _instanceLock = new Mutex(initiallyOwned: true, @"Global\ZapretTR-tek-ornek", out var yeni);
            return yeni;
        }
        catch (Exception)
        {
            // Kilit kurulamadi. Tek ornek guvencesi bir kolaylik; uygulamanin
            // hic acilmamasina sebep olmamali.
            return true;
        }
    }

    /// <summary>
    /// Beklenmedik hatayi diske yazar ve kullaniciya soyler.
    /// </summary>
    /// <remarks>
    /// Dosyaya yazmak sart: cokme uygulama kapanirken olursa pencere gosterecek
    /// zaman kalmiyor, ama kullanicinin bize gonderebilecegi bir dosya kaliyor.
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
            // Diske yazamadiysak en azindan pencerede gosterelim.
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
            // Pencere de acilamiyorsa yapilabilecek bir sey kalmadi.
        }
    }

    /// <summary>Kayitli yapilandirmayla servisleri kurar. Cikis kodu doner.</summary>
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
                // Kayitli bir secim yoksa hangi stratejinin kurulacagini tahmin
                // etmek yanlis olur; kullanici once uygulamayi acip secmeli.
                return 3;
            }

            var vendor = VendorPaths.Locate();
            var profiles = ProfileStore.Load(learned: ConfigStore.LoadLearned());
            var profile = config.SelectedIspId is null ? null : profiles.FindById(config.SelectedIspId);

            var winners = RuntimeSelection.Build(profile, config.SelectedStrategyArgs);
            var arguments = new WinwsCommandBuilder(vendor).BuildRuntimeCommand(winners);

            var steps = ServiceManager
                .InstallAsync(vendor, arguments, config.SecureDnsEnabled)
                .GetAwaiter().GetResult();

            return steps.All(s => s.Succeeded) ? 0 : 1;
        }
        catch (Exception)
        {
            return 4;
        }
    }

    private static void RunServiceCleanup()
    {
        try
        {
            // Kaldirici zaten yonetici olarak calisiyor; yine de yetki yoksa
            // sessizce gecmek yerine hicbir sey yapmamak dogru: yarim kalmis bir
            // temizlik, hic yapilmamis olandan daha kotu durumlar birakabilir.
            if (!ElevationGuard.IsElevated())
            {
                return;
            }

            ServiceManager.UninstallAsync().GetAwaiter().GetResult();

            // SURUCUYU DE CEKIRDEKTEN KALDIR. ServiceManager yalnizca ZapretTR
            // servislerini soker; "windivert" surucusune dokunmaz. Sonucu gercek bir
            // kullanicida goruldu: bir test kosumundan sonra surucu cekirdekte asili
            // kaliyor, WinDivert64.sys kilitleniyor ve
            //
            //   - YUKSELTME dosyayi degistiremiyor: "DeleteFile tamamlanamadi; kod 5.
            //     Erisim engellendi." Kullaniciya "bu dosya atlansin" demekten baska
            //     secenek kalmiyor.
            //   - KALDIRMA klasoru bosaltamiyor; geriye kalinti dosyalar kaliyor ve
            //     bir sonraki kurulum ayni duvara tosluyor.
            //
            // removeConfig: false -- bu yol yukseltme sirasinda da calisiyor ve
            // kullanicinin profil secimini, ogrenilmis dogrulamalarini silmek
            // yanlis olurdu. Kaldirma zaten [UninstallDelete] ile klasoru temizliyor.
            WinDivertCleanup.RunAsync(removeConfig: false).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Kaldirmayi bir istisna yuzunden durdurmuyoruz. Servis zaten yoksa
            // ya da baska bir sey ters gittiyse kullanicinin kaldirma islemi
            // yine de tamamlanmali.
        }
    }
}
