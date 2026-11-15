using MALPlus.Controls;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;
using MALPlus.XShared.Utils;

namespace MALPlus.Views;

[QueryProperty(nameof(TargetUser), "user")]
public partial class FriendsPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    public string TargetUser { get; set; }

    private FriendsPageViewModel Vm => (FriendsPageViewModel)BindingContext;

    public FriendsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.Friends;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SetFriendsTab(Vm?.FriendsTabIndex ?? 0);
        if (_initialized) return;
        _initialized = true;
        try
        {
            Vm.NavigatedTo(new FriendsPageNavArgs { TargetUser = new MALPlus.Models.Models.MalUser { Name = TargetUser ?? Credentials.UserName } });
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.Loading) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS FriendsPage Init failed: " + ex.Message);
        }
    }

    private void OnTabButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx))
        {
            SetFriendsTab(idx);
        }
    }

    private void OnTabTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is string s && int.TryParse(s, out int idx))
        {
            SetFriendsTab(idx);
        }
    }

    private void SetFriendsTab(int idx)
    {
        if (Vm == null) return;
        Vm.FriendsTabIndex = idx;
        if (TabFriends != null && TabRequests != null)
        {
            TabChipHelper.ApplyStyle(TabFriends, idx == 0);
            TabChipHelper.ApplyStyle(TabRequests, idx == 1);
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        if (Vm == null) return false;
        var current = Vm.FriendsTabIndex;
        var next = current + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 1)
            return false;

        SetFriendsTab(next);
        return true;
    }

    private void OnRefreshFriends(object sender, EventArgs e)
    {
        try { Vm.RefreshFriendsCommand?.Execute(null); } catch { }
    }

    private void OnRefreshRequests(object sender, EventArgs e)
    {
        try { Vm.RefreshPendingCommand?.Execute(null); } catch { }
    }

    private async void OnRequestUserTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var req = e.Parameter as MALPlus.Models.Models.MalSpecific.MalFriendRequest;
            var username = req?.User?.Name;
            if (!string.IsNullOrEmpty(username))
            {
                await Shell.Current.GoToAsync($"profile?user={Uri.EscapeDataString(username)}");
            }
        }
        catch { }
    }

    private async void OnFriendTapped(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is MALPlus.Models.Models.MalSpecific.MalFriend f)
            {
                ((CollectionView)sender).SelectedItem = null;
                await Shell.Current.GoToAsync($"profile?user={Uri.EscapeDataString(f.User.Name)}");
            }
        }
        catch { }
    }

    private void OnAcceptClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button b && b.CommandParameter is object req)
            {
                Vm.AcceptFriendRequestCommand?.Execute(req);
            }
        }
        catch { }
    }

    private void OnDenyClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button b && b.CommandParameter is object req)
            {
                Vm.DenyFriendRequestCommand?.Execute(req);
            }
        }
        catch { }
    }
}
