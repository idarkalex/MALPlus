using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Adapters;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.Misc;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Profile;
using MALPlus.XShared.Delegates;
using MALPlus.XShared.Utils;
using Newtonsoft.Json;
using Settings = MALPlus.XShared.Utils.Settings;

namespace MALPlus.XShared.ViewModels
{
    public class SettingsPageEntry
    {
        public string Header { get; set; }
        public string Subtitle { get; set; }
        public SettingsSymbolsEnum Symbol { get; set; }
        public SettingsPageIndex PageType { get; set; }
    }


    public abstract class SettingsViewModelBase : ViewModelBase
    {
        public abstract event SettingsNavigationRequest NavigationRequest;

protected bool _cachedItemsLoaded;
protected ICommand _requestNavigationCommand;
        protected ICommand _reviewCommand;
        private ICommand _syncFavsCommand;

        public SettingsViewModelBase()
        {
            CachedEntries.CollectionChanged += (sender, args) =>
            {
                if (CachedEntries.Count == 0)
                {
                    EmptyCachedListVisiblity = true;
                }
            };
        }

        public abstract ICommand ReviewCommand { get; }

        public abstract ICommand RequestNavigationCommand { get; }

        public ICommand SyncFavsCommand => _syncFavsCommand ?? (_syncFavsCommand = new RelayCommand(async () =>
        {
            IsSyncFavsButtonEnabled = false;
            await new ProfileQuery().GetProfileData(true, true);
            IsSyncFavsButtonEnabled = true;
        }));

        public ObservableCollection<Tuple<AnimeListDisplayModes, string>> DisplayModes { get; } = new ObservableCollection
            <Tuple<AnimeListDisplayModes, string>>
        {
            new Tuple<AnimeListDisplayModes, string>(AnimeListDisplayModes.IndefiniteList, "Detailed List"),
            new Tuple<AnimeListDisplayModes, string>(AnimeListDisplayModes.IndefiniteGrid, "Grid"),
            new Tuple<AnimeListDisplayModes, string>(AnimeListDisplayModes.IndefiniteCompactList, "Compact List")
        };

        public virtual List<SettingsPageEntry> SettingsPages { get; } = new List<SettingsPageEntry>
        {
            new SettingsPageEntry
            {
                Header = "General",
                Subtitle = "Default filters, theme etc.",
                Symbol = SettingsSymbolsEnum.Setting,
                PageType = SettingsPageIndex.General
            },
            new SettingsPageEntry
            {
                Header = "Caching",
                Subtitle = "Cached data and caching options.",
                Symbol = SettingsSymbolsEnum.SaveLocal,
                PageType = SettingsPageIndex.Caching
            },
            new SettingsPageEntry
            {
                Header = "Calendar",
                Subtitle = "Build options, behaviours etc.",
                Symbol = SettingsSymbolsEnum.CalendarWeek,
                PageType = SettingsPageIndex.Calendar
            },
            new SettingsPageEntry
            {
                Header = "Articles&news",
                Subtitle = "Article&news view settings and live tiles.",
                Symbol = SettingsSymbolsEnum.PreviewLink,
                PageType = SettingsPageIndex.Articles
            },
            new SettingsPageEntry
            {
                Header = "Friends feeds",
                Subtitle = "Customize friends feeds behaviour.",
                Symbol = SettingsSymbolsEnum.ContactInfo,
                PageType = SettingsPageIndex.Feeds
            },
            new SettingsPageEntry
            {
                Header = "Notifications",
                Subtitle = "Notifications types, polling frequency...",
                Symbol = SettingsSymbolsEnum.Important,
                PageType = SettingsPageIndex.Notifications
            },
            new SettingsPageEntry
            {
                Header = "About",
                Subtitle = "Github repo, donations etc.",
                Symbol = SettingsSymbolsEnum.Manage,
                PageType = SettingsPageIndex.About
            },
            new SettingsPageEntry
            {
                Header = "Account",
                Subtitle = "MyAnimelist authentication.",
                Symbol = SettingsSymbolsEnum.Contact,
                PageType = SettingsPageIndex.LogIn
            },
            new SettingsPageEntry
            {
                Header = "Ads",
                Subtitle = "Support me with ads on demand...",
                Symbol = SettingsSymbolsEnum.SwitchApps,
                PageType = SettingsPageIndex.Ads
            },
            new SettingsPageEntry
            {
                Header = "Miscellaneous",
                Subtitle = "Review popup settings...",
                Symbol = SettingsSymbolsEnum.Placeholder,
                PageType = SettingsPageIndex.Misc
            }
        };

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForWatching
        {
            get => DisplayModes[(int)Settings.WatchingDisplayMode];
            set => Settings.WatchingDisplayMode = value.Item1;
        }

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForCompleted
        {
            get => DisplayModes[(int)Settings.CompletedDisplayMode];
            set => Settings.CompletedDisplayMode = value.Item1;
        }

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForOnHold
        {
            get => DisplayModes[(int)Settings.OnHoldDisplayMode];
            set => Settings.OnHoldDisplayMode = value.Item1;
        }

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForDropped
        {
            get => DisplayModes[(int)Settings.DroppedDisplayMode];
            set => Settings.DroppedDisplayMode = value.Item1;
        }

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForPlanned
        {
            get => DisplayModes[(int)Settings.PlannedDisplayMode];
            set => Settings.PlannedDisplayMode = value.Item1;
        }

