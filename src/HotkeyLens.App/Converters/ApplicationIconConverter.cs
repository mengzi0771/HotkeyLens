using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HotkeyLens.App.Converters;

/// <summary>
/// Loads an application icon without taking a lock on the executable. Paths may be
/// plain files or registry-style icon references such as <c>"C:\App\app.exe",0</c>.
/// </summary>
public sealed class ApplicationIconConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = value as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var path = NormalizePath(raw);
        if (path is null)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        var image = LoadIcon(path);
        lock (CacheLock)
        {
            Cache[path] = image;
        }

        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static BitmapSource? LoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }

            var large = IntPtr.Zero;
            var small = IntPtr.Zero;
            try
            {
                var extracted = ExtractIconEx(path, 0, out large, out small, 1);
                if (extracted == 0)
                {
                    return null;
                }

                var handle = large != IntPtr.Zero ? large : small;
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                if (large != IntPtr.Zero)
                {
                    DestroyIcon(large);
                }

                if (small != IntPtr.Zero)
                {
                    DestroyIcon(small);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? NormalizePath(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0 || value.StartsWith('%'))
        {
            return null;
        }

        if (value.StartsWith('"'))
        {
            var closing = value.IndexOf('"', 1);
            if (closing > 1)
            {
                return value[1..closing];
            }
        }

        var comma = value.LastIndexOf(',');
        if (comma > 0 &&
            int.TryParse(value[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            value = value[..comma].Trim();
        }

        return value.Length == 0 ? null : value;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        int nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}