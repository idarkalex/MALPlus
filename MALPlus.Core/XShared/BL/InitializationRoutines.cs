using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MALClient.Models.Enums;
using MALClient.XShared.Comm;
using MALClient.XShared.Comm.Anime;
using MALClient.XShared.Interfaces;
using MALClient.XShared.Utils;
using MALClient.XShared.Utils.Managers;
using MALClient.XShared.ViewModels;

namespace MALClient.XShared.BL
{
    /// <summary>
    /// Platfrom independent init chain
    /// </summary>
    public static class InitializationRoutines
    {
        public static TaskCompletionSource<bool> AwaitableCompletion { get; } = new TaskCompletionSource<bool>();

        public static async Task InitApp()
        {
            ResourceLocator.ConnectionInfoProvider.Init();
            Credentials.Init();
            // A dropped vault write must not cost the user their session: the refresh token
            // is enough to rebuild it.
            await Credentials.TryRestoreSessionAsync();
            // The forums and the rest of the community endpoints authenticate with the MAL
            // website cookies, not with the api token, so they need restoring too.
            if (Credentials.Authenticated)
                Credentials.TryRestoreWebSession();
            _ = FavouritesManager.LoadDataAsync();
            await AnimeImageQuery.Init();
            ViewModelLocator.ForumsMain.LoadPinnedTopics();

            var tokenTask = Credentials.Authenticated
                ? ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync()
                : Task.CompletedTask;
            var englishTask = ResourceLocator.EnglishTitlesProvider.Init();
            var forceAiring = false;
            try
            {
                const string mandatoryKey = "AiringMandatoryLastRefresh";
                var lastMan = ResourceLocator.ApplicationDataService[mandatoryKey] as string;
                DateTime lastDt = DateTime.MinValue;
                if (!string.IsNullOrEmpty(lastMan) && DateTime.TryParse(lastMan, out var parsed)) lastDt = parsed;
                if (DateTime.UtcNow - lastDt > TimeSpan.FromMinutes(30))
                    forceAiring = true;
            } catch { }
            var airingTask = ResourceLocator.AiringInfoProvider.Init(false, forceAiring);
            await englishTask;
            // airing refresh is progressive: don't block grid, update in background when ready
            _ = Task.Run(async () =>
            {
                try { await airingTask; } catch { }
                try
                {
                    ResourceLocator.AiringInfoProvider.NotifyUpdated();
                } catch { }
            });
            _ = Task.Run(async () =>
            {
                try { await tokenTask; } catch { }
            });

            // Start the start tab's library request while the splash is still on screen.
            // LibraryListQuery shares in-flight requests, so the list page finds the data
            // already coming instead of opening its own, and this never delays startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!Credentials.Authenticated || string.IsNullOrWhiteSpace(Credentials.UserName))
                        return;

                    await tokenTask;
                    var startTab = Settings.DefaultMenuTab ?? "anime";
                    var mode = string.Equals(startTab, "manga", StringComparison.OrdinalIgnoreCase)
                        ? AnimeListWorkModes.Manga
                        : AnimeListWorkModes.Anime;

                    Console.WriteLine($"Preloading start tab library: {mode}");
                    await new LibraryListQuery(Credentials.UserName, mode).GetLibrary(false);
                    Console.WriteLine("Preloaded start tab library");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Start tab preload failed: " + e.Message);
                }
            });

            if (Settings.NotificationCheckInRuntime && Credentials.Authenticated)
                ResourceLocator.SchdeuledJobsManger.StartJob(ScheduledJob.FetchNotifications, 5,
                    () => { ResourceLocator.NotificationsTaskManager.CallTask(BgTasks.Notifications); });
            ResourceLocator.HandyDataStorage.Init();
            AwaitableCompletion.SetResult(true);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000);
                    using var client = new HttpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var result = await client.GetAsync("https://myanimelist.net", cts.Token);
                    result.EnsureSuccessStatusCode();
                }
                catch (Exception e)
                {
                    // only show MAL down if we actually have no cached data
                    if (!ResourceLocator.AiringInfoProvider.InitializationSuccess)
                    {
                        ResourceLocator.DispatcherAdapter.Run(() =>
                        {
                            ResourceLocator.MessageDialogProvider.ShowMessageDialog(
                                "Failed to connect to MyAnimeList.net. Website is probably down, try checking on the browser. If it's down try again later.",
                                "MAL is down");
                        });
                    }
                }
            });


        }

        public static void InitPostUpdate()
        {
            var previousVersion = Settings.AppVersion;
            var currentVersion = ResourceLocator.ChangelogProvider.CurrentVersion;
            var isNewVersion = false;
            if (previousVersion != null)
            {
                if (previousVersion.Substring(0, previousVersion.LastIndexOf('.')) !=
                    currentVersion.Substring(0, currentVersion.LastIndexOf('.')))
                    isNewVersion = true;
            }

            ResourceLocator.ChangelogProvider.NewChangelog = isNewVersion;
            Settings.AppVersion = ResourceLocator.ChangelogProvider.CurrentVersion;
        }
    }
}
