using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.ScrappedDetails;
using MALPlus.Services;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Details;

namespace MALPlus.Views;

[QueryProperty(nameof(StaffId), "id")]
public partial class StaffDetailsPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _loadedId = -1;
    private StaffDetailsViewModel _subscribedVm;
    private int _selectedTab;
    private readonly List<int> _visibleTabs = new() { 0 };

    public string StaffId { get; set; }

    private StaffDetailsViewModel Vm => BindingContext as StaffDetailsViewModel;

    public StaffDetailsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.StaffDetails;
        UpdateTabVisuals();
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
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS StaffDetails Init failed: " + ex);
            }
            ApplyTabsAndLanding(resetToFirstTab: true);
        }
        else
        {
            ApplyTabsAndLanding(resetToFirstTab: false);
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
        if (index < 0 || index > 2 || !_visibleTabs.Contains(index))
            return;
        _ = SelectTabAsync(index, true);
    }

    private void ApplyTabsAndLanding(bool resetToFirstTab)
    {
        var data = Vm?.Data;
        var hasInfo = (data?.Details?.Count ?? 0) > 0;
        var hasVoice = (data?.ShowCharacterPairs?.Count ?? 0) > 0;
        var hasProd = (data?.StaffPositions?.Count ?? 0) > 0;

        // If nothing has content, still keep Info visible to show the empty notice cleanly.
        if (!hasInfo && !hasVoice && !hasProd)
            hasInfo = true;

        _visibleTabs.Clear();
        if (hasInfo) _visibleTabs.Add(0);
        if (hasVoice) _visibleTabs.Add(1);
        if (hasProd) _visibleTabs.Add(2);

        TabInfo.IsVisible = hasInfo;
        TabVoiceRoles.IsVisible = hasVoice;
        TabProductionRoles.IsVisible = hasProd;
        TabStripScroll.IsVisible = _visibleTabs.Count > 1;

        if (InfoEmptyNotice != null)
            InfoEmptyNotice.IsVisible = (data?.Details?.Count ?? 0) == 0;

        if (RoleBadgeLabel != null)
        {
            RoleBadgeLabel.Text = (hasVoice, hasProd) switch
            {
                (true, true) => "SEIYUU & STAFF",
                (true, false) => "VOICE ACTOR",
                (false, true) => "STAFF",
                _ => "PERSON"
            };
        }

        if (resetToFirstTab || !_visibleTabs.Contains(_selectedTab))
        {
            _ = SelectTabAsync(_visibleTabs[0], false, forceApply: true);
        }
        else
        {
            UpdateTabVisuals();
        }
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
        if (_visibleTabs.Count <= 1)
            return false;

        var pos = _visibleTabs.IndexOf(_selectedTab);
        if (pos < 0)
            pos = 0;

        var nextPos = pos + (direction > 0 ? 1 : -1);
        if (nextPos < 0 || nextPos >= _visibleTabs.Count)
            return false;

        _ = SelectTabAsync(_visibleTabs[nextPos], true);
        return true;
    }

    private async Task SelectTabAsync(int index, bool animate, bool forceApply = false)
    {
        index = Math.Clamp(index, 0, 2);
        if (!forceApply && index == _selectedTab)
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
        if (index >= 0 && index < buttons.Length && buttons[index] != null && buttons[index].IsVisible)
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
