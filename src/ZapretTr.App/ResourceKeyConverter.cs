using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZapretTr.App;

/// <summary>
/// Kaynak anahtari dizgisini gercek fircaya cevirir.
/// </summary>
/// <remarks>
/// Gorunum modeli durumu bir anahtar olarak veriyor ("StatusRunningBrush"), fircanin
/// kendisini degil. Boylece renk kararlari App.xaml'de tek yerde kaliyor ve gorunum
/// modeli cizim tipleriyle ugrasmiyor -- ileride koyu tema eklendiginde degisecek
/// tek yer kaynak sozlugu olur.
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
