using System.Collections.ObjectModel;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.Favourites;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Details;
using MALPlus.Services;
using Microsoft.Maui.Graphics;

namespace MALPlus.Views;

[QueryProperty(nameof(CharId), "id")]
public partial class CharacterDetailsPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _loadedId = -1;
    private int _selectedTab;

    public string CharId { get; set; }

    public ObservableCollection<FavouriteViewModel> TopVoiceActors { get; } = new();
    public ObservableCollection<AnimeLightEntry> TopAnimeography { get; } = new();
    public ObservableCollection<AnimeLightEntry> TopMangaography { get; } = new();

    private CharacterDetailsViewModel Vm => BindingContext as CharacterDetailsViewModel;

    public CharacterDetailsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.CharacterDetails;
        if (Vm != null)
            Vm.PropertyChanged += OnVmPropertyChanged;
        UpdateTabVisuals();
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterDetailsViewModel.Data) ||
            e.PropertyName == nameof(CharacterDetailsViewModel.VoiceActors))
        {
            MainThread.BeginInvokeOnMainThread(PopulateOverviewShelves);
        }
    }

    private void PopulateOverviewShelves()
    {
        var vm = Vm;
        if (vm == null) return;

        TopVoiceActors.Clear();
        foreach (var va in vm.VoiceActors.Take(10))
            TopVoiceActors.Add(va);

        TopAnimeography.Clear();
        if (vm.Data?.Animeography != null)
        {
            foreach (var a in vm.Data.Animeography.Take(10))
                TopAnimeography.Add(a);
        }

        TopMangaography.Clear();
        if (vm.Data?.Mangaography != null)
        {
            foreach (var m in vm.Data.Mangaography.Take(10))
                TopMangaography.Add(m);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var vm = Vm;
        if (vm == null)
            return;
        int.TryParse(CharId, out int id);
        if (!_initialized || _loadedId != id)
        {
            try
            {
                await vm.Init(new CharacterDetailsNavigationArgs { Id = id });
                _loadedId = id;
                _initialized = true;
                PopulateOverviewShelves();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS CharacterDetails Init failed: " + ex);
            }
        }
        else
        {
            PopulateOverviewShelves();
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
            Console.WriteLine("MALPLUS CharacterDetails Back failed: " + ex.Message);
        }
    }

    private async void OnTabOverviewClicked(object sender, EventArgs e) => await SelectTabAsync(0, true);
    private async void OnTabVoiceActorsClicked(object sender, EventArgs e) => await SelectTabAsync(1, true);
    private async void OnTabAnimeographyClicked(object sender, EventArgs e) => await SelectTabAsync(2, true);
    private async void OnTabMangaographyClicked(object sender, EventArgs e) => await SelectTabAsync(3, true);

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var next = _selectedTab + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 3)
            return false;

        _ = SelectTabAsync(next, true);
        return true;
    }

    private async Task SelectTabAsync(int index, bool animate)
    {
        index = Math.Clamp(index, 0, 3);
        if (index == _selectedTab)
            return;

        int direction = index > _selectedTab ? 1 : -1;
        OverviewTab.IsVisible = index == 0;
        VoiceActorsTab.IsVisible = index == 1;
        AnimeographyTab.IsVisible = index == 2;
        MangaographyTab.IsVisible = index == 3;
        _selectedTab = index;
        UpdateTabVisuals();
        ScrollTabStripIntoView(index);

        if (animate && TabContentHost != null && TabContentHost.Width > 0)
        {
            TabContentHost.TranslationX = direction * TabContentHost.Width;
            await TabContentHost.TranslateTo(0, 0, 160, Easing.CubicOut);
        }
        else if (TabContentHost != null)
        {
            TabContentHost.TranslationX = 0;
        }
    }

    private void ScrollTabStripIntoView(int index)
    {
        if (TabStripScroll == null) return;
        var buttons = new[] { TabOverview, TabVoiceActors, TabAnimeography, TabMangaography };
        if (index >= 0 && index < buttons.Length && buttons[index] != null)
        {
            var btn = buttons[index];
            var targetX = Math.Max(0, btn.X - 16);
            _ = TabStripScroll.ScrollToAsync(targetX, 0, true);
        }
    }

    private void UpdateTabVisuals()
    {
        var buttons = new[] { TabOverview, TabVoiceActors, TabAnimeography, TabMangaography };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
                TabChipHelper.ApplyStyle(buttons[i], i == _selectedTab);
        }
    }

    private async void OnVoiceActorTapped(object sender, TappedEventArgs e)
    {
        try
        {
            int id = 0;
            if (e?.Parameter is string idStr && int.TryParse(idStr, out var parsedId))
                id = parsedId;
            else if (e?.Parameter is int numId)
                id = numId;
            else if (sender is BindableObject bo && bo.BindingContext is FavouriteViewModel fav && int.TryParse(fav.Data?.Id, out var favId))
                id = favId;

            if (id > 0 && Shell.Current != null)
                await Shell.Current.GoToAsync($"staff?id={id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnVoiceActorTapped failed: " + ex.GetType().Name);
        }
    }

    private async void OnAnimeographyTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var entry = (e?.Parameter as AnimeLightEntry)
                        ?? (sender as BindableObject)?.BindingContext as AnimeLightEntry;
            if (entry == null || entry.Id <= 0)
                return;

            var args = new AnimeDetailsPageNavigationArgs(entry.Id, entry.Title, null, null, null)
            {
                AnimeMode = entry.IsAnime,
                Source = PageIndex.PageCharacterDetails
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
            Console.WriteLine("MALPLUS OnAnimeographyTapped failed: " + ex.GetType().Name);
        }
    }
}
