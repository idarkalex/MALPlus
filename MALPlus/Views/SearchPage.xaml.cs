using MALPlus.Controls;
using System.Collections.ObjectModel;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Favourites;
using MALPlus.Models.Models.Search;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Items;
using MALPlus.XShared.ViewModels.Main;
using MALPlus.XShared.Utils;
using MALPlus.Services;

namespace MALPlus.Views;

[QueryProperty(nameof(InitialQuery), "query")]
[QueryProperty(nameof(InitialMode), "mode")]
public partial class SearchPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _searchTabIndex;
    private bool _catalogueModeIsGenre = true;

    public string InitialQuery { get; set; }
    public string InitialMode { get; set; }

    private SearchPageViewModel MainVm => (SearchPageViewModel)BindingContext;
    private CharacterSearchViewModel CharVm => ViewModelLocator.CharacterSearch;
    public SearchEverywhereViewModel EverywhereVm => ViewModelLocator.SearchEverywhereViewModel;

    private string _allQuery;
    public string AllQuery
    {
        get => _allQuery;
        set { _allQuery = value; OnPropertyChanged(); }
    }

    public int SearchTabIndex
    {
        get => _searchTabIndex;
        set
        {
            _searchTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAllTabVisible));
            OnPropertyChanged(nameof(IsMainTabVisible));
            UpdateTabChipStyles();
        }
    }

    public bool IsAllTabVisible => SearchTabIndex == 0;
    public bool IsMainTabVisible => SearchTabIndex == 1 || SearchTabIndex == 2;

    private string _charQuery;
    public string CharQuery
    {
        get => _charQuery;
        set { _charQuery = value; OnPropertyChanged(); }
    }

    // Forwarded property for XAML binding (CharacterSearch VM lives separately)
    public System.Collections.IEnumerable CharacterResults => CharVm.FoundCharacters;

    public SearchPage()
    {
        InitializeComponent();
#if WINDOWS
        if (SearchBackButton != null)
            SearchBackButton.IsVisible = true;
#endif
        BindingContext = ViewModelLocator.SearchPage;
        // Subscribed here rather than through XAML: the Focused attribute on an Entry does
        // not reach the handler on Android, so the dropdown never opened.
        SearchEntry.Focused += OnSearchEntryFocused;
            // Infinite scroll is list-level, so it lives on the grid's inner collection.
            ResultsGrid.Inner.SelectionMode = SelectionMode.Single;
            ResultsGrid.Inner.SelectionChanged += OnResultSelected;
            ResultsGrid.Inner.RemainingItemsThreshold = 4;
            ResultsGrid.Inner.RemainingItemsThresholdReachedCommand = MainVm?.LoadMoreCatalogueCommand;
            // Scrolling the results means the user has moved on from the dropdown, and there is
            // no scrim to catch the tap any more, so the scroll itself has to dismiss it.
            ResultsGrid.Inner.Scrolled += OnResultsScrolled;
// Studio logos arrive after the row that asked for one has already been bound, so the
              // cache has to push them back in. One subscription for the whole list rather than
              // one per studio. Subscribed on appearing and not in the constructor: StudioIconCache
              // raises STATIC events, so a page that subscribes once and never leaves keeps the page
              // itself alive for the whole process lifetime, and every later visit added another one.
          }

