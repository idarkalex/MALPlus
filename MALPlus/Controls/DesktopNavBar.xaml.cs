using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MALPlus.Controls;

public partial class DesktopNavBar : ContentView
{
    private static readonly Color SelectedBgColor = Color.FromArgb("#0066FF");
    private static readonly Color UnselectedBgColor = Colors.Transparent;
    private static readonly Color SelectedTextColor = Colors.White;
    private static readonly Color UnselectedTextColor = Color.FromArgb("#A0FFFFFF");

    private Shell _subscribedShell;

    public DesktopNavBar()
    {
        InitializeComponent();

#if !WINDOWS
        IsVisible = false;
        HeightRequest = 0;
        MaximumHeightRequest = 0;
#else
        IsVisible = true;
        HeightRequest = 54;
#endif
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent == null)
        {
            UnsubscribeFromShell();
            return;
        }

        SubscribeToShell();
        RefreshSelection();
    }

    private void SubscribeToShell()
    {
        var shell = Shell.Current;
        if (shell == null || ReferenceEquals(_subscribedShell, shell))
            return;

        UnsubscribeFromShell();
        _subscribedShell = shell;
        _subscribedShell.Navigated += OnShellNavigated;
    }

    private void UnsubscribeFromShell()
    {
        if (_subscribedShell != null)
        {
            _subscribedShell.Navigated -= OnShellNavigated;
            _subscribedShell = null;
        }
    }

    private void OnShellNavigated(object sender, ShellNavigatedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshSelection);
    }

    public void RefreshSelection()
    {
        try
        {
            var location = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
            var path = location.Split(new[] { '?' }, 2)[0].ToLowerInvariant();

            bool isDiscover = path.Contains("discover");
            bool isAnime = path.Contains("anime");
            bool isManga = path.Contains("manga");
            bool isMore = path.Contains("more");

            if (!isDiscover && !isAnime && !isManga && !isMore)
            {
                var route = Shell.Current?.CurrentItem?.Route?.ToLowerInvariant() ?? string.Empty;
                isDiscover = route.Contains("discover");
                isAnime = route.Contains("anime");
                isManga = route.Contains("manga");
                isMore = route.Contains("more");
            }

            SetTabState(TabDiscoverBorder, TabDiscoverLabel, TabDiscoverIcon, isDiscover);
            SetTabState(TabAnimeBorder, TabAnimeLabel, TabAnimeIcon, isAnime);
            SetTabState(TabMangaBorder, TabMangaLabel, TabMangaIcon, isManga);
            SetTabState(TabMoreBorder, TabMoreLabel, TabMoreIcon, isMore);

            if (NavBackButton != null)
            {
                var navStack = Shell.Current?.Navigation?.NavigationStack;
                NavBackButton.IsVisible = navStack != null && navStack.Count > 1;
            }
        }
        catch { }
    }

    private static void SetTabState(Border border, Label label, Image icon, bool isSelected)
    {
        if (border == null || label == null || icon == null)
            return;

        border.BackgroundColor = isSelected ? SelectedBgColor : UnselectedBgColor;
        label.TextColor = isSelected ? SelectedTextColor : UnselectedTextColor;
        icon.Opacity = isSelected ? 1.0 : 0.65;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null && nav.NavigationStack.Count > 1)
            {
                await nav.PopAsync();
            }
            else
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        catch
        {
            await NavigateToRoute("///discover");
        }
    }

    private async void OnLogoClicked(object sender, EventArgs e)
    {
        await NavigateToRoute("///discover", "discover");
    }

    private async void OnDiscoverClicked(object sender, EventArgs e)
    {
        await NavigateToRoute("///discover", "discover");
    }

    private async void OnAnimeClicked(object sender, EventArgs e)
    {
        await NavigateToRoute("///anime?mode=0&status=0", "anime");
    }

    private async void OnMangaClicked(object sender, EventArgs e)
    {
        await NavigateToRoute("///manga?mode=2&status=0", "manga");
    }

    private async void OnMoreClicked(object sender, EventArgs e)
    {
        await NavigateToRoute("///more", "more");
    }

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current?.GoToAsync("search");
        }
        catch { }
    }

    private static async Task NavigateToRoute(string route, string section = null)
    {
        try
        {
            var shell = Shell.Current;
            if (shell == null)
                return;

            MALPlus.Services.Navigation.BackRouter.Clear();

            bool isList = string.Equals(section, "anime", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(section, "manga", StringComparison.OrdinalIgnoreCase);

            if (isList)
                MALPlus.Views.AnimeListPage.SetPendingRootRoute(route);

            if (shell.Navigation?.NavigationStack?.Count > 1)
            {
                try
                {
                    await shell.Navigation.PopToRootAsync(false);
                }
                catch { }
            }

            await shell.GoToAsync(route);

            if (isList)
            {
                if (shell.CurrentPage is MALPlus.Views.AnimeListPage listPage)
                {
                    listPage.ApplyRouteFromShell(route);
                }
                else
                {
                    MALPlus.Views.AnimeListPage.ClearPendingRootRoute(route);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DesktopNavBar] Navigation to '{route}' failed: {ex.Message}");
        }
    }
}
