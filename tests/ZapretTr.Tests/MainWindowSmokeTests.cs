using System.Windows;
using System.Windows.Controls;
using ZapretTr.App;
using ZapretTr.App.ViewModels;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Arayüzün gerçekten kurulabildiğini doğrular.
/// </summary>
/// <remarks>
/// Derlemenin geçmesi XAML'ın çalıştığı anlamına gelmiyor: eksik bir kaynak anahtarı,
/// yanlış yazılmış bir bağlama yolu ya da çözülemeyen bir dönüştürücü ancak pencere
/// gerçekten kurulup yerleşim hesaplanırken patlar. Bu test tam olarak o anı
/// yakalıyor; yoksa hatayı ilk gören kullanıcı olurdu.
///
/// WPF STA iş parçacığı gerektirdiği için test kendi iş parçacığını açıyor.
/// </remarks>
public sealed class MainWindowSmokeTests
{
    [Fact]
    public void MainWindow_Kurulabiliyor_ve_YerlesimHesaplaniyor()
    {
        var error = RunOnStaThread(() =>
        {
            // App.xaml'deki kaynak sözlüğü yüklensin: pencere StaticResource ile
            // oradaki fırçalara ve stillere başvuruyor.
            var app = new global::ZapretTr.App.App();
            app.InitializeComponent();

            var window = new MainWindow();

            // Yerleşimi zorlamak şart: bağlamalar ancak burada değerlendiriliyor.
            window.Measure(new Size(440, 720));
            window.Arrange(new Rect(0, 0, 440, 720));
            window.UpdateLayout();

            // Üst kısım kayabilen panelde olmalı. Düz Grid'e dönülürse varsayılan
            // pencerede "Çıkış", "Ayrıntılar" ve alt bilgi yine ekrandan taşar
            // (0.1.21'de gerçek kurulumda ölçüldü). Panelin davranışı
            // SigdirmaPaneliTests'te; burada XAML'ın onu kullandığı sabitleniyor.
            // Günlük, tema düğmesiyle birlikte bir Grid'in içinde.
            if (window.Content is not SigdirmaPaneli panel
                || panel.Children.Count != 3
                || panel.Children[0] is not ScrollViewer
                || panel.Children[1] is not Grid gunlukSatiri
                || !gunlukSatiri.Children.OfType<Expander>().Any())
            {
                throw new InvalidOperationException(
                    "Pencerenin kok yerlesimi SigdirmaPaneli [ScrollViewer, Grid(Expander, tema), alt bilgi] degil.");
            }

            // KOYU TEMA DA KURULABİLMELİ. Tema sözlüğünde eksik bir anahtar ya da
            // bozuk bir şablon ancak tema uygulanıp yerleşim yeniden hesaplanırken
            // patlar; ne derleme ne de açık temadaki koşum bunu görür.
            ThemeManager.Apply(AppTheme.Dark);
            window.UpdateLayout();
            window.Measure(new Size(440, 720));
            window.Arrange(new Rect(0, 0, 440, 720));

            if (Application.Current.TryFindResource("ThemeName") as string != "Dark")
            {
                throw new InvalidOperationException("Koyu tema sozlugu uygulanmadi.");
            }

            ThemeManager.Apply(AppTheme.Light);
            window.UpdateLayout();

            if (Application.Current.TryFindResource("ThemeName") as string != "Light")
            {
                throw new InvalidOperationException("Acik temaya geri donulmedi.");
            }

            // Düğmeler AYNI pencerede sınanıyor: bir süreçte yalnızca tek bir WPF
            // Application olabildiği için her kontrole ayrı test sınıfı açmak
            // koşumu kilitliyordu.
            foreach (var etiket in new[] { "Raporu Kaydet", "Hata Bildir", "Tüm Ayarları Sıfırla", "Çıkış", "tema" })
            {
                var dugme = DugmeyiBul(window, etiket)
                            ?? throw new InvalidOperationException(
                                $"\"{etiket}\" dugmesi pencerede yok.");

                if (dugme.Visibility != Visibility.Visible)
                {
                    throw new InvalidOperationException(
                        $"\"{etiket}\" gorunur degil: " + dugme.Visibility);
                }

                // ASIL KONTROL. "Raporu Kaydet" önce kapalı bir Expander'ın
                // içindeydi ve ağaçta GÖRÜNÜYORDU; yani yukarıdaki iki kontrol
                // de geçiyordu. Kullanıcı için ise düğme yoktu: paneli açmadan
                // göremiyordu ve gerçek bir kurulumda tam olarak bunu bildirdi
                // (0.1.10). Aynı tuzağa bir daha düşmeyelim diye her yeni düğme
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

            // "Bilmiyorum" + en az bir gerçek profil.
            Assert.True(viewModel.IspChoices.Count > 1, "ISS listesi bos geldi.");
            Assert.Contains(viewModel.IspChoices, c => c.Profile is null);
            Assert.Contains(viewModel.IspChoices, c => c.Profile?.Id == "superonline");

            // İLK AÇILIŞ: tespit edilmemiş bir sağlayıcı seçilmiş gibi GÖSTERİLMEZ.
            //
            // Kurulum paketiyle gerçek bir makinede görüldü: Türk Telekom hattında
            // uygulama "Turkcell Superonline" seçili açılıyordu, çünkü liste ilk
            // gerçek profili seçiyordu. Kullanıcının Başlat'a basması, kendi hattında
            // hiç denenmemiş bir stratejiyi trafiğe uygulaması demekti.
            //
            // ConfigStore statik ve %ProgramData%'dan okuyor; kayıtlı seçimi olan bir
            // makinede geri yükleme doğru şekilde devreye girer ve bu iddia geçersiz
            // olur. O durumda atlıyoruz; sessizce yanlış doğrulamaktansa.
            if (ConfigStore.Load().SelectedIspId is null)
            {
                Assert.NotNull(viewModel.SelectedIsp);
                Assert.Null(viewModel.SelectedIsp!.Profile);
                Assert.Null(viewModel.SelectedStrategy);
                Assert.Empty(viewModel.StrategyChoices);
                Assert.False(viewModel.CanStart, "Saglayici bilinmeden Baslat acik olmamali.");
            }

            // Bir İSS SEÇİLDİĞİNDE strateji listesi dolmalı.
            viewModel.SelectedIsp = viewModel.IspChoices.First(c => c.Profile?.Id == "turk-telekom");
            Assert.NotEmpty(viewModel.StrategyChoices);
            Assert.NotNull(viewModel.SelectedStrategy);
            Assert.True(viewModel.CanStart);

            // DURUM BANDI, STRATEJİ YOKKEN "HAZIR" DEMEMELİ.
            //
            // Yeni kurulmuş makinede tablo şuydu: strateji seçilmemiş, Başlat
            // kapalı, winws çalışmıyor, koruma yok; ve ekranın en üstünde, en
            // büyük puntoyla "SİSTEM HAZIR". Teknik olmayan kullanıcı bunu
            // "kuruldu, çalışıyor" diye okuyup pencereyi kapatıyordu. Sahadan
            // gelen "kurdum, olmadı" bildiriminin en ucuz açıklaması buydu.
            //
            // İddia KOŞULSUZ olarak burada: ilk açılış durumuna yukarıdaki
            // `if` içinde bakmak, kayıtlı yapılandırması olan bir makinede
            // testin sessizce hiç koşmaması demekti; yani her zaman yeşil
            // ama hiçbir şeyi sınamayan bir iddia. Onun yerine "Bilmiyorum"a
            // ELLE dönülüyor; bu, makinenin durumundan bağımsız olarak
            // stratejisiz hâli kuruyor.
            viewModel.SelectedIsp = viewModel.IspChoices.First(c => c.Profile is null);
            Assert.Null(viewModel.SelectedStrategy);
            Assert.False(viewModel.CanStart);

            // İki şey birden bekleniyor: yanlış cümle GİTMİŞ olmalı ve yerine
            // SIRADAKİ ADIM yazılmış olmalı. Yalnızca birincisini sınamak,
            // başlığı boşaltan bir değişikliğe de yeşil verirdi.
            Assert.DoesNotContain("HAZIR", viewModel.StatusHeadline, StringComparison.Ordinal);
            Assert.Contains("PARAMETRE TESTİ", viewModel.StatusDetail, StringComparison.Ordinal);
        });

        Assert.Null(error);
    }

    [Fact]
    public void Superonline_Secildiginde_DogrulanmadiRozeti_Gorunur()
    {
        // Başlangıçta hiçbir aday doğrulanmış değil, dolayısıyla rozet görünmeli.
        // Bu, kullanıcıya "denenmeye değer" ile "çalışıyor" arasındaki farkı
        // gösteren tek işaret.
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

    /// <summary>Verilen işi STA iş parçacığında çalıştırır; yakalanan hatayı döndürür.</summary>
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

        // Askıda kalan bir WPF iş parçacığı test koşumunu sonsuza kadar bekletir.
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            return new TimeoutException("STA is parcacigi 30 saniyede bitmedi.");
        }

        return captured;
    }

    /// <summary>Düğmenin ataları arasında bir Expander var mı.</summary>
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

    /// <summary>Mantıksal ağaçta içeriği verilen etiketi taşıyan düğmeyi arar.</summary>
    /// <remarks>
    /// GÖRSEL ağaç değil: pencere hiç gösterilmediğinde şablonlar uygulanmadığı
    /// için görsel ağaç eksik kalıyor ve var olan düğmeler de bulunamıyor.
    /// Mantıksal ağaç XAML'da yazdığımız yapıyı yansıtıyor; sorulan soru da bu:
    /// düğme pencereye KONULMUŞ mu.
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
