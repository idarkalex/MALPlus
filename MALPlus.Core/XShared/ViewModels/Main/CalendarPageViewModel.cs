using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Ioc;
using MALPlus.Adapters;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Delegates;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.ViewModels.Main
{
    public class CalendarPivotPage
    {
        public DayOfWeek DayOfWeek { get; set; }
        public string Header { get; set; }
        public string Sub { get; set; }
        public string FullHeader => Utils.Utilities.ShortDayToFullDay(Header); //full name
        public List<AnimeItemViewModel> Items { get; set; } = new List<AnimeItemViewModel>();
    }

    //just to indicate different data type
    public sealed class CalendarSummaryPivotPage : CalendarPivotPage
    {
        //Header/Sub used to be re-declared here with 'new' read-only properties. That never took
        //effect: both the day strip and the pager bind them on a CalendarPivotPage-typed
        //reference, so they always read the base auto-property, which nothing ever assigned.
        //Now the summary is an ordinary page and gets real values.
        public CalendarSummaryPivotPage()
        {
            Header = "All";
        }

        public List<Tuple<string, List<AnimeItemViewModel>>> Data { get; set; } =
            new List<Tuple<string, List<AnimeItemViewModel>>>();
    }


    public class CalendarPageViewModel : ViewModelBase
    {
        private readonly IAnimeLibraryDataStorage _animeLibraryDataStorage;
        public event EmptyEventHander PivotSelectedIndexChange;

        public ObservableCollection<CalendarPivotPage> CalendarData { get; set; } =
            new ObservableCollection<CalendarPivotPage>();

        // UI state preservation (scroll positions, expanded panels, etc.) per session
        public Dictionary<string, object> UiState { get; } = new Dictionary<string, object>();

        public static double ItemWidth { get; private set; }

        private int _calendarPivotIndex;

        public int CalendarPivotIndex
        {
            get { return _calendarPivotIndex; }
            set
            {
                if (_calendarPivotIndex == value)
                    return;
                _calendarPivotIndex = value;
                // The calendar pager binds this TwoWay, so without the notification a day cell
                // could be tapped and the pager would never move. v2 did not need this because the
                // fragment subscribed to PivotSelectedIndexChange and paged by hand.
                RaisePropertyChanged(() => CalendarPivotIndex);
                PivotSelectedIndexChange?.Invoke();
            }
        }

        public CalendarPivotPage CurrentPivotPage { get; set; }

        private bool _calendarBuildingVisibility = false;

        public bool CalendarBuildingVisibility
        {
            get { return _calendarBuildingVisibility; }
            set
            {
                _calendarBuildingVisibility = value;
                RaisePropertyChanged(() => CalendarBuildingVisibility);
            }
        }

        private bool _calendarVisibility = false;

        public bool CalendarVisibility
        {
            get { return _calendarVisibility; }
            set
            {
                _calendarVisibility = value;
                RaisePropertyChanged(() => CalendarVisibility);
            }
        }

        private int _progressValue;

        public int ProgressValue
        {
            get { return _progressValue; }
            set
            {
                _progressValue = value;
                RaisePropertyChanged(() => ProgressValue);
            }
        }

        private int _maxProgressValue;

        public int MaxProgressValue
        {
            get { return _maxProgressValue; }
            set
            {
                _maxProgressValue = value;
                RaisePropertyChanged(() => MaxProgressValue);
            }
        }

        private ICommand _refreshCalendarCommand;

        public ICommand RefreshCalendarCommand
            => _refreshCalendarCommand ?? (_refreshCalendarCommand = new RelayCommand(() => Init(true)));


        private ICommand _exportToCalendarCommand;

        public ICommand ExportToCalendarCommand
            => _exportToCalendarCommand ?? (_exportToCalendarCommand = new RelayCommand<AnimeItemViewModel>(entry =>
               {
                   try
                   {
                       SimpleIoc.Default.GetInstance<ICalendarExportProvider>().ExportToCalendar(entry);
                   }
                   catch (Exception)
                   {
                       //no calendar on platofirm
                   }
               }));


        private static List<CalendarPivotPage> CreatePages()
        {
            return new List<CalendarPivotPage>
            {
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarPivotPage(),
                new CalendarSummaryPivotPage(),
            };
        }

        public CalendarPageViewModel(IAnimeLibraryDataStorage animeLibraryDataStorage)
        {
            _animeLibraryDataStorage = animeLibraryDataStorage;
            ItemWidth = AnimeItemViewModel.MaxWidth - 8;
            ResourceLocator.AiringInfoProvider.AiringsUpdated += () =>
            {
                if (!_initialized)
                    return;

                // Never interrupt the user with a reload when our own build/refresh triggered this
                // or when the calendar already has populated schedule data on screen.
                if (_buildGate.CurrentCount == 0 ||
                    (CalendarData != null && CalendarData.Any(p => p.Items != null && p.Items.Count > 0)))
                {
                    if (!IsPageActive)
                        CalendarBuiltKey = string.Empty;
                    return;
                }

                if (IsPageActive)
                    ResourceLocator.DispatcherAdapter.Run(() => _ = InitAsync(true, resetToDesiredTab: true, silent: true));
                else
                    CalendarBuiltKey = string.Empty;
            };
        }

        private bool _initialized;
        private readonly SemaphoreSlim _buildGate = new SemaphoreSlim(1, 1);
        private int _buildVersion;
        public bool IsPageActive { get; set; }
        public event Action DayStripChanged;

        /// <summary>
        ///     Which content of calendar was last built: true for everything airing, false for only the
        ///     reader's own list.
        /// </summary>
        /// <remarks>
        ///     The view model is a singleton that outlives the page, so this is the only way a newly
        ///     created page can tell whether the data in front of it is the data that was asked for.
        ///     It used to be inferred from the setting, and since the page writes that setting before it
        ///     asks, the two agreed while the grid below them still held the other list.
        /// </remarks>
        public bool CalendarBuiltForAllAiring { get; private set; }

        /// <summary>
        ///     The build-affecting settings as they were when the data on screen was produced.
        ///     The page compares this against <see cref="CurrentSettingsKey"/> on every appearance:
        ///     equal means the calendar can be shown as it is, different means a rebuild.
        /// </summary>
        public string CalendarBuiltKey { get; private set; } = string.Empty;

        public static string CurrentSettingsKey()
        {
            return string.Join("|",
                Settings.CalendarShowAllAiring,
                Settings.CalendarSwitchMonSun,
                Settings.CalendarRemoveEmptyDays,
                Settings.CalendarMaxItems,
                Settings.CalendarIncludeWatching,
                Settings.CalendarIncludePlanned,
                Settings.PreferEnglishTitles,
                Settings.ShowFormatTag,
                Settings.PosterScoreDisplay,
                Settings.CalendarTimeZoneId,
                SettingsViewModelBase.SettingsRevision);
        }

        private bool IsCurrentBuild(int version) => version == Volatile.Read(ref _buildVersion);

        /// <summary>
        ///     Waits for the airing provider to have data, but not for as long as it takes: a cold
        ///     start can spend fifteen seconds inside its own network bound, and the calendar is
        ///     useful before that. Polls for applied data rather than the init task, because with a
        ///     disk cache the provider publishes it within milliseconds while the task itself stays
        ///     alive until the refetch is done. Whatever was not there in time is picked up by the
        ///     rebuild the provider fires when it lands.
        /// </summary>
        private static async Task WaitForProviderAsync(int milliseconds)
        {
            try
            {
                var provider = ResourceLocator.AiringInfoProvider;
                var init = provider.Init(false);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!provider.InitializationSuccess && sw.ElapsedMilliseconds < milliseconds)
                    await Task.Delay(80);
                if (!init.IsCompleted)
                    init.ContinueWith(_ => { }, TaskScheduler.Default);
            }
            catch (Exception)
            {
            }
        }

        public Task Init(bool force = false)
            => InitAsync(force, resetToDesiredTab: force, silent: false);

        public async Task InitAsync(bool force = false, bool resetToDesiredTab = false, bool silent = false)
        {
            if (_initialized && !force)
            {
                if (resetToDesiredTab && CalendarData != null && CalendarData.Count > 0)
                {
                    var desired = ResolveDesiredTabIndex(CalendarData);
                    if (desired >= 0 && desired < CalendarData.Count)
                    {
                        _calendarPivotIndex = desired;
                        RaisePropertyChanged(() => CalendarPivotIndex);
                    }
                }
                CalendarBuildingVisibility = false;
                CalendarVisibility = true;
                return;
            }

            _initialized = true;

            // One build at a time, and only the newest one is allowed to finish. AiringsUpdated,
            // the mode switch and a page re-entry can all land while a build is in flight, and
            // they used to interleave: two builds wrote into the same day pages, so entries were
            // duplicated, a sort ran on a list the other build had already shrunk (the
            // ArgumentOutOfRangeException), and late adds landed after the sort.
            var version = Interlocked.Increment(ref _buildVersion);
            await _buildGate.WaitAsync();
            try
            {
                if (!IsCurrentBuild(version))
                    return;
                await BuildCore(version, resetToDesiredTab, silent);
            }
            catch (Exception ex)
            {
                if (IsCurrentBuild(version))
                    BuildError = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                _buildGate.Release();
                if (IsCurrentBuild(version))
                {
                    CalendarBuildingVisibility = false;
                    CalendarVisibility = true;
                }
            }
        }

        private async Task BuildCore(int version, bool resetToDesiredTab, bool silent)
        {
            // Captured before the assignment below: a rebuild that lands while the reader is
            // browsing must not jump them back to the landing page, but a mode switch or initial open must.
            var modeChanged = CalendarBuiltForAllAiring != Settings.CalendarShowAllAiring;
            // Written before the reads below, so it names what is being built and not what the
            // setting said: the two are the same value but only this one is inside the build.
            CalendarBuiltForAllAiring = Settings.CalendarShowAllAiring;
            CalendarBuiltKey = CurrentSettingsKey();
            RaisePropertyChanged(() => CalendarBuiltForAllAiring);
            RaisePropertyChanged(() => CalendarBuiltKey);
            // The library can change between builds (the user just added something), so the
            // membership set backing IsOnMyList cannot be cached across them.
            _myListIds = null;
            BuildError = string.Empty;
            if (!silent)
                CalendarBuildingVisibility = true;

            // Everything lands in a local list first. The visible collection is replaced once the
            // build is complete, so an interrupted build cannot leave half a calendar on screen,
            // and nothing that is reading CalendarData ever sees it change underneath them.
            var pages = CreatePages();

            List<AnimeItemAbstraction> abstractions;
            List<AnimeItemAbstraction> unresolved = null;
            if (Settings.CalendarShowAllAiring)
            {
                // Global waits for its own sources in full. A bound here used to publish an empty
                // calendar when the network was slow, which is worse than a late one: the season
                // list and the declared dates are both fetched inside, each already bounded.
                abstractions = await BuildAllAiringAbstractionsAsync();
            }
            else
            {
                await WaitForProviderAsync(1500);
                if (!IsCurrentBuild(version))
                    return;

                var library = _animeLibraryDataStorage.AllLoadedAuthAnimeItems;
                if (library == null || library.Count == 0)
                    library = _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions;

                if (library == null || library.Count == 0)
                {
                    if (Credentials.Authenticated && !string.IsNullOrWhiteSpace(Credentials.UserName))
                    {
                        try
                        {
                            await ViewModelLocator.AnimeList.FetchData(false, AnimeListWorkModes.Anime);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                var built = await Task.Run(BuildMyListFast);
                abstractions = built.Covered;
                unresolved = built.Missing;
            }

            if (!IsCurrentBuild(version))
                return;

            PlaceAbstractions(pages, abstractions);
            SortDays(pages);
            ApplyDayHeaders(pages);
            BuildSummary(pages);

            if (!IsCurrentBuild(version))
                return;

            var firstPublish = CalendarData == null || CalendarData.Count == 0;
            // A publish replaces the collection, and that swap is what the pager's touch handling
            // does not survive: after a rebuild lands, swipes stop working while taps keep going.
            // Rebuilds land all the time (the deferred season/date top-ups, every AiringsUpdated)
            // and most of them produce the same days with the same entries, so identical content
            // keeps the live collection untouched and the pager keeps its input.
            var changed = !firstPublish && !CalendarContentMatches(CalendarData, pages);

            if (firstPublish || modeChanged || resetToDesiredTab)
            {
                var desired = ResolveDesiredTabIndex(pages);
                if (desired >= 0 && desired < pages.Count)
                    _calendarPivotIndex = desired;
            }
            else if (_calendarPivotIndex >= pages.Count)
            {
                _calendarPivotIndex = pages.Count - 1;
            }

            if (firstPublish || changed || modeChanged)
            {
                CalendarData = new ObservableCollection<CalendarPivotPage>(pages);
                RaisePropertyChanged(() => CalendarData);
            }
            RaisePropertyChanged(() => CalendarPivotIndex);

            // Safety net only: almost everything already carries a date from the provider.
            RefreshCountdownsAsync();
            StartMissingEnrichment(version, unresolved);
        }

        /// <summary>
        ///     Places each abstraction on its day column, bucketed in the zone chosen in settings.
        /// </summary>
        private static void PlaceAbstractions(List<CalendarPivotPage> pages, List<AnimeItemAbstraction> abstractions)
        {
            var timeZone = CalendarTimeZone.Resolve();

            foreach (var abstraction in abstractions)
            {
                try
                {
                    var nowUtc = DateTime.UtcNow;

                    //Flagged by hand as not airing: the source keeps advertising a weekly slot
                    //for it, so without this it would still land on some day.
                    if (AiringOverrides.IsOverriddenAsNotAiring(abstraction.Id))
                        continue;

                    DateTime airUtc;
                    if (ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(abstraction.Id, out var declaredAir) &&
                        (declaredAir > nowUtc || AirTimeUtils.IsInAiringWindow(declaredAir, nowUtc)))
                    {
                        if ((declaredAir - nowUtc).TotalDays >= 7)
                            continue;
                        abstraction.ViewModel.SetNextAirCache(declaredAir);
                        airUtc = declaredAir;
                    }
                    else if (ResourceLocator.AiringInfoProvider.TryGetNextAirDate(abstraction.Id, nowUtc, out var nextAirDate) &&
                             (nextAirDate > nowUtc || AirTimeUtils.IsInAiringWindow(nextAirDate, nowUtc)))
                    {
                        if ((nextAirDate - nowUtc).TotalDays >= 7)
                            continue;
                        abstraction.ViewModel.SetNextAirCache(nextAirDate);
                        airUtc = nextAirDate;
                    }
                    else if (abstraction.RepresentsAnime &&
                             DataCache.TryRetrieveDataForId(abstraction.Id, out var volatileData) &&
                             volatileData.NextAirUtc.HasValue &&
                             (volatileData.NextAirUtc.Value > nowUtc || AirTimeUtils.IsInAiringWindow(volatileData.NextAirUtc.Value, nowUtc)) &&
                             (volatileData.NextAirUtc.Value - nowUtc).TotalDays < 7)
                    {
                        abstraction.ViewModel.SetNextAirCache(volatileData.NextAirUtc.Value);
                        airUtc = volatileData.NextAirUtc.Value;
                    }
                    else
                    {
                        continue;
                    }

                    // The instant is absolute, but the Japanese weekday is not the day the user
                    // watches on: a 07:30 JST Tuesday slot airs Monday night west of Tokyo.
                    var day = (int) CalendarTimeZone.ToZoneDay(airUtc, timeZone);
                    if (day >= 0 && day <= 6)
                    {
                        if (abstraction.ViewModel.GlobalScore <= 0 && DataCache.TryRetrieveDataForId(abstraction.Id, out var vdScore) && vdScore.GlobalScore > 0)
                            abstraction.ViewModel.GlobalScore = vdScore.GlobalScore;
                        pages[day].Items.Add(abstraction.ViewModel);
                    }
                }
                catch (Exception e)
                {
                    // there are some numm ref crashes and I don't know really know where
                    // probably MAL returns some odd stuff and we cannot get details
                    Console.WriteLine("MALPLUS calendar day placement failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        ///     Sort each day by next air time (most proximate first) - single datum NextAirUtc JST.
        /// </summary>
        private static void SortDays(List<CalendarPivotPage> pages)
        {
            for (int d = 0; d < 7; d++)
                pages[d].Items.Sort(CompareNextAir);
        }

        private static DateTime? ResolveNextAir(AnimeItemViewModel item)
        {
            var id = item.ParentAbstraction?.MalId ?? item.Id;
            if (DataCache.TryRetrieveDataForId(id, out var cached) && cached.NextAirUtc.HasValue)
                return cached.NextAirUtc;
            if (ResourceLocator.AiringInfoProvider.TryGetNextAirDate(id, DateTime.UtcNow, out var next))
                return next;
            return null;
        }

        private static int CompareNextAir(AnimeItemViewModel a, AnimeItemViewModel b)
        {
            var aNext = ResolveNextAir(a);
            var bNext = ResolveNextAir(b);
            if (!aNext.HasValue) return 1;
            if (!bNext.HasValue) return -1;
            return aNext.Value.CompareTo(bNext.Value);
        }


        private static void ApplyDayHeaders(List<CalendarPivotPage> pages)
        {
            if (Settings.CalendarSwitchMonSun)
            {
                var sunday = pages[0];
                pages.RemoveAt(0);
                pages.Insert(6, sunday);
                pages[0].Header = Utilities.DayToString(DayOfWeek.Monday, true);
                pages[0].DayOfWeek = DayOfWeek.Monday;
                pages[6].Header = Utilities.DayToString(DayOfWeek.Sunday, true);
                pages[6].DayOfWeek = DayOfWeek.Sunday;
                for (int i = 1; i < 6; i++)
                {
                    pages[i].Header = Utilities.DayToString((DayOfWeek) i + 1, true);
                    pages[i].DayOfWeek = (DayOfWeek) i + 1;
                }
            }
            else
            {
                for (int i = 0; i < 7; i++)
                {
                    pages[i].Header = Utilities.DayToString((DayOfWeek) i, true);
                    pages[i].DayOfWeek = (DayOfWeek) i;
                }
            }
        }

        /// <summary>
        ///     Sets each day's count, drops the empty days when that is asked for, and fills the
        ///     summary page the "All" tab renders from.
        /// </summary>
        private static void BuildSummary(List<CalendarPivotPage> pages)
        {
            // Hold the summary page by reference. It used to be reached as CalendarData[7],
            // which is only valid while all seven day pages are still in the collection: with
            // "remove empty days" on, the removals below shrink the list and that index throws
            // ArgumentOutOfRangeException, which aborted the whole rebuild and left the
            // previous calendar on screen. Season mode never hit it because no day is empty.
            var summaryPage = pages[pages.Count - 1] as CalendarSummaryPivotPage;

            var emptyPages = new List<CalendarPivotPage>();
            foreach (var calendarPivotPage in pages.Take(pages.Count - 1))
            {
                if (calendarPivotPage.Items.Count > 0)
                    calendarPivotPage.Sub = calendarPivotPage.Items.Count.ToString();
                else
                {
                    if (Settings.CalendarRemoveEmptyDays)
                        emptyPages.Add(calendarPivotPage);
                    else
                        calendarPivotPage.Sub = "-";
                }
                if (calendarPivotPage.Items.Count != 0)
                    summaryPage?.Data.Add(
                        new Tuple<string, List<AnimeItemViewModel>>(calendarPivotPage.FullHeader,
                            calendarPivotPage.Items));
            }
            foreach (var emptyPage in emptyPages)
                pages.Remove(emptyPage);

            // The shared item template binds Items, which the summary subclass never filled,
            // so the Summary tab always showed "No airing today".
            if (summaryPage != null)
            {
                summaryPage.Items = summaryPage.Data.SelectMany(entry => entry.Item2).ToList();
                // The tab is read as one list, so it is ordered by what airs first. The day
                // sections it is flattened from are in weekday order, which starts wherever
                // the week starts, not where the week's airings start.
                summaryPage.Items.Sort(CompareNextAir);
                summaryPage.Sub = summaryPage.Items.Count > 0
                    ? summaryPage.Items.Count.ToString()
                    : "-";
            }
        }

        private string _buildError;
        public string BuildError
        {
            get => _buildError;
            set
            {
                _buildError = value;
                RaisePropertyChanged(() => BuildError);
            }
        }

        /// <summary>
        ///     Kicks a background countdown refresh for the items the provider could not date, so
        ///     their badge is not blank. The provider path already handled the rest with zero
        ///     network calls, which is what keeps Season mode (about 300 entries) from stampeding.
        /// </summary>
        public void RefreshCountdownsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var pending = new List<AnimeItemViewModel>();
                foreach (var page in CalendarData)
                {
                    if (page is CalendarSummaryPivotPage)
                        continue;
                    foreach (var item in page.Items)
                    {
                        if (string.IsNullOrEmpty(item.TimeTillNextAirCache))
                            pending.Add(item);
                    }
                }

                if (pending.Count == 0)
                    return;

                foreach (var item in pending)
                    item.RefreshTimeTillNextAirInBackground();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS calendar countdown refresh failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        ///     Whether a series is already on the user's list. Season mode mixes entries the user
        ///     follows with ones they do not, and re-running Add on a listed entry resets local
        ///     progress and score, so the long-press sheet has to know.
        /// </summary>
        private IEnumerable<AnimeItemAbstraction> GetLoadedAuthAnime()
        {
            var list = _animeLibraryDataStorage.AllLoadedAuthAnimeItems;
            if (list != null && list.Count > 0)
                return list;
            return _animeLibraryDataStorage.AllLoadedAnimeItemAbstractions ?? Enumerable.Empty<AnimeItemAbstraction>();
        }

        public bool IsOnMyList(int malId)
        {
            if (malId <= 0)
                return false;
            if (_myListIds == null)
                _myListIds = new HashSet<int>(GetLoadedAuthAnime()
                    .Where(abstraction => abstraction != null)
                    .Select(abstraction => abstraction.MalId));
            return _myListIds.Contains(malId);
        }

        private HashSet<int> _myListIds;

        private List<AnimeItemAbstraction> ProviderMyListAbstractions()
        {
            var abstractions = GetLoadedAuthAnime().Where(abstraction =>
                ResourceLocator.AiringInfoProvider.HasAiringEntry(abstraction.Id) ||
                ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(abstraction.Id, out _)).Where(abstraction =>
            {
                if (DataCache.TryRetrieveDataForId(abstraction.Id, out var vd) && !string.IsNullOrEmpty(vd.LastKnownStatus) && !AirTimeUtils.IsCurrentlyAiringStatus(vd.LastKnownStatus))
                    return false;
                return true;
            }).Where(
                abstraction => (abstraction.Type == (int)AnimeType.TV || abstraction.Type == (int)AnimeType.ONA) && (
                    (Settings.CalendarIncludePlanned &&
                     abstraction.MyStatus == AnimeStatus.PlanToWatch) ||
                    (Settings.CalendarIncludeWatching && abstraction.MyStatus == AnimeStatus.Watching))).ToList();

            //Limit items to at most the configured amount
            var maxItems = Math.Max(1, Settings.CalendarMaxItems);
            if (abstractions.Count > maxItems)
            {
                var watchingCount = abstractions.Count(abstraction => abstraction.MyStatus == AnimeStatus.Watching);
                //with currently watched ones having most priority
                if (watchingCount > maxItems)
                    abstractions = abstractions.Where(abstraction => abstraction.MyStatus == AnimeStatus.Watching).Take(maxItems).ToList();
                else
                {
                    //take all watching and add ptw to make at most maxItems entries
                    abstractions = abstractions.Where(abstraction => abstraction.MyStatus == AnimeStatus.Watching)
                        .Concat(abstractions.Where(abstraction => abstraction.MyStatus == AnimeStatus.PlanToWatch)
                            .Take(maxItems - watchingCount)).ToList();
                }
            }

            return abstractions;
        }
        /// <summary>
        ///     The entries My List can place without touching the network: what the provider already
        ///     knows, plus everything that carries a persisted next air date. Anything else is
        ///     handed back as missing so it can be resolved after the calendar is on screen.
        /// </summary>
        private (List<AnimeItemAbstraction> Covered, List<AnimeItemAbstraction> Missing) BuildMyListFast()
        {
            var covered = ProviderMyListAbstractions();
            var maxItems = Math.Max(1, Settings.CalendarMaxItems);
            var coveredIds = new HashSet<int>(covered.Select(abstraction => abstraction.Id));
            var missing = new List<AnimeItemAbstraction>();
            var nowUtc = DateTime.UtcNow;

            foreach (var abstraction in GetLoadedAuthAnime())
            {
                if (abstraction == null || !abstraction.RepresentsAnime)
                    continue;
                // ONA as well as TV: a weekly donghua or streaming release airs on a fixed
                // weekday exactly like a broadcast slot, and the TV-only gate kept the whole
                // of that side of the library out of My List.
                if (abstraction.Type != (int) AnimeType.TV && abstraction.Type != (int) AnimeType.ONA)
                    continue;
                if (!((Settings.CalendarIncludePlanned && abstraction.MyStatus == AnimeStatus.PlanToWatch) ||
                      (Settings.CalendarIncludeWatching && abstraction.MyStatus == AnimeStatus.Watching)))
                    continue;
                if (coveredIds.Contains(abstraction.Id))
                    continue;

                //defensive: currently-airing library entries with declared AniList dates
                //or persisted countdown dates resolve immediately before going to the network.
                if (ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(abstraction.Id, out var declared) &&
                    (declared > nowUtc || AirTimeUtils.IsInAiringWindow(declared, nowUtc)) &&
                    (declared - nowUtc).TotalDays < 7)
                {
                    abstraction.ViewModel.SetNextAirCache(declared);
                    covered.Add(abstraction);
                    coveredIds.Add(abstraction.Id);
                    continue;
                }

                if (DataCache.TryRetrieveDataForId(abstraction.Id, out var volatileData) &&
                    volatileData.NextAirUtc.HasValue)
                {
                    var nextAir = volatileData.NextAirUtc.Value;
                    if ((nextAir > nowUtc || AirTimeUtils.IsInAiringWindow(nextAir, nowUtc)) &&
                        (nextAir - nowUtc).TotalDays < 7)
                    {
                        abstraction.ViewModel.SetNextAirCache(nextAir);
                        covered.Add(abstraction);
                        coveredIds.Add(abstraction.Id);
                        continue;
                    }
                }

                missing.Add(abstraction);
            }

            if (covered.Count > maxItems)
                covered = covered
                    .OrderByDescending(abstraction => abstraction.MyStatus == AnimeStatus.Watching)
                    .Take(maxItems)
                    .ToList();

            var budget = Math.Max(0, maxItems - covered.Count);
            if (missing.Count > budget)
                missing = missing.Take(budget).ToList();

            return (covered, missing);
        }

        /// <summary>
        ///     Resolves, once the calendar is already on screen, the entries that had no date to be
        ///     placed with. Doing this inside the build is what made My List take up to half a
        ///     minute: every one of them is a network round trip.
        /// </summary>
        private void StartMissingEnrichment(int version, List<AnimeItemAbstraction> missing)
        {
            if (missing == null || missing.Count == 0)
                return;
            _ = EnrichMissingAsync(version, missing);
        }

        private async Task EnrichMissingAsync(int version, List<AnimeItemAbstraction> missing)
        {
            foreach (var abstraction in missing)
            {
                if (!IsCurrentBuild(version))
                    return;
                try
                {
                    var nextAir = await abstraction.ViewModel.GetTimeTillNextAirAsync();
                    if (!nextAir.HasValue)
                        continue;
                    var now = DateTime.UtcNow;
                    if (!((nextAir.Value > now || AirTimeUtils.IsInAiringWindow(nextAir.Value, now)) &&
                          (nextAir.Value - now).TotalDays < 7))
                        continue;

                    abstraction.ViewModel.SetNextAirCache(nextAir);
                    var air = nextAir.Value;
                    ResourceLocator.DispatcherAdapter.Run(() => InsertEnriched(version, abstraction, air));
                }
                catch (Exception)
                {
                }
            }
        }

        private void InsertEnriched(int version, AnimeItemAbstraction abstraction, DateTime nextAirUtc)
        {
            if (!IsCurrentBuild(version))
                return;
            var day = (int) CalendarTimeZone.ToZoneDay(nextAirUtc, CalendarTimeZone.Resolve());
            if (day < 0 || day > 6)
                return;

            CalendarPivotPage target = null;
            foreach (var page in CalendarData)
            {
                if (page is CalendarSummaryPivotPage)
                    continue;
                if ((int) page.DayOfWeek == day)
                {
                    target = page;
                    break;
                }
            }

            // The day may have been removed by "hide empty days". Putting it back would rearrange
            // the strip while the reader is looking at it, so the entry waits for the next build.
            if (target == null || target.Items.Contains(abstraction.ViewModel))
                return;

            target.Items.Add(abstraction.ViewModel);
            target.Items.Sort(CompareNextAir);
            target.Sub = target.Items.Count.ToString();

            foreach (var page in CalendarData)
            {
                if (page is not CalendarSummaryPivotPage summary)
                    continue;
                if (!summary.Items.Contains(abstraction.ViewModel))
                {
                    summary.Items.Add(abstraction.ViewModel);
                    summary.Items.Sort(CompareNextAir);
                    summary.Sub = summary.Items.Count.ToString();
                }
                break;
            }

            DayStripChanged?.Invoke();
        }

        /// <summary>
        ///     True when the freshly built pages say exactly what the live collection says: same
        ///     days, same headers, same entries in the same order. Used to skip a publish (and the
        ///     ItemsSource swap it causes) when a rebuild has nothing new to show.
        /// </summary>
        private static bool CalendarContentMatches(IList<CalendarPivotPage> current, IList<CalendarPivotPage> built)
        {
            if (current == null || built == null || current.Count != built.Count)
                return false;
            for (var i = 0; i < built.Count; i++)
            {
                var a = current[i];
                var b = built[i];
                if (a == null || b == null || a.GetType() != b.GetType())
                    return false;
                if (!string.Equals(a.Header, b.Header, StringComparison.Ordinal) ||
                    !string.Equals(a.Sub, b.Sub, StringComparison.Ordinal))
                    return false;
                var ai = a.Items;
                var bi = b.Items;
                if (ai == null || bi == null || ai.Count != bi.Count)
                    return false;
                for (var j = 0; j < bi.Count; j++)
                {
                    if (ai[j]?.Id != bi[j]?.Id)
                        return false;
                }
            }
            return true;
        }

        private async Task<List<AnimeItemAbstraction>> BuildAllAiringAbstractionsAsync()
        {
            return await Task.Run(async () =>
            {
                var result = new List<AnimeItemAbstraction>();
                var nowUtc = DateTime.UtcNow;

                // Start the seasonal query concurrently with the provider check
                var seasonTask = SeasonalAiringAsync();

                // The provider's ids are one of the three sources below, so it has to be in before
                // they are read: at startup it is reset and refilled behind a network bound, and
                // reading it early is how a whole season of provider ids goes missing.
                await WaitForProviderAsync(1500);

                // The ids worth asking about. The schedule endpoint is one source, but it misses
                // series that really are airing. The user's own list and the current season cover the rest.
                var ids = new HashSet<int>();
                try
                {
                    foreach (var id in ResourceLocator.AiringInfoProvider.GetAllAiringIds())
                        ids.Add(id);
                }
                catch (Exception)
                {
                }
                try
                {
                    foreach (var id in _animeLibraryDataStorage.AllLoadedAuthAnimeItems
                                 .Where(i => i != null && i.RepresentsAnime)
                                 .Select(i => i.MalId))
                        ids.Add(id);
                }
                catch (Exception)
                {
                }
                var seasonalData = new Dictionary<int, SeasonalAnimeData>();
                try
                {
                    var seasonDone = await Task.WhenAny(seasonTask, Task.Delay(5000));
                    if (seasonDone == seasonTask)
                    {
                        foreach (var season in await seasonTask)
                        {
                            if (season == null || season.Id <= 0)
                                continue;
                            seasonalData[season.Id] = season;
                            ids.Add(season.Id);
                        }
                    }
                    else
                    {
                        _ = seasonTask.ContinueWith(_ => { }, TaskScheduler.Default);
                    }
                }
                catch (Exception)
                {
                }

                // Ask about the whole set and keep those with a real date in the week ahead, plus
                // the one airing right now: an episode that just started is still on today.
                // Bounded inside this single build so opening the calendar doesn't schedule
                // delayed rebuilds that interrupt the user while they are browsing.
                try
                {
                    var refreshTask = ResourceLocator.AiringInfoProvider.RefreshDeclaredNextAirAsync(ids);
                    var refreshDone = await Task.WhenAny(refreshTask, Task.Delay(3500));
                    if (refreshDone != refreshTask)
                        _ = refreshTask.ContinueWith(_ => { }, TaskScheduler.Default);

                    var wanted = ids
                        .Where(id => ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(id, out var d) &&
                                     d > nowUtc.AddHours(-1) && (d - nowUtc).TotalDays < 7)
                        .OrderBy(id => ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(id, out var x) ? x : DateTime.MaxValue)
                        .ToList();

                    foreach (var id in wanted)
                    {
                        var hasEntry = ResourceLocator.AiringInfoProvider.TryGetEntry(id, out var entry);
                        var libraryItem = GetLoadedAuthAnime()
                            .FirstOrDefault(a => a != null && a.RepresentsAnime && a.MalId == id);

                        float score = 0f;
                        if (seasonalData.TryGetValue(id, out var s) && s.Score > 0)
                            score = s.Score;
                        else if (hasEntry && entry.Score > 0)
                            score = entry.Score;
                        else if (DataCache.TryRetrieveDataForId(id, out var vd) && vd.GlobalScore > 0)
                            score = vd.GlobalScore;

                        AnimeItemAbstraction abstraction;
                        if (libraryItem != null)
                        {
                            // The user's own entry. It is the only source that also carries the
                            // poster, the total and the watched count for a series the schedule
                            // feed misses, and reusing it gives the card the same badge the
                            // library shows instead of a synthesized one.
                            abstraction = libraryItem;
                            if (abstraction.GlobalScore <= 0 && score > 0)
                                abstraction.GlobalScore = score;
                        }
                        else if (hasEntry)
                        {
                            abstraction = new AnimeItemAbstraction(false, new AnimeLibraryItemData
                            {
                                Id = id,
                                MalId = id,
                                Title = entry.Title ?? "",
                                ImgUrl = entry.ImgUrl,
                                AllEpisodes = entry.AllEpisodes,
                                Type = entry.Type,
                                AlternateTitle = entry.Title ?? "",
                                GlobalScore = score
                            });
                            if (score > 0)
                                abstraction.GlobalScore = score;
                        }
                        else if (seasonalData.TryGetValue(id, out var season))
                        {
                            // On neither the feed nor the user's list, but the season list knows
                            // the title and the poster. Without this the cell is a hole: it used
                            // to be built from the missing feed entry alone and drew blank.
                            abstraction = new AnimeItemAbstraction(season, true);
                            if (season.Score > 0)
                                abstraction.GlobalScore = season.Score;
                        }
                        else
                        {
                            // Nothing carries a title or a poster for it.
                            continue;
                        }

                        // Hand the placed item the date that put it here. The schedule feed does
                        // not know these series, so without the seed their countdown has no
                        // source and the card sits with an empty counter.
                        if (ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(id, out var declared))
                            abstraction.ViewModel.SetNextAirCache(declared);

                        if (score > 0 && abstraction.ViewModel.GlobalScore <= 0)
                            abstraction.ViewModel.GlobalScore = score;

                        result.Add(abstraction);
                    }
                }
                catch (Exception)
                {
                }

                return result;
            });
        }

        /// <summary>
        ///     The entries of the current season, which is where the schedule endpoint's gaps show up.
        ///     Read through the same query the Seasonal page uses, so both agree on what is on.
        /// </summary>
        private async Task<List<SeasonalAnimeData>> SeasonalAiringAsync()
        {
            try
            {
                // "Airing" is the season the anime list uses for what is on right now, and it is the
                // list the Seasonal page reads, so this agrees with what the user is looking at.
                var data = await new AnimeSeasonalQuery(
                    new Models.Models.Anime.AnimeSeason { Name = "Airing" }).GetSeasonalAnime();
                return data ?? new List<SeasonalAnimeData>();
            }
            catch (Exception)
            {
                return new List<SeasonalAnimeData>();
            }
        }

        public static int ResolveDesiredTabIndex(IList<CalendarPivotPage> pages)
        {
            if (pages == null || pages.Count == 0)
                return 0;

            var summaryIndex = -1;
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] is CalendarSummaryPivotPage)
                {
                    summaryIndex = i;
                    break;
                }
            }
            var fallback = summaryIndex >= 0 ? summaryIndex : pages.Count - 1;

            if (Settings.CalendarStartOnToday)
            {
                var timeZone = CalendarTimeZone.Resolve();
                var todayDayOfWeek = CalendarTimeZone.ToZoneDay(DateTime.UtcNow, timeZone);

                for (int i = 0; i < pages.Count; i++)
                {
                    if (i == summaryIndex)
                        continue;
                    if (pages[i].DayOfWeek == todayDayOfWeek)
                        return i;
                }

                // If today is empty / was removed, find the first available non-summary day page
                for (int i = 0; i < pages.Count; i++)
                {
                    if (i != summaryIndex)
                        return i;
                }
                return 0;
            }

            return fallback;
        }

        public Task GoToDesiredTab()
        {
            if (CalendarData != null && CalendarData.Count > 0)
            {
                CalendarPivotIndex = ResolveDesiredTabIndex(CalendarData);
            }
            return Task.CompletedTask;
        }

    }
}
