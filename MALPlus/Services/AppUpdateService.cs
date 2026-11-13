using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Utils;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace MALPlus.Services;

public static class AppUpdateService
{
    private static DateTime? _lastStartupCheckUtc;
    private static readonly SemaphoreSlim _downloadLock = new(1, 1);
    private static string _pendingInstallApkPath;

    public static async Task CheckStartupUpdateAsync()
    {
        try
        {
            // Courtesy throttle: don't check more than once every 15 minutes during quick app switches/restarts
            if (_lastStartupCheckUtc.HasValue && (DateTime.UtcNow - _lastStartupCheckUtc.Value).TotalMinutes < 15)
            {
                AppLog.Debug("Update", $"Startup update check throttled (last checked {_lastStartupCheckUtc.Value:HH:mm:ss} UTC)");
                return;
            }

            _lastStartupCheckUtc = DateTime.UtcNow;
            Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            AppLog.Info("Update", "Performing startup background update check...");

            var query = new AppUpdateQuery();
            var info = await query.GetUpdateInfo();
            if (info == null)
            {
                AppLog.Info("Update", "Startup check completed: no new release found.");
                return;
            }

            var currentVersionStr = AppInfo.VersionString;
            if (Version.TryParse(info.Version, out var remoteVer) &&
                Version.TryParse(currentVersionStr, out var currentVer))
            {
                if (remoteVer > currentVer)
                {
                    AppLog.Info("Update", $"New version found: v{info.Version} (current: v{currentVersionStr})");
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await PromptUpdateAsync(info);
                    });
                }
                else
                {
                    AppLog.Info("Update", $"App is up to date: v{currentVersionStr} >= v{info.Version}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Update", $"Startup update check failed: {ex.Message}");
        }
    }

    public static async Task CheckManualUpdateAsync()
    {
        try
        {
            AppLog.Info("Update", "Manual update check initiated by user.");
            var query = new AppUpdateQuery();
            var info = await query.GetUpdateInfo();

            var currentVersionStr = AppInfo.VersionString;
            if (info != null &&
                Version.TryParse(info.Version, out var remoteVer) &&
                Version.TryParse(currentVersionStr, out var currentVer) &&
                remoteVer > currentVer)
            {
                await PromptUpdateAsync(info);
            }
            else
            {
                await OptionsSheetHost.ConfirmAsync("Updates",
                    $"You are using the latest version (v{currentVersionStr}).", "OK");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "Manual update check failed", ex);
            await OptionsSheetHost.ConfirmAsync("Updates",
                $"Unable to check for updates: {ex.Message}", "OK");
        }
    }

    private static async Task PromptUpdateAsync(AppUpdateInfo info)
    {
        if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
            return;

        if (!info.DownloadUrl.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            var shouldUpdate = await OptionsSheetHost.ShowUpdateAsync(info);
            if (shouldUpdate)
            {
                try
                {
                    await Launcher.Default.OpenAsync(new Uri(info.DownloadUrl));
                }
                catch (Exception ex)
                {
                    AppLog.Error("Update", $"Failed to launch update URL: {ex.Message}", ex);
                }
            }
            return;
        }

        AppLog.Info("Update", $"Showing update prompt for v{info.Version}. URL: {info.DownloadUrl}");
        await OptionsSheetHost.ShowUpdateAsync(info, async (reportProgress) =>
        {
            return await DownloadAndInstallApkAsync(info.DownloadUrl, info.Version, reportProgress);
        });
    }

