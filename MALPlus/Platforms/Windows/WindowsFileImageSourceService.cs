#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace MALPlus.Platforms.Windows;

/// <summary>
///     Custom file image source service for Windows Desktop (WinUI 3 unpackaged).
///     Resolves assets regardless of whether referenced with or without extension,
///     with .svg or .png, and handles Resizetizer's generated .scale-*.png variants cleanly.
/// </summary>
public class WindowsFileImageSourceService : ImageSourceService, IImageSourceService<IFileImageSource>
{
    private static readonly string BaseDir = AppContext.BaseDirectory;
    private static readonly ConcurrentDictionary<string, WImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public override Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default) =>
        GetImageSourceAsync((IFileImageSource)imageSource, scale, cancellationToken);

    public async Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IFileImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
    {
        if (imageSource == null || imageSource.IsEmpty || string.IsNullOrWhiteSpace(imageSource.File))
            return null;

        var filename = imageSource.File;

        if (Cache.TryGetValue(filename, out var cached))
            return new ImageSourceServiceResult(cached);

        try
        {
            var resolvedPath = ResolveFilePath(filename);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                var file = await StorageFile.GetFileFromPathAsync(resolvedPath);
                var stream = await file.OpenReadAsync();

                return await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        if (Cache.TryGetValue(filename, out var c))
                        {
                            stream.Dispose();
                            return new ImageSourceServiceResult(c);
                        }

                        using (stream)
                        {
                            var bitmap = new BitmapImage();
                            await bitmap.SetSourceAsync(stream);
                            Cache[filename] = bitmap;
                            return (IImageSourceServiceResult<WImageSource>?)new ImageSourceServiceResult(bitmap);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WindowsFileImageSourceService] UI thread SetSourceAsync failed: {ex.Message}");
                        return null;
                    }
                });
            }

            var resourceName = Path.GetFileName(filename);
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    var fallback = new BitmapImage(new Uri("ms-appx:///" + resourceName));
                    return (IImageSourceServiceResult<WImageSource>?)new ImageSourceServiceResult(fallback);
                }
                catch
                {
                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsFileImageSourceService] Failed to load '{filename}': {ex.Message}");
            return null;
        }
    }

    private static string? ResolveFilePath(string filename)
    {
        if (Path.IsPathRooted(filename) && File.Exists(filename))
            return filename;

        var clean = Path.GetFileName(filename);
        if (clean.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];
        else if (clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];

        string[] searchDirs = [BaseDir, Path.Combine(BaseDir, "Assets")];
        string[] candidateExtensions = [".png", ".scale-100.png", ".scale-150.png", ".scale-200.png", ".scale-125.png", ".scale-400.png", ".jpg", ".jpeg", ".svg", ""];

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var direct = Path.Combine(dir, filename);
            if (File.Exists(direct)) return direct;

            foreach (var ext in candidateExtensions)
            {
                var candidate = Path.Combine(dir, clean + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
