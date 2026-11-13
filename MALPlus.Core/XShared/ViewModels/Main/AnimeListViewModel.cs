using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Enums;
using MALPlus.Models.Models;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.BL;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Comm.Manga;
using MALPlus.XShared.Delegates;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels.Interfaces;
using AnimeType = MALPlus.Models.Enums.AnimeType;
using MangaType = MALPlus.Models.Enums.MangaType;

namespace MALPlus.XShared.ViewModels.Main
{
    public partial class AnimeListViewModel : ViewModelBase
    {
        private readonly IAnimeLibraryDataStorage _animeLibraryDataStorage;
        private const int ItemPrefferedWidth = 385;
        private const int LastIndexPositionOnRefresh = -10; //just a constant
        private const int ItemsPerPage = 25; //matches what one fetch really adds per page
        private const int ItemsPerGenereStudioPage = 25; //just a constant

        private SmartObservableCollection<AnimeItemViewModel> _animeItems =
            new SmartObservableCollection<AnimeItemViewModel>();

        private List<AnimeItemAbstraction> _animeItemsSet =
            new List<AnimeItemAbstraction>(); //All for current list

        private bool _initializing;
        private bool _queryHandler;

        public bool ResetedNavBack { get; set; } = true;

        private AnimeListDisplayModes? _manuallySelectedViewMode;
        private string _prevListSource;
        private int _prevStatus = -1;

        /// <summary>
        ///     What the grid is actually showing, as opposed to what was last fetched. The fetch memo
        ///     cannot be trusted on its own here: the panels reuse this page and repaint it without
        ///     ever recording a fetch, so "already loaded" said true while a ranking was on screen.
        ///     -1 means nothing has been drawn yet, which never matches and so never short-circuits.
        /// </summary>
        private AnimeListWorkModes _displayedMode = (AnimeListWorkModes)(-1);
        private int _displayedStatus = -1;

        private string _prevQuery = "";
        private AnimeStatus _prevAnimeStatus;
        private bool _invalidatePreviousSearchResults;


        private AnimeListWorkModes _prevWorkMode = AnimeListWorkModes.Anime;
        private bool _scrollHandlerAdded;

        private bool _canLoadMoreFilterLock;
        private bool _drainWholeList;

        public bool CanAddScrollHandler;
        public AnimeSeason CurrentSeason;

        public AnimeGenreSearch Genre { get; set; }
        public AnimeStudios Studio { get; set; }

        public IDimensionsProvider DimensionsProvider { get; set; }

        public bool Initializing
        {
            get { return _initializing; }
            private set
            {
                _initializing = value;
                if (!value)
                    Initialized?.Invoke();
            }
        }

        public SmartObservableCollection<AnimeItemViewModel> AnimeItems
        {
            get { return _animeItems; }
            set
            {
                if (_animeItems != null)
                    _animeItems.CollectionChanged -= OnSourceItemsChanged;
                _animeItems = value;
                // Rebuilt here because this is where the instance is swapped, and the hook has to
                // follow it. Without it the panel filter kept matching against the detached
                // collection and the grid came back empty after a refresh.
                _animeItems.CollectionChanged += OnSourceItemsChanged;
                // The list binds AnimeItems directly, not the display-mode aliases below, so
                // swapping the instance without notifying it left the CollectionView pointing
                // at the old, detached collection: coming back from any pushed page showed an
                // empty grid even though the ViewModel had repopulated the new collection.
                RaisePropertyChanged(() => AnimeItems);
                RaisePropertyChanged(() => AnimeListItems);
                RaisePropertyChanged(() => AnimeCompactItems);
                RaisePropertyChanged(() => AnimeGridItems);
                RebuildDisplayedItems();
            }
        }

        /// <summary>
        ///     What the grid actually binds. Identical to <see cref="AnimeItems" /> until a panel
        ///     filter is typed, at which point it holds the matches only.
        ///     <para>
        ///     This exists rather than filtering in place because the grid's infinite scroll appends
        ///     to AnimeItems from six different places, and a filtered view over the live collection
        ///     would have to be kept in step with all of them. A separate collection that is rebuilt
        ///     from the source means those six sites need no changes at all.
        ///     </para>
        /// </summary>
        public SmartObservableCollection<AnimeItemViewModel> DisplayedItems { get; } =
            new SmartObservableCollection<AnimeItemViewModel>();

        /// <summary>
        ///     What the grid content belongs to. A root page only takes over the shared collection
        ///     once this names its own mode, so it keeps its preview instead of flashing the other
        ///     tab's rows while the fetch for the new mode is still running.
        /// </summary>
        public AnimeListWorkModes DisplayedMode => _displayedMode;

        /// <summary>
        ///     Bumped whenever a library list is (re)populated, so a root page can tell a stale
        ///     preview snapshot from a current one without rebuilding it on every navigation event.
        /// </summary>
        public int LibraryVersion { get; private set; }

        private string _panelFilter = "";

        /// <summary>
        ///     Free text typed in the magnifier of the top bar. Filters what is already on screen and
        ///     never goes to the network, so the scope is whatever the panel is showing: inside
        ///     Watching it only matches Watching, inside a genre it only matches that genre.
        /// </summary>
        public string PanelFilter
        {
            get => _panelFilter;
            set
            {
                var next = value ?? "";
                if (string.Equals(_panelFilter, next, StringComparison.Ordinal))
                    return;
                _panelFilter = next;
                RebuildDisplayedItems();
                RaisePropertyChanged(() => PanelFilterActive);
                RaisePropertyChanged(() => PanelFilterEmpty);
            }
        }

        public bool PanelFilterActive => !string.IsNullOrWhiteSpace(_panelFilter);

        /// <summary>True when a filter is on and it matched nothing, so the notice can be shown.</summary>
        public bool PanelFilterEmpty => PanelFilterActive && DisplayedItems.Count == 0;

        private void OnSourceItemsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => RebuildDisplayedItems();

        private void RebuildDisplayedItems()
        {
            try
            {
                var source = _animeItems;
                var filter = _panelFilter?.Trim() ?? "";
                // Materialised, because the prefix check below needs to compare entry by entry and
                // because a lazily filtered sequence cannot be walked twice.
                var matches = new List<AnimeItemViewModel>();
                if (source != null)
                {
                    if (string.IsNullOrEmpty(filter))
                        matches.AddRange(source);
                    else
                        matches.AddRange(source.Where(item => item != null &&
                                                            (item.Title ?? "").IndexOf(filter,
                                                                StringComparison.OrdinalIgnoreCase) >= 0));
                }

                // When the list has only GREW, the rows on screen are exactly the head of what is
                // wanted, and the tail is all that is new. Clearing and re-adding the whole thing in
                // that case is what the grid read as a reload: it drops its adapter and rebuilds
                // every visible cell, which is the flash at the end of the panel.
                //
                // The check is made against what is actually there rather than against a flag, so it
                // cannot be wrong about the list state: a removal, a re-order, a different filter or
                // a different panel all fail the comparison and take the full rebuild below, exactly
                // as before.
                if (IsUnchangedPrefixOf(matches))
                {
                    // One batch, never a loop. Appending row by row raises a collection change per
                    // row, and the grid re-lays out and re-warms on every one of them: three hundred
                    // forced layouts for a single page, which is enough to stop the UI answering and
                    // leave the grid blank. AddRange raises it once.
                    var tail = matches.Count - DisplayedItems.Count;
                    if (tail > 0)
                        DisplayedItems.AddRange(matches.Skip(DisplayedItems.Count).ToList());
                    RaisePropertyChanged(() => PanelFilterEmpty);
                    return;
                }

                DisplayedItems.Clear();
                DisplayedItems.AddRange(matches);
                RaisePropertyChanged(() => PanelFilterEmpty);
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS panel filter failed: " + ex.Message);
            }
        }

        /// <summary>
        ///     The rows a root tab shows before it is the live one. Both root list pages are backed by
        ///     this same view model, so the page that is off screen would otherwise paint the live
        ///     tab's collection: dragging Anime towards Manga showed the anime list on both pages.
        ///     The snapshot is the other mode's own library, filtered exactly like a bare root reset
        ///     leaves it (Watching), which is what the drag is previewing.
        /// </summary>
        public IReadOnlyList<AnimeItemViewModel> GetLibraryPreview(bool manga)
        {
            var source = manga
                ? _animeLibraryDataStorage.AllLoadedMangaItemAbstractions
                : _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions;

            if (source.Count > 0)
                return source
                    .Where(item => item != null &&
                                   (item.MyStatus == AnimeStatus.Watching || item.IsRewatching))
                    .Select(item => item.ViewModel)
                    .ToList();

            return (manga ? _previewMangaSnapshot : _previewAnimeSnapshot)
                   ?? Array.Empty<AnimeItemViewModel>();
        }

        private IReadOnlyList<AnimeItemViewModel> _previewAnimeSnapshot;
        private IReadOnlyList<AnimeItemViewModel> _previewMangaSnapshot;
        private bool _previewAnimeLoading;
        private bool _previewMangaLoading;

        public event Action PreviewLibraryChanged;

        public void InvalidatePreviews()
        {
            _previewAnimeSnapshot = null;
            _previewMangaSnapshot = null;
            LibraryVersion++;
            PreviewLibraryChanged?.Invoke();
        }

        public void ResetData()
        {
            _prevWorkMode = (AnimeListWorkModes)(-1);
            _prevListSource = null;
            _prevStatus = -1;
            _displayedMode = (AnimeListWorkModes)(-1);
            _displayedStatus = -1;
            _animeItemsSet = new List<AnimeItemAbstraction>();
            AnimeItems = new SmartObservableCollection<AnimeItemViewModel>();
            DisplayedItems.Clear();
            InvalidatePreviews();
        }

