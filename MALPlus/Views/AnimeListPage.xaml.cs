using MALClient.Models.Enums;
using MALClient.Models.Models.Anime;
using MALClient.XShared.BL;
using MALClient.XShared.Comm.Anime;
using MALClient.XShared.NavArgs;
using MALClient.XShared.Utils;
using MALClient.XShared.ViewModels;
using MALClient.XShared.ViewModels.Main;
using MALPlus.Services;

namespace MALPlus.Views;

[QueryProperty(nameof(Mode), "mode")]
[QueryProperty(nameof(Status), "status")]
[QueryProperty(nameof(Type), "type")]
[QueryProperty(nameof(Menu), "menu")]
[QueryProperty(nameof(Reset), "reset")]
public partial class AnimeListPage : ContentPage
{
    private bool _authedAtLoad;
    private string _userAtLoad;
    private bool _refreshArmed;
    private bool _applied;
    private int _appliedMode = -1;
    private int _appliedStatus = -1;
    private string _appliedType;
    private int _lastMode = -1;
    private int _lastStatus = -1;
    private bool _navConnected;
    private TopAnimeType _topType = TopAnimeType.General;
    private MangaTopType _mangaTopType = MangaTopType.All;
    private MangaAdaptedType _adaptedType = MangaAdaptedType.AiringNow;

    private const int DefaultStatusIndex = 0;
    private const int MaxStatusIndex = 5;

    public string Mode { get; set; }
    public string Status { get; set; }
    public string Type { get; set; }
    public string Menu { get; set; }
    public string Reset { get; set; }

    private AnimeListViewModel Vm => (AnimeListViewModel)BindingContext;

    // Tabs in display order; the value is the StatusSelectorSelectedIndex the shared VM expects:
    // 0=Watching, 1=Completed, 2=OnHold, 3=Dropped, 4=PlanToWatch, 5=All.
    private static readonly (string Label, int StatusSelectorIndex)[] AnimeStatusTabs = new[]
    {
        ("All", 5),
        ("Watching", 0),
        ("Completed", 1),
        ("On Hold", 2),
        ("Dropped", 3),
        ("Planned", 4),
    };

    private static readonly (string Label, int StatusSelectorIndex)[] MangaStatusTabs = new[]
    {
        ("All", 5),
        ("Reading", 0),
        ("Completed", 1),
        ("On Hold", 2),
        ("Dropped", 3),
        ("Planned", 4),
    };

