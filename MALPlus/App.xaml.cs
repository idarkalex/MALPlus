using MALClient.XShared.BL;
using MALClient.XShared.Utils;
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

        System.Diagnostics.Debug.WriteLine("=== MALPLUS App.OnStart ===");

        Task.Run(async () =>
        {
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
                        var client = await MALClient.XShared.ViewModels.ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync();
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
}