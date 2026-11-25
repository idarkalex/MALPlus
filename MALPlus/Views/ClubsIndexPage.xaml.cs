using MALPlus.Controls;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Clubs;

namespace MALPlus.Views;

public partial class ClubsIndexPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private ClubIndexViewModel Vm => (ClubIndexViewModel)BindingContext;

    public ClubsIndexPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ClubIndex;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // The page instance is reused, so a previous visit could leave the view
        // model pointing at another tab and none of the containers visible.
        Vm.ClubsTabIndex = 0;
        if (TabStrip.Children.Count > 0)
            HighlightTab(0);
        if (_initialized) return;
        _initialized = true;
        BuildTabs();
        try
        {
            Vm.ReloadMyClubs();
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.Loading) break;
            }
            HighlightTab(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ClubsIndexPage Init failed: " + ex.Message);
        }
    }

    private void BuildTabs()
    {
        TabStrip.Children.Clear();
        var tabs = new[] { ("My Clubs", 0), ("Search", 1), ("Activity", 2) };
        for (int i = 0; i < tabs.Length; i++)
        {
            var (label, index) = tabs[i];
            int captured = index;
            var button = TabChipHelper.CreateChip(label, captured, (s, e) => OnTabClicked(captured), index == Vm.ClubsTabIndex);
            TabStrip.Children.Add(button);
        }
    }

    private void HighlightTab(int activeIndex)
    {
        for (int i = 0; i < TabStrip.Children.Count; i++)
        {
            if (TabStrip.Children[i] is Button b)
            {
                var isActive = i == activeIndex;
                TabChipHelper.ApplyStyle(b, isActive);
                if (isActive && TabStripScroll != null)
                {
                    _ = TabStripScroll.ScrollToAsync(b, ScrollToPosition.Center, true);
                }
            }
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        if (Vm == null) return false;
        var current = Vm.ClubsTabIndex;
        var next = current + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 2)
            return false;

        OnTabClicked(next);
        return true;
    }

    private void OnTabClicked(int index)
    {
        Vm.ClubsTabIndex = index;
        HighlightTab(index);
        // The activity list was never requested on tab switch, so the tab stayed
        // blank instead of loading the club posts.
        if (index == 2)
            Vm.LoadClubActivityCommand?.Execute(null);
    }

    private void OnRefreshMy(object sender, EventArgs e)
    {
        try { Vm.ReloadMyClubs(); } catch { }
    }

    private void OnSearchRefresh(object sender, EventArgs e)
    {
        ExecuteSearch();
    }

    private void OnActivityRefresh(object sender, EventArgs e)
    {
        try { Vm.LoadClubActivityCommand?.Execute(null); } catch { }
    }

    private void OnSearchButtonTapped(object sender, EventArgs e)
    {
        ExecuteSearch();
    }

    private void OnSearchCompleted(object sender, EventArgs e)
    {
        ExecuteSearch();
    }

    private void ExecuteSearch()
    {
        try
        {
            if (SearchEntry != null)
            {
                SearchEntry.Unfocus();
                if (Vm != null && !string.IsNullOrWhiteSpace(SearchEntry.Text))
                {
                    Vm.SearchQuery = SearchEntry.Text.Trim();
                }
            }
            Vm?.SearchCommand?.Execute(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS club search failed: " + ex.Message);
        }
    }

    private async void OnClubTapped(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is MalClubEntry entry)
            {
                ((CollectionView)sender).SelectedItem = null;
                await Shell.Current.GoToAsync($"clubdetails?id={entry.Id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS club nav failed: " + ex.Message);
        }
    }

    private async void OnActivityTapped(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is ClubActivityEntry entry)
            {
                ((CollectionView)sender).SelectedItem = null;
                await Shell.Current.GoToAsync($"forumtopic?id={entry.TopicId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS activity nav failed: " + ex.Message);
        }
    }
}
