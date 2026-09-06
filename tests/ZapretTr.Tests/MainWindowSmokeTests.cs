using System.Windows;
using ZapretTr.App;
using ZapretTr.App.ViewModels;

namespace ZapretTr.Tests;

/// <summary>
/// Arayuzun gercekten kurulabildigini dogrular.
/// </summary>
/// <remarks>
/// Derlemenin gecmesi XAML'in calistigi anlamina gelmiyor: eksik bir kaynak anahtari,
/// yanlis yazilmis bir baglama yolu ya da cozulemeyen bir donusturucu ancak pencere
/// gercekten kurulup yerlesim hesaplanirken patlar. Bu test tam olarak o ani
/// yakaliyor -- yoksa hatayi ilk goren kullanici olurdu.
///
/// WPF STA is parcacigi gerektirdigi icin test kendi is parcacigini aciyor.
/// </remarks>
public sealed class MainWindowSmokeTests
{
    [Fact]
    public void MainWindow_Kurulabiliyor_ve_YerlesimHesaplaniyor()
    {
        var error = RunOnStaThread(() =>
        {
            // App.xaml'deki kaynak sozlugu yuklensin: pencere StaticResource ile
            // oradaki fircalara ve stillere basvuruyor.
            var app = new global::ZapretTr.App.App();
            app.InitializeComponent();

            var window = new MainWindow();

            // Yerlesimi zorlamak sart: baglamalar ancak burada degerlendiriliyor.
            window.Measure(new Size(440, 720));
            window.Arrange(new Rect(0, 0, 440, 720));
            window.UpdateLayout();

            window.Close();
            app.Shutdown();
        });

        Assert.Null(error);
    }

    [Fact]
    public void GorunumModeli_Profilleri_Yukluyor()
    {
        var error = RunOnStaThread(() =>
        {
            var viewModel = new MainViewModel();

            // "Bilmiyorum" + en az bir gercek profil.
            Assert.True(viewModel.IspChoices.Count > 1, "ISS listesi bos geldi.");
            Assert.Contains(viewModel.IspChoices, c => c.Profile is null);
            Assert.Contains(viewModel.IspChoices, c => c.Profile?.Id == "superonline");

            // Bir ISS secili oldugunda strateji listesi de dolmali.
            Assert.NotEmpty(viewModel.StrategyChoices);
            Assert.NotNull(viewModel.SelectedStrategy);
        });

        Assert.Null(error);
    }

    [Fact]
    public void Superonline_Secildiginde_DogrulanmadiRozeti_Gorunur()
    {
        // Baslangicta hicbir aday dogrulanmis degil, dolayisiyla rozet gorunmeli.
        // Bu, kullaniciya "denenmeye deger" ile "calisiyor" arasindaki farki
        // gosteren tek isaret.
        var error = RunOnStaThread(() =>
        {
            var viewModel = new MainViewModel();
            var superonline = viewModel.IspChoices.First(c => c.Profile?.Id == "superonline");

            viewModel.SelectedIsp = superonline;

            Assert.NotNull(viewModel.SelectedStrategy);
            Assert.True(viewModel.IsSelectedStrategyUnverified);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.VerificationNote));
        });

        Assert.Null(error);
    }

    /// <summary>Verilen isi STA is parcaciginda calistirir; yakalanan hatayi dondurur.</summary>
    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Askida kalan bir WPF is parcacigi test kosumunu sonsuza kadar bekletir.
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            return new TimeoutException("STA is parcacigi 30 saniyede bitmedi.");
        }

        return captured;
    }
}