        /// <summary>
        ///     Loads the mode whose own list has never been fetched, so the tab a drag previews is
        ///     not an empty page. Read only: the snapshot never writes the storage nor the shared
        ///     collections, so it cannot race the live page's fetch.
        /// </summary>
        public void EnsurePreviewLibraryLoaded(bool manga)
        {
            if (!Credentials.Authenticated || string.IsNullOrWhiteSpace(Credentials.UserName))
                return;

            var loaded = manga
                ? _animeLibraryDataStorage.AllLoadedMangaItemAbstractions.Count > 0
                : _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions.Count > 0;
            if (loaded)
                return;

            if (manga ? _previewMangaLoading : _previewAnimeLoading)
                return;
            if (manga)
                _previewMangaLoading = true;
            else
                _previewAnimeLoading = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    var mode = manga ? AnimeListWorkModes.Manga : AnimeListWorkModes.Anime;
                    var data = await new LibraryListQuery(Credentials.UserName, mode).GetLibrary(false);
                    if (data == null || data.Count == 0)
                        return;

                    var readOnlyManga = Settings.SelectedApiType == ApiType.Mal;
                    var snapshot = data
                        .Select(item => manga
                            ? new AnimeItemAbstraction(readOnlyManga, item as MangaLibraryItemData)
                            : new AnimeItemAbstraction(true, item as AnimeLibraryItemData))
                        .Where(item => item != null &&
                                       (item.MyStatus == AnimeStatus.Watching || item.IsRewatching))
                        .Select(item => item.ViewModel)
                        .ToList();

                    if (manga)
                        _previewMangaSnapshot = snapshot;
                    else
                        _previewAnimeSnapshot = snapshot;

                    LibraryVersion++;
                    ResourceLocator.DispatcherAdapter.Run(() => PreviewLibraryChanged?.Invoke());
                }
                catch (Exception e)
                {
                    Console.WriteLine("Preview library preload failed: " + e.Message);
                }
            });
        }

        /// <summary>
        ///     True when what is already shown is the same run of entries the list now starts with.
        /// </summary>
        /// <remarks>
        ///     Compared on the Mal id, NOT on object identity. The rows are rebuilt on every page:
        ///     the fetch replaces the storage list with a new one and the row builder creates a fresh
        ///     abstraction and a fresh view model for each entry, so the same anime is a different
        ///     object on every page. Comparing references therefore never matched, the list was always
        ///     judged as changed, and the grid was cleared and rebuilt on every page that arrived,
        ///     which is the flash over posters that had not changed. The id is the one thing that
        ///     survives being rebuilt.
        ///     <para>
        ///     Anything that is a genuine change still fails this and takes the full rebuild: a
        ///     different order, a missing entry, a re-filtered list or a different panel.
        ///     </para>
        /// </remarks>
        private bool IsUnchangedPrefixOf(List<AnimeItemViewModel> wanted)
        {
            if (DisplayedItems.Count > wanted.Count)
                return false;
            for (var i = 0; i < DisplayedItems.Count; i++)
            {
                if (DisplayedItems[i] == null || wanted[i] == null)
                    return false;
                if (DisplayedItems[i].Id != wanted[i].Id)
                    return false;
            }
            return true;
        }

        /// <summary>
        ///     Same test, against the bound collection, for the queue about to be drained.
        /// </summary>
        /// <remarks>
        ///     The rows on screen and the rows queued come from the same rebuild, so their view models
        ///     are as different from each other as they are from the previous page. Same reason as
        ///     above: the Mal id is what identifies the entry.
        /// </remarks>
        private bool AreDrawnRowsTheHeadOf(List<AnimeItemViewModel> queued)
        {
            if (AnimeItems.Count > queued.Count)
                return false;
            for (var i = 0; i < AnimeItems.Count; i++)
            {
                if (AnimeItems[i] == null || queued[i] == null)
                    return false;
                if (AnimeItems[i].Id != queued[i].Id)
                    return false;
            }
            return true;
        }

        public SmartObservableCollection<AnimeItemViewModel> AnimeListItems
            => DisplayMode == AnimeListDisplayModes.IndefiniteList ? AnimeItems : null;

        public SmartObservableCollection<AnimeItemViewModel> AnimeGridItems
            => DisplayMode == AnimeListDisplayModes.IndefiniteGrid ? AnimeItems : null;

        public SmartObservableCollection<AnimeItemViewModel> AnimeCompactItems
            => DisplayMode == AnimeListDisplayModes.IndefiniteCompactList ? AnimeItems : null;

        public ObservableCollection<AnimeSeason> SeasonSelection { get; } = new ObservableCollection<AnimeSeason>();

        public bool AreThereItemsWaitingForLoad => _animeItemsSet.Count != 0;

        /// <summary>Diagnostics helper: how many items are still queued for the page.</summary>
        public int PendingItemCount => _animeItemsSet.Count;

        public int CurrentStatus
        {
            get { return (int)GetDesiredStatus(); }
            set
            {
                SetDesiredStatus(value);
                SetDisplayMode((AnimeStatus)GetDesiredStatus());

            }
        }

        public double ListItemGridWidth
        {
            get
            {
                var width = DimensionsProvider?.ActualWidth ?? 1000;
                var items = (int) width/ItemPrefferedWidth;
                items = items == 0 ? 1 : items;
                var widthRest = width - items*ItemPrefferedWidth;
                var sum = ItemPrefferedWidth + widthRest/items;
                return sum < ItemPrefferedWidth ? ItemPrefferedWidth : sum;
            }
        }

        public AnimeListViewModel(IAnimeLibraryDataStorage animeLibraryDataStorage)
        {
            _animeLibraryDataStorage = animeLibraryDataStorage;

            _animeLibraryDataStorage.AnimeRemoved += OnAnimeEntryRemoved;
            ResourceLocator.AiringInfoProvider.AiringsUpdated += OnAiringsUpdated;

            for (int i = 2000; i < DateTime.Now.Year + 2; i++)
            {
                SeasonYears.Add(i.ToString());
            }
        }

        /// <summary>
        /// Set -10 to scroll to last item
        /// </summary>
        public int CurrentIndexPosition { get; set; }

        public Dictionary<string, object> UiState { get; } = new Dictionary<string, object>();

        public void SetListScroll(int firstVisiblePosition, int pixelOffset)
        {
            UiState["AnimeListScroll"] = (firstVisiblePosition, pixelOffset);
        }

        public (int position, int offset) GetListScroll()
        {
            if (UiState.TryGetValue("AnimeListScroll", out var v) && v is ValueTuple<int, int> t)
                return t;
            return (0, 0);
        }

        public event AnimeItemListInitialized Initialized;
        public event ScrollIntoViewRequest ScrollIntoViewRequested;
        public event SortingSettingChange SortingSettingChanged;
        public event SelectionResetRequest SelectionResetRequested;
        public event EmptyEventHander HideSeasonSelectionFlyout;
        public event EmptyEventHander HideFiltersFlyout;
        public event EmptyEventHander HideSortingFlyout;
        public event EmptyEventHander HideViewsFlyout;
        public event EmptyEventHander ScrollToTopRequest;
        public event EmptyEventHander AddScrollHandlerRequest;
        public event EmptyEventHander RemoveScrollHandlerRequest;
        public event EmptyEventHander RemoveScrollingConatinerReferenceRequest;

        private bool _pendingWipe;

        /// <summary>
        /// Wipes the page only when this Init actually points at a different list than the one
        /// already on screen. A back-nav re-entry re-runs Init with the same identity, and
        /// FetchData short-circuits on that identity, so wiping here left the grid permanently
        /// empty with nothing left to repopulate it.
        /// </summary>
        private void ResetForInit()
        {
            if (!_pendingWipe)
                return;
            _pendingWipe = false;

            var sameEntry = _prevListSource == ListSource
                            && _prevWorkMode == WorkMode
                            && _prevStatus == (int)GetDesiredStatus();
            // _animeItemsSet is a queue that UpdatePageSetup drains into AnimeItems, so it is
            // legitimately empty once the page has content: the check has to be on what is
            // actually on screen.
            if (sameEntry && (AnimeItems?.Count ?? 0) > 0)
            {
                // Nothing to redo: the items are still loaded and already bound, so keep them.
                Loading = false;
                return;
            }

            _animeItemsSet = new List<AnimeItemAbstraction>();
            AnimeItems = new SmartObservableCollection<AnimeItemViewModel>();
        }

        public async Task Init(AnimeListPageNavigationArgs args)
        {
            try
            {
                await InitializationRoutines.AwaitableCompletion
                    .Task; //wait for all necessary modelus to complete loading
                //base
                _scrollHandlerAdded = false;
                Initializing = true;
                _manuallySelectedViewMode = null;
                //take out trash
                // The wipe is deferred until the args have been applied, so we can tell a real
                // entry change from a plain back-nav re-entry (see ResetForInit).
                _pendingWipe = true;
                _randomedIds = new List<int>();
                _fetching = _fetchingSeasonal = false;

                if (args != null && args.ResetBackNav && !args.FromMore)
                    ViewModelLocator.NavMgr.ResetMainBackNav();

                if (!_queryHandler)
                {
                    ViewModelLocator.GeneralMain.OnSearchDelayedQuerySubmitted += OnOnSearchDelayedQuerySubmitted;
                    ViewModelLocator.GeneralMain.OnSearchQuerySubmitted += OnOnSearchDelayedQuerySubmitted;
                }

                _queryHandler = true;

                //give visual feedback
                Loading = true;
                CanLoadMore = false;
                await Task.Delay(10);

                //depending on args
                var gotArgs = false;
                // A different panel is a different set of rows, so the one-shot seeding of the top of
                // the grid has to be armed again or the new panel's first rows never get a date.
                _airTimesPrefetched = false;
                if (args != null) //Save current mode
                {
                    ResetedNavBack = args.ResetBackNav;
                    WorkMode = args.WorkMode;
                    if (WorkMode == AnimeListWorkModes.TopAnime)
                    {
                        TopAnimeWorkMode = args.TopWorkMode;
                        // Read after the work mode and subcategory above, because a band is only
                        // meaningful on the tabs that ARE the ranking and this setter compares the
                        // two to decide whether to raise CanLoadMore.
                        TopRangeIndex = ResolveRangeIndex(args.TopRangeIndex);
                        ViewModelLocator.GeneralHamburger.SetActiveButton(args.TopWorkMode); //we have to have it
                    }
                    else if (WorkMode == AnimeListWorkModes.TopManga)
                    {
                        MangaTopWorkMode = args.MangaTopWorkMode;
                        TopRangeIndex = ResolveRangeIndex(args.TopRangeIndex);
                    }
                    else if (WorkMode == AnimeListWorkModes.MangaAdapted)
                    {
                        MangaAdaptedWorkMode = args.MangaAdaptedWorkMode;
                    }
                    else if (WorkMode == AnimeListWorkModes.AnimeByGenre)
                    {
                        Genre = args.Genre;
                    }
                    else if (WorkMode == AnimeListWorkModes.AnimeByStudio)
                    {
                        Studio = args.Studio;
                    }
                    else
                    {
                        // Not a top panel at all. The band is cleared rather than left standing so
                        // that coming back to a top panel cannot inherit one chosen for a different
                        // list: HasBandRange would refuse it anyway, but the field would keep saying
                        // otherwise to whatever read it next.
                        TopRangeIndex = -1;
                    }

                    if (!string.IsNullOrEmpty(args.ListSource))
                        ListSource = args.ListSource;
                    else
                        ListSource = Credentials.UserName;


                    if (args.NavArgs) // Use args if we have any
                    {
                        SortDescending = SortDescending = args.Descending;
                        SetSortOrder(args.SortOption); //index
                        SetDesiredStatus(args.Status);
                        CurrentIndexPosition = args.SelectedItemIndex;
                        CurrentSeason = args.CurrSeason;
                        DisplayMode = args.DisplayMode;
                        gotArgs = true;
                    }
                }
                else //assume default AnimeList
                {
                    WorkMode = AnimeListWorkModes.Anime;
                    ListSource = Credentials.UserName;
                }

                ViewModelLocator.GeneralHamburger.UpdateAnimeFiltersSelectedIndex();
                RaisePropertyChanged(() => CurrentlySelectedDisplayMode);
                ResetForInit();
                switch (WorkMode)
                {
                    case AnimeListWorkModes.Manga:
                    case AnimeListWorkModes.Anime:
                        if (!gotArgs)
                            SetDefaults(args?.StatusIndex);

                        AppBtnListSourceVisibility = true;
                        AppbarBtnPinTileVisibility = false;
                        AppBtnSortingVisibility = true;
                        AnimeItemsDisplayContext = AnimeItemDisplayContext.AirDay;
                        if (WorkMode == AnimeListWorkModes.Anime)
                        {
                            SortAirDayVisibility = true;
                            Sort3Label = "Watched";
                            StatusAllLabel = "All";
                            Filter1Label = "Watching";
                            Filter5Label = "Plan to watch";
                        }
                        else // manga
                        {
                            SortAirDayVisibility = false;
                            Sort3Label = "Read";
                            StatusAllLabel = "All";
                            Filter1Label = "Reading";
                            Filter5Label = "Plan to read";
                        }

                        //try to set list source - display notice on fail
                        if (string.IsNullOrWhiteSpace(ListSource))
                        {
                            if (!string.IsNullOrWhiteSpace(Credentials.UserName))
                                ListSource = Credentials.UserName;
                        }

                        if (string.IsNullOrWhiteSpace(ListSource))
                        {
                            EmptyNoticeVisibility = true;
                            EmptyNoticeContent =
                                "We have come up empty...\nList source is not set.\nLog in or set it manually.";
                            BtnSetSourceVisibility = true;
                            Loading = false;
                        }
                        else
                            await FetchData(); //we have source we can fetch

                        break;                    case AnimeListWorkModes.SeasonalAnime:
                    case AnimeListWorkModes.TopAnime:
                    case AnimeListWorkModes.TopManga:
                    case AnimeListWorkModes.MangaAdapted:
                    case AnimeListWorkModes.AnimeByGenre:
                    case AnimeListWorkModes.AnimeByStudio:
                        Loading = true;
                        EmptyNoticeVisibility = false;

                        AppBtnListSourceVisibility = false;
                        AppBtnGoBackToMyListVisibility = false;
                        BtnSetSourceVisibility = false;

                        ViewModelLocator.NavMgr.DeregisterBackNav();
                        ViewModelLocator.NavMgr.RegisterBackNav(
                            args?.FromMore == true ? PageIndex.PageMore : PageIndex.PageAnimeList, null);


                        if (!gotArgs)
                        {
                            SortDescending = false;
                            //Seasonal defaults to MAL score (descending), other modes to index order
                            SetSortOrder(WorkMode == AnimeListWorkModes.SeasonalAnime ? SortOptions.SortScore : SortOptions.SortWatched);
                            SetDesiredStatus(null);
                            CurrentSeason = null;
                            SeasonSelection.Clear();
                        }

                        //StatusAllLabel = WorkMode == AnimeListWorkModes.SeasonalAnime ? "Airing" : "All";

                        Sort3Label = "Index";
                        await FetchSeasonalData();
                        if (WorkMode == AnimeListWorkModes.TopAnime || WorkMode == AnimeListWorkModes.TopManga ||
                            WorkMode == AnimeListWorkModes.MangaAdapted)
                        {
                            AppbarBtnPinTileVisibility = AppBtnSortingVisibility = false;
                            AnimeItemsDisplayContext = AnimeItemDisplayContext.Index;
                        }
                        else
                        {
                            if (WorkMode == AnimeListWorkModes.AnimeByGenre ||
                                WorkMode == AnimeListWorkModes.AnimeByStudio)
                            {
                                AppbarBtnPinTileVisibility = false;
                                AppBtnSortingVisibility = true;
                                AnimeItemsDisplayContext = AnimeItemDisplayContext.Index;
                            }
                            else
                            {
                                AppbarBtnPinTileVisibility = AppBtnSortingVisibility = true;
                                AnimeItemsDisplayContext = AnimeItemDisplayContext.AirDay;
                            }


                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                RaisePropertyChanged(() => LoadAllDetailsButtonVisiblity);
                SortingSettingChanged?.Invoke(SortOption, SortDescending);

                UpdateUpperStatus();
            }
            finally
            {
                Initializing = false;
                // Loading is only cleared inside UpdatePageSetup, so any path that
                // throws or returns early used to leave the spinner on permanently.
                if (Loading && (AnimeItems == null || AnimeItems.Count == 0))
                    Loading = false;
            }
        }

        public void OnNavigatedFrom()
        {
            ViewModelLocator.GeneralMain.OnSearchDelayedQuerySubmitted -= OnOnSearchDelayedQuerySubmitted;
            ViewModelLocator.GeneralMain.OnSearchQuerySubmitted -= OnOnSearchDelayedQuerySubmitted;
            _queryHandler = false;
        }

        private void OnAiringsUpdated()
        {
            ResourceLocator.DispatcherAdapter.Run(() =>
            {
                foreach (var vm in AnimeItems.Take(12))
                    vm.RefreshTimeTillNextAirInBackground();
            });
        }

        private void OnOnSearchDelayedQuerySubmitted(string query)
        {
            RefreshList(true);
        }

        /// <summary>
        ///     Main refresh function
        /// </summary>
        /// <param name="searchSource">
        ///     If it's from search -> check if there's anuthing to update before refreshing.
        /// </param>
        /// <param name="fakeDelay">
        ///     To make app more responsive micro delays are good to trigger spinners and such.
        /// </param>
        /// <returns></returns>
        public async void RefreshList(bool searchSource = false, bool fakeDelay = false)
        {
            //await Task.Run(() =>
            //{
            var query = ViewModelLocator.GeneralMain.CurrentSearchQuery;

            var queryCondition = !string.IsNullOrWhiteSpace(query) && query.Length > 1;
            if (!WasPreviousQuery && searchSource && !queryCondition)
                // refresh was requested from search but there's nothing to update
            {
                return;
            }
            if (!queryCondition)
            {
                _prevQuery = null;
                _invalidatePreviousSearchResults = false;
            }
            else
            {
                query = query.Trim();
            }

            if(queryCondition && !WasPreviousQuery)
                SetDesiredStatus((int)AnimeStatus.AllOrAiring);
            else if(!queryCondition && WasPreviousQuery)
                SetDesiredStatus((int)_prevAnimeStatus);

            WasPreviousQuery = queryCondition;


            var status = GetDesiredStatus();

            IEnumerable<AnimeItemAbstraction> items;
            if (queryCondition && !_invalidatePreviousSearchResults &&
                WasPreviousQuery &&
                !string.IsNullOrEmpty(_prevQuery) &&
                query.Length > _prevQuery.Length &&
                query.Substring(0, _prevQuery.Length-1) == _prevQuery) //use previous results if query is more detailed
                items = _animeItemsSet.Union(AnimeItems.Select(model => model.ParentAbstraction));
            else
                switch (WorkMode)
                {
                    case AnimeListWorkModes.Anime:
                        items = _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions;
                        break;
                    case AnimeListWorkModes.SeasonalAnime:
                    case AnimeListWorkModes.TopAnime:
                    case AnimeListWorkModes.AnimeByGenre:
                    case AnimeListWorkModes.AnimeByStudio:
                        items = _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems;
                        break;
                    case AnimeListWorkModes.Manga:
                        items = _animeLibraryDataStorage.AllLoadedMangaItemAbstractions;
                        break;
                    case AnimeListWorkModes.TopManga:
                    case AnimeListWorkModes.MangaAdapted:
                        items = _animeLibraryDataStorage.AllLoadedSeasonalMangaItems;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            if(queryCondition)
                _prevQuery = query;
            _animeItemsSet.Clear();

            items = items.Where(item => status == AnimeStatus.AllOrAiring || item.MyStatus == status || (item.IsRewatching && status == AnimeStatus.Watching));

            if (HideNotAired)
                items = items.Where(item => item.AirDay == -1 && item.AllEpisodes != 0);

            if (!queryCondition)
                _prevAnimeStatus = status;

            if (queryCondition)
            {
                query = query.ToLower();
                bool alreadyFiltered = false;
                if (MainViewModelBase.AnimeMediaTypeHints.Contains(query))
                {
                    var type = 0;
                    if (query == "tv")
                        type = (int)AnimeType.TV;
                    else if (query == "movie")
                        type = (int)AnimeType.Movie;
                    else if (query == "special")
                        type = (int)AnimeType.Special;
                    else if (query == "ova")
                        type = (int)AnimeType.OVA;
                    items = items.Where(item => item.Type == type);
                    alreadyFiltered = true;
                }
                else if (MainViewModelBase.MangaMediaTypeHints.Contains(query))
                {
                    var type = 0;
                    if (query == "manga")
                        type = (int)MangaType.Manga;
                    else if (query == "novel")
                        items = items.Where(item =>
                            item.Type == (int)MangaType.Novel || item.Type == (int)MangaType.LightNovel);
                    if (query != "novel")
                        items = items.Where(item => item.Type == type);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query,@"^ep>\d+$"))
                {
                    var eps = GetNumberFromQuery('>');
                    items = items.Where(item => item.AllEpisodes > eps);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query,@"^ep<\d+$"))
                {
                    var eps = GetNumberFromQuery('<');
                    items = items.Where(item => item.AllEpisodes < eps);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query,@"^ep=\d+$"))
                {
                    var eps = GetNumberFromQuery('=');
                    items = items.Where(item => item.AllEpisodes == eps);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query,@"^score>\d+$"))
                {
                    var score = GetNumberFromQuery('>');
                    items = items.Where(item => item.MyScore > score);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query, @"^score<\d+$"))
                {
                    var score = GetNumberFromQuery('<');
                    items = items.Where(item => item.MyScore < score);
                    alreadyFiltered = true;
                }
                else if (Regex.IsMatch(query, @"^score=\d+$"))
                {
                    var score = GetNumberFromQuery('=');
                    items = items.Where(item => item.MyScore == score);
                    alreadyFiltered = true;
                }

                int GetNumberFromQuery(char separator) => int.Parse(query.Split(separator).Last());


                _invalidatePreviousSearchResults = alreadyFiltered; //mangaa will not yield anything more manga
                if (!alreadyFiltered)
                {
                    try
                    {
                        if (ViewModelLocator.GeneralMain.SearchHints.Count > 0) //if there are any tags to begin with
                            items = items.Where(item => item.Title.ToLower().Contains(query.ToLower()) || item.Tags.Select(s => s.ToLower()).Contains(query.ToLower()));
                        else
                            items = items.Where(item => item.Title.ToLower().Contains(query.ToLower()));
                    }
                    catch (Exception e)
                    {
                        //ResourceLocator.TelemetryProvider.TrackException(e);
                    }

                }

            }
            if (WorkMode == AnimeListWorkModes.TopAnime || WorkMode == AnimeListWorkModes.TopManga)
                // Items that carry no rank (some rows in the API come back without one, notably a lot
                // of the Music type) keep their Index at zero instead of being told the list put them
                // top. OrderBy alone would send those zeros to the very top of the panel, which is why
                // Music entries were showing as numbers one and two on the Airing tab. Send them to
                // the very end, where they belong when the source cannot rank them.
                items = items.OrderBy(item => item.Index > 0 ? item.Index : int.MaxValue);
            else
                switch (SortOption)
                {
                    case SortOptions.SortTitle:
                        items = items.OrderBy(item => item.Title);
                        break;
                    case SortOptions.SortScore:
                        if (WorkMode != AnimeListWorkModes.SeasonalAnime)
                            items = items.OrderBy(item => item.MyScore).ThenByDescending(item => item.Title);
                        else
                            // Index, not GlobalScore, and this is not a preference. Index is the row's
                            // position in the season, which is fixed the moment the source answers, so
                            // the grid does not move when the rest of the pages land. GlobalScore was
                            // the alternative and it shuffled the list under the finger: the first
                            // page painted with whatever scores the shared cache already had, and the
                            // full result then re-sorted by the real ones. The score is still shown on
                            // the card, so nothing is lost by ordering on the stable key.
                            items = items.OrderBy(item => item.Index);
                        break;
                    case SortOptions.SortWatched:
                        if (WorkMode == AnimeListWorkModes.SeasonalAnime || WorkMode == AnimeListWorkModes.AnimeByStudio || WorkMode == AnimeListWorkModes.AnimeByGenre)
                            items = items.OrderBy(item => item.Index);
                        else
                            items = items.OrderBy(item => item.MyEpisodes);
                        break;
                    case SortOptions.SortLastWatched:
                        items = items.OrderBy(abstraction => abstraction.LastWatched);
                        break;
                    case SortOptions.SortPriority:
                        items = items.OrderBy(abstraction => abstraction.Priority);
                        break;
                    case SortOptions.SortNothing:
                        break;
                    case SortOptions.SortAirDay:
                        var today = (int) DateTime.Now.DayOfWeek;
                        today++;
                        var nonAiringItems = items.Where(abstraction => abstraction.AirDay == -1);
                        var airingItems = items.Where(abstraction => abstraction.AirDay != -1);
                        var airingAfterToday = airingItems.Where(abstraction => abstraction.AirDay >= today);
                        var airingBeforeToday = airingItems.Where(abstraction => abstraction.AirDay < today);
                        if (SortDescending)
                            items =
                                airingAfterToday.OrderByDescending(abstraction => today - abstraction.AirDay)
                                    .Concat(
                                        airingBeforeToday.OrderByDescending(
                                                abstraction => today - abstraction.AirDay)
                                            .Concat(nonAiringItems));
                        else
                            items =
                                airingBeforeToday.OrderBy(abstraction => today - abstraction.AirDay)
                                    .Concat(
                                        airingAfterToday.OrderBy(abstraction => today - abstraction.AirDay)
                                            .Concat(nonAiringItems));

                        break;
                    case SortOptions.SortStartDate:
                        items = items.OrderBy(abstraction => abstraction.MyStartDate);
                        break;
                    case SortOptions.SortEndDate:
                        items = items.OrderBy(abstraction => abstraction.MyEndDate);
                        break;
                    case SortOptions.SortSeason:
                        var itemsWithStartDate = new List<AnimeItemAbstraction>();
                        var itemsWithoutStartDate = new List<AnimeItemAbstraction>();
                        foreach (var item in items)
                        {
                            if (string.IsNullOrEmpty(item.AirStartDate))
                            {
                                itemsWithoutStartDate.Add(item);
                            }
                            else
                            {
                                itemsWithStartDate.Add(item);
                            }
                        }
                        if (SortDescending)
                        {
                            try
                            {
                                itemsWithStartDate =
                                    itemsWithStartDate.OrderByDescending(
                                            item => int.Parse(item.AirStartDate.Substring(0, 4)))
                                        .ThenByDescending(item => Utils.Utilities.DateToSeason(item.AirStartDate))
                                        .ThenBy(item => item.Title)
                                        .ToList();
                                items = itemsWithStartDate.Concat(itemsWithoutStartDate.OrderBy(item => item.Title));
                            }
                            catch
                            {
                                items = itemsWithStartDate.Concat(itemsWithoutStartDate.OrderBy(item => item.Title));
                            }
                        }
                        else
                        {
                            try
                            {
                                itemsWithStartDate =
                                    itemsWithStartDate.OrderBy(
                                            item => int.Parse(item.AirStartDate.Substring(0, 4)))
                                        .ThenBy(item => Utils.Utilities.DateToSeason(item.AirStartDate))
                                        .ThenBy(item => item.Title)
                                        .ToList();
                                items = itemsWithStartDate.Concat(itemsWithoutStartDate.OrderBy(item => item.Title));
                            }
                            catch
                            {
                                items = itemsWithStartDate.Concat(itemsWithoutStartDate.OrderBy(item => item.Title));
                            }

                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(SortOption), SortOption, null);
                }
            //If we are descending then reverse order
            // Seasonal is excluded, along with the two it always was. Its descending default exists to
            // mean "best score first", but the order now comes from the season's own ranking, and
            // reversing it would put the last entry of the season at the top of the grid.
            if (SortDescending && SortOption != SortOptions.SortAirDay && SortOption != SortOptions.SortSeason
                && WorkMode != AnimeListWorkModes.SeasonalAnime)
                items = items.Reverse();
            //Add all abstractions to current set (spread across pages)
            // RefreshList always rebuilds the whole list (init, entry change, sort order, a
            // forced refresh) and UpdatePageSetup then replaces AnimeItems with a fresh
            // collection, so the queue has to start empty to match it. It used to be appended
            // to, which left anything still queued from the previous entry at the front, and
            // that stale item was drained into the new grid first: a manga card briefly showed
            // up in the anime list and then vanished when the rebuild finished.
            _animeItemsSet = new List<AnimeItemAbstraction>();
            _animeItemsSet.AddRange(items);
            _drainWholeList = true;

            // Recorded here, at the one point every reuse of this page goes through, so the early
            // return in FetchDataCore can tell "already on screen" from "already fetched". A panel
            // repainting this grid is exactly what invalidates the fetch memo.
            _displayedMode = WorkMode;
            _displayedStatus = (int)status;
            RaisePropertyChanged(nameof(DisplayedMode));

            //});
            //If we have items then we should hide EmptyNotice
            EmptyNoticeVisibility = _animeItemsSet.Count == 0;

            UpdatePageSetup();
            UpdateUpperStatus();
        }

        /// <summary>
        ///     Sets provided sort mode or takes one from settings
        /// </summary>
        /// <param name="option"></param>
        public void SetSortOrder(SortOptions? option)
        {
            if (option == null)
            {
                var saved = WorkMode == AnimeListWorkModes.Manga ? Settings.MangaSortOrder : Settings.AnimeSortOrder;
                if (WorkMode == AnimeListWorkModes.SeasonalAnime && saved == SortOptions.SortTitle)
                    option = SortOptions.SortScore;
                else
                    option = saved;
            }
            if (Settings.AutoDescendingSorting && option != null)
            {
                switch (option)
                {
                    case SortOptions.SortTitle:
                        _sortDescending = false;
                        break;
                    case SortOptions.SortScore:
                        _sortDescending = true;
                        break;
                    case SortOptions.SortWatched:
                        _sortDescending = WorkMode == AnimeListWorkModes.Anime || WorkMode == AnimeListWorkModes.Manga;
                        break;
                    case SortOptions.SortAirDay:
                        _sortDescending = true;
                        break;
                    case SortOptions.SortLastWatched:
                        _sortDescending = true;
                        break;
                    case SortOptions.SortStartDate:
                        _sortDescending = false;
                        break;
                    case SortOptions.SortEndDate:
                        _sortDescending = true;
                        break;
                    case SortOptions.SortNothing:
                        break;
                    case SortOptions.SortSeason:
                        _sortDescending = true;
                        break;
                    case SortOptions.SortPriority:
                        _sortDescending = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                SortingSettingChanged?.Invoke(option.Value,_sortDescending);
            }
            SortOption = option.Value;
        }

        private void SetDefaults(int? statusOverride = null)
        {
            SetSortOrder(null);
            if (statusOverride == null)
                SetDesiredStatus(null);
            else
                StatusSelectorSelectedIndex = statusOverride.Value;
            if (!Settings.AutoDescendingSorting)
                SortDescending = WorkMode == AnimeListWorkModes.Manga
                    ? Settings.IsMangaSortDescending
                    : Settings.IsSortDescending;
        }

        private async void LoadMore()
        {
            // Single flight. This is async void driven by scroll events, so several copies run at once
            // and each one advances the page counter on its own: the log showed the panel walking
            // 1, 2, 6, 10, 12 within milliseconds while the fetches themselves were all refused by the
            // in-progress guard further down. The pages were burned without a single row arriving,
            // which is what the "stops at fifty" and "stops at seventy five" reports were.
            //
            // Re-entrant calls are dropped rather than queued: the one already running will bring in
            // everything it was asked for, and the scroll fires again the moment it lands.
            if (_loadingMore)
                return;
            _loadingMore = true;
            try
            {
                await LoadMoreCore();
            }
            finally
            {
                _loadingMore = false;
            }
        }

        private bool _loadingMore;

        private async Task LoadMoreCore()
        {
            if (_animeItemsSet.Count > 0)
            {
                var count = Math.Max(10, GetGridItemsToLoad());
                var items = _animeItemsSet.Take(count).ToList();
                _animeItemsSet.RemoveRange(0, items.Count);
                AnimeItems.AddRange(items.Select(item => item.ViewModel));
                PrefetchAirTimesAsync();
                return;
            }

            if (!CanLoadMore || CurrentPage >= MaxPage)
            {
                CanLoadMore = false;
                return;
            }
            var prevCount = AnimeItems.Count + _animeItemsSet.Count;

            // Pull a screenful at a time rather than a page at a time. A page of the top lists is
            // twenty-five rows, and requesting them one by made the panel feel jerky because the load
            // finished before the scroll stopped. Loading several pages per threshold meant the grid
            // had content to show while the next request was still being decided.
            var added = 0;
            var iterations = 0;
            const int FullPage = 25;
            while (CanLoadMore && CurrentPage < MaxPage && added < FullPage * 2 && iterations < 4)
            {
                CurrentPage++;
                await FetchSeasonalData(true, CurrentPage);
                var nowCount = AnimeItems.Count + _animeItemsSet.Count;
                added += Math.Max(0, nowCount - prevCount);
                prevCount = nowCount;
                iterations++;
            }
            CanLoadMore = CurrentPage < MaxPage &&
                          WorkMode.GetAttribute<EnumUtilities.AnimeListWorkModeEnumMember>().AllowLoadingMore;
        }

        /// <summary>
        ///     How deep the paginated panels may go.
        /// </summary>
        /// <remarks>
        ///     Twelve pages, roughly three hundred rows against a source that answers twenty five to
        ///     a page. Past that the append made the grid grow too tall and it started blocking. The
        ///     General ranking alone has a thousand one hundred and nine pages, so this is a chosen
        ///     stopping point rather than the end of the data.
        ///     <para>
        ///     Pages are only read as the user scrolls, so this bounds how far they can scroll and not
        ///     what gets downloaded: nothing is fetched until the list asks for the next page.
        ///     </para>
        /// </remarks>
        internal const int MaxPage = 12;

        public void UpdateGridItemWidth(Tuple<Tuple<double,double>, Tuple<double, double>> args)
        {
            //prevwirdth - curr width || prevHeight - currHeight
            if(args.Item1.Item1 - args.Item1.Item2 < -600 || args.Item2.Item1 - args.Item2.Item2 < -350)
                if(ViewModelLocator.AnimeList.AreThereItemsWaitingForLoad)
                    ViewModelLocator.AnimeList.RefreshList();
            if (DisplayMode == AnimeListDisplayModes.IndefiniteList)
                RaisePropertyChanged(() => ListItemGridWidth);
        }

        #region Pagination

        private bool WasPreviousQuery
        {
            get { return _wasPreviousQuery; }
            set
            {
                _wasPreviousQuery = value;
                RaisePropertyChanged(() => CanLoadMore);
            }
        }

        /// <summary>
        ///     This method is fully responsible for preparing the view.
        ///     Depending on display mode it distributes items to right containers.
        /// </summary>
        private void UpdatePageSetup()
        {
            // Clear the collection the grid is already bound to instead of handing it a brand
            // new one. Replacing ItemsSource tears down and rebuilds every cell, and while a
            // refresh was doing that a stray card with no poster appeared at the end of the
            // grid and then vanished when the rebuild finished.
            // Only clear when what is already on screen is NOT the head of the queue about to be drained.
            // A paginated panel hands over the whole list every time a page lands, so the rows already
            // drawn sit at the head of the queue. Clearing and re-draining them is what the grid read
            // as a full reload: the adapter is dropped and every visible cell is rebuilt, which is the
            // flash over posters that had not changed. Skipping them appends only the new rows.
            //
            // Verified against the queue rather than a flag, so it cannot be wrong about the list
            // state: a refresh, a different panel or a different filter all fail the comparison and
            // take the original clear-and-rebuild path exactly as before.
            var queued = _animeItemsSet;
            var skip = 0;
            if (AnimeItems == null)
                AnimeItems = new SmartObservableCollection<AnimeItemViewModel>();
            else if (AreDrawnRowsTheHeadOf(queued.Select(abstraction => abstraction.ViewModel).ToList()))
                skip = AnimeItems.Count;
            else
                AnimeItems.Clear();
            _lastOffset = 0;
            RaisePropertyChanged(() => DisplayMode);
            var minItems = GetGridItemsToLoad();
            if (CurrentIndexPosition == LastIndexPositionOnRefresh)
                // Counted before the drain, and only for a rebuild. While extending, the arithmetic
                // included the rows already on screen and pointed past the end of the list, so the
                // grid was asked to scroll into a row that does not exist and lost its position.
                CurrentIndexPosition = skip > 0 ? -1 : _animeItemsSet.Count + AnimeItems.Count - 1;
            minItems = minItems < 10 ? 10 : minItems;
            var minimumIndex = CurrentIndexPosition == -1
                ? minItems
                : CurrentIndexPosition + 1 <= minItems ? minItems : CurrentIndexPosition + 1;
            // A refresh hands over the complete list, so leaving part of it queued only made
            // the grid show a shorter list and then grow it back, which is what pushed the
            // last card into view for a moment on every pull to refresh.
            if (_drainWholeList)
                minimumIndex = _animeItemsSet.Count;
            // While extending, the rows already drawn are skipped rather than taken again, and
            // everything the page brought is drained at once so the next page can start fetching.
            if (skip > 0)
                minimumIndex = _animeItemsSet.Count;
            var allItems = _animeItemsSet.Count;
            var toTake = skip > 0 ? queued.Skip(skip) : queued.Take(minimumIndex);
            switch (DisplayMode)
            {
                case AnimeListDisplayModes.IndefiniteCompactList:
                case AnimeListDisplayModes.IndefiniteList:
                case AnimeListDisplayModes.IndefiniteGrid:
                    AnimeItems.AddRange(toTake.Select(abstraction => abstraction.ViewModel).ToList());
                    _animeItemsSet = skip > 0
                        ? new List<AnimeItemAbstraction>()
                        : queued.Skip(minimumIndex).ToList();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            RaisePropertyChanged(() => AnimeItems);
            AddScrollHandler();
            PrefetchAirTimesAsync();
            if (CurrentIndexPosition != -1)
            {
                try
                {
                    ScrollIntoViewRequested?.Invoke(AnimeItems[CurrentIndexPosition]);
                }
                catch (Exception)
                {
                    //no index
                }
                CurrentIndexPosition = -1;
            }
            ViewModelLocator.GeneralMain.ScrollToTopButtonVisibility = CurrentIndexPosition > minItems;
            // While the list is only growing, CurrentPage belongs to LoadMore and must not be
            // recomputed here. It was derived from the size of the queue, which while extending no
            // longer grows the way the formula assumes: the counter kept reading back the page it had
            // already fetched, LoadMore asked for that same page again, got the rows it already had,
            // and the list stopped where it was (observed at two hundred rows, eight pages).
            if (skip == 0)
                CurrentPage = (int) Math.Ceiling((double) allItems /
                                                 (WorkMode == AnimeListWorkModes.AnimeByGenre ||
                                                  WorkMode == AnimeListWorkModes.AnimeByStudio
                                                     ? ItemsPerGenereStudioPage
                                                     : ItemsPerPage));
            Loading = false;
            _randomedIds = new List<int>();
            _drainWholeList = false;
        }

        /// <summary>
        ///     Asks for the air time of the first rows, so they carry a date before they are scrolled to.
        /// </summary>
        /// <remarks>
        ///     It used to walk the first twelve rows on EVERY page that landed, which meant the same
        ///     dozen titles were re-asked twelve times over while the list grew, and each miss went out
        ///     to the network. That is the stutter: the requests pile up on the same rows while the
        ///     grid is trying to draw. The grid's own warm window already walks the rows as they are
        ///     scrolled, so this only has to seed the very top, and only once per list.
        /// </remarks>
        private bool _airTimesPrefetched;

        private void PrefetchAirTimesAsync()
        {
            if (_airTimesPrefetched)
                return;
            _airTimesPrefetched = true;

            var visible = AnimeItems.Take(12)
                .Where(item => item.ParentAbstraction.RepresentsAnime && string.IsNullOrEmpty(item.TimeTillNextAirCache))
                .ToList();
            if (visible.Count == 0)
                return;
            foreach (var item in visible)
                item.RefreshTimeTillNextAirInBackground();
        }
        private int GetGridItemsToLoad()
        {
            if (DimensionsProvider?.ActualHeight < 0 && DimensionsProvider?.ActualWidth < 0)
                return int.MaxValue; //load all
            var width = DimensionsProvider?.ActualWidth ?? 1920;
            var height = DimensionsProvider?.ActualHeight ?? 1080;
            if (width == 0 || height == 0)
            {
                width = 1920;
                height = 1080; //because
            }
            switch (DisplayMode)
            {
                case AnimeListDisplayModes.IndefiniteCompactList:
                    return (int)Math.Ceiling(height / 50) + 2;
                case AnimeListDisplayModes.IndefiniteList:
                    return (int)Math.Ceiling(width / ListItemGridWidth * height / 170) + 2;
                case AnimeListDisplayModes.IndefiniteGrid:
                    return (int)Math.Ceiling(width / 200 * height / 300) + 2; //2 for good measure
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }

        #endregion

        #region CacheManip

        public void OnAnimeEntryRemoved(AnimeItemAbstraction parentAbstraction)
        {
            if (AnimeItems.Contains(parentAbstraction.ViewModel))
                AnimeItems.Remove(parentAbstraction.ViewModel);
        }

        #endregion

        #region IndefiniteScrollerino

        private int _lastOffset;

        /// <summary>
        ///     Event handler for event fired by one of two scroll viewrs in List and Grid view mode.
        ///     It loads more items as user is scroling further.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public void IndefiniteScrollViewerOnViewChanging(double finalVerticalOffset)
        {
            var offset = (int)finalVerticalOffset;
            ViewModelLocator.GeneralMain.ScrollToTopButtonVisibility = offset > 300 ? true : false;
            if (_animeItemsSet.Count == 0)
                return;
            //Depending on display mode we load more or less items.
            //This is the place where offset thresholds are defined
            if (offset - _lastOffset >
                (DisplayMode == AnimeListDisplayModes.IndefiniteList
                    ? 75
                    : (DisplayMode == AnimeListDisplayModes.IndefiniteCompactList ? 50 : 100)) ||
                (DisplayMode == AnimeListDisplayModes.IndefiniteList && _animeItemsSet.Count == 1) ||
                (DisplayMode == AnimeListDisplayModes.IndefiniteGrid && _animeItemsSet.Count <= 2))
            {
                _lastOffset = offset;
                int itemsCount;
                switch (DisplayMode)
                {
                    case AnimeListDisplayModes.IndefiniteList:
                        itemsCount = (int)DimensionsProvider.ActualWidth/200;
                        AnimeItems.AddRange(_animeItemsSet.Take(itemsCount).Select(abstraction => abstraction.ViewModel));
                        _animeItemsSet = _animeItemsSet.Skip(itemsCount).ToList();
                        break;
                    case AnimeListDisplayModes.IndefiniteGrid:
                        itemsCount = (int)DimensionsProvider.ActualWidth / 160;
                        AnimeItems.AddRange(_animeItemsSet.Take(itemsCount).Select(abstraction => abstraction.ViewModel));
                        _animeItemsSet = _animeItemsSet.Skip(itemsCount).ToList();
                        break;
                    case AnimeListDisplayModes.IndefiniteCompactList:
                        itemsCount = (int)DimensionsProvider.ActualHeight / 50;
                        AnimeItems.AddRange(_animeItemsSet.Take(itemsCount).Select(abstraction => abstraction.ViewModel));
                        _animeItemsSet = _animeItemsSet.Skip(itemsCount).ToList();
                        break;
                }
            }
        }

        /// <summary>
        ///     Adds handler to scroll viewer provided by view.
        /// </summary>
        private void AddScrollHandler()
        {
            if (!CanAddScrollHandler || _scrollHandlerAdded)
                return;
            _lastOffset = 0; //we are resseting this because we ARE on the very to of the list view when adding handler
            _scrollHandlerAdded = true;
            try
            {
                AddScrollHandlerRequest?.Invoke();
            }
catch (Exception)
                        {
                            //we didn't get scroll handler -> add all items
                            AnimeItems.AddRange(_animeItemsSet.Select(abstraction => abstraction.ViewModel));
                            _animeItemsSet.Clear();
                        }
        }

        /// <summary>
        ///     Scrolls to top of current indefinite scroll viewer.
        /// </summary>
        public  void ScrollToTop()
        {
            ScrollToTopRequest?.Invoke();
        }

        #endregion

        #region FetchAndPopulate

        /// <summary>
        ///     Fetches seasonal data and top manga/anime.
        ///     Results are saved in appropriate containers for further operations.
        /// </summary>
        /// <param name="force"></param>
        /// <returns></returns>
        private bool _fetchingSeasonal;

        /// <summary>
        ///     Fetches the seasonal panels.
        /// </summary>
        /// <remarks>
        ///     The concurrent guard used to be an unconditional early return, which quietly threw away
        ///     the request that arrived while one was running: switching season fast enough left the
        ///     grid holding whichever fetch happened to be in progress rather than the one asked for.
        ///     A caller that can be superseded passes a token and is cancelled instead; only a caller
        ///     with no such token still defers.
        /// </remarks>
        private async Task FetchSeasonalData(bool force = false, int page = 0,
            CancellationToken cancellationToken = default)
        {
            if (_fetchingSeasonal && !cancellationToken.CanBeCanceled)
                return;
            _fetchingSeasonal = true;
            try
            {
                await FetchSeasonalDataCore(force, page, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer season. Nothing to report: the season that cancelled this one
                // is already loading, and an error here would be reported on top of it.
            }
            catch (Exception ex)
            {
                LoadError = "Could not load this list. Pull down to try again.";
                LoadErrorVisibility = true;
                Console.WriteLine("MALPLUS season error: " + ex);
            }
            finally
            {
                // Same as FetchData: an unhandled exception here used to leave the
                // guard latched and the spinner on forever.
                _fetchingSeasonal = false;
                if (AnimeItems == null || AnimeItems.Count == 0)
                    Loading = false;
            }
        }

        /// <summary>
        ///     Puts an exact season on the Seasonal panel and reloads it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Init cannot be used to change the season. Every mode whose args do not carry the full
        ///     navigation set goes through the branch that resets the season to null, and the fetch
        ///     that follows then falls back to whatever is airing now, so a season chosen inside the
        ///     panel was silently discarded and the grid never changed.
        ///     </para>
        ///     <para>
        ///     Setting the season before asking for the data is what makes it stick: the fetch takes
        ///     the season as it finds it, and the index load underneath is a no-op once the list is
        ///     populated, which is the case for every season change after the first open.
        ///     </para>
        /// </remarks>
        public async Task ShowSeasonAsync(AnimeSeason season)
        {
            if (season == null || WorkMode != AnimeListWorkModes.SeasonalAnime)
                return;

            Console.WriteLine(
                $"MALPLUS season: asked for {season.Name} ({season.Season} {season.Year}) was {CurrentSeason?.Name}");

            CurrentSeason = season;
            if (SeasonSelection.Count == 0)
                await LoadSeasonSelection(false);

            // Whatever season was still loading is now the wrong answer, and its pages are sitting in
            // the client's queue ahead of the new ones. That is what piled up: a season is eight or
            // nine requests, so flipping through several leaves dozens queued and the season you end
            // on waits for all of them. Cancelling the previous one drops them immediately and lets
            // this season start now.
            _seasonRequest?.Cancel();
            _seasonRequest?.Dispose();
            _seasonRequest = new CancellationTokenSource();
            var token = _seasonRequest.Token;

            // The wipe cannot go through ResetForInit. That decides by comparing ListSource, work
            // mode and status, and none of the three change when only the season does, so it took the
            // "nothing to redo" branch and left the queue alone. Items reach the grid through
            // _animeItemsSet, which UpdatePageSetup drains, so the new season's items were appended
            // behind the previous season's and the grid never visibly changed.
            //
            // Wiped directly, and the request guard is reset with it: that guard is what made Top
            // Anime stop answering too, because the page keeps one view model and a season left
            // loaded in it counted as "already fetched this" for whatever came next.
            _pendingWipe = false;
            _animeItemsSet = new List<AnimeItemAbstraction>();
            AnimeItems = new SmartObservableCollection<AnimeItemViewModel>();
            _prevWorkMode = (AnimeListWorkModes)(-1);
            _prevListSource = null;
            _prevStatus = -1;
            // Cleared so a cancelled fetch never paints over the season that replaced it.
            _seasonFirstPageShown = false;
            _seasonGeneration++;

            await FetchSeasonalData(false, 0, token);
        }

        private CancellationTokenSource _seasonRequest;

        /// <summary>
        ///     Bumped on every season change. A fetch that was still running compares it before it
        ///     touches anything, so a slow season cannot paint over a newer one.
        /// </summary>
        private int _seasonGeneration;

        private async Task FetchSeasonalDataCore(bool force, int page, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadError = null;
            LoadErrorVisibility = false;

            Loading = true;
            EmptyNoticeVisibility = false;
            var setDefaultSeason = false;
            if (CurrentSeason == null)
            {
                CurrentSeason = new AnimeSeason {Name = "Airing" };
                setDefaultSeason = true;
            }
            await LoadSeasonSelection(setDefaultSeason);
            //get top or seasonal anime
            var data = new List<ISeasonalAnimeBaseData>();
            // Which season this fetch belongs to. Taken before anything is awaited, because the
            // point is to compare against whatever the panel is showing when the fetch comes back.
            var generation = _seasonGeneration;
            page = page == 0 ? 1 : page;
            switch (WorkMode)
            {
                case AnimeListWorkModes.SeasonalAnime:
                    List<SeasonalAnimeData> tResponse = null;
                    // Paints the first page while the rest of the season is still on its way. A season
                    // is eight or nine requests, and holding the grid until the last one landed was
                    // the entire wait: the first page already holds the entries worth showing, since
                    // the source returns them ordered. Whatever arrives late is added after, so a
                    // stalled page costs a shorter list rather than an empty screen.
                    var seasonQuery = new AnimeSeasonalQuery(CurrentSeason);
                    seasonQuery.FirstPageReady = batch =>
                        ShowFirstSeasonPageAsync(batch, generation, cancellationToken);
                    seasonQuery.RequestCancelled = () => cancellationToken.IsCancellationRequested;
                    await Task.Run(async () => tResponse = await seasonQuery.GetSeasonalAnime(force));
                    cancellationToken.ThrowIfCancellationRequested();
                    data.AddRange(tResponse ?? new List<SeasonalAnimeData>());
                    break;
                case AnimeListWorkModes.TopAnime:
                case AnimeListWorkModes.TopManga:
                    List<TopAnimeData> topResponse = null;
                    // A band is asked for on page one only and brings the whole range back. Handing it
                    // any other page number would ask for a slice of pages that the band reader ignores
                    // anyway, but passing the real page keeps the filtered tabs below it working off
                    // exactly the same call.
                    var topPage = HasBandRange ? 1 : page - 1;
                    var topRange = HasBandRange ? TopRangeIndex : -1;
                    await Task.Run(async () =>
                        topResponse = WorkMode == AnimeListWorkModes.TopAnime
                            ? await new AnimeTopQuery(TopAnimeWorkMode, topPage, topRange).GetTopAnimeData(force)
                            : await new AnimeTopQuery(MangaTopWorkMode, topPage, topRange).GetTopAnimeData(force));
                    data.AddRange(topResponse ?? new List<TopAnimeData>());
                    break;
                case AnimeListWorkModes.MangaAdapted:
                    List<TopAnimeData> adaptedResponse = null;
                    await Task.Run(async () =>
                        adaptedResponse = await new AnimeAdaptedToAnimeQuery(MangaAdaptedWorkMode).GetAdaptedToAnimeData(force));
                    data.AddRange(adaptedResponse ?? new List<TopAnimeData>());
                    break;
                case AnimeListWorkModes.AnimeByGenre:
                case AnimeListWorkModes.AnimeByStudio:
                    var query = WorkMode == AnimeListWorkModes.AnimeByStudio
                        ? new AnimeGenreStudioQuery(Studio, page)
                        : new AnimeGenreStudioQuery(Genre, page);
                    List<AnimeGeneralDetailsData> gResponse = null;
                    await Task.Run(async () => gResponse = await query.GetAnime());
                    int gIndex = (page - 1) * 25 + 1;
                    foreach (var gItem in gResponse ?? new List<AnimeGeneralDetailsData>())
                        data.Add(new SeasonalAnimeData
                        {
                            Id = gItem.MalId,
                            Title = gItem.Title,
                            ImgUrl = gItem.ImgUrl,
                            Episodes = gItem.AllEpisodes.ToString(),
                            Score = gItem.GlobalScore,
                            Genres = gItem.Genres,
                            Index = gIndex++,
                            Type = (int) MalTypeParser.ParseAnimeType(gItem.Type)
                        });
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            Console.WriteLine(
                $"MALPLUS panel: fetched {data.Count} rows for {PanelName()} ({WorkMode})");
                //if we don't have any we cannot do anything I guess...
                if (data.Count == 0)
                {
                    // Names the panel that actually failed. It used to say CurrentSeason?.Name, which
                    // on a Top panel is the placeholder the seasonal path leaves behind, so every Top
                    // filter reported "Could not load Airing" no matter which one was tapped.
                    bool hasItems = WorkMode == AnimeListWorkModes.TopAnime || WorkMode == AnimeListWorkModes.TopManga
                        ? _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems.Count > 0
                        : WorkMode == AnimeListWorkModes.MangaAdapted || WorkMode == AnimeListWorkModes.Manga
                            ? _animeLibraryDataStorage.AllLoadedSeasonalMangaItems.Count > 0
                            : _animeItems.Count > 0;
                    if (hasItems && page > 1)
                    {
                        // One of the pages came back empty after we had already populated the list.
                        // Do not blank the whole panel and do not show the error overlay: just stop
                        // asking for more. The user still sees everything we already had.
                        Console.WriteLine($"MALPLUS could-not-extend {PanelName()} ({WorkMode}) requested page={page} current={CurrentPage} after existing={_animeItems.Count} items");
                        CanLoadMore = false;
                        return;
                    }
                    LoadError = $"Could not load {PanelName()}. Pull down to try again.";
                    LoadErrorVisibility = true;
                    RefreshList();
                    Console.WriteLine(
                        $"MALPLUS could-not-load {PanelName()} ({WorkMode}) requested page={page} (CurrentPage={CurrentPage}) after {AnimeItems.Count} items from {WorkMode} cache");
                    return;
                }
                LoadError = null;
                LoadErrorVisibility = false;
            List<AnimeItemAbstraction> source;
            List<AnimeItemAbstraction> target;
            if (WorkMode == AnimeListWorkModes.TopManga || WorkMode == AnimeListWorkModes.MangaAdapted)
            {
                //We have to load base mnga item first if not loaded before.
                if (_animeLibraryDataStorage.AllLoadedMangaItemAbstractions.Count == 0 && !_attemptedMangaFetch)
                    await FetchData(false, AnimeListWorkModes.Manga);

                target = _animeLibraryDataStorage.AllLoadedSeasonalMangaItems = new List<AnimeItemAbstraction>();
                source = _animeLibraryDataStorage.AllLoadedAuthMangaItems.Count > 0 ? _animeLibraryDataStorage.AllLoadedAuthMangaItems : new List<AnimeItemAbstraction>();
            }
            else
            {
                if (WorkMode != AnimeListWorkModes.AnimeByGenre && WorkMode != AnimeListWorkModes.AnimeByStudio)
                    if (_animeLibraryDataStorage.AllLoadedAnimeItemAbstractions.Count == 0 && !_attemptedAnimeFetch)
                        await FetchData(false, AnimeListWorkModes.Anime);
                if ((WorkMode == AnimeListWorkModes.AnimeByGenre || WorkMode == AnimeListWorkModes.AnimeByStudio) && page > 1)
                {
                    target = _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems;
                }
                else
                {
                    target = _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems = new List<AnimeItemAbstraction>();
                }
                // The first page was already put on screen by the progressive render, so the full
                // result continues that list instead of starting a fresh one and blanking the grid
                // for a moment before repainting the same cards.
                if (_seasonFirstPageShown && WorkMode == AnimeListWorkModes.SeasonalAnime)
                {
                    target = _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems;
                    _seasonFirstPageShown = false;
                }
                source = _animeLibraryDataStorage.AllLoadedAuthAnimeItems.Count > 0 ? _animeLibraryDataStorage.AllLoadedAuthAnimeItems : new List<AnimeItemAbstraction>();

            }

            AppendSeasonRows(data, source, target, Settings.SelectedApiType == ApiType.Mal);

            _fetchingSeasonal = false;
            RefreshList();
            LoadError = null;
            LoadErrorVisibility = false;
        }

        private bool _seasonFirstPageShown;

        /// <summary>
        ///     Paints the first page of a season before the rest of it has arrived.
        /// </summary>
        /// <remarks>
        ///     Runs on a worker, so everything that touches a collection the grid binds to has to be
        ///     marshalled back. It deliberately uses the same row mounting as the full path: two
        ///     builders would drift, and the symptom of that drift is a list that changes its mind when
        ///     the remaining pages land.
        /// </remarks>
        private Task ShowFirstSeasonPageAsync(List<SeasonalAnimeData> firstPage, int generation,
            CancellationToken cancellationToken)
        {
            if (firstPage == null || firstPage.Count == 0)
                return Task.CompletedTask;

            try
            {
                ResourceLocator.DispatcherAdapter.Run(() =>
                {
                    // The season that asked for this page is no longer the one on screen, so painting
                    // it would overwrite the newer list with a superseded one.
                    if (WorkMode != AnimeListWorkModes.SeasonalAnime ||
                        generation != _seasonGeneration ||
                        cancellationToken.IsCancellationRequested)
                        return;
                    var target = _animeLibraryDataStorage.AllLoadedSeasonalAnimeItems =
                        new List<AnimeItemAbstraction>();
                    AppendSeasonRows(firstPage, null, target, Settings.SelectedApiType == ApiType.Mal);
                    _seasonFirstPageShown = true;
                    Loading = false;
                    RefreshList();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS season first page failed: " + ex.Message);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Turns fetched rows into the abstractions the grid binds to, in place.
        /// </summary>
        /// <remarks>
        ///     Shared by the full fetch and by the progressive first page so both produce the same
        ///     cards. A null source is the first-page case, where nothing may be reused.
        /// </remarks>
        private void AppendSeasonRows(IEnumerable<ISeasonalAnimeBaseData> data,
            List<AnimeItemAbstraction> source, List<AnimeItemAbstraction> target, bool updateScore)
        {
            foreach (var animeData in data)
            {
                try
                {
                    if (WorkMode == AnimeListWorkModes.SeasonalAnime && Settings.SelectedApiType == ApiType.Mal)
                        //seasonal anme comes with mal score, we don't want to polute hummingbird data
                        DataCache.RegisterVolatileData(animeData.Id, new VolatileDataCache
                        {
                            DayOfAiring = animeData.AirDay, GlobalScore = animeData.Score, Genres = animeData.Genres, AirStartDate = animeData.AirStartDate == AnimeItemViewModel.InvalidStartEndDate ? null : animeData.AirStartDate
                        });
                    // Seasonal always builds its own view. Every other mode here reuses the one already
                    // in the user's library so the card keeps the local progress and notes, and that
                    // reuse is what broke Seasonal:
                    //
                    //  - the reused AnimeItemViewModel is the SAME instance the grid already holds, so
                    //    clearing and re-adding the collection handed the recycled cell back its own
                    //    stale data. Only the anime that were new looked right, which is what made it
                    //    read as "some of them do not update".
                    //  - UpdateWithSeasonData only refreshes the score and the episode label. Title,
                    //    poster and episode count came from whichever season first created the view,
                    //    so an anime airing in two seasons kept the older one's title and image.
                    //
                    // Cost: the card shows that season's numbers and the local progress is not shown on
                    // this panel. The library itself is untouched, and Top Anime is left alone because
                    // its cards never move between panels while Seasonal does.
                    if (WorkMode == AnimeListWorkModes.SeasonalAnime || source == null)
                    {
                        target.Add(new AnimeItemAbstraction(animeData as SeasonalAnimeData, true));
                        continue;
                    }

                    AnimeItemAbstraction abstraction = null;
                    if (Settings.SelectedApiType == ApiType.Mal)
                        abstraction = source.FirstOrDefault(item => item.Id == animeData.Id);
                    else
                        abstraction = source.FirstOrDefault(item => item.MalId == animeData.Id);
                    // The same entry can arrive on a later page, and re-adding it is what
                    // multiplied the cards two-, three-, even four-fold. Top manga/anime merge
                    // every already loaded page into whatever comes back, so the panel has to
                    // hold each id once.
                    if (target.Any(a => a.Id == animeData.Id || (a.MalId != 0 && a.MalId == animeData.Id)))
                        continue;
                    if (abstraction == null)
                        target.Add(new AnimeItemAbstraction(animeData as SeasonalAnimeData, WorkMode != AnimeListWorkModes.TopManga && WorkMode != AnimeListWorkModes.MangaAdapted));
                    else
                    {
                        //AnimeAirDay
                        //if(animeData.AirDay != -1)
                        //    abstraction.AirDay = animeData.AirDay;
                        if (updateScore)
                            abstraction.GlobalScore = animeData.Score;
                        abstraction.Index = animeData.Index;
                        abstraction.ViewModel.UpdateWithSeasonData(animeData as SeasonalAnimeData, updateScore);
                        target.Add(abstraction);
                    }

                    // The rank chip is for Top Anime alone. Set here, on the abstraction that was just
                    // added, rather than from the grid: the view model is created lazily the first time
                    // something reads it, so enabling it later would be too late for a cell that had
                    // already bound an empty rank.
                    if (WorkMode == AnimeListWorkModes.TopAnime || WorkMode == AnimeListWorkModes.TopManga)
                        target[target.Count - 1].ViewModel.EnableRank();
                }
                catch (Exception e)
                {
                    // wat
                }
            }
        }

        /// <summary>
        ///     What to call this panel in a message, which is not always a season.
        /// </summary>
        /// <remarks>
        ///     The empty-list message is shown by every one of these panels and only Seasonal has a
        ///     season to name. Naming the others after CurrentSeason meant every Top filter reported
        ///     the same placeholder word, which is how "Could not load Airing" came up for all of them.
        /// </remarks>
        private string PanelName()
            => WorkMode switch
            {
                AnimeListWorkModes.SeasonalAnime => CurrentSeason?.Name ?? "this season",
                AnimeListWorkModes.TopAnime => TopAnimeWorkMode.ToString(),
                AnimeListWorkModes.TopManga => MangaTopWorkMode.ToString(),
                // Read straight off the enum rather than through the query's display helper: naming
                // the failure does not need the prose label, and reaching for that type here does not
                // compile from inside this file.
                AnimeListWorkModes.MangaAdapted => MangaAdaptedWorkMode.ToString(),
                AnimeListWorkModes.AnimeByGenre => Genre.ToString(),
                AnimeListWorkModes.AnimeByStudio => Studio.ToString(),
                _ => "this list"
            };

        /// <summary>
        ///     Fills <see cref="SeasonSelection" /> from the Tenrai seasons index, once. It already ran
        ///     as part of the seasonal init, so the page only calls this when the picker is opened on a
        ///     list that has not been through it, which costs one cached request rather than one per
        ///     open.
        /// </summary>
        public Task PopulateSeasonSelectionAsync()
            => LoadSeasonSelection(false);

        private async Task LoadSeasonSelection(bool setDefaultSeason)
        {
            if (WorkMode != AnimeListWorkModes.SeasonalAnime || SeasonSelection.Count != 0)
                return;

            SeasonSelection.Clear();
            SeasonYears.Clear();
            SeasonYears.Add(DateTime.UtcNow.Year.ToString());
            var currentSeasonName = $"{GetCurrentSeasonName()} {DateTime.UtcNow.Year}";
            try
            {
                var (items, _) = await TenraiClient.GetPaginatedAsync("seasons");

                foreach (var s in items.Take(3))
                {
                    var year = s.GetProperty("year").GetInt32();
                    var yearStr = year.ToString();
                    if (!SeasonYears.Contains(yearStr))
                        SeasonYears.Add(yearStr);
                    var seasons = s.GetProperty("seasons");
                    foreach (var yearSeason in seasons.EnumerateArray())
                    {
                        var seasonStr = yearSeason.GetString();
                        SeasonSelection.Add(new AnimeSeason
                        {
                            Name = $"{CapitalizeSeason(seasonStr)} {year}",
                            Year = year,
                            Season = seasonStr switch
                            {
                                "winter" => Season.Winter,
                                "spring" => Season.Spring,
                                "summer" => Season.Summer,
                                "fall" => Season.Fall,
                                _ => Season.Fall
                            }
                        });
                    }
                }
            }
            catch (Exception)
            {
            }

            if (SeasonSelection.Count == 0)
            {
                var currentYear = DateTime.UtcNow.Year;
                foreach (var seasonStr in new[] { "winter", "spring", "summer", "fall" })
                    SeasonSelection.Add(new AnimeSeason
                    {
                        Name = $"{CapitalizeSeason(seasonStr)} {currentYear}",
                        Year = currentYear,
                        Season = seasonStr switch
                        {
                            "winter" => Season.Winter,
                            "spring" => Season.Spring,
                            "summer" => Season.Summer,
                            _ => Season.Fall
                        }
                    });
            }

            var ordered = SeasonSelection
                .OrderByDescending(s => s.Year)
                .ThenByDescending(s => SeasonRank(s.Season))
                .ToList();
            SeasonSelection.Clear();
            foreach (var season in ordered)
            {
                season.IsCurrentSeason = season.Name == currentSeasonName;
                SeasonSelection.Add(season);
            }

            SeasonYears.Sort();
            SeasonYears.Reverse();

            var currSeasonIndex = SeasonSelection.FindIndex(s => s.Name == currentSeasonName);

            if (setDefaultSeason && currSeasonIndex != -1)
            {
                CurrentSeason = SeasonSelection[currSeasonIndex];
                _seasonalUrlsSelectedIndex = currSeasonIndex;
                RaisePropertyChanged(() => SeasonalUrlsSelectedIndex);
            }
        }

        private static string CapitalizeSeason(string season)
        {
            if (string.IsNullOrEmpty(season))
                return season;
            return char.ToUpperInvariant(season[0]) + season.Substring(1);
        }

        private static string GetCurrentSeasonName()
        {
            return DateTime.UtcNow.Month switch
            {
                <= 3 => "Winter",
                <= 6 => "Spring",
                <= 9 => "Summer",
                _ => "Fall"
            };
        }

        private static int SeasonRank(Season season)
        {
            return season switch
            {
                Season.Winter => 0,
                Season.Spring => 1,
                Season.Summer => 2,
                Season.Fall => 3,
                _ => 3
            };
        }

        /// <summary>
        ///     Forces currently loaded page to download new data.
        /// </summary>
        private async void ReloadList()
        {
            await RefreshListAsync();
        }

        /// <summary>
        ///     Re-reads the panel from the first page after the band under the cursor changed.
        /// </summary>
        /// <remarks>
        ///     Not the same as a refresh, and the difference matters. A refresh forces the read past
        ///     the file cache, which is right when the reader pulled the list down wanting the current
        ///     answer. Switching band is not that: the band being asked for has its own file, and on
        ///     the first visit it simply does not exist yet. Forcing would be harmless there, but it
        ///     also skips the live read on a band that has been read before, which is exactly where the
        ///     cached answer is worth replacing.
        /// </remarks>
        public async Task RefetchForRangeChange()
        {
            CurrentPage = 1;
            await FetchSeasonalData(false);
        }

        /// <summary>
        ///     The band this panel should show, given what the caller asked for and what the panel is.
        /// </summary>
        /// <remarks>
        ///     The two lists that ARE MAL's ranking always answer with a band, and they fall back to the
        ///     first one when the caller named none. That is the whole fix for the top lists crawling:
        ///     nothing used to ask for a band at all when a panel was opened from the menu, so these two
        ///     fell through to paging twenty five rows at a time behind an infinite scroll, forever, and
        ///     the bar meanwhile named a band the grid had never been asked for.
        ///     <para>
        ///     The filtered tabs get -1 whatever the caller said. They read a slice (Airing, TV,
        ///     Upcoming) whose rows keep their own global rank but have no place of their own to page
        ///     through, so there is no band to show and the selector stays hidden.
        ///     </para>
        /// </remarks>
        private int ResolveRangeIndex(int requested)
        {
            var isWholeRanking =
                WorkMode == AnimeListWorkModes.TopAnime && TopAnimeWorkMode == TopAnimeType.General ||
                WorkMode == AnimeListWorkModes.TopManga && MangaTopWorkMode == MangaTopType.All;
            if (!isWholeRanking)
                return -1;
            return requested >= 0 && requested < AnimeTopQuery.RangeCount ? requested : 0;
        }

        /// <summary>
        /// The pull to refresh gesture needs to know when the reload is over so it can put its
        /// spinner away. Going through RefreshCommand lost that: the command wraps an async void,
        /// so the caller had nothing to await and IsRefreshing stayed true for good, which is
        /// what left the arrow spinning at the top of the list forever.
        /// </summary>
        public async Task RefreshListAsync()
        {
            if (WorkMode == AnimeListWorkModes.SeasonalAnime || WorkMode == AnimeListWorkModes.TopAnime ||
                WorkMode == AnimeListWorkModes.TopManga || WorkMode == AnimeListWorkModes.MangaAdapted ||
                WorkMode == AnimeListWorkModes.AnimeByGenre ||
                WorkMode == AnimeListWorkModes.AnimeByStudio)
                await FetchSeasonalData(true);
            else
                await FetchData(true);
        }

        private bool _attemptedMangaFetch;
        private bool _attemptedAnimeFetch;

        /// <summary>
        ///     Feteches manga and anime data for currstnt ListSource.
        /// </summary>
        /// <param name="force">Forces downloading new data from MAL.</param>
        /// <param name="modeOverride">
        ///     When we are accessing deatils or top anime/manga without having it pulled we can use this
        ///     override to fetch this data and do nothing else with it.That way we will avoid situation where item is on user's
        ///     list
        ///     but it wasn't downloaded by the application.
        /// </param>
        /// <returns></returns>
        private bool _fetching;

        public async Task FetchData(bool force = false, AnimeListWorkModes? modeOverride = null)
        {
            if (_fetching)
            {
                // A fetch is already running. This call is not going to produce anything, so it must
                // not leave the caller with a spinner nobody will lower.
                if (modeOverride == null)
                    Loading = false;
                return;
            }
            _fetching = true;
            try
            {
                await FetchDataCore(force, modeOverride);
            }
            catch (Exception ex)
            {
                LoadError = "Could not load your list. Pull down to try again.";
                LoadErrorVisibility = true;
            }
            finally
            {
                _fetching = false;
                // Without this the loading indicator stays on forever after any failure.
                if (AnimeItems == null || AnimeItems.Count == 0)
                    Loading = false;
            }
        }

        private async Task FetchDataCore(bool force, AnimeListWorkModes? modeOverride)
        {
            var requestedMode = modeOverride ?? WorkMode;

            Console.WriteLine($"MALPLUS fetch: source={ListSource} mode={requestedMode} force={force} auth={Credentials.Authenticated}");

            // The status filter is part of the request, not just the source: ListSource only holds
            // the user name, so without it here every status change looked like the request that
            // was already loaded and returned early with the spinner left on.
            //
            // AND what is actually drawn has to match too. The memo above records what was asked
            // for, but the panels overwrite the grid through a different path that never touches
            // it, so coming back from Seasonal or a Top list it still claimed the library was
            // loaded, took the early return and repainted nothing. The grid came up empty until
            // a manual refresh. Written as a separate condition rather than as "clear the memo
            // when entering a panel" because that would be one more list of panel modes to
            // remember when a new one is added, and this one cannot be forgotten: any reuse of
            // this page goes through RefreshList, which sets it.
            var sameRequest = !force && _prevListSource == ListSource && _prevWorkMode == requestedMode &&
                              _prevStatus == (int)GetDesiredStatus() &&
                              _displayedMode == requestedMode &&
                              _displayedStatus == (int)GetDesiredStatus();
            if (sameRequest)
            {
                // Nothing to fetch, so nothing will lower the spinner the caller already raised.
                Loading = false;
                return;
            }
            if (WorkMode == requestedMode)
                _prevWorkMode = WorkMode;
            _prevListSource = ListSource;
            _prevStatus = (int)GetDesiredStatus();

            LoadError = null;
            LoadErrorVisibility = false;
            Loading = modeOverride == null;
            BtnSetSourceVisibility = false;
            EmptyNoticeVisibility = false;

            if (string.IsNullOrWhiteSpace(ListSource))
            {
                EmptyNoticeVisibility = true;
                EmptyNoticeContent = "We have come up empty...\nList source is not set.\nLog in or set it manually.";
                BtnSetSourceVisibility = true;
            }
            else
            {
                EmptyNoticeContent = "We have come up empty...";
            }

            switch (requestedMode)
            {
                case AnimeListWorkModes.Anime:
                    _attemptedAnimeFetch = true;
                    _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions = new List<AnimeItemAbstraction>();
                    if (force)
                        _animeLibraryDataStorage.AllLoadedAuthAnimeItems = new List<AnimeItemAbstraction>();
                    else if (_animeLibraryDataStorage.AllLoadedAuthAnimeItems.Count > 0 && string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                        _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions = _animeLibraryDataStorage.AllLoadedAuthAnimeItems;
                    break;
                case AnimeListWorkModes.Manga:
                    _attemptedMangaFetch = true;
                    _animeLibraryDataStorage.AllLoadedMangaItemAbstractions = new List<AnimeItemAbstraction>();
                    if (force)
                        _animeLibraryDataStorage.AllLoadedAuthMangaItems = new List<AnimeItemAbstraction>();
                    else if (_animeLibraryDataStorage.AllLoadedAuthMangaItems.Count > 0 && string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                        _animeLibraryDataStorage.AllLoadedMangaItemAbstractions = _animeLibraryDataStorage.AllLoadedAuthMangaItems;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }


            if (requestedMode == AnimeListWorkModes.Anime ? _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions.Count == 0 : _animeLibraryDataStorage.AllLoadedMangaItemAbstractions.Count == 0)
            {
                List<ILibraryData> data = null;
                List<ILibraryData> cachedData = null;
                List<ILibraryData> cachedDataManga = null;
                if (Settings.EnableOfflineSync &&
                    ResourceLocator.ConnectionInfoProvider.HasInternetConnection)
                {
                    if(Settings.AnimeSyncRequired)
                        cachedData = await DataCache.RetrieveDataForUser(ListSource, AnimeListWorkModes.Anime);
                    if (Settings.MangaSyncRequired)
                        cachedDataManga = await DataCache.RetrieveDataForUser(ListSource, AnimeListWorkModes.Manga);
                    force = cachedData != null || cachedDataManga != null || force;
                }
                await Task.Run(async () => data =
                    await new LibraryListQuery(ListSource, requestedMode).GetLibrary(force));
                if (data?.Count == 0)
                {
                    //no data?
                    RefreshList();
                    return;
                }

                var auth = Credentials.Authenticated && string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase);
                switch (requestedMode)
                {
                    case AnimeListWorkModes.Anime:

                        bool failed = false;
                        if (cachedData != null) //we have to sync
                        {
                            AnimeUpdateQuery.SuppressOfflineSync = true;
                            foreach (var cachedEntry in cachedData)
                            {
                                var syncedItem =
                                    data.FirstOrDefault(libraryData => libraryData.Id == cachedEntry.Id);
                                if (syncedItem != null)
                                {
                                    if (cachedEntry.LastWatched > syncedItem.LastWatched)
                                    {
                                        var query = new AnimeUpdateQuery(
                                            new AnimeItemAbstraction(true, cachedEntry as AnimeLibraryItemData)
                                                .ViewModel);
                                        var result = await query.GetRequestResponse();
                                        if (result != "Updated")
                                        {
                                            failed = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            AnimeUpdateQuery.SuppressOfflineSync = false;
                            if (!failed) //403 for example
                            {
                                Settings.AnimeSyncRequired = false;
                                await Task.Run(async () => data =
                                    await new LibraryListQuery(ListSource, requestedMode).GetLibrary(force));
                            }

                        }

                        if (cachedDataManga != null && !failed) //we have to sync
                        {
                            var mangaData = await Task.Run(async () =>
                                await new LibraryListQuery(ListSource, AnimeListWorkModes.Manga).GetLibrary(force));
                            MangaUpdateQuery.SuppressOfflineSync = true;
                            foreach (var cachedEntry in cachedDataManga)
                            {
                                var syncedItem =
                                    mangaData.FirstOrDefault(libraryData => libraryData.Id == cachedEntry.Id);
                                if (syncedItem != null)
                                {
                                    if (cachedEntry.LastWatched > syncedItem.LastWatched)
                                    {
                                        var query = new MangaUpdateQuery(
                                            new AnimeItemAbstraction(true, cachedEntry as MangaLibraryItemData)
                                                .ViewModel);
                                        var result = await query.GetRequestResponse();
                                        if (result != "Updated")
                                        {
                                            failed = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            MangaUpdateQuery.SuppressOfflineSync = false;
                            if (!failed) //403 for example
                            {
                                Settings.MangaSyncRequired = false;
                            }
                        }



                        foreach (var item in data)
                            _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions.Add(new AnimeItemAbstraction(auth, item as AnimeLibraryItemData));

                        if (string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                            _animeLibraryDataStorage.AllLoadedAuthAnimeItems = _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions;

                        break;
                    case AnimeListWorkModes.Manga:
                        foreach (var item in data)
                            _animeLibraryDataStorage.AllLoadedMangaItemAbstractions.Add(new AnimeItemAbstraction(auth && Settings.SelectedApiType == ApiType.Mal, item as MangaLibraryItemData)); //read only manga for hummingbird

                        if (string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase))
                            _animeLibraryDataStorage.AllLoadedAuthMangaItems = _animeLibraryDataStorage.AllLoadedMangaItemAbstractions;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                LibraryVersion++;
            }

            _fetching = false;
            if (WorkMode != requestedMode)
                return; // manga or anime is loaded top manga can proceed loading something else

            AppBtnGoBackToMyListVisibility = Credentials.Authenticated && !string.Equals(ListSource, Credentials.UserName, StringComparison.CurrentCultureIgnoreCase) ? true : false;
            // The old "load tags" line here rebuilt a search-suggestion list from every
            // entry in the library (Tags splits Notes on every access, so a 1500-entry
            // library meant 1500 list allocations + 1500 string splits) on the main
            // thread, on every fetch. Its destination, MainViewModelBase.SearchHints,
            // is a plain property that nothing in the MAUI app binds to.
            RefreshList();
            LoadError = null;
            LoadErrorVisibility = false;
        }

        /// <summary>
        ///     Method used by details page to associate itself with authenticated item in order to allow for list updates.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="anime"></param>
        /// <returns></returns>
        public async Task<IAnimeData> TryRetrieveAuthenticatedAnimeItem(int id, bool anime = true,
            bool forceMal = false)
        {
            if (!Credentials.Authenticated)
                return null;
            try
            {
                if (anime)
                {
                    if (_animeLibraryDataStorage.AllLoadedAnimeItemAbstractions.Count == 0 && !_attemptedAnimeFetch)
                        await FetchData(false, AnimeListWorkModes.Anime);
                }
                else if (_animeLibraryDataStorage.AllLoadedMangaItemAbstractions.Count == 0 && !_attemptedMangaFetch)
                    await FetchData(false, AnimeListWorkModes.Manga);

                return anime
                    ? _animeLibraryDataStorage.AllLoadedAuthAnimeItems.First(abstraction =>
                        forceMal ? abstraction.MalId == id : abstraction.Id == id).ViewModel
                    : _animeLibraryDataStorage.AllLoadedAuthMangaItems.First(abstraction =>
                        forceMal ? abstraction.MalId == id : abstraction.Id == id).ViewModel;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public IAnimeData TryRetrieveAuthenticatedAnimeItemSync(int id, bool anime = true, bool forceMal = false)
        {
            if (!Credentials.Authenticated)
                return null;
            try
            {
                return anime
                    ? _animeLibraryDataStorage.AllLoadedAuthAnimeItems.First(abstraction =>
                        forceMal ? abstraction.MalId == id : abstraction.Id == id).ViewModel
                    : _animeLibraryDataStorage.AllLoadedAuthMangaItems.First(abstraction =>
                        forceMal ? abstraction.MalId == id : abstraction.Id == id).ViewModel;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region StatusRelatedStuff

        private void UpdateUpperStatus()
        {
            var page = ViewModelLocator.GeneralMain;

            if(page.CurrentMainPageKind != PageIndex.PageAnimeList) //we are in async void domain so we somethimes have to skip this
                return;

            if (WorkMode != AnimeListWorkModes.SeasonalAnime)
                if (WorkMode == AnimeListWorkModes.TopAnime)
                    page.CurrentStatus = $"Top {TopAnimeWorkMode} - {Utils.Utilities.StatusToString((int)GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";
                else if (WorkMode == AnimeListWorkModes.TopManga)
                    page.CurrentStatus = $"Top {(MangaTopWorkMode == MangaTopType.All ? "Manga" : MangaTopWorkMode.ToString())} - {Utils.Utilities.StatusToString((int)GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";
                else if (WorkMode == AnimeListWorkModes.MangaAdapted)
                    page.CurrentStatus = $"Adapted to anime - {AnimeAdaptedToAnimeQuery.ToDisplayName(MangaAdaptedWorkMode)}";
                else if (WorkMode == AnimeListWorkModes.AnimeByStudio)
                    page.CurrentStatus = $"Studio - {Studio.GetDescription()}"; //Page 1 - {CurrentPage}
                else if (WorkMode == AnimeListWorkModes.AnimeByGenre)
                    page.CurrentStatus = $"Genre - {Genre.GetDescription()}"; //Page 1 - {CurrentPage}
                else if (!string.IsNullOrWhiteSpace(ListSource))
                    page.CurrentStatus = $"{ListSource} - {Utils.Utilities.StatusToString((int)GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";
                else
                    page.CurrentStatus = $"{(WorkMode == AnimeListWorkModes.Anime ? "Anime list" : "Manga list")}";
            else
                page.CurrentStatus = $"{CurrentSeason?.Name} - {Utils.Utilities.StatusToString((int)GetDesiredStatus(), WorkMode == AnimeListWorkModes.Manga)}";


            if (WorkMode == AnimeListWorkModes.Anime || WorkMode == AnimeListWorkModes.Manga || WorkMode == AnimeListWorkModes.SeasonalAnime || WorkMode == AnimeListWorkModes.AnimeByGenre || WorkMode == AnimeListWorkModes.AnimeByStudio)
                page.CurrentStatusSub = SortOption != SortOptions.SortWatched ? SortOption.GetDescription() : Sort3Label;
            else
                page.CurrentStatusSub = "";
        }

        public AnimeStatus GetDesiredStatus()
        {
            var value = StatusSelectorSelectedIndex;
            value++;
            return (AnimeStatus)(value == 0 ? 1 : value == 5 || value == 6 ? value + 1 : value);
        }

        private void SetDisplayMode(AnimeStatus val)
        {
            if (_manuallySelectedViewMode == null)
            {
                switch (val)
                {
                    case AnimeStatus.Watching:
                        DisplayMode = Settings.WatchingDisplayMode;
                        break;
                    case AnimeStatus.Completed:
                        DisplayMode = Settings.CompletedDisplayMode;
                        break;
                    case AnimeStatus.OnHold:
                        DisplayMode = Settings.OnHoldDisplayMode;
                        break;
                    case AnimeStatus.Dropped:
                        DisplayMode = Settings.DroppedDisplayMode;
                        break;
                    case AnimeStatus.PlanToWatch:
                        DisplayMode = Settings.PlannedDisplayMode;
                        break;
                    case AnimeStatus.AllOrAiring:
                        DisplayMode = Settings.AllDisplayMode;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(val), val, null);
                }
                RaisePropertyChanged(() => DisplayMode);
            }
        }

        private void SetDesiredStatus(int? value)
        {
            var setDisp = value == null;
            if (value == null &&
                (WorkMode == AnimeListWorkModes.SeasonalAnime || WorkMode == AnimeListWorkModes.AnimeByGenre ||
                 WorkMode == AnimeListWorkModes.AnimeByStudio || WorkMode == AnimeListWorkModes.TopAnime ||
                 WorkMode == AnimeListWorkModes.TopManga || WorkMode == AnimeListWorkModes.MangaAdapted))
                value = (int) AnimeStatus.AllOrAiring;

            value = value ??
                    (WorkMode == AnimeListWorkModes.Manga ? Settings.DefaultMangaFilter : Settings.DefaultAnimeFilter);
            if (setDisp)
                SetDisplayMode((AnimeStatus) value);

            value = value == 6 || value == 7 ? value - 1 : value;
            value--;
            _initializing = true;
            StatusSelectorSelectedIndex = (int) value;
            _initializing = false;
        }

        #endregion

        #region AllItemLoading

        private bool _loadingAllDetailsVisibility;
        private int _allItemsToLoad;
        private int _itemsLoaded;
        private ICommand _cancelLoadingAllItemsCommand;
        private ICommand _loadAllItemsDetailsCommand;

        public bool LoadingAllDetailsVisibility
        {
            get { return _loadingAllDetailsVisibility; }
            set
            {
                _loadingAllDetailsVisibility = value;
                RaisePropertyChanged(() => LoadingAllDetailsVisibility);
            }
        }

        public int AllItemsToLoad
        {
            get { return _allItemsToLoad; }
            set
            {
                _allItemsToLoad = value;
                RaisePropertyChanged(() => AllItemsToLoad);
            }
        }

        public int ItemsLoaded
        {
            get { return _itemsLoaded; }
            set
            {
                _itemsLoaded = value;
                RaisePropertyChanged(() => ItemsLoaded);
                RaisePropertyChanged(() => LoadingItemsStatus);
            }
        }

        public string LoadingItemsStatus => $"{ItemsLoaded}/{AllItemsToLoad}";

        public ICommand CancelLoadingAllItemsCommand
            => _cancelLoadingAllItemsCommand ?? (_cancelLoadingAllItemsCommand = new RelayCommand(() => _cancelLoadingAllItems = true));

        public ICommand LoadAllItemsDetailsCommand
            => _loadAllItemsDetailsCommand ?? (_loadAllItemsDetailsCommand = new RelayCommand(LoadAllItemsDetails));

        public bool IsMangaWorkMode => WorkMode == AnimeListWorkModes.Manga || WorkMode == AnimeListWorkModes.TopManga;

        private bool _cancelLoadingAllItems;
        private bool _wasPreviousQuery;

        private async void LoadAllItemsDetails()
        {
            var idsToFetch = new List<AnimeItemAbstraction>();
            foreach (var animeItemViewModel in _animeItemsSet.Concat(AnimeItems.Select(model => model.ParentAbstraction)))
            {
                if (!animeItemViewModel.LoadedVolatile)
                    idsToFetch.Add(animeItemViewModel);
            }


            if (idsToFetch.Count > 0)
            {
                ItemsLoaded = 0;
                LoadingAllDetailsVisibility = true;
                AllItemsToLoad = idsToFetch.Count;
                foreach (var abstraction in idsToFetch)
                {
                    if (_cancelLoadingAllItems)
                    {
                        _cancelLoadingAllItems = false;
                        break;
                    }

                    try
                    {
                        var data =
                            await
                                new AnimeGeneralDetailsQuery().GetAnimeDetails(false, abstraction.Id.ToString(),
                                    abstraction.Title, true);

                        DataCache.RegisterVolatileData(abstraction.Id, new VolatileDataCache
                        {
                            GlobalScore = data.GlobalScore,
                            AirStartDate =
                                data.StartDate == AnimeItemViewModel.InvalidStartEndDate ? null : data.StartDate
                        });
                        if (!abstraction.LoadedVolatile)
                        {
                            if (abstraction.TryRetrieveVolatileData())
                            {
                                if(abstraction.LoadedModel)
                                    abstraction.ViewModel.UpdateVolatileDataBindings();
                            }
                        }
                        ItemsLoaded++;
                    }
                    catch (Exception e)
                    {
                        //searching for crash source
                    }
                }
                LoadingAllDetailsVisibility = false;
            }
        }

        #endregion


    }
}