protected override void OnDisappearing()
      {
          base.OnDisappearing();
          // The overlay is left on screen otherwise, so coming back to the tab finds it open.
          CloseRecentSearches();
          // Paired with the subscriptions in OnAppearing: static events outlive the page, so
          // leaving them attached kept every SearchPage ever created reachable from a root.
          StudioIconCache.IconResolved -= OnStudioIconResolved;
          StudioIconCache.MapReady -= OnStudioIconMapReady;
      }
  
      protected override async void OnAppearing()
      {
          base.OnAppearing();
          StudioIconCache.IconResolved -= OnStudioIconResolved;
          StudioIconCache.MapReady -= OnStudioIconMapReady;
          StudioIconCache.IconResolved += OnStudioIconResolved;
          StudioIconCache.MapReady += OnStudioIconMapReady;
          ResultsGrid?.InvalidateLayout();
          if (_initialized)
              return;
        _initialized = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(InitialQuery))
            {
                if (InitialMode == "anime")
                {
                    SelectTab(1);
                    MainVm.Init(new SearchPageNavigationArgs { Anime = true, Query = InitialQuery, ForceQuery = true });
                }
                else if (InitialMode == "manga")
                {
                    SelectTab(2);
                    MainVm.Init(new SearchPageNavigationArgs { Anime = false, Query = InitialQuery, ForceQuery = true });
                }
                else
                {
                    // Global "All" search across MAL
                    AllQuery = InitialQuery;
                    SelectTab(0);
                    EverywhereVm.Init(new SearchPageNavigationArgs { Query = InitialQuery, ForceQuery = true });
                    MainVm.InternalQuery = InitialQuery;
                    CharQuery = InitialQuery;
                }
            }
            else
            {
                SelectTab(0);
                MainVm.Init(new SearchPageNavigationArgs { Anime = true, Query = "" });
            }
            MainVm.LoadRecentSearches();
            CharVm.Init(new SearchPageNavArgsBase());
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage Init failed: " + ex);
        }
    }

    private void OnSearchButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out var idx))
        {
            SelectTab(idx);
        }
    }

    private void OnSearchTabTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string s || !int.TryParse(s, out var idx))
            return;

        SelectTab(idx);
    }

    private void SelectTab(int idx)
    {
        SearchTabIndex = idx;
        CloseRecentSearches();
        SearchEntry?.Unfocus();
        CharSearchEntry?.Unfocus();
        AllSearchEntry?.Unfocus();
        HideKeyboard();
        ScrollTabIntoView(idx);
        switch (idx)
        {
            case 0:
                if (!string.IsNullOrEmpty(AllQuery))
                    EverywhereVm.Init(new SearchPageNavigationArgs { Query = AllQuery, ForceQuery = true });
                break;
            case 1:
                MainVm.GenreSelectionGridVisibility = false;
                MainVm.AnimeSearch = true;
                _catalogueOpen = false;
                RestoreSearchScroll();
                break;
            case 2:
                MainVm.GenreSelectionGridVisibility = false;
                MainVm.AnimeSearch = false;
                _catalogueOpen = false;
                RestoreSearchScroll();
                break;
            case 3:
                if (!string.IsNullOrEmpty(CharQuery))
                    CharVm.Init(new SearchPageNavArgsBase());
                break;
            case 4:
                _catalogueModeIsGenre = true;
                MainVm.Init(new SearchPageNavigationArgs { ByGenre = true, Anime = true, IsCatalogue = true });
                RestoreListScroll(GenreList, _genreListScrollIndex);
                break;
            case 5:
                _catalogueModeIsGenre = false;
                MainVm.Init(new SearchPageNavigationArgs { ByStudio = true, Anime = true, IsCatalogue = true });
                var studioIds = MainVm.AvailableSelectionChoices
                    .Where(choice => choice.MalId > 0)
                    .Select(choice => choice.MalId);
                StudioIconCache.WarmMap(studioIds);
                RestoreListScroll(StudioList, _studioListScrollIndex);
                break;
        }
    }

    private void ScrollTabIntoView(int idx)
    {
        Button target = idx switch
        {
            0 => TabAll,
            1 => TabAnime,
            2 => TabManga,
            3 => TabCharacters,
            4 => TabGenres,
            5 => TabStudios,
            _ => null
        };
        if (target != null && TabStripScroll != null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await TabStripScroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, true);
                }
                catch { }
            });
        }
    }

    private void UpdateTabChipStyles()
    {
        if (TabAll == null || TabAnime == null || TabManga == null || TabCharacters == null || TabGenres == null || TabStudios == null)
            return;

        TabChipHelper.ApplyStyle(TabAll, _searchTabIndex == 0);
        TabChipHelper.ApplyStyle(TabAnime, _searchTabIndex == 1);
        TabChipHelper.ApplyStyle(TabManga, _searchTabIndex == 2);
        TabChipHelper.ApplyStyle(TabCharacters, _searchTabIndex == 3);
        TabChipHelper.ApplyStyle(TabGenres, _searchTabIndex == 4);
        TabChipHelper.ApplyStyle(TabStudios, _searchTabIndex == 5);
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var next = _searchTabIndex + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 5)
            return false;

        SelectTab(next);
        return true;
    }

    private void OnAllSearchCompleted(object sender, EventArgs e) => ExecuteAllSearch();
    private void OnAllSearchClicked(object sender, EventArgs e) => ExecuteAllSearch();
    private void OnClearAllQueryClicked(object sender, EventArgs e)
    {
        AllQuery = "";
        AllSearchEntry?.Focus();
    }

    private void ExecuteAllSearch()
    {
        var q = AllQuery?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            EverywhereVm.Init(new SearchPageNavigationArgs { Query = q, ForceQuery = true });
            MainVm.InternalQuery = q;
            CharQuery = q;
        }
    }

    private void OnEverywhereItemTapped(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not ISearchEverywhereItem item)
            return;
        if (item is SearchEverywhereAnimeItem anime)
            EverywhereVm.NavigateAnimeDetails(anime);
        else if (item is SearchEverywhereMangaItem manga)
            EverywhereVm.NavigateMangaDetails(manga);
        else if (item is SearchEverywhereCharacterItem ch)
            EverywhereVm.NavigateCharacterDetails(ch);
        else if (item is SearchEverywhereUserItem u)
            EverywhereVm.NavigateUserDetails(u);
        else if (item is SearchEverywherePersonItem p)
            EverywhereVm.NavigatePersonDetails(p);
    }

    /// <summary>
    ///     A studio's logo arrived after the row that asked for it had already been bound, so it
    ///     gets pushed back in. One subscription for the whole list rather than one per studio.
    /// </summary>
    private void OnStudioIconResolved(int malId, string url)
    {
        var choices = MainVm.AvailableSelectionChoices;
        if (choices == null || string.IsNullOrEmpty(url))
            return;
        foreach (var choice in choices)
        {
            if (choice.MalId == malId)
            {
                choice.IconUrl = url;
                return;
            }
        }
    }

    /// <summary>
    ///     The bulk map landed, so every row gets asked again rather than waiting for its own
    ///     single fetch to come back one at a time.
    /// </summary>
    private void OnStudioIconMapReady()
    {
        var choices = MainVm.AvailableSelectionChoices;
        if (choices == null)
            return;
        foreach (var choice in choices)
            choice.IconUrl = StudioIconCache.GetIconUrl(choice.MalId);
    }

    private void OnSearchCompleted(object sender, EventArgs e)
    {
        CloseRecentSearches();
        try { MainVm.SearchCommand.Execute(null); } catch { }
    }

    // The dropdown hangs off the search bar: it opens when the bar takes focus and closes on
    // a tap anywhere else, on a picked entry, or on back. It is deliberately NOT closed from
    // Unfocused, because Android raises that the moment a finger lands inside the list, which
    // would close it before the row handled the tap. The scrim covers "tap outside" instead.
    private void OnSearchEntryFocused(object sender, FocusEventArgs e)
        => OpenRecentSearches();

    private void OpenRecentSearches()
    {
        if (IsRecentSearchesOpen)
            return;

        try
        {
            if (MainVm?.RecentSearches == null || MainVm.RecentSearches.Count == 0)
                return;
            RecentSearchesDrop.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS recent searches open failed: " + ex.Message);
        }
    }

    private void CloseRecentSearches()
    {
        try
        {
            if (!IsRecentSearchesOpen)
                return;

            RecentSearchesDrop.IsVisible = false;
            // Release the bar as well. While it keeps the focus, a tap on it raises no Focused
            // event, which is why the list used to open only every other time.
            SearchEntry.Unfocus();
        }
        catch { }
    }

    /// <summary>
    ///     Tracked off the dropdown itself. It used to read a scrim that no longer exists, because
    ///     the scrim was what broke scrolling: it covered the results grid and Android gave it the
    ///     touch first.
    /// </summary>
    private bool IsRecentSearchesOpen => RecentSearchesDrop?.IsVisible == true;

    private void OnRecentSearchRemoveTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e?.Parameter is string q && !string.IsNullOrWhiteSpace(q))
                MainVm.RemoveRecentSearch(q);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS recent search remove failed: " + ex.Message);
        }
    }

    // The container below the search bar carries this. It used to be a full screen scrim, but
    // a transparent overlay that overlaps the bar is always above it on Android, and it
    // swallowed every tap meant for the rows, so the list only ever closed.
    /// <summary>
    ///     Scrolling the results dismisses the recent searches. With no scrim above the grid this is
    ///     what stops a leftover dropdown from hovering over the list the user is trying to read.
    /// </summary>
    private void OnResultsScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        // Kept live rather than sampled on the way out, because by the time back is pressed the
        // catalogue is on screen and the grid underneath is no longer the one being asked about.
        _searchScrollIndex = e.LastVisibleItemIndex;

        if (IsRecentSearchesOpen)
            CloseRecentSearches();
    }

    private void OnRecentSearchesDismissed(object sender, TappedEventArgs e)
        => CloseRecentSearches();

    private void OnRecentSearchRowTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e?.Parameter is not string q || string.IsNullOrWhiteSpace(q))
                return;

            SearchEntry.Text = q;
            // Set the query directly instead of trusting the bar's binding to have flushed by
            // the time the command reads it: Search() bails out on an empty query.
            MainVm.InternalQuery = q;
            MainVm.SearchCommand.Execute(null);
            CloseRecentSearches();
            // Drop the focus too: while the bar keeps it, the next tap raises no Focused
            // event and the list would appear to be stuck shut.
            SearchEntry.Unfocus();
            HideKeyboard();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS recent search failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     A genre or studio was picked, so the poster grid is showing a catalogue rather than the
    ///     user's own results. Back has to unwind that before it would leave search.
    /// </summary>
    private bool _catalogueOpen;

    /// <summary>Which of the two search tabs we were on before the catalogue took over.</summary>
    private int _tabBeforeCatalogue;

    /// <summary>
    ///     Where the search grid was scrolled to, remembered so back returns there. The view model
    ///     had fields for exactly this and never wrote them, which is why leaving a catalogue and
    ///     coming back dropped the user at the top of the list.
    /// </summary>
    private int _searchScrollIndex;
    private int _genreListScrollIndex;
    private int _studioListScrollIndex;

    protected override bool OnBackButtonPressed()
    {
        // Back drops the dropdown first and only leaves the page once it is closed.
        if (IsRecentSearchesOpen)
        {
            CloseRecentSearches();
            return true;
        }

        // A catalogue opened from Genres or Studios is a layer inside search, not a page: back
        // returns to the list, at the row it was left at, and only the next back leaves.
        if (_catalogueOpen)
        {
            CloseCatalogue();
            return true;
        }

        SaveScrollPositions();
        return base.OnBackButtonPressed();
    }

    private void CloseCatalogue()
    {
        _catalogueOpen = false;
        SearchTabIndex = _tabBeforeCatalogue;
        // Clears the catalogue and recomputes the empty state in one go. Setting the mode instead
        // would repopulate from the search lists while the grid still held the catalogue, which is
        // how results and No results found ended up on screen together.
        MainVm.ClearCatalogueSession();
        RestoreSearchScroll();
    }

    /// <summary>
    ///     The scroll has to be applied after the tab is visible, or the grid has no height yet and
    ///     the position is discarded.
    /// </summary>
    private void RestoreSearchScroll()
    {
        if (_searchScrollIndex <= 0)
            return;
        try
        {
            ResultsGrid.Inner.ScrollTo(_searchScrollIndex, false);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    ///     Puts a picker list back where it was. Applied after the tab is up, since a
    ///     CollectionView has no height to scroll until it is visible and silently drops the position.
    /// </summary>
    private void RestoreListScroll(CollectionView list, int index)
    {
        if (list == null || index <= 0)
            return;
        try
        {
            list.ScrollTo(index, false);
        }
        catch (Exception)
        {
        }
    }

    private void SaveScrollPositions()
    {
        // Recorded continuously in OnResultsScrolled, so the grid position is already current. The
        // picker lists are not observed that way, so they are read on demand.
        _genreListScrollIndex = Math.Max(_genreListScrollIndex, 0);
        _studioListScrollIndex = Math.Max(_studioListScrollIndex, 0);
    }

    private static void HideKeyboard()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var inputManager = activity?.GetSystemService(global::Android.Content.Context.InputMethodService)
            as global::Android.Views.InputMethods.InputMethodManager;
        var token = activity?.Window?.DecorView?.WindowToken;
        if (inputManager != null && token != null)
            inputManager.HideSoftInputFromWindow(token, global::Android.Views.InputMethods.HideSoftInputFlags.None);
#endif
    }

    private async void OnResultTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not AnimeSearchItemViewModel item)
            return;
        await NavigateToResultAsync(item);
    }

    private async void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is AnimeSearchItemViewModel item)
            {
                ResultsGrid.Inner.SelectedItem = null;
                await NavigateToResultAsync(item);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage result nav failed: " + ex.Message);
        }
    }

    private async Task NavigateToResultAsync(AnimeSearchItemViewModel item)
    {
        CloseRecentSearches();
        SearchEntry.Unfocus();
        HideKeyboard();
        var titleQs = Uri.EscapeDataString(item.Title ?? string.Empty);
        var manga = !item.AnimeMode;
        var handoffArgs = new AnimeDetailsPageNavigationArgs(item.Id, item.Title, null, item,
            new SearchPageNavigationArgs
            {
                Query = MainVm.PrevQuery,
                Anime = item.AnimeMode,
                DisplayMode = MainVm.PrevArgs?.DisplayMode ?? SearchPageDisplayModes.Main
            })
        {
            Source = item.AnimeMode ? PageIndex.PageSearch : PageIndex.PageMangaSearch,
            AnimeMode = item.AnimeMode
        };
        MauiDetailsNavigationHandoff.Set(handoffArgs);
        var route = manga
            ? $"animedetails?id={item.Id}&title={titleQs}&manga=true"
            : $"animedetails?id={item.Id}&title={titleQs}";
        await Shell.Current.GoToAsync(route);
    }

