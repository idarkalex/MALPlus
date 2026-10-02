using System.Collections.ObjectModel;
using MALClient.Models.Enums;
using MALClient.Models.Models.Favourites;
using MALClient.XShared.Comm.Anime;
using MALClient.XShared.NavArgs;
using MALClient.XShared.ViewModels;
using MALClient.XShared.ViewModels.Items;
using MALClient.XShared.ViewModels.Main;
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
        }
    }

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
        var entryTap = new TapGestureRecognizer();
        entryTap.Tapped += OnSearchEntryTapped;
        SearchEntry.GestureRecognizers.Add(entryTap);
            // Infinite scroll is list-level, so it lives on the grid's inner collection.
            ResultsGrid.Inner.SelectionMode = SelectionMode.Single;
            ResultsGrid.Inner.SelectionChanged += OnResultSelected;
            ResultsGrid.Inner.RemainingItemsThreshold = 4;
            ResultsGrid.Inner.RemainingItemsThresholdReachedCommand = MainVm?.LoadMoreCatalogueCommand;
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
                SearchTabIndex = 0;
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
        if (e.Parameter is string s && int.TryParse(s, out int idx))
        {
            SearchTabIndex = idx;
            if (idx == 2)
            {
                _catalogueModeIsGenre = true;
                MainVm.Init(new SearchPageNavigationArgs { ByGenre = true, Anime = true, IsCatalogue = true });
            }
            else if (idx == 3)
            {
                _catalogueModeIsGenre = false;
                MainVm.Init(new SearchPageNavigationArgs { ByStudio = true, Anime = true, IsCatalogue = true });
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

    // Tapping the bar while it already has focus raises no Focused event, so the list would
    // stay shut until the bar was blurred first. This covers that case.
    private void OnSearchEntryTapped(object sender, TappedEventArgs e)
    {
        if (!IsRecentSearchesOpen)
            OpenRecentSearches();
    }

    private void OpenRecentSearches()
    {
        if (IsRecentSearchesOpen)
            return;

        try
        {
            if (MainVm?.RecentSearches == null || MainVm.RecentSearches.Count == 0)
                return;

            RecentSearchesScrim.IsVisible = true;
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
            RecentSearchesScrim.IsVisible = false;
            RecentSearchesDrop.IsVisible = false;
        }
        catch { }
    }

    private bool IsRecentSearchesOpen => RecentSearchesScrim.IsVisible;

    private void OnRecentSearchesDismissed(object sender, TappedEventArgs e)
        => CloseRecentSearches();

    private void OnRecentSearchRowTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e?.Parameter is not string q || string.IsNullOrWhiteSpace(q))
                return;

            SearchEntry.Text = q;
            // Search first, then close, then drop the keyboard: hiding the keyboard raises
            // Unfocused and can cancel the rest of the work if it goes the other way round.
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
            if (e.CurrentSelection.FirstOrDefault() is Enum choice)
            {
                ((CollectionView)sender).SelectedItem = null;
                var isGenre = SearchTabIndex == 2 || (SearchTabIndex == 0 && _catalogueModeIsGenre);
                var args = new SearchPageNavigationArgs
                {
                    Anime = true,
                    IsCatalogue = true,
                    CatalogueTitle = choice.ToString(),
                    ByGenre = isGenre,
                    ByStudio = !isGenre
                };
                if (isGenre)
                    args.Genre = (AnimeGenreSearch)choice;
                else
                    args.Studio = (AnimeStudios)choice;
                await MainVm.LoadCatalogue(args);
                // Switch to Main tab (index 0) to show results
                SearchTabIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS SearchPage genre/studio failed: " + ex.Message);
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
