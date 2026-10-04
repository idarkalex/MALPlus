using System.Collections.ObjectModel;
using MALClient.Models.Enums;
using MALClient.Models.Models.Favourites;
using MALClient.XShared.Comm.Anime;
using MALClient.XShared.NavArgs;
using MALClient.XShared.ViewModels;
using MALClient.XShared.ViewModels.Items;
using MALClient.XShared.ViewModels.Main;
using MALClient.XShared.Utils;
using MALPlus.Services;

namespace MALPlus.Views;

[QueryProperty(nameof(InitialQuery), "query")]
[QueryProperty(nameof(InitialMode), "mode")]
public partial class SearchPage : ContentPage
{
    private bool _initialized;
    private int _searchTabIndex;
    private bool _catalogueModeIsGenre = true;

    public string InitialQuery { get; set; }
    public string InitialMode { get; set; }

    private SearchPageViewModel MainVm => (SearchPageViewModel)BindingContext;
    private CharacterSearchViewModel CharVm => ViewModelLocator.CharacterSearch;

    public int SearchTabIndex
    {
        get => _searchTabIndex;
        set
        {
            _searchTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMainTabVisible));
        }
    }

    /// <summary>
    ///     The search UI is hosted by two tabs, Anime and Manga, because they are the same screen
    ///     with a different mode rather than two screens. Both indices light up the same content,
    ///     which is why this exists instead of an index comparison in the XAML.
    /// </summary>
    public bool IsMainTabVisible => SearchTabIndex <= 1;

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
            // one per studio.
            StudioIconCache.IconResolved += OnStudioIconResolved;
        }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // The overlay is left on screen otherwise, so coming back to the tab finds it open.
        CloseRecentSearches();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            // If a query was passed in, switch to Main tab and search.
            if (!string.IsNullOrWhiteSpace(InitialQuery))
            {
                var anime = string.IsNullOrEmpty(InitialMode) || InitialMode == "anime";
                SearchTabIndex = anime ? 0 : 1;
                MainVm.Init(new SearchPageNavigationArgs { Anime = anime, Query = InitialQuery, ForceQuery = true });
            }
            else
            {
                // First visit: show recent searches
                MainVm.Init(new SearchPageNavigationArgs { Anime = true, Query = "" });
            }
            // Load recent searches
            MainVm.LoadRecentSearches();
            // Init the character search VM too (it subscribes to the global search query)
            CharVm.Init(new SearchPageNavArgsBase());
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage Init failed: " + ex);
        }
    }

    private void OnSearchTabTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string s || !int.TryParse(s, out var idx))
            return;

        SearchTabIndex = idx;
        CloseRecentSearches();
        switch (idx)
        {
            // Anime and Manga are the two halves of the same screen, so picking one is also what
            // switches the mode. Setting it before anything else means the filter bar is rebuilt
            // for the right format list by the time the tab paints.
            case 0:
                MainVm.AnimeSearch = true;
                break;
            case 1:
                MainVm.AnimeSearch = false;
                break;
            case 3:
                _catalogueModeIsGenre = true;
                MainVm.Init(new SearchPageNavigationArgs { ByGenre = true, Anime = true, IsCatalogue = true });
                break;
            case 4:
                _catalogueModeIsGenre = false;
                MainVm.Init(new SearchPageNavigationArgs { ByStudio = true, Anime = true, IsCatalogue = true });
                break;
        }
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

    protected override bool OnBackButtonPressed()
    {
        // Back drops the dropdown first and only leaves the page once it is closed.
        if (IsRecentSearchesOpen)
        {
            CloseRecentSearches();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private static void HideKeyboard()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var inputManager = activity?.GetSystemService(global::Android.Content.Context.InputMethodService)
            as global::Android.Views.InputMethods.InputMethodManager;
        var token = activity?.Window?.DecorView?.WindowToken;
        if (inputManager != null && token != null)
            inputManager.HideSoftInputFromWindow(token, global::Android.Views.InputMethods.HideSoftInputFlags.None);
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
                var isGenre = SearchTabIndex == 3 || (IsMainTabVisible && _catalogueModeIsGenre);
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

                // Mode and tab first, catalogue second, and the order matters. Setting AnimeSearch
                // repopulates from the search lists, and setting the tab does not; doing either of
                // them after the catalogue load would reset the browse and leave the empty state
                // showing on top of the results that had just loaded.
                SearchTabIndex = 0;
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
