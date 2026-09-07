using System.Windows;
using ZapretTr.Core.Engine;

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

        base.OnStartup(e);
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
        }
        catch (Exception)
        {
            // Kaldirmayi bir istisna yuzunden durdurmuyoruz. Servis zaten yoksa
            // ya da baska bir sey ters gittiyse kullanicinin kaldirma islemi
            // yine de tamamlanmali.
        }
    }
}
