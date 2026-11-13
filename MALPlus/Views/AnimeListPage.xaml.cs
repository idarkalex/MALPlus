using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.BL;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;
using MALPlus.Services;

namespace MALPlus.Views;

[QueryProperty(nameof(Mode), "mode")]
    [QueryProperty(nameof(Status), "status")]
    [QueryProperty(nameof(Type), "type")]
    [QueryProperty(nameof(Menu), "menu")]
    [QueryProperty(nameof(Reset), "reset")]
    [QueryProperty(nameof(RouteSeason), "season")]
    [QueryProperty(nameof(RouteYear), "year")]
    [QueryProperty(nameof(SourceUser), "user")]
public partial class AnimeListPage : ContentPage, ITabSwipeHost
{
    internal string RootRoute { get; set; }
    public string SourceUser { get; set; }
    private string _appliedUser;

    private static string _pendingRootRoute;
    private static DateTime _pendingRootRouteAt;
    private string _rootResetSuppressedFor;
    private DateTime _rootResetSuppressedAt;

    internal static void SetPendingRootRoute(string route)
    {
        if (string.IsNullOrEmpty(route) || !route.Contains('?'))
            return;
        _pendingRootRoute = route;
        _pendingRootRouteAt = DateTime.UtcNow;
    }

    internal static void ClearPendingRootRoute(string route)
    {
        if (string.IsNullOrEmpty(route) ||
            string.Equals(_pendingRootRoute, route, StringComparison.Ordinal))
            _pendingRootRoute = null;
    }

    private static bool TryTakePendingRootRoute(string rootRoute, out string route)
    {
        route = null;
        var pending = _pendingRootRoute;
        if (string.IsNullOrEmpty(pending) || string.IsNullOrEmpty(rootRoute))
            return false;
        if ((DateTime.UtcNow - _pendingRootRouteAt).TotalSeconds > 5)
        {
            _pendingRootRoute = null;
            return false;
        }
        if (!RouteTargetsRoot(pending, rootRoute))
            return false;

        route = pending;
        _pendingRootRoute = null;
        return true;
    }

    private static bool RouteTargetsRoot(string route, string rootRoute)
    {
        var path = route.Split(new[] { '?' }, 2)[0];
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 &&
               string.Equals(segments[0], rootRoute, StringComparison.OrdinalIgnoreCase);
    }

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
    private long _appliedSettingsRevision;
    /// <summary>
    ///     Set when this page pushes a details page, and consumed by the very next ApplyRoute.
    /// </summary>
    /// <remarks>
    ///     It means "this is the way back from a details page", which is the only navigation that has to
    ///     reopen the panel that was on screen rather than resolve afresh. Cleared by ApplyRoute itself,
    ///     so it can never outlive the navigation that set it.
    /// </remarks>
    private bool _returningFromDetails;
    private TopAnimeType _topType = TopAnimeType.General;
    private MangaTopType _mangaTopType = MangaTopType.All;
    private MangaAdaptedType _adaptedType = MangaAdaptedType.AiringNow;
    private int _rangeIndex = DefaultRangeIndex;

    /// <summary>
    ///     Which content tier the panel was last built under.
    /// </summary>
    /// <remarks>
    ///     Compared against the setting on every load so a change of setting can tell the difference
    ///     between "the reader reopened the panel" and "the panel is showing a different list now
    ///     because what the app is allowed to show changed underneath it". Only the second one resets
    ///     the scroll position.
    /// </remarks>
    private string _appliedContentTag = ContentFilter.CacheTag;
    private AnimeGenreSearch? _genreFilter;
    private AnimeStudios? _studioFilter;

    private const int DefaultStatusIndex = 0;
    private const int MaxStatusIndex = 5;

    /// <summary>
    ///     The band of MAL's ranking an unfiltered top list opens on, and the count of them.
    /// </summary>
    /// <remarks>
    ///     Taken from the query rather than written here, because the band size and the count are
    ///     decided by what the source will actually serve: two hundred and fifty ranks is five pages of
    ///     fifty, which is the biggest page it answers to.
    /// </remarks>
    private const int DefaultRangeIndex = 0;

    public string Mode { get; set; }
    public string Status { get; set; }
    public string Type { get; set; }
    public string Menu { get; set; }
    public string Reset { get; set; }

    /// <summary>
    ///     Seasonal only, and written by the Shell route rather than set from anywhere in the page.
    ///     Coming back from an anime re-enters through a route that names the season it wants;
    ///     arriving from the More menu names none, which is what lands on the current season.
    ///     Named RouteSeason rather than Season so it cannot shadow the Season enum used all over
    ///     this class.
    /// </summary>
    public string RouteSeason { get; set; }
    public string RouteYear { get; set; }

    private AnimeListViewModel Vm => (AnimeListViewModel)BindingContext;

    private bool _liveLibrary;
    private bool _liveBound;
    private IReadOnlyList<AnimeItemViewModel> _previewList;
    private int _previewVersion = -1;
    private bool _previewManga;

    /// <summary>
    ///     Which of the two root list pages is the one that owns the shared view model. Both pages
    ///     bind the same singleton, so a page that is not live must not paint the shared collection:
    ///     it would show the other tab's list (dragging Anime towards Manga showed anime on both).
    ///     A page that is not live paints its own mode's library instead, which is what the drag is
    ///     previewing and what a bare root switch leaves on screen.
    /// </summary>
    internal void SetLiveLibrary(bool live)
    {
        if (live)
        {
            if (_liveLibrary && _liveBound)
                return;
            _liveLibrary = true;
            TryBindLive();
            return;
        }

        _liveLibrary = false;
        _liveBound = false;
        if (string.IsNullOrEmpty(RootRoute))
            return;

        var manga = string.Equals(RootRoute, "manga", StringComparison.OrdinalIgnoreCase);
        // This runs from every navigation sync, so it has to be idempotent: re-assigning the grid's
        // ItemsSource rebuilds its adapter and resets the scroll, which is the reload the user sees.
        if (_previewList != null && _previewManga == manga && _previewVersion == Vm.LibraryVersion)
            return;

        ApplyPreview(manga);
    }

    /// <summary>
    ///     Takes over the shared collection, but only once it actually holds this page's mode. Until
    ///     then the preview stays on screen: binding the old rows and swapping them a moment later
    ///     was the grid repainting itself from scratch at the end of every swipe.
    /// </summary>
    private void TryBindLive()
    {
        if (!_liveLibrary || _liveBound || Vm == null)
            return;

        var mode = string.Equals(RootRoute, "manga", StringComparison.OrdinalIgnoreCase)
            ? AnimeListWorkModes.Manga
            : AnimeListWorkModes.Anime;
        if (Vm.DisplayedMode != mode)
            return;

        _liveBound = true;
        AnimeGrid.RemoveBinding(AnimePosterGrid.ItemsSourceProperty);
        AnimeGrid.SetBinding(AnimePosterGrid.ItemsSourceProperty,
            nameof(AnimeListViewModel.DisplayedItems));
    }

    private void ApplyPreview(bool manga)
    {
        var preview = Vm?.GetLibraryPreview(manga);
        _previewList = preview;
        _previewManga = manga;
        _previewVersion = Vm?.LibraryVersion ?? -1;
        AnimeGrid.RemoveBinding(AnimePosterGrid.ItemsSourceProperty);
        AnimeGrid.ItemsSource = preview;
        if (preview == null || preview.Count == 0)
            Vm?.EnsurePreviewLibraryLoaded(manga);
    }

    private void OnPreviewLibraryChanged()
    {
        if (_liveLibrary || string.IsNullOrEmpty(RootRoute))
            return;

        ApplyPreview(string.Equals(RootRoute, "manga", StringComparison.OrdinalIgnoreCase));
    }

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
        // A panel dismissed by a tap leaves the back callback armed, and the next Back press would
        // then be eaten by a panel that is no longer on screen.
        PosterSheet.Closed += OnPosterSheetClosed;
        Vm.PreviewLibraryChanged += OnPreviewLibraryChanged;
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
            if (e.PropertyName == nameof(AnimeListViewModel.DisplayedMode) || e.PropertyName == null)
                TryBindLive();

            if (e.PropertyName != nameof(AnimeListViewModel.AnimeItems) && e.PropertyName != null)
                return;

            if ((Vm.AnimeItems?.Count ?? 0) <= 0)
                return;

