using MALPlus.XShared.BL;
using MALPlus.XShared.Utils;
using MALPlus.Services;

namespace MALPlus;

public partial class App : Application
{
    public App()
    {
        StartupTrace.Mark("app-ctor-begin");
        InitializeComponent();
        MauiCrashDiagnostics.Install();
        MainPage = new AppShell();
        StartupTrace.Mark("shell-built");
    }

    private static string TryPeekVaultUser()
    {
        try
        {
            return Credentials.PasswordVault.Get("MALPlus")?.UserName;
        }
        catch (Exception ex)
        {
            return "err:" + ex.GetType().Name;
        }
    }

    protected override void OnStart()
    {
base.OnStart();

        System.Diagnostics.Debug.WriteLine("=== MALPlus App.OnStart ===");

        Task.Run(async () =>
        {
            // Reads the saved dictionary off the UI thread. This used to be an unawaited call sitting
            // directly in OnStart, so the part of the read that runs before its first await blocked the
            // thread that paints the first frame: the window came up, nothing was drawn over the
            // background colour, and on a cold cache it sat there looking frozen until the read gave up.
            try
            {
                await MALPlus.XShared.ViewModels.ResourceLocator.EnglishTitlesProvider.Init();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPlus EnglishTitles init failed: " + ex.GetType().Name);
            }

            StartupTrace.Mark("initapp-begin");
            var initTask = InitializationRoutines.InitApp();
            try
            {
                await Task.WhenAny(InitializationRoutines.AwaitableCompletion.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            }
            catch
            {
            }
            finally
            {
                InitializationRoutines.AwaitableCompletion.TrySetResult(true);
                initTask.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }

            StartupTrace.Mark("initapp-ready");
            try
            {
                Console.WriteLine("MALPLUS boot state: auth=" + Credentials.Authenticated +
                                  " user='" + (Credentials.UserName ?? "<null>") + "'" +
                                  " id=" + Credentials.Id +
                                  " token=" + !string.IsNullOrEmpty(Settings.ApiToken) +
                                  " refresh=" + !string.IsNullOrEmpty(Settings.RefreshToken) +
                                  " vaultUser=" + (TryPeekVaultUser() ?? "<none>"));
                if (!Credentials.Authenticated || string.IsNullOrWhiteSpace(Credentials.UserName))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        Shell.Current?.GoToAsync("login"));
                }
                else
                {
                    // Apply DefaultMenuTab setting (anime/manga/discover)
                    try
                    {
                        var startTab = Settings.DefaultMenuTab ?? "anime";
                        var route = startTab switch
                        {
                            "discover" => "//discover",
                            "manga" => "//manga",
                            _ => "//anime"
                        };
                        StartupTrace.Mark("start-tab-" + startTab);
                        await MainThread.InvokeOnMainThreadAsync(() =>
                            Shell.Current?.GoToAsync(route));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("MALPLUS start tab nav failed: " + ex.Message);
                    }
                    try
                    {
                        var client = await MALPlus.XShared.ViewModels.ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync();
                        Console.WriteLine("MALPLUS boot api client ok=" + (client != null));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("MALPLUS boot api client FAILED " + ex.GetType().Name + " " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS login nav failed: " + ex.Message);
            }
            await MainActivity.ApplyPendingDeepLinkAsync();
        });
    }

    /// <summary>
    ///     Writes out the English titles the reader collected this session.
    /// </summary>
    /// <remarks>
    ///     Without this the dictionary stayes in memory only, so every cold start began with an empty
    ///     one and the switch had no effect on a list that was not already showing English titles.
    ///     Nothing else in the app ever saved it: the provider had no caller at all.
    /// </remarks>
    protected override void OnSleep()
    {
        base.OnSleep();
        try
        {
            // Started and left running rather than waited on. Blocking here on GetResult is the
            // classic way to wedge the platform's own save path: resume has to come back through
            // this same thread, and anything the write needs from it cannot get it while we hold it.
            // The write is skipped entirely when nothing changed, so there is rarely anything to lose.
            _ = MALPlus.XShared.ViewModels.ResourceLocator.EnglishTitlesProvider.SaveData();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS EnglishTitles save on sleep failed: " + ex.GetType().Name);
        }
    }
}