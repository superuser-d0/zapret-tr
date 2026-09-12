using System.Windows;
using System.Windows.Controls;
using ZapretTr.App;
using ZapretTr.App.ViewModels;
using ZapretTr.Core.Engine;

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

            // Dugmeler AYNI pencerede sinaniyor: bir surecte yalnizca tek bir WPF
            // Application olabildigi icin her kontrole ayri test sinifi acmak
            // kosumu kilitliyordu.
            foreach (var etiket in new[] { "Raporu Kaydet", "Hata Bildir" })
            {
                var dugme = DugmeyiBul(window, etiket)
                            ?? throw new InvalidOperationException(
                                $"\"{etiket}\" dugmesi pencerede yok.");

                if (dugme.Visibility != Visibility.Visible)
                {
                    throw new InvalidOperationException(
                        $"\"{etiket}\" gorunur degil: " + dugme.Visibility);
                }

                // ASIL KONTROL. "Raporu Kaydet" once kapali bir Expander'in
                // icindeydi ve agacta GORUNUYORDU -- yani yukaridaki iki kontrol
                // de geciyordu. Kullanici icin ise dugme yoktu: paneli acmadan
                // goremiyordu ve gercek bir kurulumda tam olarak bunu bildirdi
                // (0.1.10). Ayni tuzaga bir daha dusmeyelim diye her yeni dugme
                // bu listeye ekleniyor.
                if (ExpanderAltinda(dugme))
                {
                    throw new InvalidOperationException(
                        $"\"{etiket}\" bir Expander icinde: kullanici paneli " +
                        "acmadan goremez.");
                }
            }

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

            // ILK ACILIS: tespit edilmemis bir saglayici secilmis gibi GOSTERILMEZ.
            //
            // Kurulum paketiyle gercek bir makinede goruldu: Turk Telekom hattinda
            // uygulama "Turkcell Superonline" secili aciliyordu, cunku liste ilk
            // gercek profili seciyordu. Kullanicinin Baslat'a basmasi, kendi hattinda
            // hic denenmemis bir stratejiyi trafige uygulamasi demekti.
            //
            // ConfigStore statik ve %ProgramData%'dan okuyor; kayitli secimi olan bir
            // makinede geri yukleme dogru sekilde devreye girer ve bu iddia gecersiz
            // olur. O durumda atliyoruz -- sessizce yanlis dogrulamaktansa.
            if (ConfigStore.Load().SelectedIspId is null)
            {
                Assert.NotNull(viewModel.SelectedIsp);
                Assert.Null(viewModel.SelectedIsp!.Profile);
                Assert.Null(viewModel.SelectedStrategy);
                Assert.Empty(viewModel.StrategyChoices);
                Assert.False(viewModel.CanStart, "Saglayici bilinmeden Baslat acik olmamali.");
            }

            // Bir ISS SECILDIGINDE strateji listesi dolmali.
            viewModel.SelectedIsp = viewModel.IspChoices.First(c => c.Profile?.Id == "turk-telekom");
            Assert.NotEmpty(viewModel.StrategyChoices);
            Assert.NotNull(viewModel.SelectedStrategy);
            Assert.True(viewModel.CanStart);

            // DURUM BANDI, STRATEJI YOKKEN "HAZIR" DEMEMELI.
            //
            // Yeni kurulmus makinede tablo suydu: strateji secilmemis, Baslat
            // kapali, winws calismiyor, koruma yok -- ve ekranin en ustunde, en
            // buyuk puntoyla "SİSTEM HAZIR". Teknik olmayan kullanici bunu
            // "kuruldu, calisiyor" diye okuyup pencereyi kapatiyordu. Sahadan
            // gelen "kurdum, olmadi" bildiriminin en ucuz aciklamasi buydu.
            //
            // Iddia KOSULSUZ olarak burada: ilk acilis durumuna yukaridaki
            // `if` icinde bakmak, kayitli yapilandirmasi olan bir makinede
            // testin sessizce hic kosmamasi demekti -- yani her zaman yesil
            // ama hicbir seyi sinamayan bir iddia. Onun yerine "Bilmiyorum"a
            // ELLE donuluyor; bu, makinenin durumundan bagimsiz olarak
            // stratejisiz hali kuruyor.
            viewModel.SelectedIsp = viewModel.IspChoices.First(c => c.Profile is null);
            Assert.Null(viewModel.SelectedStrategy);
            Assert.False(viewModel.CanStart);

            // Iki sey birden bekleniyor: yanlis cumle GITMIS olmali ve yerine
            // SIRADAKI ADIM yazilmis olmali. Yalnizca birincisini sinamak,
            // basligi bosaltan bir degisiklige de yesil verirdi.
            Assert.DoesNotContain("HAZIR", viewModel.StatusHeadline, StringComparison.Ordinal);
            Assert.Contains("PARAMETRE TESTİ", viewModel.StatusDetail, StringComparison.Ordinal);
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

    /// <summary>Dugmenin atalari arasinda bir Expander var mi.</summary>
    private static bool ExpanderAltinda(DependencyObject el)
    {
        for (var p = LogicalTreeHelper.GetParent(el); p is not null;
             p = LogicalTreeHelper.GetParent(p))
        {
            if (p is Expander)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Mantiksal agacta icerigi verilen etiketi tasiyan dugmeyi arar.</summary>
    /// <remarks>
    /// GORSEL agac degil: pencere hic gosterilmediginde sablonlar uygulanmadigi
    /// icin gorsel agac eksik kaliyor ve var olan dugmeler de bulunamiyor.
    /// Mantiksal agac XAML'de yazdigimiz yapiyi yansitiyor -- sorulan soru da bu:
    /// dugme pencereye KONULMUS mu.
    /// </remarks>
    private static Button? DugmeyiBul(DependencyObject kok, string etiket)
    {
        foreach (var cocuk in LogicalTreeHelper.GetChildren(kok))
        {
            if (cocuk is Button b &&
                b.Content is string metin &&
                metin.Contains(etiket, StringComparison.Ordinal))
            {
                return b;
            }

            if (cocuk is DependencyObject d && DugmeyiBul(d, etiket) is { } alt)
            {
                return alt;
            }
        }

        return null;
    }
}