    private static async Task<bool> DownloadAndInstallApkAsync(
        string downloadUrl,
        string version,
        Action<double, string> onProgress = null)
    {
        if (!await _downloadLock.WaitAsync(0))
        {
            AppLog.Warn("Update", "A download is already in progress, ignoring duplicate request.");
            return false;
        }

        try
        {
            var cacheDir = FileSystem.CacheDirectory;
            var apkPath = Path.Combine(cacheDir, $"malplus_{version}.apk");

            if (File.Exists(apkPath))
            {
                var existingLength = new FileInfo(apkPath).Length;
                if (existingLength > 20 * 1024 * 1024)
                {
                    AppLog.Info("Update", $"Found complete valid APK at {apkPath} ({existingLength:N0} bytes), installing directly.");
                    onProgress?.Invoke(1.0, "Ready to install. Starting installer...");
                    await Task.Delay(350);
#if ANDROID
                    InstallApk(apkPath);
#else
                    await Launcher.Default.OpenAsync(new Uri(downloadUrl));
#endif
                    return true;
                }
                try { File.Delete(apkPath); } catch { }
            }

            onProgress?.Invoke(0.0, "Connecting to GitHub Releases...");
            AppLog.Info("Update", $"Starting download: {downloadUrl} -> {apkPath}");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MALPlus-Android");
            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(apkPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                var lastReport = DateTime.UtcNow;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 200)
                    {
                        lastReport = DateTime.UtcNow;
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            var fraction = (double)totalRead / totalBytes.Value;
                            var mbRead = totalRead / (1024.0 * 1024.0);
                            var mbTotal = totalBytes.Value / (1024.0 * 1024.0);
                            onProgress?.Invoke(fraction, $"{mbRead:F1} / {mbTotal:F1} MB ({(int)(fraction * 100)}%)");
                        }
                        else
                        {
                            var mbRead = totalRead / (1024.0 * 1024.0);
                            onProgress?.Invoke(-1, $"{mbRead:F1} MB downloaded");
                        }
                    }
                }
            }

            AppLog.Info("Update", $"Download completed successfully ({new FileInfo(apkPath).Length:N0} bytes).");
            onProgress?.Invoke(1.0, "Download finished. Starting installer...");
            await Task.Delay(300);

#if ANDROID
            InstallApk(apkPath);
#else
            await Launcher.Default.OpenAsync(new Uri(downloadUrl));
#endif
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "DownloadAndInstallApkAsync failed", ex);
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

#if ANDROID
    private static void InstallApk(string apkPath)
    {
        try
        {
            var context = Android.App.Application.Context;
            var file = new Java.IO.File(apkPath);
            if (!file.Exists())
            {
                AppLog.Error("Update", $"APK file does not exist at {apkPath}");
                return;
            }

            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                context,
                $"{context.PackageName}.fileprovider",
                file);

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            intent.AddFlags(Android.Content.ActivityFlags.ClearTop);

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                if (!context.PackageManager.CanRequestPackageInstalls())
                {
                    AppLog.Warn("Update", "CanRequestPackageInstalls is false, saving pending path and opening settings for unknown app sources");
                    _pendingInstallApkPath = apkPath;
                    Preferences.Default.Set("pending_update_apk_path", apkPath);

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Android.Widget.Toast.MakeText(
                            context,
                            "Please allow MAL+ to install updates, then return to the app.",
                            Android.Widget.ToastLength.Long)?.Show();
                    });

                    var settingsIntent = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources);
                    settingsIntent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
                    settingsIntent.AddFlags(Android.Content.ActivityFlags.NewTask);
                    context.StartActivity(settingsIntent);
                    return;
                }
            }

            AppLog.Info("Update", $"Starting package installer intent for {uri}...");
            _pendingInstallApkPath = null;
            Preferences.Default.Remove("pending_update_apk_path");
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "InstallApk failed", ex);
        }
    }

    public static void ResumePendingInstallIfAny()
    {
        try
        {
            var pendingPath = _pendingInstallApkPath;
            if (string.IsNullOrEmpty(pendingPath))
            {
                pendingPath = Preferences.Default.Get("pending_update_apk_path", string.Empty);
            }

            if (string.IsNullOrEmpty(pendingPath) || !File.Exists(pendingPath))
                return;

            var context = Android.App.Application.Context;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                if (!context.PackageManager.CanRequestPackageInstalls())
                {
                    AppLog.Debug("Update", "ResumePendingInstall: CanRequestPackageInstalls is still false.");
                    return;
                }
            }

            AppLog.Info("Update", $"ResumePendingInstall: Permission granted! Resuming pending install for {pendingPath}");
            _pendingInstallApkPath = null;
            Preferences.Default.Remove("pending_update_apk_path");
            InstallApk(pendingPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "ResumePendingInstallIfAny failed", ex);
        }
    }
#endif

    public static async Task ShowCurrentChangelogAsync()
    {
        try
        {
            var changelogText = @"• Complete migration to .NET 11 and .NET MAUI for Android.
• Modernized dark-first visual design with fluid ViewPager2 gestures.
• Offline seed bundle: 500+ anime titles preloaded with details and episodes.
• AnimeThemes integration for high quality OP/ED video and audio playback.
• Native MyAnimeList forum browser with clean pagination and quick replies.
• 100% local diagnostic logger with exportable support bundle in Settings.
• Content rating filters, localized title preferences, and live MAL sync.";

            await OptionsSheetHost.ShowChangelogAsync(
                "Release Notes",
                $"MAL+ v{AppInfo.VersionString} (Build {AppInfo.BuildString})",
                changelogText);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "ShowCurrentChangelogAsync failed", ex);
        }
    }
}
