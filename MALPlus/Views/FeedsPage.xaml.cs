using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

public partial class FeedsPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private FriendsFeedsViewModel Vm => (FriendsFeedsViewModel)BindingContext;

    public FeedsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.FriendsFeeds;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SetFilterTab(Vm?.FilterIndex ?? 0);
        if (_initialized) return;
        _initialized = true;
        try
        {
            Vm.Init(false);
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.Loading) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS FeedsPage Init failed: " + ex.Message);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try { Vm.Init(true); } catch { }
    }

    private void OnTabButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx))
            SetFilterTab(idx);
    }

    private void SetFilterTab(int idx)
    {
        if (Vm == null) return;
        Vm.FilterIndex = idx;
        if (TabAll != null && TabAnime != null && TabManga != null)
        {
            TabChipHelper.ApplyStyle(TabAll, idx == 0);
            TabChipHelper.ApplyStyle(TabAnime, idx == 1);
            TabChipHelper.ApplyStyle(TabManga, idx == 2);
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        if (Vm == null) return false;
        var next = Vm.FilterIndex + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 2)
            return false;
        SetFilterTab(next);
        return true;
    }

    private async void OnEntryTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is not UserFeedEntryModel entry || entry.Id <= 0)
                return;

            MALPlus.Services.MauiDetailsNavigationHandoff.Set(
                new AnimeDetailsPageNavigationArgs(entry.Id, entry.Title, null, null, null)
                {
                    AnimeMode = !entry.IsManga,
                    Source = PageIndex.PageFeeds
                });
            var title = Uri.EscapeDataString(entry.Title ?? string.Empty);
            var qs = entry.IsManga ? "&manga=true" : string.Empty;
            await Shell.Current.GoToAsync($"animedetails?id={entry.Id}&title={title}{qs}");
        }
        catch { }
    }

    private async void OnUserTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is not UserFeedEntryModel entry)
                return;
            var username = entry.User?.Name;
            if (!string.IsNullOrWhiteSpace(username))
                await Shell.Current.GoToAsync($"profile?user={Uri.EscapeDataString(username)}");
        }
        catch { }
    }
}