    public AnimeListPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.AnimeList;
        // MAUI's CollectionView virtualizes rendering, so we load the ENTIRE user list
        // into AnimeItems (negative dimensions => GetGridItemsToLoad returns int.MaxValue).
        // Otherwise UpdatePageSetup only ever exposes ~27 items and never drains the rest.
            Vm.DimensionsProvider = new MauiDimensionsProvider();
            WireInfiniteScroll();
            WatchFirstItems();
        }

    private bool _watchedFirstItems;

    // The moment the grid has something in it is the moment the user stops looking at
    // an empty screen, so that is the one startup number worth having.
    private void WatchFirstItems()
    {
        if (_watchedFirstItems || Vm is not System.ComponentModel.INotifyPropertyChanged vm)
            return;

        _watchedFirstItems = true;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AnimeListViewModel.AnimeItems) && e.PropertyName != null)
                return;

            if ((Vm.AnimeItems?.Count ?? 0) <= 0)
                return;

            StartupTrace.Mark("first-items-visible");
            StartupTrace.LogSummary();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_navConnected)
        {
            BottomNav.SectionActivated += OnSectionActivated;
            BottomNav.SectionChanging += OnSectionChanging;
            _navConnected = true;
        }
        ApplyRoute(null);
    }

    private void OnSectionActivated(string route)
    {
        ApplyRoute(route);
    }

    /// <summary>
    ///     Runs on the tap, before the shell has navigated, so the status name and the dropdown
    ///     follow the finger. Waiting for <see cref="OnSectionActivated" /> meant the name only
    ///     changed once the pager had finished scrolling, which read as lag on the label.
    /// </summary>
    private void OnSectionChanging(string route)
    {
        try
        {
            var query = ParseQuery(route);
            var mode = ResolveMode(query, out var modeFromQuery);
            var status = ResolveStatus(query, modeFromQuery);
            _lastMode = mode;
            _lastStatus = status;
            CloseStatusDropdown();
            UpdateContextBars();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS section changing failed: " + ex.Message);
        }
    }

    private void ApplyRoute(string route)
    {
        try
        {
            var query = ParseQuery(route);
            var mode = ResolveMode(query, out var modeFromQuery);
            var status = ResolveStatus(query, modeFromQuery);
            var type = ResolveType(query, modeFromQuery);
            ParseTypeQuery(mode, type);

            var openStatusMenu = string.Equals(QueryValue(query, "menu", Menu), "status", StringComparison.OrdinalIgnoreCase);
            var forceReset = string.Equals(QueryValue(query, "reset", Reset), "1", StringComparison.Ordinal);
            Menu = null;
            Reset = null;
            // The query-backed properties are consumed above, so they must not leak into the
            // next entry. ApplyRoute also runs on a plain back-nav with no query at all, and a
            // Status left over from an earlier route kept ResolveStatus pinned to that stale
            // value, which is why returning from a details page reopened the default tab
            // instead of the one that had been selected.
            Mode = null;
            Status = null;
            Type = null;

            var alreadyShowing =
                !forceReset &&
                _applied &&
                _appliedMode == mode &&
                _appliedStatus == status &&
                string.Equals(_appliedType, type, StringComparison.Ordinal) &&
                (Vm.AnimeItems?.Count ?? 0) > 0;
            Android.Util.Log.Info("MALPLUS",
                $"AnimeList apply route='{route ?? "(properties)"}' -> mode={mode} status={status} type={type} reset={forceReset} skip={alreadyShowing} applied=({_appliedMode},{_appliedStatus},{_appliedType})");
            if (alreadyShowing)
            {
if (openStatusMenu)
                OpenStatusMenu(mode);
                UpdateContextBars();
                return;

            }

            _applied = true;
            _appliedMode = mode;
            _appliedStatus = status;
            _appliedType = type;
            _lastMode = mode;
            _lastStatus = status;
            UpdateContextBars();

            _ = InitAndObserveAsync(mode, status, openStatusMenu);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS AnimeListPage ApplyRoute failed: " + ex);
        }
    }

    private static Dictionary<string, string> ParseQuery(string route)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(route))
            return result;
        var parts = route.Split(new[] { '?' }, 2);
        if (parts.Length < 2)
            return result;
        foreach (var pair in parts[1].Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split(new[] { '=' }, 2);
            var key = Uri.UnescapeDataString(kv[0]);
            if (result.ContainsKey(key))
                continue;
            result[key] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
        }
        return result;
    }

    private static string QueryValue(Dictionary<string, string> query, string key, string fallback)
        => query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private async Task InitAndObserveAsync(int mode, int status, bool openStatusMenu)
    {
        try
        {
            var args = BuildArgs(mode, status);
            await Vm.Init(args);
            _refreshArmed = true;
        }
        catch (Exception ex)
        {
            _applied = false;
            Console.WriteLine("MALPLUS AnimeListPage Init failed: " + ex);
        }
        finally
        {
            UpdateContextBars();
            if (openStatusMenu)
                MainThread.BeginInvokeOnMainThread(() => OpenStatusMenu(mode));
        }
    }

    /// <summary>
    ///     Both context bars read from the applied route, never from the VM, so they always say what
    ///     the grid is actually showing. The status bar replaces the "⋮" glyph that used to sit inside
    ///     the Anime and Manga cells of the navigation bar: the four tabs were already centred, but
    ///     those two cells carried an extra element and the row read as off centre. The season bar is
    ///     Seasonal only and sits directly above the navigation so the grid never moves.
    /// </summary>
    private void UpdateContextBars()
    {
        try
        {
            var manga = _lastMode == (int)AnimeListWorkModes.Manga;
            var isList = _lastMode == (int)AnimeListWorkModes.Anime || manga;

            StatusBar.IsVisible = isList;
            if (isList)
            {
                var tabs = manga ? MangaStatusTabs : AnimeStatusTabs;
                var label = tabs.FirstOrDefault(tab => tab.StatusSelectorIndex == _lastStatus).Label;
                StatusLabel.Text = label ?? tabs[1].Label;
                StatusLabel.Text = manga && label == "Watching" ? "Reading" : label ?? StatusLabel.Text;
            }

            var seasonal = _lastMode == (int)AnimeListWorkModes.SeasonalAnime;
            SeasonBar.IsVisible = seasonal;
            if (seasonal)
                UpdateSeasonBar();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS context bars failed: " + ex.Message);
        }
    }

    private static AnimeSeason SeasonStep(AnimeSeason season, int delta)
    {
        var order = new[] { Season.Winter, Season.Spring, Season.Summer, Season.Fall };
        var current = season ?? DefaultSeason();
        var index = Array.IndexOf(order, current.Season);
        if (index < 0)
            index = 0;
        var next = (index + delta + order.Length) % order.Length;
        // Seasons run Winter -> Spring -> Summer -> Fall within a year, so stepping back from
        // Winter lands on the previous year's Fall and stepping forward from Fall on the next
        // year's Winter.
        var year = current.Year;
        if (delta < 0 && next > index)
            year--;
        else if (delta > 0 && next < index)
            year++;
        return new AnimeSeason { Year = year, Season = order[next] };
    }

    private static AnimeSeason DefaultSeason()
    {
        var now = DateTime.Now;
        return new AnimeSeason
        {
            Year = now.Year,
            Season = now.Month switch
            {
                >= 3 and <= 5 => Season.Spring,
                >= 6 and <= 8 => Season.Summer,
                >= 9 and <= 11 => Season.Fall,
                _ => Season.Winter
            }
        };
    }

    private static string SeasonLabel(AnimeSeason season)
    {
        var name = season?.Season switch
        {
            Season.Winter => "Winter",
            Season.Spring => "Spring",
            Season.Summer => "Summer",
            _ => "Fall"
        };
        return $"{name} {season?.Year}";
    }

    private void UpdateSeasonBar()
    {
        var season = Vm?.CurrentSeason ?? DefaultSeason();
        SeasonPicker.Text = SeasonLabel(season);
    }

    private async void OnSeasonStepTapped(int delta)
    {
        try
        {
            var season = SeasonStep(Vm?.CurrentSeason ?? DefaultSeason(), delta);
            await LoadSeasonAsync(season);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS season step failed: " + ex.Message);
        }
    }

    private void OnSeasonPreviousTapped(object sender, EventArgs e) => OnSeasonStepTapped(-1);

    private void OnSeasonNextTapped(object sender, EventArgs e) => OnSeasonStepTapped(1);

    private async void OnSeasonPickerTapped(object sender, EventArgs e)
    {
        try
        {
            // The VM already loads this list from Tenrai when the mode initialises, so the picker
            // costs nothing extra: it only decides when that happens.
            if (Vm.SeasonSelection.Count == 0)
                await Vm.PopulateSeasonSelectionAsync();
            if (Vm.SeasonSelection.Count == 0)
                return;

            var current = SeasonLabel(Vm.CurrentSeason ?? DefaultSeason());
            var labels = Vm.SeasonSelection.Select(season => SeasonLabel(season)).ToArray();
            var choice = await DisplayActionSheet("Season", "Cancel", null,
                labels.Append(current).Distinct().ToArray());
            if (string.IsNullOrEmpty(choice) || choice == "Cancel" || choice == current)
                return;

            var picked = Vm.SeasonSelection.FirstOrDefault(season => SeasonLabel(season) == choice);
            if (picked != null)
                await LoadSeasonAsync(picked);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS season picker failed: " + ex.Message);
        }
    }

    private async Task LoadSeasonAsync(AnimeSeason season)
    {
        if (season == null)
            return;
        var args = BuildArgs((int)AnimeListWorkModes.SeasonalAnime, _lastStatus);
        args.CurrSeason = season;
        _lastMode = (int)AnimeListWorkModes.SeasonalAnime;
        await Vm.Init(args);
        UpdateContextBars();
    }

    private async void OnStatusDropdownTapped(object sender, EventArgs e)
    {
        try
        {
            UpdateContextBars();
            OpenStatusMenu(_lastMode);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS status dropdown failed: " + ex.Message);
        }
    }

    private int ResolveMode(Dictionary<string, string> query, out bool fromQuery)
    {
        fromQuery = false;
        if (query.TryGetValue("mode", out var raw) &&
            int.TryParse(raw, out var parsed) &&
            System.Enum.IsDefined(typeof(AnimeListWorkModes), parsed))
        {
            fromQuery = true;
            return parsed;
        }
        if (!string.IsNullOrWhiteSpace(Mode) &&
            int.TryParse(Mode, out var fromProperty) &&
            System.Enum.IsDefined(typeof(AnimeListWorkModes), fromProperty))
        {
            fromQuery = true;
            return fromProperty;
        }
        return IsMangaLocation() ? (int) AnimeListWorkModes.Manga : (int) AnimeListWorkModes.Anime;
    }

    private static bool IsMangaLocation()
    {
        var location = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
        var path = location.Split(new[] { '?' }, 2)[0];
        foreach (var segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, "manga", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private int ResolveStatus(Dictionary<string, string> query, bool fromQuery)
    {
        if (fromQuery && query.TryGetValue("status", out var raw) &&
            int.TryParse(raw, out var parsed) && parsed >= 0 && parsed <= MaxStatusIndex)
            return parsed;
        return ResolveStatus();
    }

    private int ResolveStatus()
    {
        if (!string.IsNullOrWhiteSpace(Status))
        {
            if (int.TryParse(Status, out var parsed) && parsed >= 0 && parsed <= MaxStatusIndex)
                return parsed;
            return DefaultStatusIndex;
        }

        // No query and no [QueryProperty]: this is a back-nav from a details page, so the
        // tab the user had chosen is still the right one. Falling back to the default here
        // is what made "Planned" or "All" reopen as "Watching".
        if (_appliedStatus >= 0)
            return _appliedStatus;
        if (_lastStatus >= 0)
            return _lastStatus;

        return DefaultStatusIndex;
    }

    private string ResolveType(Dictionary<string, string> query, bool fromQuery)
    {
        if (fromQuery && query.TryGetValue("type", out var raw) && !string.IsNullOrWhiteSpace(raw))
            return raw.Trim();
        return ResolveType();
    }

    private string ResolveType()
        => string.IsNullOrWhiteSpace(Type) ? null : Type.Trim();

    private void ParseTypeQuery(int mode, string type)
    {
        _topType = TopAnimeType.General;
        _mangaTopType = MangaTopType.All;
        _adaptedType = MangaAdaptedType.AiringNow;
        if (string.IsNullOrWhiteSpace(type))
            return;
        try
        {
            switch ((AnimeListWorkModes)mode)
            {
                case AnimeListWorkModes.TopAnime:
                    _topType = (TopAnimeType)System.Enum.Parse(typeof(TopAnimeType), type);
                    break;
                case AnimeListWorkModes.TopManga:
                    _mangaTopType = (MangaTopType)System.Enum.Parse(typeof(MangaTopType), type);
                    break;
                case AnimeListWorkModes.MangaAdapted:
                    _adaptedType = (MangaAdaptedType)System.Enum.Parse(typeof(MangaAdaptedType), type);
                    break;
            }
        }
        catch
        {
            // fall back to defaults
        }
    }

    /// <summary>
    ///     The status picker, as a panel under the bar instead of a native action sheet. The sheet
    ///     was the last thing here still looking like the old app, and it also meant the choice could
    ///     not show which one was active until it was dismissed.
    /// </summary>
    private void OpenStatusMenu(int mode)
    {
        try
        {
            CloseStatusDropdown();
            var manga = mode == (int)AnimeListWorkModes.Manga;
            var tabs = manga ? MangaStatusTabs : AnimeStatusTabs;

            StatusDropdownList.Clear();
            var sheetColor = Color.FromArgb("#0A1F2E");
            var accent = Color.FromArgb("#0066FF");
            foreach (var tab in tabs)
            {
                var selected = tab.StatusSelectorIndex == _lastStatus;
                var selectorIndex = tab.StatusSelectorIndex;

                // A Grid carrying only a TapGestureRecognizer is not hit tested on Android unless it
                // has a real background, so the row is painted in the panel's own colour: invisible
                // against it, but tappable. A Button cannot be used here because it cannot host
                // content in this MAUI version.
                var row = new Grid
                {
                    BackgroundColor = sheetColor,
                    HeightRequest = 44,
                    Padding = new Thickness(12, 0),
                    ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("Auto") },
                    AutomationId = $"StatusRow{selectorIndex}"
                };
                var label = new Label
                {
                    Text = manga && tab.Label == "Watching" ? "Reading" : tab.Label,
                    FontFamily = "InterSemiBold",
                    FontSize = 15,
                    TextColor = selected ? accent : Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                var check = new Image
                {
                    Source = "icon_check",
                    WidthRequest = 18,
                    HeightRequest = 18,
                    IsVisible = selected,
                    // The row is the tap target, so the glyph must not take it.
                    InputTransparent = true,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(check, 1);
                row.Add(label);
                row.Add(check);
                var tap = new TapGestureRecognizer { CommandParameter = selectorIndex };
                tap.Tapped += (_, args) => OnStatusRowTapped(Convert.ToInt32(args.Parameter));
                row.GestureRecognizers.Add(tap);
                StatusDropdownList.Add(row);
            }

            StatusDropdownOverlay.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS status dropdown build failed: " + ex.Message);
        }
    }

    private void OnStatusRowTapped(int statusSelectorIndex)
    {
        CloseStatusDropdown();
        if (statusSelectorIndex == _lastStatus)
            return;
        OnStatusTabClicked(statusSelectorIndex);
    }

    private void CloseStatusDropdown()
    {
        try
        {
            if (StatusDropdownOverlay != null)
                StatusDropdownOverlay.IsVisible = false;
        }
        catch { }
    }

    private void OnStatusDropdownDismissed(object sender, TappedEventArgs e)
    {
        CloseStatusDropdown();
        Unfocus();
    }

    private void OnStatusDropdownTapped(object sender, TappedEventArgs e)
    {
        try
        {
            UpdateContextBars();
            if (StatusDropdownOverlay.IsVisible)
            {
                CloseStatusDropdown();
                Unfocus();
                return;
            }

            OpenStatusMenu(_lastMode);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS status dropdown toggle failed: " + ex.Message);
        }
    }

    private async void OnStatusTabClicked(int statusSelectorIndex)
    {
        try
        {
            _lastStatus = statusSelectorIndex;
            // Keep the applied status in step with the tab the user just picked. Coming back
            // from a details page re-runs ApplyRoute with no query, and that guard compares
            // against _appliedStatus: without this the list silently reopened on the default
            // tab instead of the one that was selected.
            _appliedStatus = statusSelectorIndex;
            // This path does not go through ApplyRoute, so the bar would keep showing the old
            // status until something else happened to refresh it.
            UpdateContextBars();
            var args = BuildArgs(_lastMode, statusSelectorIndex);
            await Vm.Init(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS status tab failed: " + ex.Message);
        }
    }

    private AnimeListPageNavigationArgs BuildArgs(int mode, int statusSelectorIndex)
    {
        var workMode = (AnimeListWorkModes)mode;
        switch (workMode)
        {
            case AnimeListWorkModes.Manga:
                return new AnimeListPageNavigationArgs(statusSelectorIndex, AnimeListWorkModes.Manga);
            case AnimeListWorkModes.Anime:
                // Never silently switch work mode when there is no session: let the
                // ViewModel surface its "log in or set it manually" notice instead.
                return new AnimeListPageNavigationArgs(statusSelectorIndex, AnimeListWorkModes.Anime)
                {
                    ListSource = Credentials.UserName
                };
            case AnimeListWorkModes.SeasonalAnime:
                return AnimeListPageNavigationArgs.Seasonal;
            case AnimeListWorkModes.TopAnime:
                return AnimeListPageNavigationArgs.TopAnime(_topType);
            case AnimeListWorkModes.TopManga:
                return AnimeListPageNavigationArgs.TopMangaCategory(_mangaTopType);
            case AnimeListWorkModes.MangaAdapted:
                return AnimeListPageNavigationArgs.MangaAdapted(_adaptedType);
            default:
                return AnimeListPageNavigationArgs.TopAnime(_topType);
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            if (!_refreshArmed || Vm == null || Vm.Initializing)
            {
                ListRefresh.IsRefreshing = false;
                return;
            }
            // Awaited, and the spinner is put away in the finally. RefreshCommand wraps an async
            // void, so there was nothing to await here and IsRefreshing stayed true once the
            // gesture had set it: the arrow up top spun forever.
            //
            // Infinite loading is unhooked for the duration. The grid gets shorter while the
            // refresh rebuilds it, and that trips RemainingItemsThresholdReached, whose command
            // drains the pending items straight into the list: one extra card appeared and then
            // vanished again when the refresh finished repopulating.
            var loadMore = _grid?.RemainingItemsThresholdReachedCommand;
            if (_grid != null)
                _grid.RemainingItemsThresholdReachedCommand = null;
            try
            {
                await Vm.RefreshListAsync();
            }
            finally
            {
                if (_grid != null)
                    _grid.RemainingItemsThresholdReachedCommand = loadMore;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS refresh failed: " + ex.Message);
        }
        finally
        {
            // Both flags, not just the view one. Loading is only cleared by UpdatePageSetup, and
            // a list that already had items never goes through the empty branch that used to
            // lower it as a backstop.
            try
            {
                ListRefresh.IsRefreshing = false;
                if (Vm != null)
                    Vm.Loading = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS refresh cleanup failed: " + ex.Message);
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_navConnected)
        {
            BottomNav.SectionActivated -= OnSectionActivated;
            BottomNav.SectionChanging -= OnSectionChanging;
            _navConnected = false;
        }
        // MAUI re-applies query attributes on re-navigation but never clears a
        // [QueryProperty] that the new route omits, which would leak the previous
        // work mode into this page. Drop them so the next apply resolves fresh.
        Mode = null;
        Status = null;
        Type = null;
        Menu = null;
        Reset = null;
        _applied = false;
    }

    private bool _navigatingToDetails;

    /// <summary>
    ///     Infinite scroll is a list-level feature, so it is wired on the grid's inner collection
    ///     instead of being re-declared as XAML on every poster grid in the app.
    /// </summary>
    private void WireInfiniteScroll()
    {
        _grid = AnimeGrid.Inner;
        _grid.SelectionMode = SelectionMode.Single;
        _grid.SelectionChanged += OnSelectionChanged;
        _grid.RemainingItemsThreshold = 4;
        _grid.RemainingItemsThresholdReachedCommand = Vm?.LoadMoreCommand;
        AnimeGrid.ItemTapped += OnGridItemTapped;
    }

    private CollectionView _grid;

    private async void OnGridItemTapped(object sender, object item)
    {
        if (item is AnimeItemViewModel viewModel)
            await NavigateToDetailsAsync(viewModel);
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is AnimeItemViewModel item)
            {
                AnimeGrid.Inner.SelectedItem = null;
                await NavigateToDetailsAsync(item);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS selection nav failed: " + ex.Message);
        }
    }

    private async Task NavigateToDetailsAsync(AnimeItemViewModel item)
    {
        if (_navigatingToDetails)
            return;
        _navigatingToDetails = true;
        try
        {
            var wm = (AnimeListWorkModes)_lastMode;
            var manga = wm == AnimeListWorkModes.Manga || wm == AnimeListWorkModes.TopManga || wm == AnimeListWorkModes.MangaAdapted;
            var titleQs = Uri.EscapeDataString(item.Title ?? string.Empty);
            var source = wm == AnimeListWorkModes.Manga
                ? PageIndex.PageMangaList
                : PageIndex.PageAnimeList;
            var detailsArgs = new AnimeDetailsPageNavigationArgs(item.Id, item.Title, null, item,
                BuildArgs(_lastMode, _lastStatus))
            {
                Source = source,
                AnimeMode = !manga
            };
            MauiDetailsNavigationHandoff.Set(detailsArgs);
            var route = manga
                ? $"animedetails?id={item.Id}&title={titleQs}&manga=true"
                : $"animedetails?id={item.Id}&title={titleQs}";
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS selection nav failed: " + ex.Message);
        }
        finally
        {
            _navigatingToDetails = false;
        }
    }

    private sealed class MauiDimensionsProvider : MALClient.XShared.ViewModels.Interfaces.IDimensionsProvider
    {
        public double ActualWidth => DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        public double ActualHeight => DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
    }
}
