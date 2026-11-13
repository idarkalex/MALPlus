using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

[QueryProperty(nameof(TargetUser), "user")]
public partial class ProfilePage : ContentPage
{
    private bool _initialized;

    public string TargetUser { get; set; }

    private ProfilePageViewModel Vm => (ProfilePageViewModel)BindingContext;

    public ProfilePage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ProfilePage;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;
        _initialized = true;

        var user = TargetUser ?? Credentials.UserName;
        var args = new ProfilePageNavigationArgs
        {
            TargetUser = user
        };

        try
        {
            await Vm.LoadProfileData(args);
            ApplyProfileTabColors();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS Profile Init failed: " + ex);
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS Profile back failed: " + ex.Message);
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            if (Vm == null) return;
            await Vm.LoadProfileData(new ProfilePageNavigationArgs
            {
                TargetUser = TargetUser ?? Credentials.UserName
            }, force: true);
            ApplyProfileTabColors();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS Profile refresh failed: " + ex.Message);
        }
    }

    private void OnTabButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string raw && int.TryParse(raw, out var index))
        {
            Vm.SelectedTab = index;
            ApplyProfileTabColors();
        }
    }

    private void ApplyProfileTabColors()
    {
        if (TabBtnGeneral == null || TabBtnHistory == null || TabBtnFavourites == null || TabBtnStats == null)
            return;

        var selected = Vm?.SelectedTab ?? 0;
        var buttons = new[] { TabBtnGeneral, TabBtnHistory, TabBtnFavourites, TabBtnStats };
        var accentColor = Application.Current?.Resources?.TryGetValue("EmAccent", out var acc) == true && acc is Color c
            ? c
            : Color.FromArgb("#0066FF");

        for (var i = 0; i < buttons.Length; i++)
        {
            var active = i == selected;
            buttons[i].BackgroundColor = active ? accentColor : Color.FromArgb("#141D2B");
            buttons[i].TextColor = active ? Colors.White : Color.FromArgb("#8899A6");
        }
    }

    private void OnAddFriendClicked(object sender, EventArgs e)
    {
        try
        {
            Vm.SendFriendRequestCommand?.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS add friend failed: " + ex.Message);
        }
    }

    #region Action Handlers

    private async void OnHistoryClicked(object sender, EventArgs e)
    {
        try
        {
            var user = TargetUser ?? Credentials.UserName;
            await Shell.Current.GoToAsync($"history?user={Uri.EscapeDataString(user ?? "")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS History nav failed: " + ex.Message);
        }
    }

    private void OnOpenMalClicked(object sender, EventArgs e)
    {
        try
        {
            Vm.OpenMalProfileCommand?.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS Open MAL failed: " + ex.Message);
        }
    }

    #endregion

    #region Item Taps

    private async void OnFavAnimeItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is AnimeItemViewModel item)
        {
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}");
        }
    }

    private async void OnFavMangaItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is AnimeItemViewModel item)
        {
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}&manga=true");
        }
    }

    private async void OnFavCharacterItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is FavouriteViewModel item && int.TryParse(item.Data.Id, out int id))
        {
            await Shell.Current.GoToAsync($"character?id={id}");
        }
    }

    private async void OnFavStaffItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is FavouriteViewModel item && int.TryParse(item.Data.Id, out int id))
        {
            await Shell.Current.GoToAsync($"staff?id={id}");
        }
    }

    private async void OnRecentAnimeItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is AnimeItemViewModel item)
        {
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}");
        }
    }

    private async void OnRecentMangaItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is AnimeItemViewModel item)
        {
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}&manga=true");
        }
    }

    private async void OnFriendItemTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is MALPlus.Models.Models.MalUser user && !string.IsNullOrEmpty(user.Name))
        {
            await Shell.Current.GoToAsync($"profile?user={Uri.EscapeDataString(user.Name)}");
        }
    }

    #endregion
}
