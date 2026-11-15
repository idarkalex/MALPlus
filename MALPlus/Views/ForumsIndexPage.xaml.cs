using MALPlus.Controls;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;
using MALPlus.XShared.ViewModels.Forums.Items;

namespace MALPlus.Views;

public partial class ForumsIndexPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private bool _navigating;
    private int _forumsTabIndex = 0;
    private ForumIndexViewModel Vm => (ForumIndexViewModel)BindingContext;

    public int ForumsTabIndex
    {
        get => _forumsTabIndex;
        set { _forumsTabIndex = value; OnPropertyChanged(); }
    }

    public ForumsIndexPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ForumsIndex;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _navigating = false;
#if ANDROID
        global::MALPlus.MainActivity.ArmMenuBackCallback(() => _ = Shell.Current.GoToAsync(".."));
#endif
        if (_initialized) return;
        _initialized = true;
        BuildTabs();
        try
        {
            Vm.Init(true);
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.LoadingSideContentVisibility) break;
            }
            HighlightTab(0);
        }
        catch
        {
        }
    }

    protected override void OnDisappearing()
    {
        _navigating = false;
#if ANDROID
        global::MALPlus.MainActivity.DisarmMenuBackCallback();
#endif
        base.OnDisappearing();
    }

    private void BuildTabs()
    {
        TabStrip.Children.Clear();
        var tabs = new[] { ("Boards", 0), ("Recent Posts", 1) };
        for (int i = 0; i < tabs.Length; i++)
        {
            var (label, index) = tabs[i];
            int captured = index;
            var button = TabChipHelper.CreateChip(label, captured, (s, e) => OnTabClicked(captured), index == _forumsTabIndex);
            TabStrip.Children.Add(button);
        }
    }

    private void HighlightTab(int activeIndex)
    {
        for (int i = 0; i < TabStrip.Children.Count; i++)
        {
            if (TabStrip.Children[i] is Button b)
            {
                TabChipHelper.ApplyStyle(b, i == activeIndex);
            }
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var next = _forumsTabIndex + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 1)
            return false;

        OnTabClicked(next);
        return true;
    }

    private void OnTabClicked(int index)
    {
        _forumsTabIndex = index;
        OnPropertyChanged(nameof(ForumsTabIndex));
        HighlightTab(index);
    }

    private async void OnBoardTapped(object sender, TappedEventArgs e)
    {
        if (_navigating)
            return;
        try
        {
            if (sender is BindableObject bo && bo.BindingContext is ForumBoardEntryViewModel item)
            {
                _navigating = true;
                await Shell.Current.GoToAsync($"forumboard?board={(int)item.Board}");
            }
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"MALPLUS OnBoardTapped navigation failed: {ex}");
        }
        finally
        {
            _ = Task.Delay(400).ContinueWith(_ => _navigating = false);
        }
    }

    private async void OnRecentPostTapped(object sender, SelectionChangedEventArgs e)
    {
        if (_navigating)
            return;
        try
        {
            if (e.CurrentSelection?.FirstOrDefault() is MALPlus.Models.Models.Forums.ForumBoardEntryPeekPost post && !string.IsNullOrEmpty(post.Id))
            {
                _navigating = true;
                if (sender is CollectionView cv)
                    cv.SelectedItem = null;
                await Shell.Current.GoToAsync(
                    $"forumtopic?id={post.Id}");
            }
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"MALPLUS OnRecentPostTapped navigation failed: {ex}");
        }
        finally
        {
            _ = Task.Delay(400).ContinueWith(_ => _navigating = false);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try { Vm.Init(true); } catch { }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = Shell.Current.GoToAsync("..");
        return true;
    }
}
