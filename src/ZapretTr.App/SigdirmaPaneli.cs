using System.Windows;
using System.Windows.Controls;

namespace ZapretTr.App;

/// <summary>
/// Pencereyi üç parçaya bölen panel: üstte kayabilen içerik, ortada günlük,
/// altta alt bilgi.
/// </summary>
/// <remarks>
/// Önce düz bir Grid'di ve 0.1.21'de gerçek bir kurulumda ölçüldü: varsayılan
/// 440x720 pencerede "Çıkış" yarım kalıyordu, "Ayrıntılar" ve sürümü yazan alt
/// bilgi hiç görünmüyordu. Güncelleme bandı, doğrulanmamış strateji notu ya da
/// test ilerleme çubuğu çıkınca "Tüm Ayarları Sıfırla" da kayboluyordu. Kaydırma
/// yoktu; kullanıcı pencereyi büyütmeyi akıl etmezse düğmeler onun için YOKTU.
///
/// Grid bunu tek başına çözemiyor. Üst kısmı bir ScrollViewer'a koymak yetmez:
/// Auto satırda ScrollViewer sonsuz yükseklik alıp hiç kaymıyor, yıldız satırda
/// ise günlük kapalıyken bile içeriğin altında boş bir yer bırakıyor. İstenen
/// sıralama şu:
///
///   1. Alt bilgi her zaman tam görünür.
///   2. Günlük her zaman en az kendi alt sınırı kadar yer alır: kapalıyken yalnızca
///      başlığı, açıksa <see cref="GunlukEnAzYukseklik"/>.
///   3. İçerik doğal yüksekliğini alır; sığmıyorsa geri kalana sıkışıp kayar.
///   4. Artan yer günlüğe gider; pencere büyütülünce günlük büyür.
///
/// Çocuklar sırayla: [0] içerik (ScrollViewer olmalı), [1] günlük, [2] alt bilgi.
/// </remarks>
public sealed class SigdirmaPaneli : Panel
{
    /// <summary>Açık günlüğün, içerik kaysa bile bırakılmayan yüksekliği.</summary>
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

    /// <summary>Açık günlüğün, içerik kaysa bile bırakılmayan yüksekliği.</summary>
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

        // Günlüğün alt sınırı: GunlukEnAzYukseklik kadar alanla ölçülüyor. Kapalı
        // Expander yalnızca başlığını istiyor, açık olan verilen alanın tamamını.
        //
        // Sıfırla ölçmek işe yaramıyor: FrameworkElement DesiredSize'ı verilen alana
        // kırpıyor ve başlık da sıfır çıkıyordu; ilk denemede "Ayrıntılar" bu yüzden
        // ekrandan kayboldu. Sonsuzla ölçmek de olmaz: açık günlükte liste bütün
        // satırları için kap üretir.
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

        // Ölçümden sonra yükseklik değişmiş olabilir (pencere kenarı sürükleniyor):
        // öncelik sırası aynı kalsın diye paylaşım burada yeniden yapılıyor.
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
