using System.Windows;
using System.Windows.Controls;

namespace ZapretTr.App;

/// <summary>
/// Pencereyi uc parcaya bolen panel: ustte kayabilen icerik, ortada gunluk,
/// altta alt bilgi.
/// </summary>
/// <remarks>
/// Once duz bir Grid'di ve 0.1.21'de gercek bir kurulumda olculdu: varsayilan
/// 440x720 pencerede "Çıkış" yarim kaliyordu, "Ayrıntılar" ve surumu yazan alt
/// bilgi hic gorunmuyordu. Guncelleme bandi, dogrulanmamis strateji notu ya da
/// test ilerleme cubugu cikinca "Tüm Ayarları Sıfırla" da kayboluyordu. Kaydirma
/// yoktu; kullanici pencereyi buyutmeyi akil etmezse dugmeler onun icin YOKTU.
///
/// Grid bunu tek basina cozemiyor. Ust kismi bir ScrollViewer'a koymak yetmez:
/// Auto satirda ScrollViewer sonsuz yukseklik alip hic kaymiyor, yildiz satirda
/// ise gunluk kapaliyken bile icerigin altinda bos bir yer birakiyor. Istenen
/// siralama su:
///
///   1. Alt bilgi her zaman tam gorunur.
///   2. Gunluk her zaman en az kendi alt siniri kadar yer alir: kapaliyken yalnizca
///      basligi, aciksa <see cref="GunlukEnAzYukseklik"/>.
///   3. Icerik dogal yuksekligini alir; sigmiyorsa geri kalana sikisip kayar.
///   4. Artan yer gunluge gider -- pencere buyutulunce gunluk buyur.
///
/// Cocuklar sirayla: [0] icerik (ScrollViewer olmali), [1] gunluk, [2] alt bilgi.
/// </remarks>
public sealed class SigdirmaPaneli : Panel
{
    /// <summary>Acik gunlugun, icerik kaysa bile birakilmayan yuksekligi.</summary>
    public static readonly DependencyProperty GunlukEnAzYukseklikProperty =
        DependencyProperty.Register(
            nameof(GunlukEnAzYukseklik),
            typeof(double),
            typeof(SigdirmaPaneli),
            new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double _icerik;
    private double _gunluk;
    private double _gunlukAltSinir;
    private double _altBilgi;

    /// <summary>Acik gunlugun, icerik kaysa bile birakilmayan yuksekligi.</summary>
    public double GunlukEnAzYukseklik
    {
        get => (double)GetValue(GunlukEnAzYukseklikProperty);
        set => SetValue(GunlukEnAzYukseklikProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var (icerik, gunluk, altBilgi) = Cocuklar();
        var genislik = availableSize.Width;
        var sonsuz = new Size(genislik, double.PositiveInfinity);

        altBilgi?.Measure(sonsuz);
        _altBilgi = altBilgi?.DesiredSize.Height ?? 0;

        // Gunlugun alt siniri: GunlukEnAzYukseklik kadar alanla olculuyor. Kapali
        // Expander yalnizca basligini istiyor, acik olan verilen alanin tamamini.
        //
        // Sifirla olcmek ise yaramiyor: FrameworkElement DesiredSize'i verilen alana
        // kirpiyor ve baslik da sifir cikiyordu -- ilk denemede "Ayrıntılar" bu yuzden
        // ekrandan kayboldu. Sonsuzla olcmek de olmaz: acik gunlukte liste butun
        // satirlari icin kap uretir.
        gunluk?.Measure(new Size(genislik, GunlukEnAzYukseklik));
        var gunlukAltSinir = gunluk?.DesiredSize.Height ?? 0;
        _gunlukAltSinir = gunlukAltSinir;

        var yukseklik = availableSize.Height;
        var icerikIcinYer = double.IsPositiveInfinity(yukseklik)
            ? double.PositiveInfinity
            : Math.Max(0, yukseklik - _altBilgi - gunlukAltSinir);

        icerik?.Measure(new Size(genislik, icerikIcinYer));
        _icerik = Math.Min(icerik?.DesiredSize.Height ?? 0, icerikIcinYer);

        _gunluk = double.IsPositiveInfinity(yukseklik)
            ? gunlukAltSinir
            : Math.Max(gunlukAltSinir, yukseklik - _altBilgi - _icerik);
        gunluk?.Measure(new Size(genislik, _gunluk));

        var enGenis = 0.0;
        foreach (UIElement cocuk in InternalChildren)
        {
            enGenis = Math.Max(enGenis, cocuk.DesiredSize.Width);
        }

        return new Size(
            double.IsPositiveInfinity(genislik) ? enGenis : genislik,
            _icerik + _gunluk + _altBilgi);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (icerik, gunluk, altBilgi) = Cocuklar();

        // Olcumden sonra yukseklik degismis olabilir (pencere kenari surukleniyor):
        // oncelik sirasi ayni kalsin diye paylasim burada yeniden yapiliyor.
        var altBilgiH = Math.Min(_altBilgi, finalSize.Height);
        var icerikH = Math.Max(0, Math.Min(_icerik, finalSize.Height - altBilgiH - _gunlukAltSinir));
        var gunlukH = Math.Max(0, finalSize.Height - altBilgiH - icerikH);

        icerik?.Arrange(new Rect(0, 0, finalSize.Width, icerikH));
        gunluk?.Arrange(new Rect(0, icerikH, finalSize.Width, gunlukH));
        altBilgi?.Arrange(new Rect(0, icerikH + gunlukH, finalSize.Width, altBilgiH));

        return finalSize;
    }

    private (UIElement? Icerik, UIElement? Gunluk, UIElement? AltBilgi) Cocuklar()
    {
        var c = InternalChildren;
        return (
            c.Count > 0 ? c[0] : null,
            c.Count > 1 ? c[1] : null,
            c.Count > 2 ? c[2] : null);
    }
}
