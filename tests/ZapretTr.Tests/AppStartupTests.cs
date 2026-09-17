using System.IO;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Bayrakla çalıştırılan uygulama arayüz KURMAMALI.
/// </summary>
/// <remarks>
/// App.xaml'daki StartupUri, OnStartup Shutdown() çağırıp dönse bile ana pencereyi
/// kuruyordu; pencereyle birlikte görünüm modeli de kurulup iş yapıyordu. Ölçülen
/// sonucu: kurulum paketinin çağırdığı --uninstall-services, servisleri sildikten
/// sonra bu görünmez arayüz üzerinden config.json'a "servicePaused": false yazdı
/// ve kullanıcının VPN için duraklattığı servis yükseltmeden sonra çalışır geldi.
/// Aynı yol 10 dakikada bir koşan DNS bekçisinde ve ikinci örnekte de açıktı.
///
/// Davranışı WPF'in kendisi belirliyor ve bir birim testinde uygulama başlatmak
/// mümkün değil; bu yüzden koşul kaynakta sabitleniyor.
/// </remarks>
public sealed class AppStartupTests
{
    [Fact]
    public void App_xaml_StartupUri_kullanmiyor()
    {
        var xaml = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml"));

        Assert.DoesNotContain("StartupUri", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Eski_paket_temizligi_gorunum_modeli_kurucusundan_cagrilmiyor()
    {
        // Kurucudan çağrıldığında duman testleri geliştiricinin gerçek
        // %TEMP%\ZapretTR-guncelleme klasörünü boşalttı (2026-09-14, altı paket).
        // Görünüm modeli testlerde kuruluyor; silen iş yalnızca gerçek açılışta koşmalı.
        var vm = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "ViewModels", "MainViewModel.cs"));
        var app = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml.cs"));

        // Yalnızca tanımı: başka bir çağrı yeri yok. Güncelleme sorgusu da aynı sebeple
        // (testlerde GitHub'a gerçek sorgu, saatlik sınırın tükenmesi) açılışta.
        foreach (var ad in new[] { "DeleteOldUpdatePackagesAsync", "CheckForUpdateAsync" })
        {
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(vm, @"\b" + ad + @"\("));
            Assert.Contains(ad + "()", app, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pencere_OnStartup_icinde_kuruluyor()
    {
        // StartupUri kaldırılıp pencere kurulmazsa uygulama hiç açılmaz; bu test
        // yukarıdakinin "düzeltme" diye pencereyi tamamen kaybetmesini önlüyor.
        var kod = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml.cs"));

        Assert.Contains("new MainWindow()", kod, StringComparison.Ordinal);
    }
}
