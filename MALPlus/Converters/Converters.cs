using System.Collections.Concurrent;
using System.Globalization;
using MALPlus.Models.Enums;
using MALPlus.XShared.Utils;

namespace MALPlus.Converters;

public class StringToImageSourceConverter : IValueConverter
{
    // Every poster, avatar and thumbnail in the app goes through here, and a binding
    // is re-evaluated on every traversal and every cell recycle. Returning a NEW
    // UriImageSource each time made the image loader treat every re-evaluation as a
    // new source, so the same handful of posters were re-decoded over and over during
    // a single navigation. One shared instance per URL removes that entirely.
    private static readonly ConcurrentDictionary<string, UriImageSource> Sources = new(StringComparer.Ordinal);

    // Poster variant already on disk, so the decoded copy does not have to survive the view.
    private static readonly ConcurrentDictionary<string, FileImageSource> LocalSources = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> DownloadStarted = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> UrlToPath = new(StringComparer.Ordinal);
    private static readonly Lazy<string> CacheDir = new(() => FileSystem.CacheDirectory);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        if (LocalSources.TryGetValue(url, out var local))
            return local;

        // With a stable cache name the file can already be on disk from a previous
        // session, so serve it immediately instead of going back to the network.
        // Cache path calculation is memoized to avoid JNI calls to FileSystem and repeated SHA1 hashing.
        var path = UrlToPath.GetOrAdd(url, static u => Path.Combine(CacheDir.Value, CacheFileFor(u)));
        if (File.Exists(path))
        {
            var cached = new FileImageSource { File = path };
            LocalSources[url] = cached;
            return cached;
        }

        // Start pulling the file down once per URL in the background. The binding keeps the
        // remote source until the file lands, and later binds pick the local one up.
        if (DownloadStarted.TryAdd(url, 0))
            _ = DownloadToDiskAsync(url, path);

        return Sources.GetOrAdd(url, static key => new UriImageSource
        {
            Uri = new Uri(key),
            // Bitmaps used to be cached for 30 days with no size cap, so a details page that
            // warms Related/Recommendations/Characters/Staff held ~380MB of decoded artwork
            // (measured, all of it in GL mtrack) and got the process killed. The files stay
            // cached on disk; only the decoded copy is released with the view.
            CachingEnabled = false
        });
    }

    private static readonly HttpClient _downloadClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    // A details page warms Related/Recommendations/Characters/Staff at once, which is
    // dozens of posters. Fetching them all at full speed saturated the connection and
    // left the hero poster's own request queued behind them for seconds.
    private static readonly SemaphoreSlim _downloadGate = new(4);

    // string.GetHashCode() is randomized per process in .NET, so the cache file got a
    // different name on every launch and the disk cache never hit again. SHA1 is stable.
    private static string CacheFileFor(string url)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        return System.Convert.ToHexString(hash).ToLowerInvariant() + ".img";
    }

    private static async Task DownloadToDiskAsync(string url, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                await _downloadGate.WaitAsync();
                try
                {
                    if (!File.Exists(path))
                    {
                        var bytes = await _downloadClient.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(path, bytes);
                    }
                }
                finally
                {
                    _downloadGate.Release();
                }
            }
            LocalSources[url] = new FileImageSource { File = path };
        }
        catch
        {
            // an image that will not download just keeps using the remote source
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
///     Picks between two colours with a bool, so a chip can look tappable without a second template.
///     The parameter is the colour to use when the value is true.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value is bool b && b;
        var onColor = parameter as string;
        return on ? onColor : Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class TabColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int currentTab && parameter is string tabParam && int.TryParse(tabParam, out int tabIndex))
        {
            return currentTab == tabIndex ? "#FF6B00" : "#FFFFFF";
        }
        return "#FFFFFF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                if (parameter is string param && param == "nonempty")
                    return !string.IsNullOrWhiteSpace(s);
                if (parameter is string param3 && param3 == "longtext")
                    return s.Length > 200;
                if (parameter is string param4 && param4 == "spoiler")
                    return s.Contains("spoiler", StringComparison.OrdinalIgnoreCase) || s.Contains("spoil", StringComparison.OrdinalIgnoreCase);
                if (parameter is string param5 && param5 == "voice")
                    return s.Contains("voice", StringComparison.OrdinalIgnoreCase) || s.Contains("seiyuu", StringComparison.OrdinalIgnoreCase) || s.Contains("voice actor", StringComparison.OrdinalIgnoreCase) || s.Contains("seiyu", StringComparison.OrdinalIgnoreCase);
                return !string.IsNullOrEmpty(s);
            }
            if (parameter is string paramHasItems && paramHasItems == "hasitems")
            {
                if (value is int itemCount)
                    return itemCount > 0;
                if (value is System.Collections.ICollection col)
                    return col.Count > 0;
                if (value is System.Collections.IEnumerable list)
                {
                    foreach (var _ in list)
                        return true;
                }
                return false;
            }
            if (value is bool b)
            {
                if (parameter is string param3 && param3 == "invert")
                    return !b;
                return b;
            }
            if (value is int count)
            {
                return count != 0;
            }
            if (value is System.Enum en)
            {
                // RelatedItemType.Anime=0 (default) → hidden; Manga/Unknown (nonzero) → visible
                return System.Convert.ToInt32(en) != 0;
            }
            if (value == null)
            {
                // A null object still has to honour "invert", otherwise both the
                // visible and the inverted binding resolve to false and the page
                // renders completely blank.
                return parameter is string nullParam && nullParam == "invert";
            }
            if (parameter is string objParam)
            {
                if (objParam == "invert")
                    return false;
                if (objParam == "nonempty")
                    return true;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

public class IntEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter is string param && int.TryParse(param, out int target))
            return current == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolTabColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool current && parameter is string param && bool.TryParse(param, out bool target))
        {
            return current == target ? "#FF6B00" : "#FFFFFF";
        }
        return "#FFFFFF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class HtmlSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string html && !string.IsNullOrWhiteSpace(html))
        {
            return new HtmlWebViewSource { Html = html };
        }
        return new HtmlWebViewSource { Html = "" };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class AiringStatusVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return AirTimeUtils.IsCurrentlyAiringStatus(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class MultiAndConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.All(v => v is bool b && b);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class RelationTypeToBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RelatedItemType rt)
        {
            return rt switch
            {
                RelatedItemType.Manga => "Manga",
                RelatedItemType.Anime => "",
                _ => ""
            };
        }
        if (value is string rel && !string.IsNullOrWhiteSpace(rel))
        {
            var lower = rel.ToLowerInvariant();
            return lower switch
            {
                "prequel" => "Prequel",
                "sequel" => "Sequel",
                "parent story" => "Parent Story",
                "side story" => "Side Story",
                "alternative version" => "Alternative Version",
                "alternative setting" => "Alternative Setting",
                "full story" => "Full Story",
                "summary" => "Summary",
                "other" => "Other",
                _ => rel
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
