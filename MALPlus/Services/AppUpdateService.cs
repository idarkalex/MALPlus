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

            var currentVersionStr = GetCurrentAppVersion();
            if (TryParseVersion(info.Version, out var remoteVer) &&
                TryParseVersion(currentVersionStr, out var currentVer))
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

            var currentVersionStr = GetCurrentAppVersion();
            if (info != null &&
                TryParseVersion(info.Version, out var remoteVer) &&
                TryParseVersion(currentVersionStr, out var currentVer) &&
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

#if WINDOWS
        AppLog.Info("Update", $"Showing Windows update prompt for v{info.Version}. URL: {info.DownloadUrl}");
        await OptionsSheetHost.ShowUpdateAsync(info, async (reportProgress) =>
        {
            return await DownloadAndInstallWindowsAsync(info.DownloadUrl, info.Version, reportProgress);
        });
#else
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
#endif
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

#if WINDOWS
    private static async Task<bool> DownloadAndInstallWindowsAsync(
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
            var installerExt = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".exe";
            var installerPath = Path.Combine(cacheDir, $"malplus_setup_{version}{installerExt}");

            onProgress?.Invoke(0.0, "Connecting to download server...");
            AppLog.Info("Update", $"Starting Windows download: {downloadUrl} -> {installerPath}");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MALPlus-Windows");
            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
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

            AppLog.Info("Update", $"Windows download completed successfully ({new FileInfo(installerPath).Length:N0} bytes).");
            onProgress?.Invoke(1.0, "Download finished. Starting installer...");
            await Task.Delay(500);

            // Execute the installer and close the app so files can be updated
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Application.Current?.Quit();
            });

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "DownloadAndInstallWindowsAsync failed", ex);
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }
#endif

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
            string changelogText = null;
            string releaseTitle = $"MAL+ v{AppInfo.VersionString} (Build {AppInfo.BuildString})";

            try
            {
                var query = new AppUpdateQuery();
                var info = await query.GetUpdateInfo();
                if (info != null && !string.IsNullOrWhiteSpace(info.ReleaseNotes))
                {
                    changelogText = info.ReleaseNotes;
                    if (string.IsNullOrWhiteSpace(info.ReleaseName) ||
                        info.ReleaseName.Equals($"MAL+ v{info.Version}", StringComparison.OrdinalIgnoreCase) ||
                        info.ReleaseName.Equals($"v{info.Version}", StringComparison.OrdinalIgnoreCase))
                    {
                        releaseTitle = $"MAL+ v{info.Version}";
                    }
                    else
                    {
                        releaseTitle = $"MAL+ v{info.Version} - {info.ReleaseName}";
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Update", $"Could not fetch remote release notes: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(changelogText))
            {
                changelogText = @"• Complete migration to .NET 11 and .NET MAUI for Android.
• Modernized dark-first visual design with fluid ViewPager2 gestures.
• Offline seed bundle: 500+ anime titles preloaded with details and episodes.
• AnimeThemes integration for high quality OP/ED video and audio playback.
• Native MyAnimeList forum browser with clean pagination and quick replies.
• 100% local diagnostic logger with exportable support bundle in Settings.
• Content rating filters, localized title preferences, and live MAL sync.";
            }

            await OptionsSheetHost.ShowChangelogAsync(
                "Release Notes",
                releaseTitle,
                changelogText);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "ShowCurrentChangelogAsync failed", ex);
        }
    }

    public static string GetCurrentAppVersion()
    {
#if WINDOWS
        try
        {
            var asmVer = typeof(AppUpdateService).Assembly.GetName().Version;
            if (asmVer != null && (asmVer.Major > 0 || asmVer.Minor > 0 || asmVer.Build > 0))
            {
                return $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
            }
        }
        catch { }
#endif
        var v = AppInfo.VersionString;
        if (TryParseVersion(v, out var parsed))
        {
            return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        }
        return v;
    }

    private static bool TryParseVersion(string versionStr, out Version version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(versionStr))
            return false;

        var clean = versionStr.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(1);

        var plusIdx = clean.IndexOf('+');
        if (plusIdx >= 0)
            clean = clean.Substring(0, plusIdx);

        var dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0)
            clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], out var maj) &&
            int.TryParse(parts[1], out var min) &&
            int.TryParse(parts[2], out var build))
        {
            version = new Version(maj, min, build);
            return true;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var m) &&
            int.TryParse(parts[1], out var n))
        {
            version = new Version(m, n, 0);
            return true;
        }

        return Version.TryParse(clean, out version);
    }
}