        public Tuple<AnimeListDisplayModes, string> SelectedDefaultViewForAll
        {
            get => DisplayModes[(int)Settings.AllDisplayMode];
            set => Settings.AllDisplayMode = value.Item1;
        }

        public bool LockDisplayMode
        {
            get => Settings.LockDisplayMode;
            set => Settings.LockDisplayMode = value;
        }

        /// <summary>
        ///     Options for the "Start page" picker. The three values are the ones already
        ///     understood by App.xaml.cs and LogInViewModel.NavigateToStartPage, so a stored
        ///     value can never drift out of the set the navigation understands.
        /// </summary>
        public string[] StartPageOptions { get; } = { "Anime list", "Manga list", "Discover" };

        private static readonly string[] StartPageValues = { "anime", "manga", "discover" };

        public string StartPageOption
        {
            get
            {
                var stored = Settings.DefaultMenuTab ?? "anime";
                var index = Array.IndexOf(StartPageValues, stored);
                return StartPageOptions[index < 0 ? 0 : index];
            }
            set
            {
                var index = StartPageOptions.IndexOf(value);
                if (index < 0)
                    return;
                Settings.DefaultMenuTab = StartPageValues[index];
                RaisePropertyChanged(() => StartPageOption);
            }
        }

        public bool DisplaySeasonWithType
        {
            get => Settings.DisplaySeasonWithType;
            set
            {
                Settings.DisplaySeasonWithType = value;
                RaisePropertyChanged(() => DisplaySeasonWithType);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool ShowFormatTag
        {
            get => Settings.ShowFormatTag;
            set
            {
                Settings.ShowFormatTag = value;
                RaisePropertyChanged(() => ShowFormatTag);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool ShowAiredEpisodes
        {
            get => Settings.ShowAiredEpisodes;
            set
            {
                Settings.ShowAiredEpisodes = value;
                RaisePropertyChanged(() => ShowAiredEpisodes);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool ShowAiringCountdown
        {
            get => Settings.ShowAiringCountdown;
            set
            {
                Settings.ShowAiringCountdown = value;
                RaisePropertyChanged(() => ShowAiringCountdown);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public int PosterScoreDisplayIndex
        {
            get => Settings.PosterScoreDisplay;
            set
            {
                if (value < 0 || value > 2) return;
                Settings.PosterScoreDisplay = value;
                RaisePropertyChanged(() => PosterScoreDisplayIndex);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public string[] PosterScoreDisplayOptions { get; } = new[]
        {
            "Community score (Mean)",
            "Personal score (Your rating)",
            "Hidden (No score chip)"
        };

        public string[] LibrarySortOptions { get; } = new[]
        {
            "Title (A → Z)",
            "Personal score (Highest first)",
            "Community score (Highest first)",
            "Last updated (Most recent first)",
            "Episodes / Volumes (Most first)",
            "Start date (Newest first)",
            "Completion date (Newest first)"
        };

        public int LibrarySortIndex
        {
            get => Settings.AnimeSortOrder switch
            {
                SortOptions.SortTitle => 0,
                SortOptions.SortScore => 1,
                SortOptions.SortGlobalScore => 2,
                SortOptions.SortLastWatched => 3,
                SortOptions.SortWatched => 4,
                SortOptions.SortStartDate => 5,
                SortOptions.SortEndDate => 6,
                _ => 0
            };
            set
            {
                var option = value switch
                {
                    0 => SortOptions.SortTitle,
                    1 => SortOptions.SortScore,
                    2 => SortOptions.SortGlobalScore,
                    3 => SortOptions.SortLastWatched,
                    4 => SortOptions.SortWatched,
                    5 => SortOptions.SortStartDate,
                    6 => SortOptions.SortEndDate,
                    _ => SortOptions.SortTitle
                };
                Settings.AnimeSortOrder = option;
                Settings.MangaSortOrder = option;
                RaisePropertyChanged(() => LibrarySortIndex);
                try
                {
                    if (ViewModelLocator.AnimeList != null)
                    {
                        ViewModelLocator.AnimeList.SetSortOrder(option);
                        ViewModelLocator.AnimeList.InvalidatePreviews();
                        ViewModelLocator.AnimeList.RefreshList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS sort setting refresh failed: " + ex.Message);
                }
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool IndividualSortPerList
        {
            get => Settings.IndividualSortPerList;
            set
            {
                if (Settings.IndividualSortPerList == value) return;
                Settings.IndividualSortPerList = value;
                RaisePropertyChanged(() => IndividualSortPerList);
                InvalidateSettings(resetData: false, refreshTitles: false);
                try
                {
                    if (ViewModelLocator.AnimeList != null)
                    {
                        ViewModelLocator.AnimeList.SetSortOrder(null);
                        ViewModelLocator.AnimeList.InvalidatePreviews();
                        ViewModelLocator.AnimeList.RefreshList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS individual sort toggle failed: " + ex.Message);
                }
            }
        }

        public void ResetPerListSorts()
        {
            Settings.ClearPerListSorts();
            InvalidateSettings(resetData: false, refreshTitles: false);
            try
            {
                if (ViewModelLocator.AnimeList != null)
                {
                    ViewModelLocator.AnimeList.SetSortOrder(null);
                    ViewModelLocator.AnimeList.InvalidatePreviews();
                    ViewModelLocator.AnimeList.RefreshList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS reset per-list sorts failed: " + ex.Message);
            }
        }

        public static long SettingsRevision { get; private set; } = 1;
        public static bool ResetDataOnNextAppearance { get; set; }

        public static void InvalidateSettings(bool resetData = false, bool refreshTitles = false)
        {
            SettingsRevision++;
            if (resetData)
            {
                ResetDataOnNextAppearance = true;
                try
                {
                    ResourceLocator.AnimeLibraryDataStorage?.Reset();
                    ViewModelLocator.AnimeList?.ResetData();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS reset data failed: " + ex.Message);
                }
            }

            RefreshAllLoadedCards(refreshTitles);
        }

        public static void RefreshAllLoadedCards(bool refreshTitles = true)
        {
            void Core()
            {
                try
                {
                    var animeListVm = ViewModelLocator.AnimeList;
                    if (animeListVm != null)
                    {
                        if (animeListVm.DisplayedItems != null)
                        {
                            foreach (var item in animeListVm.DisplayedItems)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (animeListVm.AnimeItems != null)
                        {
                            foreach (var item in animeListVm.AnimeItems)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        animeListVm.InvalidatePreviews();
                    }

                    var mangaListVm = ViewModelLocator.MangaList;
                    if (mangaListVm != null)
                    {
                        if (mangaListVm.DisplayedItems != null)
                        {
                            foreach (var item in mangaListVm.DisplayedItems)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (mangaListVm.AnimeItems != null)
                        {
                            foreach (var item in mangaListVm.AnimeItems)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        mangaListVm.InvalidatePreviews();
                    }

                    var storage = ResourceLocator.AnimeLibraryDataStorage;
                    if (storage != null)
                    {
                        if (storage.AllLoadedAnimeItemAbstractions != null)
                        {
                            foreach (var abs in storage.AllLoadedAnimeItemAbstractions)
                                abs?.ViewModel?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (storage.AllLoadedMangaItemAbstractions != null)
                        {
                            foreach (var abs in storage.AllLoadedMangaItemAbstractions)
                                abs?.ViewModel?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (storage.AllLoadedAuthAnimeItems != null)
                        {
                            foreach (var abs in storage.AllLoadedAuthAnimeItems)
                                abs?.ViewModel?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (storage.AllLoadedAuthMangaItems != null)
                        {
                            foreach (var abs in storage.AllLoadedAuthMangaItems)
                                abs?.ViewModel?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (storage.AllLoadedSeasonalAnimeItems != null)
                        {
                            foreach (var abs in storage.AllLoadedSeasonalAnimeItems)
                                abs?.ViewModel?.RefreshCardDisplayProperties(refreshTitles);
                        }
                    }

                    var searchVm = ViewModelLocator.SearchPage;
                    if (searchVm != null)
                    {
                        if (searchVm.AnimeSearchItemViewModels != null)
                        {
                            foreach (var item in searchVm.AnimeSearchItemViewModels)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                        if (searchVm.MangaSearchItemViewModels != null)
                        {
                            foreach (var item in searchVm.MangaSearchItemViewModels)
                                item?.RefreshCardDisplayProperties(refreshTitles);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS RefreshAllLoadedCards failed: " + ex.Message);
                }
            }

            if (ResourceLocator.DispatcherAdapter != null)
                ResourceLocator.DispatcherAdapter.Run(Core);
            else
                Core();
        }

        public bool AutoDescendingSorting
        {
            get => Settings.AutoDescendingSorting;
            set
            {
                Settings.AutoDescendingSorting = value;
                RaisePropertyChanged(() => AutoDescendingSorting);
                InvalidateSettings(resetData: true, refreshTitles: false);
            }
        }

        public bool PullHigherQualityImages
        {
            get => Settings.PullHigherQualityImages;
            set => Settings.PullHigherQualityImages = value;
        }

        public bool HideFilterSelectionFlyout
        {
            get => Settings.HideFilterSelectionFlyout;
            set => Settings.HideFilterSelectionFlyout = value;
        }

        public bool HideViewSelectionFlyout
        {
            get => Settings.HideViewSelectionFlyout;
            set => Settings.HideViewSelectionFlyout = value;
        }

        public bool HideSortingSelectionFlyout
        {
            get => Settings.HideSortingSelectionFlyout;
            set => Settings.HideSortingSelectionFlyout = value;
        }

        public bool HamburgerAnimeFiltersExpanded
        {
            get => Settings.HamburgerAnimeFiltersExpanded;
            set => Settings.HamburgerAnimeFiltersExpanded = value;
        }

        public bool HamburgerMangaFiltersExpanded
        {
            get => Settings.HamburgerMangaFiltersExpanded;
            set => Settings.HamburgerMangaFiltersExpanded = value;
        }

        public bool HamburgerTopCategoriesExpanded
        {
            get => Settings.HamburgerTopCategoriesExpanded;
            set => Settings.HamburgerTopCategoriesExpanded = value;
        }

        public bool AnimeListEnsureSelectedItemVisibleAfterOffContentCollapse
        {
            get => Settings.AnimeListEnsureSelectedItemVisibleAfterOffContentCollapse;
            set => Settings.AnimeListEnsureSelectedItemVisibleAfterOffContentCollapse = value;
        }

        public bool RatePopUpEnable
        {
            get => Settings.RatePopUpEnable;
            set
            {
                Settings.RatePopUpEnable = value;
                RaisePropertyChanged(() => RatePopUpEnable);
            }
        }

        public bool EnableHearthAnimation
        {
            get => Settings.EnableHearthAnimation;
            set => Settings.EnableHearthAnimation = value;
        }

        public int RatePopUpStartupCounter => 7 - Settings.RatePopUpStartupCounter; //TODO Move this constant upper in dependency hierarchy

        public int AirDayOffset
        {
            get => Settings.AirDayOffset;
            set => Settings.AirDayOffset = value;
        }

        public int AiringNotificationOffset
        {
            get => Settings.AiringNotificationOffset;
            set => Settings.AiringNotificationOffset = value;
        }

        public bool DataSourceAnn
        {
            get => Settings.PrefferedDataSource == DataSource.Ann;
            set
            {
                if (value) Settings.PrefferedDataSource = DataSource.Ann;
            }
        }

        public bool DataSourceHum
        {
            get => Settings.PrefferedDataSource == DataSource.Hummingbird;
            set
            {
                if (value) Settings.PrefferedDataSource = DataSource.Hummingbird;
            }
        }

        public bool DataSourceAnnHum
        {
            get => Settings.PrefferedDataSource == DataSource.AnnHum;
            set
            {
                if (value) Settings.PrefferedDataSource = DataSource.AnnHum;
            }
        }

        public bool SetStartDateOnWatching
        {
            get => Settings.SetStartDateOnWatching;
            set
            {
                if (Settings.SetStartDateOnWatching == value) return;
                Settings.SetStartDateOnWatching = value;
                RaisePropertyChanged(() => SetStartDateOnWatching);
            }
        }

        public bool SetStartDateOnListAdd
        {
            get => Settings.SetStartDateOnListAdd;
            set => Settings.SetStartDateOnListAdd = value;
        }

        public bool SetEndDateOnDropped
        {
            get => Settings.SetEndDateOnDropped;
            set => Settings.SetEndDateOnDropped = value;
        }

        public bool SetEndDateOnCompleted
        {
            get => Settings.SetEndDateOnCompleted;
            set
            {
                if (Settings.SetEndDateOnCompleted == value) return;
                Settings.SetEndDateOnCompleted = value;
                RaisePropertyChanged(() => SetEndDateOnCompleted);
            }
        }

        public bool OverrideValidStartEndDate
        {
            get => Settings.OverrideValidStartEndDate;
            set => Settings.OverrideValidStartEndDate = value;
        }

        public bool HamburgerMenuDefaultPaneState
        {
            get => Settings.HamburgerMenuDefaultPaneState;
            set => Settings.HamburgerMenuDefaultPaneState = value;
        }

        public bool HamburgerHideMangaSection
        {
            get => Settings.HamburgerHideMangaSection;
            set
            {
                ViewModelLocator.GeneralHamburger.MangaSectionVisbility = !value;
                Settings.HamburgerHideMangaSection = value;
            }
        }

        public bool DisplayScoreDialogAfterCompletion
        {
            get => Settings.DisplayScoreDialogAfterCompletion;
            set
            {
                if (Settings.DisplayScoreDialogAfterCompletion == value) return;
                Settings.DisplayScoreDialogAfterCompletion = value;
                RaisePropertyChanged(() => DisplayScoreDialogAfterCompletion);
            }
        }

        public bool CalendarIncludeWatching
        {
            get => Settings.CalendarIncludeWatching;
            set
            {
                Settings.CalendarIncludeWatching = value;
                RaisePropertyChanged(() => CalendarIncludeWatching);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool CalendarIncludePlanned
        {
            get => Settings.CalendarIncludePlanned;
            set
            {
                Settings.CalendarIncludePlanned = value;
                RaisePropertyChanged(() => CalendarIncludePlanned);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool CalendarShowAllAiring
        {
            get => Settings.CalendarShowAllAiring;
            set => Settings.CalendarShowAllAiring = value;
        }

        /// <summary>
        ///     Exposed as a string because the settings Entry is text: a bad entry (empty, zero,
        ///     mid-typing) is ignored instead of writing a broken value into the calendar budget.
        /// </summary>
        public string CalendarMaxItems
        {
            get => Settings.CalendarMaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (int.TryParse(value, out var parsed) && parsed > 0)
                {
                    Settings.CalendarMaxItems = parsed;
                    RaisePropertyChanged();
                    InvalidateSettings(resetData: false, refreshTitles: false);
                }
                else
                    RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     Zone the calendar buckets weekdays in. Empty follows the device. The airing instants
        ///     are absolute and the countdown never changes, only the day column does.
        /// </summary>
        public string CalendarTimeZoneId
        {
            get => Settings.CalendarTimeZoneId;
            set
            {
                if (Settings.CalendarTimeZoneId == value)
                    return;
                Settings.CalendarTimeZoneId = value;
                CalendarTimeZone.Invalidate();
                RaisePropertyChanged();
                RaisePropertyChanged(() => CalendarTimeZoneIndex);
                RaisePropertyChanged(() => CalendarTimeZoneLabel);
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public IReadOnlyList<CalendarTimeZone.Option> CalendarTimeZoneOptions => CalendarTimeZone.Options;

        public string CalendarTimeZoneLabel => CalendarTimeZone.LabelFor(Settings.CalendarTimeZoneId);

        public int CalendarTimeZoneIndex
        {
            get
            {
                var id = Settings.CalendarTimeZoneId;
                var options = CalendarTimeZone.Options;
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i].Id == id)
                        return i;
                }
                return 0;
            }
            set
            {
                var options = CalendarTimeZone.Options;
                if (value < 0 || value >= options.Count)
                    return;
                CalendarTimeZoneId = options[value].Id;
            }
        }

        public bool IsCachingEnabled
        {
            get => Settings.IsCachingEnabled;
            set => Settings.IsCachingEnabled = value;
        }

        public bool CalendarStartOnToday
        {
            get => Settings.CalendarStartOnToday;
            set
            {
                Settings.CalendarStartOnToday = value;
                RaisePropertyChanged();
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool CalendarRemoveEmptyDays
        {
            get => Settings.CalendarRemoveEmptyDays;
            set
            {
                Settings.CalendarRemoveEmptyDays = value;
                RaisePropertyChanged();
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool CalendarStartOnSummary => !Settings.CalendarStartOnToday;

        /// <summary>
        ///     "Start week on" reads as a choice, not a switch: the picker shows the day itself,
        ///     which is what the row is about, and there is no second label to keep in step with
        ///     it. Index 0 is Sunday (the default), 1 is Monday.
        /// </summary>
        public IReadOnlyList<string> CalendarWeekStartOptions { get; } = new[] { "Sunday", "Monday" };

        public int CalendarWeekStartIndex
        {
            get => Settings.CalendarSwitchMonSun ? 1 : 0;
            set
            {
                var mondayFirst = value == 1;
                if (Settings.CalendarSwitchMonSun == mondayFirst)
                    return;
                Settings.CalendarSwitchMonSun = mondayFirst;
                RaisePropertyChanged();
                InvalidateSettings(resetData: false, refreshTitles: false);
            }
        }

        public bool CalendarPullExactAiringTime
        {
            get => Settings.CalendarPullExactAiringTime;
            set => Settings.CalendarPullExactAiringTime = value;
        }

        public bool EnableSwipeToIncDec
        {
            get => Settings.EnableSwipeToIncDec;
            set => Settings.EnableSwipeToIncDec = value;
        }

        public bool ReverseSwipingDirection
        {
            get => Settings.ReverseSwipingDirection;
            set => Settings.ReverseSwipingDirection = value;
        }

        public bool DetailsListReviewsView
        {
            get => Settings.DetailsListReviewsView;
            set => Settings.DetailsListReviewsView = value;
        }

        public bool DetailsListRecomsView
        {
            get => Settings.DetailsListRecomsView;
            set => Settings.DetailsListRecomsView = value;
        }

        public bool ShowPriorities
        {
            get => Settings.ShowPriorities;
            set => Settings.ShowPriorities = value;
        }

        public bool ShowLowPriorities
        {
            get => Settings.ShowLowPriorities;
            set => Settings.ShowLowPriorities = value;
        }

        public bool ArticlesLaunchExternalLinks
        {
            get => Settings.ArticlesLaunchExternalLinks;
            set => Settings.ArticlesLaunchExternalLinks = value;
        }

        public bool ArticlesDisplayScrollBar
        {
            get => Settings.ArticlesDisplayScrollBar;
            set => Settings.ArticlesDisplayScrollBar = value;
        }

        public static Action OnAmoledThemeChanged { get; set; }

        public List<string> AppThemeOptions { get; } = new()
        {
            "Midnight Blue",
            "Dark AMOLED",
            "Real Dark AMOLED"
        };

        public int AppThemeIndex
        {
            get => Settings.AppTheme;
            set
            {
                if (Settings.AppTheme == value)
                    return;

                Settings.AppTheme = value;
                RaisePropertyChanged(() => AppThemeIndex);
                RaisePropertyChanged(() => DarkThemeAmoled);
                RaisePropertyChanged(() => DarkThemeAmoledRealDark);
                OnAmoledThemeChanged?.Invoke();
            }
        }

        public bool DarkThemeAmoled
        {
            get => Settings.DarkThemeAmoled;
            set
            {
                if (Settings.DarkThemeAmoled == value)
                    return;

                Settings.DarkThemeAmoled = value;
                RaisePropertyChanged(() => DarkThemeAmoled);
                OnAmoledThemeChanged?.Invoke();
            }
        }

        public bool DarkThemeAmoledRealDark
        {
            get => Settings.DarkThemeAmoledRealDark;
            set
            {
                if (Settings.DarkThemeAmoledRealDark == value)
                    return;

                Settings.DarkThemeAmoledRealDark = value;
                RaisePropertyChanged(() => DarkThemeAmoledRealDark);
                OnAmoledThemeChanged?.Invoke();
            }
        }

        public bool PreferEnglishTitles
        {
            get => Settings.PreferEnglishTitles;
            set
            {
                if (Settings.PreferEnglishTitles == value)
                    return;

                Settings.PreferEnglishTitles = value;
                RaisePropertyChanged(() => PreferEnglishTitles);
                InvalidateSettings(resetData: false, refreshTitles: true);
            }
        }

        /// <summary>
        ///     Which content tiers the app is allowed to show. Defaults to the strictest, which is what
        ///     the app has always done, so nothing appears that did not appear before the setting.
        /// </summary>
        public ContentFilterMode ContentFilter
        {
            get => Settings.ContentFilter;
            set => Settings.ContentFilter = value;
        }

        /// <summary>
        ///     The same three choices as an index, because a picker binds to a number and not to an
        ///     enum name.
        /// </summary>
        /// <remarks>
        ///     The enum order IS the order the picker shows, and the two have to stay in step: Safe is
        ///     the first entry in the picker and also the value the app falls back to, so a bad index
        ///     lands on the strict one rather than on something the reader did not choose.
        /// </remarks>
        public int ContentFilterIndex
        {
            get => (int)Settings.ContentFilter;
            set
            {
                if (value < (int)ContentFilterMode.Safe || value > (int)ContentFilterMode.Unfiltered)
                    value = (int)ContentFilterMode.Safe;
                if (Settings.ContentFilter != (ContentFilterMode)value)
                {
                    Settings.ContentFilter = (ContentFilterMode)value;
                    RaisePropertyChanged(() => ContentFilterIndex);
                    RaisePropertyChanged(() => ContentFilter);
                    InvalidateSettings(resetData: true, refreshTitles: false);
                }
            }
        }

        public bool MakeGridItemsSmaller
        {
            get => Settings.MakeGridItemsSmaller;
            set => Settings.MakeGridItemsSmaller = value;
        }

        public bool SyncFavsFromTimeToTime
        {
            get => Settings.SyncFavsFromTimeToTime;
            set => Settings.SyncFavsFromTimeToTime = value;
        }

        public bool EnsureRandomizerAlwaysSelectsWinner
        {
            get => Settings.EnsureRandomizerAlwaysSelectsWinner;
            set => Settings.EnsureRandomizerAlwaysSelectsWinner = value;
        }

        public bool EnableImageCache
        {
            get => Settings.EnableImageCache;
            set => Settings.EnableImageCache = value;
        }

        public bool PullPeekPostsOnStartup
        {
            get => Settings.PullPeekPostsOnStartup;
            set => Settings.PullPeekPostsOnStartup = value;
        }

        public bool ForumsSearchOnCopy
        {
            get => Settings.ForumsSearchOnCopy;
            set => Settings.ForumsSearchOnCopy = value;
        }

        public AnimeStatus DefaultStatusAfterAdding
        {
            get => Settings.DefaultStatusAfterAdding;
            set => Settings.DefaultStatusAfterAdding = value;
        }

        public bool ForceSearchIntoOffPage
        {
            get => Settings.ForceSearchIntoOffPage;
            set => Settings.ForceSearchIntoOffPage = value;
        }

        public bool WatchedEpsPromptEnable
        {
            get => Settings.WatchedEpsPromptEnable;
            set => Settings.WatchedEpsPromptEnable = value;
        }

        public bool WatchedEpsPromptProceedOnDisabled
        {
            get => Settings.WatchedEpsPromptProceedOnDisabled;
            set => Settings.WatchedEpsPromptProceedOnDisabled = value;
        }

        public bool StatusPromptEnable
        {
            get => Settings.StatusPromptEnable;
            set => Settings.StatusPromptEnable = value;
        }

        public bool StatusPromptProceedOnDisabled
        {
            get => Settings.StatusPromptProceedOnDisabled;
            set => Settings.StatusPromptProceedOnDisabled = value;
        }

        public bool EnableShareButton
        {
            get => Settings.EnableShareButton;
            set
            {
                Settings.EnableShareButton = value;
                RaisePropertyChanged();
            }
        }

        public int ShareTimerDuration
        {
            get => Settings.ShareTimerDuration;
            set
            {
                Settings.ShareTimerDuration = value;
                RaisePropertyChanged();
            }
        }

        public string ShareTemplateWatched
        {
            get => Settings.ShareTemplateWatched;
            set
            {
                Settings.ShareTemplateWatched = value;
                RaisePropertyChanged();
            }
        }

        public string ShareTemplateRead
        {
            get => Settings.ShareTemplateRead;
            set
            {
                Settings.ShareTemplateRead = value;
                RaisePropertyChanged();
            }
        }

        public string ShareTemplateCompleted
        {
            get => Settings.ShareTemplateCompleted;
            set
            {
                Settings.ShareTemplateCompleted = value;
                RaisePropertyChanged();
            }
        }

        public string ShareTemplateRecommend
        {
            get => Settings.ShareTemplateRecommend;
            set
            {
                Settings.ShareTemplateRecommend = value;
                RaisePropertyChanged();
            }
        }

        public string ShareCustomSuffix
        {
            get => Settings.ShareCustomSuffix;
            set
            {
                Settings.ShareCustomSuffix = value;
                RaisePropertyChanged();
            }
        }

        public void ResetShareTemplatesToDefault()
        {
            ShareTimerDuration = Settings.DefaultShareTimerDuration;
            ShareTemplateWatched = Settings.DefaultShareTemplateWatched;
            ShareTemplateRead = Settings.DefaultShareTemplateRead;
            ShareTemplateCompleted = Settings.DefaultShareTemplateCompleted;
            ShareTemplateRecommend = Settings.DefaultShareTemplateRecommend;
            ShareCustomSuffix = Settings.DefaultShareCustomSuffix;
        }

        public bool DisplayUnsetScores
        {
            get => Settings.DisplayUnsetScores;
            set => Settings.DisplayUnsetScores = value;
        }

        public bool HideGlobalScoreInDetailsWhenNotRated
        {
            get => Settings.HideGlobalScoreInDetailsWhenNotRated;
            set => Settings.HideGlobalScoreInDetailsWhenNotRated = value;
        }

        public bool DontAskToMoveOnHoldEntries
        {
            get => Settings.DontAskToMoveOnHoldEntries;
            set => Settings.DontAskToMoveOnHoldEntries = value;
        }

        public bool HideDecrementButtons
        {
            get => Settings.HideDecrementButtons;
            set => Settings.HideDecrementButtons = value;
        }

        public bool SqueezeOneMoreGridItem
        {
            get => Settings.SqueezeOneMoreGridItem;
            set => Settings.SqueezeOneMoreGridItem = value;
        }

        public bool MangaFocusVolumes
        {
            get => Settings.MangaFocusVolumes;
            set
            {
                if (Settings.MangaFocusVolumes == value) return;
                Settings.MangaFocusVolumes = value;
                RaisePropertyChanged(() => MangaFocusVolumes);
                InvalidateSettings(resetData: false, refreshTitles: false);
                try
                {
                    var storage = ResourceLocator.AnimeLibraryDataStorage;
                    if (storage != null)
                    {
                        if (storage.AllLoadedMangaItemAbstractions != null)
                        {
                            foreach (var item in storage.AllLoadedMangaItemAbstractions)
                            {
                                if (item?.LoadedModel == true)
                                    item.ViewModel?.MangaFocusChanged(value);
                            }
                        }
                        if (storage.OthersAbstractions != null)
                        {
                            foreach (var pair in storage.OthersAbstractions)
                            {
                                if (pair.Value?.Item2 != null)
                                {
                                    foreach (var item in pair.Value.Item2)
                                    {
                                        if (item?.LoadedModel == true)
                                            item.ViewModel?.MangaFocusChanged(value);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS MangaFocusVolumes update failed: " + ex.Message);
                }
            }
        }

        public bool MalApiDependatedntSectionsVisibility
            => Settings.SelectedApiType == ApiType.Mal;

        public bool HumApiDependatedntSectionsEnabled
            => Settings.SelectedApiType != ApiType.Mal;

        public abstract void LoadCachedEntries();

        #region Notifications

        private ICommand _SetNotificationsRefreshTimeCommand;

        public ICommand SetNotificationsRefreshTimeCommand
            =>
            _SetNotificationsRefreshTimeCommand ??
            (_SetNotificationsRefreshTimeCommand = new RelayCommand<int>(i => NotificationsRefreshTime = i));

        private ICommand _callBackgroundTaskCommand;

        public ICommand CallBackgroundTaskCommand
            =>
            _callBackgroundTaskCommand ??
            (_callBackgroundTaskCommand = new RelayCommand(async () =>
            {
                ResourceLocator.NotificationsTaskManager.CallTask(BgTasks.Notifications);
                IsCallNotificationsButtonEnabled = false;
                await Task.Delay(60000);
                IsCallNotificationsButtonEnabled = true;
            }));

        private ICommand _resetSeenNotificationsCommand;

        public ICommand ResetSeenNotificationsCommand
            =>
            _resetSeenNotificationsCommand ??
            (_resetSeenNotificationsCommand = new RelayCommand(() =>
            {
                ResourceLocator.ApplicationDataService[nameof(RoamingDataTypes.ReadNotifications)] = string.Empty;
            }));

        private bool _isCallNotificationsButtonEnabled = true;

        public bool IsCallNotificationsButtonEnabled
        {
            get => _isCallNotificationsButtonEnabled;
            set
            {
                _isCallNotificationsButtonEnabled = value;
                RaisePropertyChanged(() => IsCallNotificationsButtonEnabled);
            }
        }

        public bool EnableNotifications
        {
            get => Settings.EnableNotifications;
            set
            {
                Settings.EnableNotifications = value;
                if (value)
                    ResourceLocator.NotificationsTaskManager.StartTask(BgTasks.Notifications);
                else
                    ResourceLocator.NotificationsTaskManager.StopTask(BgTasks.Notifications);
            }
        }

        public bool NotificationCheckInRuntime
        {
            get => Settings.NotificationCheckInRuntime;
            set
            {
                Settings.NotificationCheckInRuntime = value;
                if (value)
                    ResourceLocator.SchdeuledJobsManger.StartJob(ScheduledJob.FetchNotifications, 5,
                        () => ResourceLocator.NotificationsTaskManager.CallTask(BgTasks.Notifications));
                else
                    ResourceLocator.SchdeuledJobsManger.StopJob(ScheduledJob.FetchNotifications);
            }
        }

        public int NotificationsRefreshTime
        {
            get => Settings.NotificationsRefreshTime;
            set => Settings.NotificationsRefreshTime = value;
        }

        public MalNotificationsTypes EnabledNotificationTypes
        {
            get => Settings.EnabledNotificationTypes;
            set
            {
                Settings.EnabledNotificationTypes = value;
                ResourceLocator.NotificationsTaskManager.StartTask(BgTasks.Notifications);
            }
        }

        #endregion Notifications

        #region RecentlyMovedToMvvm

        private readonly ObservableCollection<CachedEntryModel> _cachedEntries =
            new ObservableCollection<CachedEntryModel>();

        public ObservableCollection<CachedEntryModel> CachedEntries
        {
            get
            {
                LoadCachedEntries();
                return _cachedEntries;
            }
        }

        private bool _emptyCachedListVisiblity;

        public bool EmptyCachedListVisiblity
        {
            get => _emptyCachedListVisiblity;
            set
            {
                _emptyCachedListVisiblity = value;
                RaisePropertyChanged(() => EmptyCachedListVisiblity);
            }
        }

        private bool _removeAllCachedDataButtonVisibility;

        public bool RemoveAllCachedDataButtonVisibility
        {
            get => _removeAllCachedDataButtonVisibility;
            set
            {
                _removeAllCachedDataButtonVisibility = value;
                RaisePropertyChanged(() => RemoveAllCachedDataButtonVisibility);
            }
        }

        private string _totalFilesCached = "N/A";

        public string TotalFilesCached
        {
            get => _totalFilesCached;
            set
            {
                _totalFilesCached = value;
                RaisePropertyChanged(() => TotalFilesCached);
            }
        }

        private bool _isSyncFavsButtonEnabled = true;
        private bool _squeezeOneMoreGridItem;

        public bool IsSyncFavsButtonEnabled
        {
            get => _isSyncFavsButtonEnabled;
            set
            {
                _isSyncFavsButtonEnabled = value;
                RaisePropertyChanged(() => IsSyncFavsButtonEnabled);
            }
        }

        #endregion RecentlyMovedToMvvm

        #region Ads

        public event EmptyEventHander OnAdsMinutesPerDayChanged;

        public bool AdsEnable
        {
            get => Settings.AdsEnable;
            set
            {
                Settings.AdsEnable = value;
                ViewModelLocator.GeneralHamburger.UpdateBottomMargin();
                OnAdsMinutesPerDayChanged?.Invoke();
            }
        }

        public int AdsSecondsPerDay
        {
            get => Settings.AdsSecondsPerDay;
            set
            {
                Settings.AdsSecondsPerDay = value;
                OnAdsMinutesPerDayChanged?.Invoke();
            }
        }

        #endregion Ads

        #region Feeds

        public bool FeedsIncludePinnedProfiles
        {
            get => Settings.FeedsIncludePinnedProfiles;
            set => Settings.FeedsIncludePinnedProfiles = value;
        }

        public int FeedsMaxEntries
        {
            get => Settings.FeedsMaxEntries;
            set => Settings.FeedsMaxEntries = value;
        }

        public int FeedsMaxEntryAge
        {
            get => Settings.FeedsMaxEntryAge;
            set => Settings.FeedsMaxEntryAge = value;
        }

        #endregion Feeds
    }
}