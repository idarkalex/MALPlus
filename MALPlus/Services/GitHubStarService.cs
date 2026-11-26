using System;
using System.Threading.Tasks;
using MALPlus.XShared.Utils;
using Microsoft.Maui.ApplicationModel;

namespace MALPlus.Services;

public enum GitHubStarChoice
{
    Star,
    Later,
    Dismiss
}

public static class GitHubStarService
{
    public const string RepoUrl = "https://github.com/idarkalex/MALPlus";
    public const int StartupThreshold = 30;
    private static bool _checkedThisLaunch;

    public static async Task MaybePromptOnStartupAsync()
    {
        try
        {
            if (_checkedThisLaunch)
                return;
            _checkedThisLaunch = true;

            if (!Credentials.Authenticated || !Settings.RatePopUpEnable)
                return;

            var count = Settings.RatePopUpStartupCounter + 1;
            if (count < StartupThreshold)
            {
                Settings.RatePopUpStartupCounter = count;
                return;
            }

            await ShowPromptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS GitHubStar startup check failed: " + ex.Message);
        }
    }

    public static async Task ShowPromptAsync()
    {
        try
        {
            var choice = await MainThread.InvokeOnMainThreadAsync(
                () => OptionsSheetHost.ShowGitHubStarAsync());

            AppLog.Info("Diagnostics", $"GitHub star prompt result: {choice}");

            switch (choice)
            {
                case GitHubStarChoice.Star:
                    Settings.RatePopUpEnable = false;
                    Settings.RatePopUpStartupCounter = 0;
                    await Launcher.Default.OpenAsync(new Uri(RepoUrl));
                    break;

                case GitHubStarChoice.Dismiss:
                    Settings.RatePopUpEnable = false;
                    Settings.RatePopUpStartupCounter = 0;
                    break;

                case GitHubStarChoice.Later:
                default:
                    Settings.RatePopUpEnable = true;
                    Settings.RatePopUpStartupCounter = 0;
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS GitHubStar prompt failed: " + ex.Message);
        }
    }

    public static async Task OpenGitHubForStarAsync()
    {
        try
        {
            Settings.RatePopUpEnable = false;
            Settings.RatePopUpStartupCounter = 0;
            await Launcher.Default.OpenAsync(new Uri(RepoUrl));
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS open github star failed: " + ex.Message);
        }
    }
}
