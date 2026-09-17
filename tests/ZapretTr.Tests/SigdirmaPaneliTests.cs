using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZapretTr.App;

namespace ZapretTr.Tests;

/// <summary>
/// Pencere yerleşimi: dar pencerede alt bilgi ve günlük görünür kalmalı, içerik kaymalı.
/// </summary>
/// <remarks>
/// 0.1.21'de düz Grid'le varsayılan pencerede "Çıkış" yarım, "Ayrıntılar" ve alt bilgi
/// görünmez kalıyordu. İlk düzeltme denemesi de "Ayrıntılar"ı kaybetti: günlüğün alt
/// sınırını sıfır yükseklikle ölçüyordu ve WPF DesiredSize'ı verilen alana kırptığı
/// için başlık 0 çıktı. İki hata da derlemeyi ve mevcut testleri geçiyordu.
/// </remarks>
public sealed class SigdirmaPaneliTests
{
    private const double Genislik = 400;

    [Fact]
    public void DarPencerede_AltBilgi_ve_GunlukBasligi_Gorunur_Icerik_Kayar()
    {
        var hata = RunOnStaThread(() =>
        {
            var (panel, icerik, gunluk, altBilgi) = Kur(icerikYuksekligi: 700, gunlukAcik: false);

            Yerlestir(panel, 500);

            Assert.Equal(20, altBilgi.ActualHeight, 1);
            Assert.Equal(480, UstKenar(altBilgi, panel), 1);
            Assert.Equal(30, gunluk.ActualHeight, 1);
            Assert.Equal(450, icerik.ActualHeight, 1);
            Assert.True(icerik.ExtentHeight > icerik.ViewportHeight,
                "Sigmayan icerik kaydirilabilir olmali.");
        });

        Assert.Null(hata);
    }

    [Fact]
    public void GenisPencerede_Icerik_Kaymaz_ArtanYer_Gunluge_Gider()
    {
        var hata = RunOnStaThread(() =>
        {
            var (panel, icerik, gunluk, altBilgi) = Kur(icerikYuksekligi: 700, gunlukAcik: true);

            Yerlestir(panel, 1000);

            Assert.Equal(700, icerik.ActualHeight, 1);
            Assert.Equal(280, gunluk.ActualHeight, 1);
            Assert.Equal(980, UstKenar(altBilgi, panel), 1);
        });

        Assert.Null(hata);
    }

    [Fact]
    public void AcikGunluk_EnAzYuksekligini_Korur()
    {
        var hata = RunOnStaThread(() =>
        {
            var (panel, icerik, gunluk, _) = Kur(icerikYuksekligi: 700, gunlukAcik: true);
            panel.GunlukEnAzYukseklik = 170;

            Yerlestir(panel, 600);

            Assert.Equal(170, gunluk.ActualHeight, 1);
            Assert.Equal(410, icerik.ActualHeight, 1);
        });

        Assert.Null(hata);
    }

    private static (SigdirmaPaneli Panel, ScrollViewer Icerik, FrameworkElement Gunluk, FrameworkElement AltBilgi)
        Kur(double icerikYuksekligi, bool gunlukAcik)
    {
        var icerik = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = icerikYuksekligi },
        };

        // Kapalı Expander gibi: yalnızca başlığı kadar yer ister. Açık günlük gibi:
        // verilen alanın tamamını ister (iç öğe her zaman daha büyük).
        FrameworkElement gunluk = gunlukAcik
            ? new Border { Child = new Border { Height = 5000 } }
            : new Border { Height = 30 };

        var altBilgi = new Border { Height = 20 };

        var panel = new SigdirmaPaneli();
        panel.Children.Add(icerik);
        panel.Children.Add(gunluk);
        panel.Children.Add(altBilgi);

        return (panel, icerik, gunluk, altBilgi);
    }

    private static void Yerlestir(FrameworkElement panel, double yukseklik)
    {
        panel.Measure(new Size(Genislik, yukseklik));
        panel.Arrange(new Rect(0, 0, Genislik, yukseklik));
        panel.UpdateLayout();
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

        return thread.Join(TimeSpan.FromSeconds(30))
            ? captured
            : new TimeoutException("STA is parcacigi 30 saniyede bitmedi.");
    }

    private static double UstKenar(FrameworkElement el, Visual kok)
        => el.TransformToAncestor(kok).Transform(new Point(0, 0)).Y;
}
