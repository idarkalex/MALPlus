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
        var shouldUpdate = await OptionsSheetHost.ShowUpdateAsync(info);
        if (shouldUpdate && !string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            AppLog.Info("Update", $"User confirmed update to v{info.Version}. URL: {info.DownloadUrl}");
            if (info.DownloadUrl.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadAndInstallApkAsync(info.DownloadUrl, info.Version);
            }
            else
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
        }
    }

    private static async Task DownloadAndInstallApkAsync(string downloadUrl, string version)
    {
        try
        {
#if ANDROID
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Android.Widget.Toast.MakeText(
                    Android.App.Application.Context,
                    $"Downloading MAL+ v{version}...",
                    Android.Widget.ToastLength.Long)?.Show();
            });
#endif

            var cacheDir = FileSystem.CacheDirectory;
            var apkPath = Path.Combine(cacheDir, $"malplus_{version}.apk");

            if (File.Exists(apkPath))
            {
                try { File.Delete(apkPath); } catch { }
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MALPlus-Android");
            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(apkPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream);
            }

#if ANDROID
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Android.Widget.Toast.MakeText(
                    Android.App.Application.Context,
                    "Download finished. Starting installer...",
                    Android.Widget.ToastLength.Short)?.Show();
            });

            InstallApk(apkPath);
#else
            await Launcher.Default.OpenAsync(new Uri(downloadUrl));
#endif
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "DownloadAndInstallApkAsync failed, opening browser fallback", ex);
#if ANDROID
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Android.Widget.Toast.MakeText(
                    Android.App.Application.Context,
                    $"Download failed: {ex.Message}. Opening browser...",
                    Android.Widget.ToastLength.Long)?.Show();
            });
#endif
            try
            {
                await Launcher.Default.OpenAsync(new Uri(downloadUrl));
            }
            catch { }
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
                    AppLog.Warn("Update", "CanRequestPackageInstalls is false, opening settings for unknown app sources");
                    var settingsIntent = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources);
                    settingsIntent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
                    settingsIntent.AddFlags(Android.Content.ActivityFlags.NewTask);
                    context.StartActivity(settingsIntent);
                    return;
                }
            }

            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "InstallApk failed", ex);
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
