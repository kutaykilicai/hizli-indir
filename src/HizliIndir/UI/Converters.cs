using System.Globalization;
using System.Windows.Data;

namespace HizliIndir.UI;

public sealed class FuncConverter : IValueConverter
{
    readonly Func<object?, object?> _f;
    public FuncConverter(Func<object?, object?> f) { _f = f; }
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => _f(value);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class Converters
{
    public static string Glyph(string? category) => category switch
    {
        "Sıkıştırılmış" => "\uF012",
        "Belgeler" => "\uE8A5",
        "Müzik" => "\uE8D6",
        "Programlar" => "\uE7B8",
        "Video" => "\uE714",
        "Torrent" => "\uE896",
        _ => "\uE7C3",
    };
    public static readonly FuncConverter CategoryGlyph = new(v => Glyph(v as string));
}
