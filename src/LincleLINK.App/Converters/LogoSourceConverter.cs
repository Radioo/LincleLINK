using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LincleLINK.App.Converters;

/// <summary>
/// Converts a logo source string (an <c>avares://</c> resource URI or an absolute
/// file path for a custom logo) into a <see cref="Bitmap"/>. Avalonia does not
/// auto-convert plain strings bound to <c>Image.Source</c>, so bindings must go
/// through this converter.
/// </summary>
public sealed class LogoSourceConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string source || source.Length == 0)
        {
            return null;
        }

        if (Cache.TryGetValue(source, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            if (source.StartsWith("avares://", StringComparison.Ordinal))
            {
                var uri = new Uri(source);
                using var stream = AssetLoader.Open(uri);
                bitmap = new Bitmap(stream);
            }
            else if (File.Exists(source))
            {
                bitmap = new Bitmap(source);
            }
        }
        catch
        {
            bitmap = null;
        }

        if (bitmap is not null)
        {
            Cache[source] = bitmap;
        }

        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