            StartupTrace.Mark("first-items-visible");
            StartupTrace.LogSummary();
        };
    }

    bool ITabSwipeHost.TrySwipe(int direction)
        => BottomNav?.StepSection(direction) == true;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_navConnected)
        {
            BottomNav.SectionActivated += OnSectionActivated;
            BottomNav.SectionChanging += OnSectionChanging;
            _navConnected = true;
        }

        if (_appliedSettingsRevision != SettingsViewModelBase.SettingsRevision)
        {
            _rootResetSuppressedFor = null;
            if (SettingsViewModelBase.ResetDataOnNextAppearance)
            {
                _applied = false;
                _appliedMode = -1;
                _appliedStatus = -1;
                _lastMode = -1;
                _lastStatus = -1;
            }
        }

        if (string.IsNullOrEmpty(RootRoute))
        {
            ApplyRoute(null);
            return;
        }

        var location = Shell.Current?.CurrentState?.Location?.OriginalString;
        if (_returningFromDetails)
        {
            ApplyRoute(null);
            return;
        }

        if (IsRootLocation(location, out var hasQuery))
        {
            if (hasQuery)
                ApplyRouteFromShell(location);
            else
                ApplyRootLocation(location);
            return;
        }

        if (string.IsNullOrEmpty(location))
            ApplyRoute(null);
    }

    private void OnSectionActivated(string route)
    {
        ApplyRouteFromShell(route);
    }

    internal void ApplyRootLocation(string location)
    {
        if (_returningFromDetails || !IsRootLocation(location, out var hasQuery) || hasQuery)
            return;

        if (_appliedSettingsRevision == SettingsViewModelBase.SettingsRevision &&
            !string.IsNullOrEmpty(_rootResetSuppressedFor) &&
            (DateTime.UtcNow - _rootResetSuppressedAt).TotalSeconds <= 3 &&
            string.Equals(_rootResetSuppressedFor, RootRoute, StringComparison.OrdinalIgnoreCase))
        {
            _rootResetSuppressedFor = null;
            return;
        }

        if (TryTakePendingRootRoute(RootRoute, out var pending))
        {
            _rootResetSuppressedFor = RootRoute;
            _rootResetSuppressedAt = DateTime.UtcNow;
            Android.Util.Log.Info("MALPLUS",
                $"AnimeList pending root route '{pending}' loc='{location}'");
            ApplyRouteFromShell(pending);
            return;
        }

        var mode = string.Equals(RootRoute, "manga", StringComparison.OrdinalIgnoreCase)
            ? (int)AnimeListWorkModes.Manga
            : (int)AnimeListWorkModes.Anime;
        var route = $"///{RootRoute}?mode={mode}&status=0";
        _lastShellRoute = location;
        _rootResetSuppressedFor = RootRoute;
        _rootResetSuppressedAt = DateTime.UtcNow;
        CloseStatusDropdown();
        Android.Util.Log.Info("MALPLUS",
            $"AnimeList root reset loc='{location}' -> {route} applied=({_appliedMode},{_appliedStatus},{_appliedType})");
        ApplyRoute(route);
    }

    private bool IsRootLocation(string location, out bool hasQuery)
    {
        hasQuery = false;
        if (string.IsNullOrEmpty(location))
            return false;

        var queryIndex = location.IndexOf('?');
        hasQuery = queryIndex >= 0;
        var path = hasQuery ? location.Substring(0, queryIndex) : location;
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 &&
               string.Equals(segments[0], RootRoute, StringComparison.OrdinalIgnoreCase);
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
            // Only when this is not the way back from a details page. This runs on the tab tap, before
            // the shell navigates, and it writes the very fields ResolveMode falls back to. Returning
            // from a details page re-activates the section, so on the way back this overwrote the panel
            // with whatever Mode held and the panel was lost before ApplyRoute ever looked at it.
            if (!_returningFromDetails)
            {
                _lastMode = mode;
                _lastStatus = status;
            }
            CloseStatusDropdown();
            UpdateContextBars();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS section changing failed: " + ex.Message);
        }
    }

    internal void ApplyRouteFromShell(string route)
    {
        if (string.IsNullOrEmpty(route))
            return;
        if (!string.IsNullOrEmpty(RootRoute) && !RouteTargetsRoot(route, RootRoute))
            return;
        ClearPendingRootRoute(route);
        if (!string.IsNullOrEmpty(RootRoute) && RouteTargetsRoot(route, RootRoute))
        {
            _rootResetSuppressedFor = RootRoute;
            _rootResetSuppressedAt = DateTime.UtcNow;
        }
        if (_applied && _appliedSettingsRevision == SettingsViewModelBase.SettingsRevision && string.Equals(_lastShellRoute, route, StringComparison.Ordinal))
            return;
        _lastShellRoute = route;
        ApplyRoute(route);
    }

    private string _lastShellRoute;

    private void ApplyRoute(string route)
    {
        try
        {
            var query = ParseQuery(route);
            var mode = ResolveMode(query, out var modeFromQuery);
            // Consumed here, AFTER the resolve above rather than before it. Clearing it first meant
            // ResolveMode never saw the flag it is supposed to honour, and the panel was flattened to
            // the plain library anyway while the log cheerfully reported back=True.
            _returningFromDetails = false;
            var status = ResolveStatus(query, modeFromQuery);
            var type = ResolveType(query, modeFromQuery);
            ParseTypeQuery(mode, type);
            ParseRangeQuery(query);

            // A genre or studio filter arrives on the pushed "animelist" route. Remembering what the
            // anime tab was showing is what lets RestoreTabAfterFilterPut it back on the way out.
            if (IsFilterMode(mode))
            {
                if (!_pushedAsFilter)
                {
                    _pushedAsFilter = true;
                    _tabMode = IsMangaLocation() ? (int)AnimeListWorkModes.Manga : (int)AnimeListWorkModes.Anime;
                    _tabStatus = DefaultStatusIndex;
                }
            }
            else
            {
                _pushedAsFilter = false;
            }

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
            RouteSeason = null;
            RouteYear = null;
            var targetUser = QueryValue(query, "user", SourceUser);
            SourceUser = null;
            if (!string.IsNullOrEmpty(targetUser))
            {
                Vm.ListSource = targetUser;
            }

            // Seasonal only: a route can name the season it wants to open, and it is read BEFORE the
            // already-showing check so that a route asking for a season other than the one on screen
            // is treated as a real change. Otherwise the check would pass on a matching work mode
            // and status and quietly keep the old grid while the route said something else.
            var routeSeason = ResolveSeason(query);
            var routeYear = ResolveYear(query);
            var seasonalRoute = routeSeason.HasValue && routeYear.HasValue;

            var settingsChanged = _appliedSettingsRevision != SettingsViewModelBase.SettingsRevision;
            var alreadyShowing =
                !forceReset &&
                !settingsChanged &&
                _applied &&
                _appliedMode == mode &&
                _appliedStatus == status &&
                string.Equals(_appliedType, type, StringComparison.Ordinal) &&
                string.Equals(_appliedUser, targetUser, StringComparison.OrdinalIgnoreCase) &&
                !(seasonalRoute && (routeSeason.Value != _shownSeason || routeYear.Value != _shownYear)) &&
                (Vm.AnimeItems?.Count ?? 0) > 0;
            Android.Util.Log.Info("MALPLUS",
                $"AnimeList apply route='{route ?? "(properties)"}' -> mode={mode} status={status} type={type} reset={forceReset} skip={alreadyShowing} settingsChanged={settingsChanged} applied=({_appliedMode},{_appliedStatus},{_appliedType})");
            if (alreadyShowing)
            {
                _appliedSettingsRevision = SettingsViewModelBase.SettingsRevision;
                if (openStatusMenu)
                    ToggleContextMenu(ContextMenuKind.Status);
                UpdateContextBars();
                return;
            }

            _appliedSettingsRevision = SettingsViewModelBase.SettingsRevision;
            _applied = true;
            _appliedMode = mode;
            _appliedStatus = status;
            _appliedType = type;
            _appliedUser = targetUser;
            _lastMode = mode;
            _lastStatus = status;
            // The magnifier scopes itself to whatever the panel is showing, so text left over from
            // the previous one would silently filter the new panel by a term the user never typed
            // here. Closing it also puts the magnifier back where they expect it.
            _panelFilterOpen = false;
            Vm.PanelFilter = "";
            UpdateContextBars();

            _ = InitAndObserveAsync(mode, status, openStatusMenu, routeSeason, routeYear);
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

    /// <summary>
    ///     The season a route is asking for, if it names one.
    /// </summary>
    /// <remarks>
    ///     Parsed rather than cast: it arrives as text off the Shell route, and an unparseable value
    ///     means the route is not one of ours, which is no reason to throw away the season already
    ///     on screen.
    /// </remarks>
    private static Season? ResolveSeason(Dictionary<string, string> query)
    {
        var raw = QueryValue(query, "season", null);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return System.Enum.TryParse<Season>(raw, true, out var parsed) && parsed != Season.Unknown
            ? parsed
            : (Season?)null;
    }

    private static int? ResolveYear(Dictionary<string, string> query)
    {
        var raw = QueryValue(query, "year", null);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed) && parsed > 1990
            ? parsed
            : (int?)null;
    }

    private async Task InitAndObserveAsync(int mode, int status, bool openStatusMenu,
Season? routeSeason = null, int? routeYear = null)
    {
        try
        {
            var args = BuildArgs(mode, status);
            // Whether the content setting changed under the panel while it was on screen. Read before
            // the load, because the load is what replaces the rows, and afterwards it is too late to
            // know they were replaced on purpose.
            var contentChanged = _appliedContentTag != ContentFilter.CacheTag;
            _appliedContentTag = ContentFilter.CacheTag;
            // Opening Seasonal means the user wants the source now, so the background warmer stands
            // down instead of keeping requests queued ahead of theirs.
            SeasonCacheWarmer.StopWarming();
            await Vm.Init(args);
            // Changing what the app is allowed to show asks a different question of the source, so the
            // panel reads again and opens at its first row. Left where the reader was, they would be
            // looking at row two hundred of a list that had just been replaced underneath them, and
            // that reads as a broken screen rather than as a setting having taken effect.
            if (contentChanged)
                ScrollGridToFirstRow();
            // Init resets the season to the current one whenever the args do not carry it, so a
            // route that named a season has to be applied on top afterwards. Doing it here rather
            // than before matters: the view model clears the season as part of initialising.
            if (mode == (int)AnimeListWorkModes.SeasonalAnime && routeSeason.HasValue && routeYear.HasValue &&
                (routeSeason.Value != _shownSeason || routeYear.Value != _shownYear))
            {
                _shownSeason = routeSeason.Value;
                _shownYear = routeYear.Value;
                await LoadSeasonAsync(ComposeSeason(_shownSeason, _shownYear));
            }
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
                MainThread.BeginInvokeOnMainThread(() => ToggleContextMenu(ContextMenuKind.Status));
        }
    }

    /// <summary>
    ///     Both context bars read from the applied route, never from the VM, so they always say what
    ///     the grid is actually showing. The status bar replaces the "â‹®" glyph that used to sit inside
    ///     the Anime and Manga cells of the navigation bar: the four tabs were already centred, but
    ///     those two cells carried an extra element and the row read as off centre. The season bar is
    ///     Seasonal only and sits directly above the navigation so the grid never moves.
    /// </summary>
    private void UpdateContextBars()
    {
        try
        {
            var manga = _lastMode == (int)AnimeListWorkModes.Manga;
            var byGenre = _lastMode == (int)AnimeListWorkModes.AnimeByGenre;
            var byStudio = _lastMode == (int)AnimeListWorkModes.AnimeByStudio;
            var isList = _lastMode == (int)AnimeListWorkModes.Anime || manga;

            // The tag and note indicators are the only place they appear, and only here: they are
            // facts about the user's own entry, which is exactly what this panel is. Manga has the
            // same fields and deliberately does not show them.
            if (AnimeGrid != null)
                AnimeGrid.ShowEntryIndicators = _lastMode == (int)AnimeListWorkModes.Anime;

            // A genre or studio panel keeps the same bar, but it names the filter instead of a
            // status. Otherwise the bar said "Watching" over a grid of Drama, which is the one thing
            // it exists to prevent.
            var seasonal = _lastMode == (int)AnimeListWorkModes.SeasonalAnime;
            var topPanel = _lastMode == (int)AnimeListWorkModes.TopAnime ||
                           _lastMode == (int)AnimeListWorkModes.TopManga ||
                           _lastMode == (int)AnimeListWorkModes.MangaAdapted;

            // Seasonal, Top and Adapted are opened from More and only borrowed this page to show
            // themselves, so the tab bar is told to mark More instead. A genre or studio panel is
            // deliberately not in here: it is opened from a details page, it is part of the anime
            // section's own subject, and back from it leads back to that entry.
            // Seasonal, Top and Adapted are opened from More and only borrowed this page to show
            // themselves, so the tab bar is told to mark More instead. Genre and studio are not
            // in here even though they are panels too: they are opened from a details page and
            // back from them leads to that entry, so marking More would point the wrong way.
            BottomNav?.SetSectionOverride(
                PanelOriginOf(_lastMode) == PanelOrigin.More ? "more" : null);
            // Seasonal gets the same bar as the list, with the season and the year standing in for
            // the status word. There is no status to choose on this panel, and the bar exists to
            // name what the grid is holding, so naming the season is exactly its job.
            StatusBar.IsVisible = isList || byGenre || byStudio || seasonal || topPanel;
            StatusWordGroup.IsVisible = isList || byGenre || byStudio;
            SeasonSelectors.IsVisible = seasonal;
            TypeSelector.IsVisible = topPanel;
            if (topPanel)
            {
                TypeLabel.Text = _lastMode == (int)AnimeListWorkModes.TopAnime
                    ? TopTypeLabel(_topType)
                    : _lastMode == (int)AnimeListWorkModes.TopManga
                        ? _mangaTopType.ToString()
                        : AnimeAdaptedToAnimeQuery.ToDisplayName(_adaptedType);
            }

            // Only the tabs that are the whole ranking have a band to choose. On a filtered one
            // (Airing, TV, Upcoming) the endpoint returns that slice and its rows keep their own
            // global rank, so there is nothing here to page through: showing the control would offer
            // a choice that cannot mean anything.
            var rankedList = _lastMode == (int)AnimeListWorkModes.TopAnime
                                 ? _topType == TopAnimeType.General
                                 : _lastMode == (int)AnimeListWorkModes.TopManga &&
                                   _mangaTopType == MangaTopType.All;
            RangeSelector.IsVisible = topPanel && rankedList;
            if (rankedList)
                RangeLabel.Text = RangeLabelText(_rangeIndex);
            else if (byGenre || byStudio)
            {
                StatusLabel.Text = byGenre
                    ? _genreFilter?.ToString() ?? "Genre"
                    : _studioFilter?.ToString() ?? "Studio";
            }
            else if (isList)
            {
                var tabs = manga ? MangaStatusTabs : AnimeStatusTabs;
                var label = tabs.FirstOrDefault(tab => tab.StatusSelectorIndex == _lastStatus).Label;
                StatusLabel.Text = manga && label == "Watching" ? "Reading" : label ?? tabs[1].Label;
            }
            else if (seasonal)
            {
                SyncSeasonFromPanel();
            }

            PanelFilterBox.IsVisible = StatusBar.IsVisible && _panelFilterOpen;
            PanelFilterButton.IsVisible = StatusBar.IsVisible && !_panelFilterOpen;
            UpdatePanelBackNavigation();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS context bars failed: " + ex.Message);
        }
    }

        /// <summary>
        ///     Swiping back out of a subpanel goes to the plain list underneath it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     These panels are a QUERY on a root section (`///anime?mode=3`), not a page pushed on the
        ///     Shell stack, so there is nothing for the back gesture to pop: the section is already the
        ///     root. What the gesture found was the app itself, which is why leaving Top or Seasonal
        ///     risked closing the app instead of the panel.
        ///     </para>
        ///     <para>
        ///     Reuses the menu back callback rather than a Shell pop. That callback is registered at the
        ///     moment the thing it dismisses opens, which is the only moment it is guaranteed to sit
        ///     ahead of the Shell's in the dispatcher, and it is already proven on the device for the
        ///     poster sheet.
        ///     </para>
        ///     <para>
        ///     Only armed for the panels that are reached from somewhere else. On the plain list the
        ///     Shell's own back behaviour is right, and taking it over there would break leaving the
        ///     app, which is the one thing back must still do.
        ///     </para>
        /// </remarks>
        private void UpdatePanelBackNavigation()
        {
            try
            {
                if (PanelOriginOf(_lastMode) == PanelOrigin.None)
                {
                    // Disarmed, not just unflagged. The flag is the page's belief about the dispatcher;
                    // clearing it while the callback is still registered left a live back handler pointing
                    // at GoToMainPageAsync with nothing left to take it down, and the next back press
                    // landed on the main list instead of doing what the reader expected.
                    DisarmPanelBack();
                    return;
                }

                if (_panelBackArmed)
                    return;

                _panelBackArmed = true;
                MainActivity.ArmMenuBackCallback(() => _ = GoToMainPageAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS panel back nav failed: " + ex.Message);
            }
        }

        private bool _panelBackArmed;

    /// <summary>
    ///     Takes the panel's back callback down and says so.
    /// </summary>
    /// <remarks>
    ///     Every place that used to write <c>_panelBackArmed = false</c> on its own left the flag and the
    ///     dispatcher disagreeing: the page believed it had released the gesture while a callback was
    ///     still registered. Since the flag is what OnDisappearing checks before disarming, that
    ///     disagreement made the disarm a no-op and the stale handler survived into the next navigation.
    /// </remarks>
    private void DisarmPanelBack()
    {
        if (!_panelBackArmed)
            return;
        _panelBackArmed = false;
        MainActivity.DisarmMenuBackCallback();
    }

        /// <summary>
        ///     Leaves a subpanel for the main page, which is where back is expected to land.
        /// </summary>
        /// <remarks>
        /// The main page is the one the reader picked in Settings and not the list that happens to
        /// sit under this panel. Back from a Top panel that was opened off the manga list used to
        /// land on the manga list, so a reader whose start page is Discover was sent to a tab they
        /// had chosen against every single time they pressed back.
        /// <para>
        /// The two list routes carry the same reset the panel itself arrives with, so landing on one
        /// of them shows the list it opens on rather than whatever state the previous visit left it
        /// in. Discover takes no such parameters.
        /// </para>
        /// </remarks>
        /// <summary>Where a work mode came from, and therefore where its back gesture leads.</summary>
        private enum PanelOrigin
        {
            /// <summary>Not a panel: the reader's own list.</summary>
            None,

            /// <summary>Opened from the More hub, so leaving it goes back to More.</summary>
            More,

            /// <summary>Opened from a details page, so leaving it goes back to that entry.</summary>
            Entry
        }

        /// <summary>
        ///     Where a work mode was opened from.
        /// </summary>
        /// <remarks>
        ///     Every mode that borrows this page instead of being a list of the reader's own is described
        ///     here and nowhere else. It used to be four places: the back callback, the context bar, the
        ///     More highlight and the destination on leaving, each with its own grouping, and they had
        ///     already drifted apart. Adding a ranking meant remembering all four, and missing one
        ///     produced a different symptom each time rather than a compile error.
        /// </remarks>
        private static PanelOrigin PanelOriginOf(int mode) => (AnimeListWorkModes)mode switch
        {
            AnimeListWorkModes.SeasonalAnime => PanelOrigin.More,
            AnimeListWorkModes.TopAnime => PanelOrigin.More,
            AnimeListWorkModes.TopManga => PanelOrigin.More,
            AnimeListWorkModes.MangaAdapted => PanelOrigin.More,
            // A genre or a studio is reached from a chip on the entry itself, so its back leads there.
            AnimeListWorkModes.AnimeByGenre => PanelOrigin.Entry,
            AnimeListWorkModes.AnimeByStudio => PanelOrigin.Entry,
            _ => PanelOrigin.None
        };

        /// <summary>
        ///     Leaves a subpanel: back to More for the ones opened there, and to the entry for the ones
        ///     opened from a details page.
        /// </summary>
        /// <remarks>
        ///     It used to go to the tab picked in Settings, which for most readers is the Anime tab. That
        ///     meant leaving a Seasonal panel dumped the reader into their library, and because the shell
        ///     still had More behind it the gesture to get back there took a second swipe.
        /// </remarks>
        private async Task GoToMainPageAsync()
        {
            DisarmPanelBack();
            var origin = PanelOriginOf(_lastMode);
            Android.Util.Log.Info("MALPLUS", $"AnimeList leaving panel mode={_lastMode} origin={origin}");
            try
            {
                await Shell.Current.GoToAsync(origin == PanelOrigin.Entry ? ".." : "///more");
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS leave panel failed: " + ex.Message);
            }
        }

    /// <summary>
    ///     Opens the magnifier field and hides the button that opened it, so there is one thing to
    ///     act on while typing. The field stays until it is cleared or the panel changes: closing it
    ///     on focus loss left the text on screen with no reachable way to clear it.
    /// </summary>
    private void OnPanelFilterTapped(object sender, TappedEventArgs e)
    {
        try
        {
            _panelFilterOpen = true;
            UpdateContextBars();
            PanelFilterEntry.Focus();
            StripEntryUnderline();
            // The field starts hidden, so its platform view only exists once the box is shown. The
            // immediate pass can therefore land before the handler exists, and this one after.
            _ = Task.Delay(200).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(StripEntryUnderline));
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS panel filter open failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Fixes the two things the Android theme does to an EditText that XAML cannot reach.
    ///     <para>
    ///     The underline is drawn as part of the themed background, so it is not a separate view and
    ///     no BackgroundColor removes it: the field carried a bright blue rule inside a bar that has
    ///     no other rules, where it read as a progress bar. The same style also carries top and bottom
    ///     padding, which pushed the text against the top of a 36dp pill. Entry exposes no Padding to
    ///     set, so both are done on the platform view.
    ///     </para>
    ///     <para>
    ///     Done here rather than in a custom handler because MAUI 11 changed the shape of the Entry
    ///     handler's connect hook, and this does not depend on that signature.
    ///     </para>
    /// </summary>
    private void StripEntryUnderline()
    {
        try
        {
            if (PanelFilterEntry?.Handler?.PlatformView is not global::Android.Widget.EditText edit)
                return;
            edit.Background = null;
            edit.SetPadding(0, 0, 0, 0);
            // The default gravity is top aligned, which is why the text still rode high once the
            // padding was gone.
            edit.Gravity = global::Android.Views.GravityFlags.CenterVertical |
                           global::Android.Views.GravityFlags.Start;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS entry fixups failed: " + ex.GetType().Name);
        }
    }

    private void OnClearPanelFilter(object sender, TappedEventArgs e)
    {
        try
        {
            Vm.PanelFilter = "";
            // Kept open on purpose. Closing it here would put the magnifier back while the keyboard
            // is still up, and the next tap would land on a control that had just moved.
            PanelFilterEntry.Focus();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS panel filter clear failed: " + ex.Message);
        }
    }

/// <summary>
    ///     The season on screen, and the year it belongs to.
    /// </summary>
    /// <remarks>
    ///     Held here rather than read back from the view model because the two selectors are edited
    ///     independently: changing the season has to keep the year that was showing, and changing
    ///     the year has to keep the season. The view model only ever sees the composed pair.
    /// </remarks>
    private Season _shownSeason = Season.Winter;
    private int _shownYear;

    private static string SeasonName(Season season)
        => MALPlus.Controls.SeasonYearSelector.SeasonName(season);

    /// <summary>
    ///     Builds the season to ask for out of the two independent picks.
    /// </summary>
    /// <remarks>
    ///     Name is not cosmetic here: it is the cache tag for the whole season's result set, since
    ///     AnimeSeasonalQuery both reads and writes DataCache under it. A season composed without
    ///     one lands on the same key as every other nameless season, so the seasons overwrite each
    ///     other's cache and going back shows the wrong list.
    /// </remarks>
    private static AnimeSeason ComposeSeason(Season season, int year)
        => new AnimeSeason { Season = season, Year = year, Name = $"{SeasonName(season)} {year}" };

    private void UpdateSeasonSelectors()
    {
        // The control owns the words now; this only tells it what the panel is showing. It paints
        // itself, so there is no label to set here any more.
        SeasonSelectors.Season = _shownSeason;
        SeasonSelectors.Year = _shownYear;
    }

    /// <summary>
    ///     Adopts whatever the panel is currently showing, so the two selectors always name the grid
    ///     and not some other state the view model happened to be left in.
    /// </summary>
    private void SyncSeasonFromPanel()
    {
        var season = Vm?.CurrentSeason;
        if (season != null && season.Year > 0)
        {
            _shownSeason = season.Season;
            _shownYear = season.Year;
        }
        else
        {
            // Same rule the query uses, so the bar never names a season the grid was not filled from.
            _shownSeason = AiringSeasonNow();
            _shownYear = DateTime.UtcNow.Year;
        }
        UpdateSeasonSelectors();
    }

    /// <summary>
    ///     Which of the bar's three dropdowns is the one currently open.
    /// </summary>
    /// <remarks>
    ///     There is one panel, so only one of them can be open and which one has to be known. The
    ///     chevrons live in the bar ABOVE the overlay, which means a tap on a chevron never reaches
    ///     the scrim that dismisses it: without this, tapping the same chevron twice opened the panel
    ///     twice over and tapping a second one left the first one standing there as well.
    /// </remarks>
    private enum ContextMenuKind
    {
        None,
        Status,
        Season,
        Year,
        Type,
        Range
    }

    private ContextMenuKind _openMenu = ContextMenuKind.None;

    /// <summary>
    ///     Bumped by every close and by every open. A menu that is still waiting on the seasons index
    ///     compares against it when it resumes and abandons itself if it has been superseded, instead
    ///     of writing its rows into a panel that now belongs to a different menu.
    /// </summary>
    private int _menuGeneration;

    /// <summary>
    ///     The shared selector asks for its season menu. Opens this page's own dropdown, which is where
    ///     a dropdown on a bar this short belongs: a panel inside the control would be clipped to the
    ///     control's bounds on Android and drawn behind it.
    /// </summary>
    private void OnSeasonPickerRequested(object sender, EventArgs e) => ToggleContextMenu(ContextMenuKind.Season);

    private void OnYearPickerRequested(object sender, EventArgs e) => ToggleContextMenu(ContextMenuKind.Year);

    private void OnSeasonMenuTapped(object sender, TappedEventArgs e)
        => ToggleContextMenu(ContextMenuKind.Season);

    private void OnYearMenuTapped(object sender, TappedEventArgs e)
        => ToggleContextMenu(ContextMenuKind.Year);

    private void OnTypeMenuTapped(object sender, TappedEventArgs e)
        => ToggleContextMenu(ContextMenuKind.Type);

    private void OnRangeMenuTapped(object sender, TappedEventArgs e)
        => ToggleContextMenu(ContextMenuKind.Range);

    /// <summary>
    ///     How a band names itself in the bar and in the menu: the first rank and the last.
    /// </summary>
    private static string RangeLabelText(int index)
    {
        var first = index * AnimeTopQuery.RangeSize + 1;
        return first + "-" + (first + AnimeTopQuery.RangeSize - 1);
    }

    /// <summary>
    ///     The band, appended to a panel switch only when the panel being switched to is one that
    ///     reads bands at all.
    /// </summary>
    /// <remarks>
    ///     Carried on the route rather than held privately so that leaving for a details page and
    ///     coming back through the registered back navigation reopens the band that was on screen.
    ///     Without it a filtered tab would inherit the last band chosen on the ranking, because both
    ///     share the one private field the bar is drawn from.
    /// </remarks>
    private string RangeQuerySuffix()
    {
        var mode = (AnimeListWorkModes)_lastMode;
        var readsBands = mode == AnimeListWorkModes.TopAnime && _topType == TopAnimeType.General ||
                         mode == AnimeListWorkModes.TopManga && _mangaTopType == MangaTopType.All;
        return readsBands ? $"&range={_rangeIndex}" : "";
    }

    /// <summary>
    ///     Puts the grid back on its first row.
    /// </summary>
    /// <remarks>
    ///     CollectionView has no ScrollToTop, so this asks for index zero, which is the same place
    ///     without needing a reference to a row the collection may not have materialised. Called after
    ///     the rows are in, so the request has something to aim at.
    /// </remarks>
    private void ScrollGridToFirstRow()
    {
        try
        {
            var grid = AnimeGrid?.Inner;
            // Animated, so the reader watches the whole band run past them to get back to the top.
            // Nothing moved: the list was replaced underneath them and the offset it kept now points
            // at a row a couple of hundred places further down. Jumping is the honest presentation of
            // that, and it is what makes band two open on its own first entry.
            if (grid != null && Vm?.AnimeItems is { Count: > 0 })
                grid.ScrollTo(0, 0, ScrollToPosition.Start, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS grid reset to first row failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     The bands of the ranking, for the range menu.
    /// </summary>
    /// <remarks>
    ///     Goes through the route rather than straight to the view model, same as the subcategory
    ///     menu: reaching the view model directly is what lets the word in the bar and the rows in the
    ///     grid disagree about which band is on screen.
    /// </remarks>
    private void FillRangeMenu()
    {
        for (var index = 0; index < AnimeTopQuery.RangeCount; index++)
        {
            var picked = index;
            AddDropdownRow(RangeLabelText(picked), picked == _rangeIndex,
                () => OnRangeRowTapped(picked));
        }
    }

    private async void OnRangeRowTapped(int index)
    {
        CloseStatusDropdown();
        if (_rangeIndex == index)
            return;
        _rangeIndex = index;
        RangeLabel.Text = RangeLabelText(index);
        Vm.TopRangeIndex = index;
        await Vm.RefetchForRangeChange();
        // On the first row of the band that was just asked for, so it opens on its own first entry:
        // band two starts at rank 251 and that is what the reader should be looking at. Left where
        // they were, the grid kept the offset it had, which lands them on whatever row happened to
        // be at that height in a list that had just been replaced underneath them.
        ScrollGridToFirstRow();
    }

    /// <summary>
    ///     Subcategory dropdown for the Top and Adapted panels.
    /// </summary>
    /// <remarks>
    ///     Goes through the route rather than straight to the view model, because these panels are
    ///     reached with a typed query already and building one by hand here is what would let the bar
    ///     and the grid disagree about which subcategory is showing.
    /// </remarks>
    private void FillTypeMenu(int mode)
    {
        if (mode == (int)AnimeListWorkModes.TopAnime)
        {
            foreach (var type in System.Enum.GetValues(typeof(TopAnimeType)).Cast<TopAnimeType>())
            {
                var picked = type;
                AddDropdownRow(TopTypeLabel(picked), picked == _topType,
                    () => OnTypeRowTapped(mode, picked.ToString()));
            }
        }
        else if (mode == (int)AnimeListWorkModes.TopManga)
        {
            // The same filter the More panel applies, so the two lists of subcategories cannot drift
            // apart. Doujinshi is offered only with the content filter off entirely.
            foreach (var type in System.Enum.GetValues(typeof(MangaTopType)).Cast<MangaTopType>()
                        .Where(ContentFilter.AllowsMangaSubcategory))
            {
                var picked = type;
                AddDropdownRow(picked.ToString(), picked == _mangaTopType,
                    () => OnTypeRowTapped(mode, picked.ToString()));
            }
        }
        else if (mode == (int)AnimeListWorkModes.MangaAdapted)
        {
            foreach (var type in System.Enum.GetValues(typeof(MangaAdaptedType)).Cast<MangaAdaptedType>())
            {
                var picked = type;
                AddDropdownRow(AnimeAdaptedToAnimeQuery.ToDisplayName(picked), picked == _adaptedType,
                    () => OnTypeRowTapped(mode, picked.ToString()));
            }
        }
    }

    private async void OnTypeRowTapped(int mode, string type)
    {
        CloseStatusDropdown();
        try
        {
            // Same route the panel was opened with, so the whole path is the one that already works:
            // mode plus the typed parameter, and the page's own ParseTypeQuery turns it back into the
            // field the grid reads.
            var query = $"animelist?mode={mode}&type={Uri.EscapeDataString(type)}&reset=1";
            await Shell.Current.GoToAsync(query);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS type row failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Words the Top anime subcategories, which are enum names and read as configuration otherwise.
    /// </summary>
    private static string TopTypeLabel(TopAnimeType type) => type switch
    {
        TopAnimeType.General => "All time",
        TopAnimeType.Airing => "Airing",
        TopAnimeType.Upcoming => "Upcoming",
        TopAnimeType.Tv => "TV",
        TopAnimeType.Movies => "Movies",
        TopAnimeType.Ovas => "OVAs",
        TopAnimeType.Popular => "Popular",
        TopAnimeType.Favourited => "Favourited",
        _ => type.ToString()
    };

    /// <summary>
    ///     Closes whatever is open, then opens this one unless it was already the open one, in which
    ///     case the tap closes it. The close happens synchronously here, before anything is awaited,
    ///     which is what stops two taps from racing to fill the same panel.
    /// </summary>
    private void ToggleContextMenu(ContextMenuKind kind)
    {
        var wasAlreadyOpen = _openMenu == kind;
        CloseStatusDropdown();
        if (wasAlreadyOpen)
            return;
        _openMenu = kind;
        _ = OpenContextMenuAsync(kind);
    }

    private async Task OpenContextMenuAsync(ContextMenuKind kind)
    {
        var generation = ++_menuGeneration;
        try
        {
            // The status list needs nothing loaded. The season menus need the seasons index, and the
            // type menu is built from an enum, so only the season pair has to wait for anything.
            if (kind == ContextMenuKind.Season || kind == ContextMenuKind.Year)
                await Vm.PopulateSeasonSelectionAsync();

            // Superseded while it was loading: whatever the user asked for instead has already
            // claimed the panel, and filling it now would put two menus' rows in the same list.
            if (generation != _menuGeneration || _openMenu != kind)
                return;

            StatusDropdownList.Clear();
            switch (kind)
            {
                case ContextMenuKind.Status:
                    FillStatusMenu(_lastMode);
                    break;
                case ContextMenuKind.Season:
                    FillSeasonMenu();
                    break;
                case ContextMenuKind.Year:
                    FillYearMenu();
                    break;
                case ContextMenuKind.Type:
                    FillTypeMenu(_lastMode);
                    break;
                case ContextMenuKind.Range:
                    FillRangeMenu();
                    break;
            }
            StatusDropdownOverlay.IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS context menu failed: " + ex.Message);
            CloseStatusDropdown();
        }
    }

    /// <summary>
    ///     Season dropdown. Chosen season first, then the calendar order.
    /// </summary>
    /// <remarks>
    ///     The list is a choice and not a history, so the season on screen opens at the top instead of
    ///     being hunted for in the middle of four entries. It reorders itself when the calendar rolls
    ///     over, which is the price of that and the reason it is a deliberate choice.
    ///     <para>
    ///     The order and the marks come from the shared control, so this page and the season themes page
    ///     cannot offer the seasons in a different order or disagree about which one is current.
    ///     </para>
    /// </remarks>
    private void FillSeasonMenu()
    {
        foreach (var choice in MALPlus.Controls.SeasonYearSelector.SeasonChoices(_shownSeason))
        {
            var picked = choice.Season;
            // "(Current)" names the season that is airing now, which is NOT the one on screen once the
            // user has walked away from it. Marking the selected one instead would call a season from
            // three years ago the current one. Both marks can land on the same row, and that is correct:
            // it is both what is showing and what is airing.
            var label = SeasonName(picked) + (choice.IsNow ? " (Current)" : "");
            AddDropdownRow(label, choice.IsChosen, () => OnSeasonRowTapped(picked));
        }
    }

    /// <summary>
    ///     Which season is airing, taken from the shared control so the bar and the grid cannot name
    ///     different ones.
    /// </summary>
    private static Season AiringSeasonNow() => MALPlus.Controls.SeasonYearSelector.AiringSeasonNow();

    private async void OnSeasonRowTapped(Season season)
    {
        CloseStatusDropdown();
        if (season == _shownSeason)
            return;
        _shownSeason = season;
        UpdateSeasonSelectors();
        await LoadSeasonAsync(ComposeSeason(_shownSeason, _shownYear));
    }

    /// <summary>
    ///     Year dropdown, offering exactly the years the source says exist.
    /// </summary>
    /// <remarks>
    ///     Not a generated range: the seasons index is also what decides whether a year can be asked
    ///     for at all, so a year it does not list is a year the menu should not offer. Falls back to
    ///     the year on screen when the index came back empty, so the dropdown is never a dead panel.
    /// </remarks>
    private void FillYearMenu()
    {
        var years = Vm.SeasonYears
            .Select(y => int.TryParse(y, out var parsed) ? parsed : 0)
            .Where(y => y > 0)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();
        if (years.Count == 0)
            years.Add(_shownYear);

        foreach (var year in years)
        {
            var picked = year;
            AddDropdownRow(picked.ToString(), picked == _shownYear,
                () => OnYearRowTapped(picked));
        }
    }

    private async void OnYearRowTapped(int year)
    {
        CloseStatusDropdown();
        if (year == _shownYear)
            return;
        _shownYear = year;
        UpdateSeasonSelectors();
        await LoadSeasonAsync(ComposeSeason(_shownSeason, _shownYear));
    }

    private async Task LoadSeasonAsync(AnimeSeason season)
    {
        if (season == null)
            return;
        _lastMode = (int)AnimeListWorkModes.SeasonalAnime;
        // Goes through the view model's own season entry point rather than Init, because Init resets
        // the season on every pass for a panel that does not carry full navigation args, and the
        // reload that followed always came back with the season that is airing now.
        await Vm.ShowSeasonAsync(season);
        UpdateSeasonSelectors();
        UpdateContextBars();
    }

    private int ResolveMode(Dictionary<string, string> query, out bool fromQuery)
    {
        fromQuery = false;

        // FIRST, ahead of the query and ahead of the Mode property, because on this one navigation both
        // of those are unreliable. The list is coming back from a details page it opened itself, so the
        // panel that was on screen is the answer by definition: the query arrives empty and Mode has
        // just been re-applied by MAUI holding whatever the shell last had, which for a panel opened
        // off More is the plain library's own value. The log caught this going out as mode=1 and
        // coming back as mode=0 with _appliedMode still on 1, and the only branch that can do that is
        // the Mode property. Checking this flag after it, as it was, could never fire.
        if (_returningFromDetails && _appliedMode >= 0 &&
            System.Enum.IsDefined(typeof(AnimeListWorkModes), _appliedMode))
            return _appliedMode;

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

        // Nothing at all asked for a mode: the panel the reader had open is still the one to reopen.
        // Falling back to "Anime" here is what made Seasonal, Top anime, Top manga, Adapted, and the
        // genre/studio panels all come back as the plain library list instead of themselves, which for a
        // Seasonal that is also what left the grid empty: the plain list filters by the status index,
        // and a library with nothing in that band has nothing to show.
        if (_appliedMode >= 0 && System.Enum.IsDefined(typeof(AnimeListWorkModes), _appliedMode))
            return _appliedMode;
        if (_lastMode >= 0 && System.Enum.IsDefined(typeof(AnimeListWorkModes), _lastMode))
            return _lastMode;

        if (string.Equals(RootRoute, "manga", StringComparison.OrdinalIgnoreCase))
            return (int)AnimeListWorkModes.Manga;
        if (string.Equals(RootRoute, "anime", StringComparison.OrdinalIgnoreCase))
            return (int)AnimeListWorkModes.Anime;

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

    private void ParseRangeQuery(Dictionary<string, string> query)
    {
        _rangeIndex = DefaultRangeIndex;
        var raw = QueryValue(query, "range", null);
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, out var parsed))
            return;
        // Clamped rather than rejected: the count is a constant the query knows nothing about, and a
        // hand-edited or stale route naming a band past the end should land on the last real band
        // instead of leaving the bar showing a range no grid was ever asked for.
        _rangeIndex = Math.Min(Math.Max(parsed, DefaultRangeIndex), AnimeTopQuery.RangeCount - 1);
    }

    private void ParseTypeQuery(int mode, string type)
    {
        _topType = TopAnimeType.General;
        _mangaTopType = MangaTopType.All;
        _adaptedType = MangaAdaptedType.AiringNow;
        _genreFilter = null;
        _studioFilter = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
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

        // The genre and the studio ride along in the same "type" parameter, because a mode has
        // exactly one of the three and the page already had a slot for it. Both are enum names, so
        // Enum.Parse resolves them; an unknown one leaves the filter null and the query falls back
        // to its default rather than throwing.
        //
        // The parse is NOT case insensitive here. Enum.TryParse would accept "gourmet" for
        // Gourmet, but it also accepts any casing, which is not the problem. The problem is the
        // reverse: a value that does not exist resolves to nothing and the page then silently shows
        // the default genre's results, so a bad chip looks like a broken chip. Comparing against the
        // real member names keeps that honest, and the caller sends exactly what it read off
        // ToString().
        if ((AnimeListWorkModes)mode == AnimeListWorkModes.AnimeByGenre)
            _genreFilter = System.Enum.GetNames(typeof(AnimeGenreSearch))
                .FirstOrDefault(name => string.Equals(name, type, StringComparison.Ordinal)) is { } genreName
                ? (AnimeGenreSearch)System.Enum.Parse(typeof(AnimeGenreSearch), genreName)
                : null;
        if ((AnimeListWorkModes)mode == AnimeListWorkModes.AnimeByStudio)
            _studioFilter = System.Enum.GetNames(typeof(AnimeStudios))
                .FirstOrDefault(name => string.Equals(name, type, StringComparison.Ordinal)) is { } studioName
                ? (AnimeStudios)System.Enum.Parse(typeof(AnimeStudios), studioName)
                : null;
    }

    /// <summary>
    ///     The status picker, as a panel under the bar instead of a native action sheet. The sheet
    ///     was the last thing here still looking like the old app, and it also meant the choice could
    ///     not show which one was active until it was dismissed.
    /// </summary>
    private void FillStatusMenu(int mode)
    {
        try
        {
            var byGenre = mode == (int)AnimeListWorkModes.AnimeByGenre;
            var byStudio = mode == (int)AnimeListWorkModes.AnimeByStudio;

            if (byGenre || byStudio)
            {
                // A filtered panel offers the way back first, then every genre or every studio, so
                // the filter can be changed or dropped without walking back through the details page.
                AddDropdownRow("Back to my list", _lastStatus == DefaultStatusIndex,
                    () => LoadPanelAsync((int)AnimeListWorkModes.Anime, null));

                if (byGenre)
                {
                    foreach (var genre in System.Enum.GetValues(typeof(AnimeGenreSearch)).Cast<AnimeGenreSearch>())
                    {
                        var picked = genre;
                        AddDropdownRow(genre.ToString(), picked == _genreFilter,
                            () => LoadPanelAsync((int)AnimeListWorkModes.AnimeByGenre, picked.ToString()));
                    }
                }
                else
                {
                    foreach (var studio in System.Enum.GetValues(typeof(AnimeStudios)).Cast<AnimeStudios>())
                    {
                        var picked = studio;
                        AddDropdownRow(picked.ToString(), picked == _studioFilter,
                            () => LoadPanelAsync((int)AnimeListWorkModes.AnimeByStudio, picked.ToString()));
                    }
                }

                return;
            }

            var manga = mode == (int)AnimeListWorkModes.Manga;
            var tabs = manga ? MangaStatusTabs : AnimeStatusTabs;
            foreach (var tab in tabs)
            {
                var selected = tab.StatusSelectorIndex == _lastStatus;
                var selectorIndex = tab.StatusSelectorIndex;
                AddDropdownRow(manga && tab.Label == "Watching" ? "Reading" : tab.Label, selected,
                    () => OnStatusRowTapped(selectorIndex));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS status dropdown build failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     One dropdown row.
    ///     <para>
    ///     A Grid carrying only a TapGestureRecognizer is not hit tested on Android unless it has a
    ///     real background, so the row is painted in the panel's own colour: invisible against it,
    ///     but tappable. A Button cannot be used here because it cannot host content in this MAUI
    ///     version.
    ///     </para>
    /// </summary>
    private void AddDropdownRow(string label, bool selected, Action onTapped)
    {
        var sheetColor = Color.FromArgb("#0A1F2E");
        var accent = Color.FromArgb("#0066FF");
        var row = new Grid
        {
            BackgroundColor = sheetColor,
            HeightRequest = 44,
            Padding = new Thickness(12, 0),
            ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("Auto") }
        };
        row.Add(new Label
        {
            Text = label,
            FontFamily = "InterSemiBold",
            FontSize = 15,
            TextColor = selected ? accent : Colors.White,
            VerticalOptions = LayoutOptions.Center
        });
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
        row.Add(check);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTapped();
        row.GestureRecognizers.Add(tap);
        StatusDropdownList.Add(row);
    }

    /// <summary>
    ///     Switches the pushed panel to another work mode, or back to the plain list. Navigating the
    ///     "animelist" route again keeps this page on the Shell stack, so Back still returns to the
    ///     entry it was opened from.
    /// </summary>
    private async Task LoadPanelAsync(int mode, string type)
    {
        try
        {
            CloseStatusDropdown();
            Unfocus();
            var query = $"animelist?mode={mode}&status={_lastStatus}" +
                        (string.IsNullOrEmpty(type) ? "" : $"&type={Uri.EscapeDataString(type)}") +
                        RangeQuerySuffix();
            await Shell.Current.GoToAsync(query);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS panel switch failed: " + ex.Message);
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
            // Bumping the generation is what abandons a menu that is still waiting on the seasons
            // index: it compares against this on resume and drops out instead of filling a panel
            // that has already been handed to something else.
            _menuGeneration++;
            _openMenu = ContextMenuKind.None;
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
            // The status, season and year dropdowns are three chevrons sharing one panel, so they
            // share this toggle too. Reading IsVisible off the overlay is not enough to decide: it is
            // the chevron that has to be closed, not whichever panel happens to be standing.
            ToggleContextMenu(ContextMenuKind.Status);
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
                    // Carries the season on show. This is what makes coming back from an anime open
                    // the season that was on screen instead of jumping to the current one, because
                    // these args are what the details page registers as its back navigation.
                    return AnimeListPageNavigationArgs.SeasonalFor(ComposeSeason(_shownSeason, _shownYear));
            case AnimeListWorkModes.TopAnime:
                // Carries the band on show, so opening an entry and coming back through the
                // registered back navigation reopens the band that was on screen instead of the
                // first one. Only the ranking itself has a band; a filtered tab ignores it.
                var topAnimeArgs = AnimeListPageNavigationArgs.TopAnime(_topType);
                topAnimeArgs.TopRangeIndex = _topType == TopAnimeType.General ? _rangeIndex : -1;
                return topAnimeArgs;
            case AnimeListWorkModes.TopManga:
                var topMangaArgs = AnimeListPageNavigationArgs.TopMangaCategory(_mangaTopType);
                topMangaArgs.TopRangeIndex = _mangaTopType == MangaTopType.All ? _rangeIndex : -1;
                return topMangaArgs;
            case AnimeListWorkModes.MangaAdapted:
                return AnimeListPageNavigationArgs.MangaAdapted(_adaptedType);
            case AnimeListWorkModes.AnimeByGenre:
                // Reached from a genre chip on the details page. The value travels as a query
                // parameter because this page rebuilds its own args from the route, so anything the
                // caller put in AnimeListPageNavigationArgs would be dropped on the way in.
                //
                // No fallback: a genre mode with no parsed filter is a routing mistake, and quietly
                // standing in Action's results would hide it. Dropping to plain Anime is the honest
                // outcome, because that is the mode the query would otherwise be lying about.
                return _genreFilter.HasValue
                    ? new AnimeListPageNavigationArgs(_genreFilter.Value)
                    : new AnimeListPageNavigationArgs(0, AnimeListWorkModes.Anime);
            case AnimeListWorkModes.AnimeByStudio:
                return _studioFilter.HasValue
                    ? new AnimeListPageNavigationArgs(_studioFilter.Value)
                    : new AnimeListPageNavigationArgs(0, AnimeListWorkModes.Anime);
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
                // Restored only if something was there to restore. Assigning the saved value back
                // unconditionally wrote a null over a command the grid had already rebound in the
                // meantime, which silently ended infinite loading for the rest of the session: the
                // grid could be scrolled to its end and would simply never ask for another page.
                if (_grid != null && loadMore != null)
                    _grid.RemainingItemsThresholdReachedCommand = loadMore;
                else if (_grid != null)
                    _grid.RemainingItemsThresholdReachedCommand = Vm?.LoadMoreCommand;
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
        // The panel's back callback is armed for as long as the reader is ON the list. Without
        // releasing it here it kept answering while a details page was in front, and back from a
        // details page was spent leaving the subpanel instead of coming back to it: the press ran
        // GoToMainPageAsync, which lands on the main list, so Seasonal (or Top anime, Top manga,
        // Adapted, a genre or studio panel) reopened as the plain library instead of itself.
        // OnAppearing runs ApplyRoute, which re-arms it, so the panel still leaves for the main page
        // on its own back press.
        DisarmPanelBack();
        // MAUI re-applies query attributes on re-navigation but never clears a [QueryProperty] that the
        // new route omits, which would leak the previous work mode into this page. Dropped here rather
        // than only inside ApplyRoute, because the shell re-applies them as the section comes back into
        // view: leaving them set meant the next visit to the Anime tab inherited the mode of the last
        // one, so opening a Seasonal panel and then tapping Anime reopened it filtered by the season
        // instead of showing the library.
        Mode = null;
        Status = null;
        Type = null;
        Menu = null;
        Reset = null;
        _applied = false;
        _rootResetSuppressedFor = null;
        RestoreTabAfterFilterPush();
        if (_navConnected)
        {
            BottomNav.SectionActivated -= OnSectionActivated;
            BottomNav.SectionChanging -= OnSectionChanging;
            _navConnected = false;
        }
    }

    /// <summary>
    ///     Puts the anime tab back the way it was after a pushed genre or studio filter is popped.
    ///     <para>
    ///     Going back to the tab re-runs its own ApplyRoute, which resolves the same mode and status
    ///     it had before and then hits the alreadyShowing guard, so it skipped the reload and left
    ///     the filtered list on screen: the tab came back showing Drama under a Watching heading and
    ///     looked broken until a manual refresh. The guard is right in general and wrong here, so the
    ///     reload is forced explicitly.
    ///     </para>
    ///     <para>
    ///     Only done when the view model really is still on a filter. If the list view model turns
    ///     out to be per page instance then the tab was never touched and re-initialising it would
    ///     cost a request for nothing.
    ///     </para>
    /// </summary>
    private void RestoreTabAfterFilterPush()
    {
        if (!_pushedAsFilter)
            return;
        _pushedAsFilter = false;
        try
        {
            if (Vm == null || (int)Vm.WorkMode == _tabMode)
                return;
            _ = Vm.Init(BuildArgs(_tabMode, _tabStatus));
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS tab restore failed: " + ex.Message);
        }
    }

    private bool _navigatingToDetails;
    private bool _pushedAsFilter;
    private bool _panelFilterOpen;
    private int _tabMode = (int)AnimeListWorkModes.Anime;
    private int _tabStatus = DefaultStatusIndex;

    private static bool IsFilterMode(int mode)
        => mode == (int)AnimeListWorkModes.AnimeByGenre || mode == (int)AnimeListWorkModes.AnimeByStudio;

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
        // The threshold command alone is not enough. On a CollectionView that is live-repopulated
        // while the user is sitting at the bottom, RemainingItemsThresholdReached fires once for the
        // whole session and never re-arms, so LoadMore ran a single time and the list stopped at
        // whatever had been fetched by then (observed: three pages, with the counter stuck at
        // queued=0 page=1). Listening to the scroll putsthe trigger back where the scroll is.
        _grid.Scrolled += OnGridScrolled;
        AnimeGrid.ItemTapped += OnGridItemTapped;
        AnimeGrid.ItemLongPressed += OnGridItemLongPressed;
    }

    /// <summary>
    ///     Asks for the next page while there is still something to look at.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The margin used to be six rows, which is roughly one screen, so the request only went out
    ///     once the user was already standing on the last row. By then the wait was in full view: the
    ///     page could not be had until it had its turn behind a 300 ms gate and a round trip, and the
    ///     list sat there while it happened. That is the "it loads twenty five at a time and sometimes
    ///     stops" report.
    ///     </para>
    ///     <para>
    ///     Three screens instead. The fetch now runs under the scroll rather than in front of it, so
    ///     by the time the user reaches the end there is already another page waiting to be drawn.
    ///     The gate inside <c>LoadMore</c> is what keeps this from running away: a fetch already in
    ///     flight ignores the command until it lands, so however many scroll events arrive the list
    ///     only ever has one request outstanding.
    ///     </para>
    /// </remarks>
    private void OnGridScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        try
        {
            if (e.LastVisibleItemIndex < 0 || Vm == null)
                return;
            var screenHeight = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Height ?? 0;
            var rowsPerScreen = Math.Max(6, (int)(screenHeight / 210));
            var nearEnd = Vm.DisplayedItems.Count - e.LastVisibleItemIndex - 1 <= rowsPerScreen * 3;
            if (nearEnd && Vm.CanLoadMore)
                Vm.LoadMoreCommand?.Execute(null);
        }
        catch
        {
            // never let a scroll glitch break the grid
        }
    }

    private CollectionView _grid;

    /// <summary>
    ///     Long press on a poster: a small floating card, no title, because on a grid the entry is
    ///     right there under it and naming it again was the biggest thing on the panel.
    ///     <para>
    ///     Set status, Set score and Priority open a second level rather than acting straight away.
    ///     They used to be dead rows, because they carried neither a command nor a submenu.
    ///     </para>
    /// </summary>
    private void OnGridItemLongPressed(object sender, object item)
    {
        try
        {
            if (item is not MALPlus.XShared.ViewModels.AnimeItemViewModel viewModel)
                return;

            var manga = _lastMode == (int)AnimeListWorkModes.Manga;
            var rows = new List<MALPlus.Controls.OptionsSheetItem>
            {
                new("Copy title", "icon_copy", viewModel.CopyTitleToClipboardCommand),
                new("Copy link", "icon_list", viewModel.CopyLinkToClipboardCommand),
                new("Open in browser", "icon_search", viewModel.OpenInMALCommand)
            };

            // Only for an entry on the list, same as the original: a status or a score on something
            // that is not in the list has nothing to write to.
            if (viewModel.Auth)
            {
                rows.Add(BuildStatusSubmenu(viewModel, manga));
                rows.Add(BuildScoreSubmenu(viewModel));
                rows.Add(BuildPrioritySubmenu(viewModel));
                rows.Add(BuildWatchedSubmenu(viewModel, manga));
            }

            _sheetTarget = viewModel;
            PosterSheet.OpenCompact(rows);
            MainActivity.ArmMenuBackCallback(() => PosterSheet.Dismiss());
            global::Android.Util.Log.Info("MALPlus",
                $"MALPLUS poster menu opened for {viewModel.Id} '{viewModel.Title}' auth={viewModel.Auth} rows={rows.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS poster options failed: " + ex.Message);
        }
    }

    private MALPlus.XShared.ViewModels.AnimeItemViewModel _sheetTarget;

    /// <summary>
    ///     The status list, with the entry's own status marked.
    ///     <para>
    ///     Reopened in place rather than as a second sheet, so Back inside the panel goes back a level
    ///     instead of closing it. The mark matters: without it there is nothing on screen to tell
    ///     whether the change landed, and an update that fails reverts itself silently.
    ///     </para>
    /// </summary>
    private MALPlus.Controls.OptionsSheetItem BuildStatusSubmenu(
        MALPlus.XShared.ViewModels.AnimeItemViewModel vm, bool manga)
    {
        var options = new List<MALPlus.Controls.OptionsSheetItem>();
        foreach (AnimeStatus status in System.Enum.GetValues(typeof(AnimeStatus)))
        {
            // AllOrAiring is a query filter, not something a list entry can be set to.
            if (status == AnimeStatus.AllOrAiring)
                continue;
            var picked = status;
            options.Add(new MALPlus.Controls.OptionsSheetItem(
                MALPlus.XShared.Utils.Utilities.StatusToString((int)status, manga),
                "icon_status",
                action: () =>
                {
                    global::Android.Util.Log.Info("MALPlus",
                        $"MALPLUS menu status -> {(int)status} for {vm.Id} (was {(int)vm.MyStatus})");
                    vm.ChangeStatus(status);
                },
                value: vm.MyStatus == status ? "Current" : null));
        }


        return new MALPlus.Controls.OptionsSheetItem("Set status", "icon_status",
            value: MALPlus.XShared.Utils.Utilities.StatusToString((int)vm.MyStatus, manga),
            submenu: sheet =>
            {
                sheet.OpenSubmenu("Status", options, () => ReopenPosterSheet(sheet));
                return true;
            });
    }

    private MALPlus.Controls.OptionsSheetItem BuildScoreSubmenu(
        MALPlus.XShared.ViewModels.AnimeItemViewModel vm)
    {
        var options = new List<MALPlus.Controls.OptionsSheetItem>
        {
            new("None", "icon_score",
                action: () => vm.ChangeScore("0"),
                value: vm.MyScore == 0 ? "Current" : null)
        };
        for (var score = 10; score >= 1; score--)
        {
            var picked = score;
            options.Add(new MALPlus.Controls.OptionsSheetItem(picked.ToString(), "icon_score",
                // A string, not an int: ChangeScore casts with "score as string", and an int there
                // becomes a null cast, which Convert.ToInt32 turns into 0. The score silently became
                // "unranked" instead of the number that was tapped.
                action: () =>
                {
                    global::Android.Util.Log.Info("MALPlus",
                        $"MALPLUS menu score -> {picked} for {vm.Id} (was {vm.MyScore})");
                    vm.ChangeScore(picked.ToString());
                },
                value: vm.MyScore == picked ? "Current" : null));
        }

        return new MALPlus.Controls.OptionsSheetItem("Set score", "icon_score",
            value: vm.MyScore == 0 ? "Unranked" : vm.MyScore.ToString("0"),
            submenu: sheet =>
            {
                sheet.OpenSubmenu("Score", options, () => ReopenPosterSheet(sheet));
                return true;
            });
    }

    private MALPlus.Controls.OptionsSheetItem BuildPrioritySubmenu(
        MALPlus.XShared.ViewModels.AnimeItemViewModel vm)
    {
        var options = new List<MALPlus.Controls.OptionsSheetItem>
        {
            new("Low", "icon_priority",
                action: () => vm.ChangePriority(AnimePriority.Low),
                value: vm.Priority == AnimePriority.Low ? "Current" : null),
            new("Medium", "icon_priority",
                action: () => vm.ChangePriority(AnimePriority.Medium),
                value: vm.Priority == AnimePriority.Medium ? "Current" : null),
            new("High", "icon_priority",
                action: () => vm.ChangePriority(AnimePriority.High),
                value: vm.Priority == AnimePriority.High ? "Current" : null)
        };

        return new MALPlus.Controls.OptionsSheetItem("Priority", "icon_priority",
            value: vm.Priority.ToString(),
            submenu: sheet =>
            {
                sheet.OpenSubmenu("Priority", options, () => ReopenPosterSheet(sheet));
                return true;
            });
    }

    private MALPlus.Controls.OptionsSheetItem BuildWatchedSubmenu(
        MALPlus.XShared.ViewModels.AnimeItemViewModel vm, bool manga)
    {
        // Whatever this entry actually counts in. For a manga with volumes in focus, "chapters" is the
        // wrong word AND the wrong field, and the entry would have been written to the other one.
        var byVolumes = manga && Settings.MangaFocusVolumes;
        var current = vm.MyEpisodesFocused;
        var total = vm.AllEpisodesFocused;

        var options = new List<MALPlus.Controls.OptionsSheetItem>
        {
            new("None", "icon_episodes",
                action: () => SetWatched(vm, 0),
                value: current == 0 ? "Current" : null)
        };
        // Up to the entry's own length where it is known, so the list is not a wall of numbers that
        // cannot be chosen anyway.
        var highest = total > 0 ? Math.Min(total, 30) : 12;
        for (var count = 1; count <= highest; count++)
        {
            var picked = count;
            options.Add(new MALPlus.Controls.OptionsSheetItem(picked.ToString(), "icon_episodes",
                action: () => SetWatched(vm, picked),
                value: current == picked ? "Current" : null));
        }

        // "Set episodes", not "Set episodes watched": the row is 264dp wide and the longer label ran
        // into the value on the right. The submenu title carries the full wording if it is needed.
        return new MALPlus.Controls.OptionsSheetItem(
            byVolumes ? "Set volumes" : manga ? "Set chapters" : "Set episodes",
            "icon_episodes",
            value: $"{current}/{(total == 0 ? "?" : total.ToString())}",
            submenu: sheet =>
            {
                sheet.OpenSubmenu(
                    byVolumes ? "Volumes read" : manga ? "Chapters read" : "Episodes watched", options,
                    () => ReopenPosterSheet(sheet));
                return true;
            });
    }

    /// <summary>
    ///     Sets the progress through the input property the entry already owns, rather than writing the
    ///     abstraction directly: the update query reads the item, not the parent, so a direct write
    ///     would send the old number.
    /// </summary>
    private static void SetWatched(MALPlus.XShared.ViewModels.AnimeItemViewModel vm, int count)
    {
        global::Android.Util.Log.Info("MALPlus",
            $"MALPLUS menu watched -> {count} for {vm.Id} (was {vm.MyEpisodesFocused})");
        vm.WatchedEpsInput = count.ToString();
        vm.ChangeWatchedEps();
    }

    /// <summary>Rebuilds the first level, so Back out of a submenu lands on the menu, not on nothing.</summary>
    private bool ReopenPosterSheet(MALPlus.Controls.OptionsSheet sheet)
    {
        var vm = _sheetTarget;
        if (vm == null)
        {
            sheet.Dismiss();
            return false;
        }
        OnGridItemLongPressed(this, vm);
        return true;
    }

    private void OnPosterSheetClosed(object sender, EventArgs e)
    {
        MainActivity.DisarmMenuBackCallback();
        // The sheet owned the back gesture while it was open and disarming it left nothing in its
        // place. On a subpanel the gesture has to go back to leaving the panel, or one dismissal
        // would leave the panel unable to be left at all.
        _panelBackArmed = false;
        UpdatePanelBackNavigation();
    }
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

            // Declared rather than inferred. Coming back from this page is the one navigation that has
            // to reopen the panel it left, and it cannot be worked out afterwards: MAUI re-applies the
            // [QueryProperty] values when the shell pops, so by the time this page appears its Mode
            // holds whatever the shell last had, which for a panel opened off More is the plain
            // library's own value rather than the panel's. The log showed mode=1 going out and mode=0
            // coming back with _appliedMode still sitting on 1, so the fallback never got a look in.
            _returningFromDetails = true;

            // The gesture is taken over rather than left to the shell. Details is pushed with a
            // relative route while the four tabs are ShellContents rather than stackable pages, so the
            // shell does not treat it as a child of this section and pops two levels on the way back:
            // from //anime/animedetails straight to //more, which is the section the panel was opened
            // from. The navigation trace showed exactly that, one gesture, //more. Naming the way back
            // here is what makes it land on this panel instead of wherever the shell feels like.
            _detailsReturnMode = _lastMode;
            _detailsReturnManga = manga;

            // The panel's own back callback is replaced by this one, so the flag has to come down with
            // it. Left set, the OnDisappearing this very navigation raises saw a panel it still believed
            // was armed and disarmed the callback just registered, so nothing was left to answer the
            // gesture and the shell popped two levels on its own. The trace showed the return handler
            // never running at all.
            _panelBackArmed = false;
            MainActivity.ArmMenuBackCallback(ReturnFromDetailsAsync);

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

    private int _detailsReturnMode = -1;
    private bool _detailsReturnManga;

    /// <summary>
    ///     The way back from a details page this list opened: straight to this list, in the state it was
    ///     showing.
    /// </summary>
    /// <remarks>
    ///     Named rather than popped, because the shell cannot do it here. The four tabs are ShellContents
    ///     and a details page is pushed with a relative route, so the two are not parent and child as far
    ///     as the navigation stack is concerned, and a back gesture unwound two levels at once and left
    ///     the panel entirely.
    ///     <para>
    ///     The query is built by the same serialiser the panel used on its way in, from the same args.
    ///     Building it here by hand is what dropped the type and sent an Upcoming panel back to the
    ///     General ranking, and there was no reason for it to be a second implementation: anything the
    ///     args carry round-trips from now on without a second place to remember.
    ///     </para>
    /// </remarks>
    private void ReturnFromDetailsAsync()
    {
        var mode = _detailsReturnMode >= 0 ? _detailsReturnMode : (int)AnimeListWorkModes.Anime;
        var root = _detailsReturnManga ? "//manga" : "//anime";
        var query = MauiMainViewModel.BuildAnimeListQuery(
            _detailsReturnManga ? PageIndex.PageMangaList : PageIndex.PageAnimeList,
            BuildArgs(mode, _lastStatus));
        Android.Util.Log.Info("MALPLUS", $"AnimeList returning from details to {root}{query}");
        _detailsReturnMode = -1;
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                // reset=1 keeps this a real arrival rather than a no-op against the page already on
                // screen, which is what makes ApplyRoute repaint instead of skipping.
                await Shell.Current.GoToAsync($"{root}{query}&reset=1");
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS return from details failed: " + ex.GetType().Name);
            }
        });
    }

    private sealed class MauiDimensionsProvider : MALPlus.XShared.ViewModels.Interfaces.IDimensionsProvider
    {
        public double ActualWidth => DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        public double ActualHeight => DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
    }
}
