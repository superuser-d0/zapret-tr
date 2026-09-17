using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZapretTr.App;

/// <summary>
/// Kaynak anahtarı dizgisini gerçek fırçaya çevirir.
/// </summary>
/// <remarks>
/// Görünüm modeli durumu bir anahtar olarak veriyor ("StatusRunningBrush"), fırçanın
/// kendisini değil. Böylece renk kararları App.xaml'de tek yerde kalıyor ve görünüm
/// modeli çizim tipleriyle uğraşmıyor; ileride koyu tema eklendiğinde değişecek
/// tek yer kaynak sözlüğü olur.
/// </remarks>
public sealed class ResourceKeyConverter : IValueConverter
{
    public static readonly ResourceKeyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return Brushes.Transparent;
        }

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Tek yonlu donusturucu.");
}
