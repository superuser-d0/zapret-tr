using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZapretTr.App;

namespace ZapretTr.Tests;

/// <summary>
/// Pencere yerlesimi: dar pencerede alt bilgi ve gunluk gorunur kalmali, icerik kaymali.
/// </summary>
/// <remarks>
/// 0.1.21'de duz Grid'le varsayilan pencerede "Çıkış" yarim, "Ayrıntılar" ve alt bilgi
/// gorunmez kaliyordu. Ilk duzeltme denemesi de "Ayrıntılar"i kaybetti: gunlugun alt
/// sinirini sifir yukseklikle olcuyordu ve WPF DesiredSize'i verilen alana kirptigi
/// icin baslik 0 cikti. Iki hata da derlemeyi ve mevcut testleri geciyordu.
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

        // Kapali Expander gibi: yalnizca basligi kadar yer ister. Acik gunluk gibi:
        // verilen alanin tamamini ister (ic eleman her zaman daha buyuk).
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

        return thread.Join(TimeSpan.FromSeconds(30))
            ? captured
            : new TimeoutException("STA is parcacigi 30 saniyede bitmedi.");
    }

    private static double UstKenar(FrameworkElement el, Visual kok)
        => el.TransformToAncestor(kok).Transform(new Point(0, 0)).Y;
}
