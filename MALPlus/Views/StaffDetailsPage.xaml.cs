using System.Collections.ObjectModel;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.ScrappedDetails;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Details;
using MALPlus.Services;
using Microsoft.Maui.Graphics;

namespace MALPlus.Views;

[QueryProperty(nameof(StaffId), "id")]
public partial class StaffDetailsPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _loadedId = -1;
    private StaffDetailsViewModel _subscribedVm;
    private int _selectedTab;

    public string StaffId { get; set; }

    public ObservableCollection<ShowCharacterPair> TopVoiceRoles { get; } = new();
    public ObservableCollection<AnimeLightEntry> TopProductionRoles { get; } = new();

    private StaffDetailsViewModel Vm => BindingContext as StaffDetailsViewModel;

    public StaffDetailsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.StaffDetails;
        if (Vm != null)
            Vm.PropertyChanged += OnVmPropertyChanged;
        UpdateTabVisuals();
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StaffDetailsViewModel.Data))
        {
            MainThread.BeginInvokeOnMainThread(PopulateOverviewShelves);
        }
    }

    private void PopulateOverviewShelves()
    {
        var vm = Vm;
        if (vm == null) return;

        TopVoiceRoles.Clear();
        if (vm.Data?.ShowCharacterPairs != null)
        {
            foreach (var pair in vm.Data.ShowCharacterPairs.Take(10))
                TopVoiceRoles.Add(pair);
        }

        TopProductionRoles.Clear();
        if (vm.Data?.StaffPositions != null)
        {
            foreach (var pos in vm.Data.StaffPositions.Take(10))
                TopProductionRoles.Add(pos);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var vm = Vm;
        if (vm == null)
            return;
        SubscribePivot(vm);
        int.TryParse(StaffId, out int id);
        if (!_initialized || _loadedId != id)
        {
            try
            {
                await vm.Init(new StaffDetailsNaviagtionArgs { Id = id });
                _loadedId = id;
                _initialized = true;
                PopulateOverviewShelves();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS StaffDetails Init failed: " + ex);
            }
            ResetRenderedItems();
        }
        else
        {
            PopulateOverviewShelves();
        }
    }

    protected override void OnDisappearing()
    {
        UnsubscribePivot();
        base.OnDisappearing();
    }

    private void SubscribePivot(StaffDetailsViewModel vm)
    {
        if (_subscribedVm == vm)
            return;
        UnsubscribePivot();
        vm.OnPivotItemSelectionRequest += OnPivotItemSelectionRequest;
        _subscribedVm = vm;
    }

    private void UnsubscribePivot()
    {
        if (_subscribedVm == null)
            return;
        _subscribedVm.OnPivotItemSelectionRequest -= OnPivotItemSelectionRequest;
        _subscribedVm = null;
    }

    private void OnPivotItemSelectionRequest(int index)
    {
        if (index < 0 || index > 2)
            return;
        _ = SelectTabAsync(index, true);
    }

    private void ResetRenderedItems()
    {
        if ((Vm?.Data?.ShowCharacterPairs?.Count ?? 0) == 0 && (Vm?.Data?.StaffPositions?.Count ?? 0) > 0)
            _ = SelectTabAsync(2, false);
        else if ((Vm?.Data?.ShowCharacterPairs?.Count ?? 0) == 0)
            _ = SelectTabAsync(1, false);
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        try
        {
            if (Shell.Current != null)
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS StaffDetails Back failed: " + ex.Message);
        }
    }

    private async void OnTabInfoClicked(object sender, EventArgs e) => await SelectTabAsync(0, true);
    private async void OnTabVoiceRolesClicked(object sender, EventArgs e) => await SelectTabAsync(1, true);
    private async void OnTabProductionRolesClicked(object sender, EventArgs e) => await SelectTabAsync(2, true);

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var next = _selectedTab + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 2)
            return false;

        _ = SelectTabAsync(next, true);
        return true;
    }

    private async Task SelectTabAsync(int index, bool animate)
    {
        index = Math.Clamp(index, 0, 2);
        if (index == _selectedTab)
        {
            await ResetTabTranslationAsync();
            return;
        }

        int direction = index > _selectedTab ? 1 : -1;
        InfoTab.IsVisible = index == 0;
        VoiceRolesTab.IsVisible = index == 1;
        ProductionRolesTab.IsVisible = index == 2;
        _selectedTab = index;
        UpdateTabVisuals();
        ScrollTabStripIntoView(index);

        if (animate && TabContentHost.Width > 0)
        {
            TabContentHost.TranslationX = direction * TabContentHost.Width;
            await TabContentHost.TranslateTo(0, 0, 160, Easing.CubicOut);
        }
        else
        {
            TabContentHost.TranslationX = 0;
        }
    }

    private void ScrollTabStripIntoView(int index)
    {
        if (TabStripScroll == null) return;
        var buttons = new[] { TabInfo, TabVoiceRoles, TabProductionRoles };
        if (index >= 0 && index < buttons.Length && buttons[index] != null)
        {
            var btn = buttons[index];
            var targetX = Math.Max(0, btn.X - 16);
            _ = TabStripScroll.ScrollToAsync(targetX, 0, true);
        }
    }

    private async Task ResetTabTranslationAsync()
    {
        if (Math.Abs(TabContentHost.TranslationX) <= 0.5)
        {
            TabContentHost.TranslationX = 0;
            return;
        }
        await TabContentHost.TranslateTo(0, 0, 140, Easing.CubicOut);
        TabContentHost.TranslationX = 0;
    }

    private void UpdateTabVisuals()
    {
        var buttons = new[] { TabInfo, TabVoiceRoles, TabProductionRoles };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
                TabChipHelper.ApplyStyle(buttons[i], i == _selectedTab);
        }
    }

    private async void OnVoiceRoleShowTapped(object sender, TappedEventArgs e)
    {
        var pair = (e?.Parameter as ShowCharacterPair)
                   ?? (sender as BindableObject)?.BindingContext as ShowCharacterPair;
        if (pair != null)
            await NavigateToMediaAsync(pair.AnimeLightEntry);
    }

    private async void OnVoiceRoleCharacterTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var pair = (e?.Parameter as ShowCharacterPair)
                       ?? (sender as BindableObject)?.BindingContext as ShowCharacterPair;
            if (pair != null && int.TryParse(pair.AnimeCharacter?.Id, out int characterId) && characterId > 0 &&
                Shell.Current != null)
                await Shell.Current.GoToAsync($"character?id={characterId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnVoiceRoleCharacterTapped failed: " + ex.GetType().Name);
        }
    }

    private async void OnStaffPositionTapped(object sender, TappedEventArgs e)
    {
        var entry = (e?.Parameter as AnimeLightEntry)
                    ?? (sender as BindableObject)?.BindingContext as AnimeLightEntry;
        if (entry != null)
            await NavigateToMediaAsync(entry);
    }

    private async Task NavigateToMediaAsync(AnimeLightEntry entry)
    {
        if (entry == null || entry.Id <= 0)
            return;
        try
        {
            var args = new AnimeDetailsPageNavigationArgs(entry.Id, entry.Title, null, null, null)
            {
                AnimeMode = entry.IsAnime,
                Source = PageIndex.PageStaffDetails
            };
            MauiDetailsNavigationHandoff.Set(args);
            var title = Uri.EscapeDataString(entry.Title ?? string.Empty);
            var route = entry.IsAnime
                ? $"animedetails?id={entry.Id}&title={title}"
                : $"animedetails?id={entry.Id}&title={title}&manga=true";
            if (Shell.Current != null)
                await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS StaffDetails media navigation failed: " + ex.GetType().Name);
        }
    }
}