private async void OnGenreStudioSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is SearchChoiceItem choice)
            {
                ((CollectionView)sender).SelectedItem = null;
                var isGenre = SearchTabIndex == 4 || (IsMainTabVisible && _catalogueModeIsGenre);
                var args = new SearchPageNavigationArgs
                {
                    Anime = true,
                    IsCatalogue = true,
                    CatalogueTitle = choice.Name,
                    ByGenre = isGenre,
                    ByStudio = !isGenre
                };
                if (isGenre)
                    args.Genre = (AnimeGenreSearch)choice.Value;
                else
                    args.Studio = (AnimeStudios)choice.Value;

                // Remembered so back can unwind the catalogue rather than leaving search.
                _catalogueOpen = true;
                _tabBeforeCatalogue = IsMainTabVisible ? SearchTabIndex : 1;

                // Mode and tab first, catalogue second, and the order matters. Setting AnimeSearch
                // repopulates from the search lists, and setting the tab does not; doing either of
                // them after the catalogue load would reset the browse and leave the empty state
                // showing on top of the results that had just loaded.
                SearchTabIndex = 1;
                MainVm.AnimeSearch = true;

                await MainVm.LoadCatalogue(args);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage genre/studio failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Tapping a format chip in the bottom bar toggles it. Several can be marked at once and
    ///     they combine as a union, so OVA plus TV shows both.
    /// </summary>
    private void OnTypeFilterTapped(object sender, TappedEventArgs e)
    {
        if (e?.Parameter is not SearchTypeFilterChip chip)
            return;
        CloseRecentSearches();
        MainVm.ToggleTypeFilter(chip);
    }

    private async void OnSearchBackClicked(object sender, EventArgs e)
    {
        try
        {
            if (Navigation?.NavigationStack?.Count > 1)
                await Navigation.PopAsync();
            else
                await Shell.Current.GoToAsync("///discover");
        }
        catch { }
    }

    /// <summary>
    ///     The clear button: text, results and filters all go, so the page is left as it was before
    ///     anything was typed. The dropdown is closed too, and the bar unfocused, otherwise the
    ///     keyboard stays up over an empty page.
    /// </summary>
    private void OnClearQueryClicked(object sender, EventArgs e)
    {
        try
        {
            CloseRecentSearches();
            SearchEntry.Text = "";
            MainVm.ClearSearch();
            SearchEntry.Unfocus();
            HideKeyboard();
        }
catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage clear failed: " + ex.GetType().Name);
        }
    }

    private void OnCharacterSearchCompleted(object sender, EventArgs e) => DoCharacterSearch();
    private void OnCharacterSearchClicked(object sender, EventArgs e) => DoCharacterSearch();

    private void DoCharacterSearch()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CharQuery)) return;
            CharVm.SearchCharacters(CharQuery.Trim());
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS Character search failed: " + ex.Message);
        }
    }

    private async void OnCharacterSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is FavouriteViewModel item)
            {
                ((CollectionView)sender).SelectedItem = null;
                CharSearchEntry.Unfocus();
                HideKeyboard();
                if (int.TryParse(item.Data.Id, out int id))
                    await Shell.Current.GoToAsync($"character?id={id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage character nav failed: " + ex.Message);
        }
    }
}
