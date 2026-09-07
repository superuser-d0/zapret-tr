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

        base.OnStartup(e);
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
