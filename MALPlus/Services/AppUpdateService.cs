using System;
using System.Threading.Tasks;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Utils;
using Microsoft.Maui.ApplicationModel;

namespace MALPlus.Services;

public static class AppUpdateService
{
    public static async Task CheckDailyUpdateAsync()
    {
        try
        {
            var lastCheck = Settings.LastUpdateCheckUtc;
            if (lastCheck.HasValue && lastCheck.Value.Date >= DateTime.UtcNow.Date)
            {
                AppLog.Debug("Update", $"Daily update check skipped: already checked on {lastCheck.Value:yyyy-MM-dd}");
                return;
            }

            Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            AppLog.Info("Update", "Performing daily background update check...");

            var query = new AppUpdateQuery();
            var info = await query.GetUpdateInfo();
            if (info == null)
            {
                AppLog.Info("Update", "Daily check completed: no new release found.");
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
            AppLog.Warn("Update", $"Daily update check failed: {ex.Message}");
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
            try
            {
                AppLog.Info("Update", $"Opening update URL: {info.DownloadUrl}");
                await Launcher.Default.OpenAsync(new Uri(info.DownloadUrl));
            }
            catch (Exception ex)
            {
                AppLog.Error("Update", $"Failed to launch update URL: {ex.Message}", ex);
            }
        }
    }

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
