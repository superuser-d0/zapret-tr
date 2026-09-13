using System.IO;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Bayrakla calistirilan uygulama arayuz KURMAMALI.
/// </summary>
/// <remarks>
/// App.xaml'daki StartupUri, OnStartup Shutdown() cagirip donse bile ana pencereyi
/// kuruyordu; pencereyle birlikte gorunum modeli de kurulup is yapiyordu. Olculen
/// sonucu: kurulum paketinin cagirdigi --uninstall-services, servisleri sildikten
/// sonra bu gorunmez arayuz uzerinden config.json'a "servicePaused": false yazdi
/// ve kullanicinin VPN icin duraklattigi servis yukseltmeden sonra calisir geldi.
/// Ayni yol 10 dakikada bir kosan DNS bekcisinde ve ikinci ornekte de aciktı.
///
/// Davranisi WPF'in kendisi belirliyor ve bir birim testinde uygulama baslatmak
/// mumkun degil; bu yuzden kosul kaynakta sabitleniyor.
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
    public void Pencere_OnStartup_icinde_kuruluyor()
    {
        // StartupUri kaldirilip pencere kurulmazsa uygulama hic acilmaz; bu test
        // yukaridakinin "duzeltme" diye pencereyi tamamen kaybetmesini onluyor.
        var kod = File.ReadAllText(IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml.cs"));

        Assert.Contains("new MainWindow()", kod, StringComparison.Ordinal);
    }
}
