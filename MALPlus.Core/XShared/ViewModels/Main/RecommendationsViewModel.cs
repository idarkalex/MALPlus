using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.ViewModels.Main
{
    public enum RecommendationsPageWorkMode
    {
        [EnumUtilities.Description("Top Anime")]
        TopAnime,
        [EnumUtilities.Description("Recent Anime")]
        Anime,
        [EnumUtilities.Description("Top Manga")]
        TopManga,
        [EnumUtilities.Description("Recent Manga")]
        Manga,
        [EnumUtilities.Description("Anime Suggestions")]
        PersonalizedAnime,
        [EnumUtilities.Description("Manga Suggestions")]
        PersonalizedManga,
    }

    public class RecommendationsViewModel : ViewModelBase
    {
        public class XPivotItem
        {
            public object Header { get; set; }
            public object Content { get; set; }
        }

        private bool _loading;
        private int _pivotItemIndex;

        private List<RecommendationData> _topAnimeItems;
        private List<RecommendationData> _recentAnimeItems;
        private List<RecommendationData> _topMangaItems;
        private List<RecommendationData> _recentMangaItems;

        private List<AnimeItemViewModel> _personalizedAnimeItems;
        private List<AnimeItemViewModel> _personalizedMangaItems;

        private RecommendationsPageWorkMode _currentWorkMode;
        private ICommand _switchWorkModeCommand;

        public List<RecommendationData> TopAnimeItems
        {
            get => _topAnimeItems;
            set
            {
                _topAnimeItems = value;
                if (CurrentWorkMode == RecommendationsPageWorkMode.TopAnime)
                {
                    CurrentIndex = 0;
                    RaiseCurrentPair();
                }
                RaisePropertyChanged(() => TopAnimeItems);
            }
        }

        public List<RecommendationData> RecommendationAnimeItems
        {
            get { return _recentAnimeItems; }
            set
            {
                _recentAnimeItems = value;
                if (CurrentWorkMode == RecommendationsPageWorkMode.Anime)
                {
                    CurrentIndex = 0;
                    RaiseCurrentPair();
                }
                RaisePropertyChanged(() => RecommendationAnimeItems);
            }
        }

        public List<RecommendationData> TopMangaItems
        {
            get => _topMangaItems;
            set
            {
                _topMangaItems = value;
                if (CurrentWorkMode == RecommendationsPageWorkMode.TopManga)
                {
                    CurrentIndex = 0;
                    RaiseCurrentPair();
                }
                RaisePropertyChanged(() => TopMangaItems);
            }
        }

        public List<RecommendationData> RecommendationMangaItems
        {
            get { return _recentMangaItems; }
            set
            {
                _recentMangaItems = value;
                if (CurrentWorkMode == RecommendationsPageWorkMode.Manga)
                {
                    CurrentIndex = 0;
                    RaiseCurrentPair();
                }
                RaisePropertyChanged(() => RecommendationMangaItems);
            }
        }

        private List<RecommendationData> CurrentItems
        {
            get
            {
                return CurrentWorkMode switch
                {
                    RecommendationsPageWorkMode.TopAnime => _topAnimeItems,
                    RecommendationsPageWorkMode.Anime => _recentAnimeItems,
                    RecommendationsPageWorkMode.TopManga => _topMangaItems,
                    RecommendationsPageWorkMode.Manga => _recentMangaItems,
                    _ => _topAnimeItems
                };
            }
        }

        /// <summary>
        ///     The entry the recommendation is based on, the "If you liked..." half.
        /// </summary>
        public AnimeItemViewModel DependentItem { get; private set; }

        /// <summary>
        ///     The entry being recommended, the "...then you might like" half.
        /// </summary>
        public AnimeItemViewModel RecommendedItem { get; private set; }

        /// <summary>
        ///     Which pair is on screen.
        /// </summary>
        /// <remarks>
        ///     The page shows one recommendation at a time and walks through them with its own arrows
        ///     rather than scrolling, so the position is part of the view model's state and both halves
        ///     of the pair are rebuilt from it whenever it moves.
        /// </remarks>
        public int CurrentIndex
        {
            get => _currentIndex;
            private set
            {
                _currentIndex = value;
                RaisePropertyChanged(nameof(CurrentIndex));
                RaisePropertyChanged(nameof(CurrentNumber));
                RaisePropertyChanged(nameof(TotalCount));
                RaisePropertyChanged(nameof(CanGoBack));
                RaisePropertyChanged(nameof(CanGoForward));
                RaisePropertyChanged(nameof(PreviousOpacity));
                RaisePropertyChanged(nameof(NextOpacity));
            }
        }

        private int _currentIndex;

        /// <summary>Position for the "3 / 100" readout. One based, because it is shown to a person.</summary>
        public int CurrentNumber => CurrentItems == null || CurrentItems.Count == 0 ? 0 : CurrentIndex + 1;

        public int TotalCount => CurrentItems?.Count ?? 0;

        /// <summary>
        ///     False on the first pair, which is what puts a disabled previous arrow there rather than
        ///     one that silently does nothing.
        /// </summary>
        public bool CanGoBack => CurrentIndex > 0;

        /// <summary>False on the last pair, so the next arrow greys out at the end of the feed.</summary>
        public bool CanGoForward => CurrentItems != null && CurrentIndex < CurrentItems.Count - 1;

        /// <summary>
        ///     Opacity for the two arrows.
        /// </summary>
        /// <remarks>
        ///     Separate from the flags because the arrows are images on a Border rather than buttons,
        ///     and a Border has no IsEnabled: it is the icon that has to look spent, not the control.
        ///     The tap is still guarded in <see cref="Step" />, so a press on a spent arrow does
        ///     nothing either way.
        /// </remarks>
        public double PreviousOpacity => CanGoBack ? 1d : 0.3d;

        public double NextOpacity => CanGoForward ? 1d : 0.3d;

        public ICommand GoToNextItemCommand => _goToNextItemCommand ?? (_goToNextItemCommand =
            new RelayCommand(() => Step(1)));

        public ICommand GoToPreviousItemCommand => _goToPreviousItemCommand ?? (_goToPreviousItemCommand =
            new RelayCommand(() => Step(-1)));

        private ICommand _goToNextItemCommand;
        private ICommand _goToPreviousItemCommand;

        private void Step(int delta)
        {
            var items = CurrentItems;
            if (items == null || items.Count == 0)
                return;
            var next = CurrentIndex + delta;
            if (next < 0 || next >= items.Count)
                return;
            CurrentIndex = next;
            RaiseCurrentPair();
        }

        private void RaiseCurrentPair()
        {
            DependentItem = null;
            RecommendedItem = null;
            CurrentRecommendation = null;

            var items = CurrentItems;
            if (items != null && CurrentIndex >= 0 && CurrentIndex < items.Count)
            {
                CurrentRecommendation = items[CurrentIndex];
                BuildPair(CurrentRecommendation);
            }

            RaisePropertyChanged(nameof(DependentItem));
            RaisePropertyChanged(nameof(RecommendedItem));
            RaisePropertyChanged(nameof(CurrentRecommendation));
            RaisePropertyChanged(nameof(HasAny));
            RaisePropertyChanged(nameof(CounterText));
            Console.WriteLine($"MALPLUS recsVM: par indice={CurrentIndex} dep={(DependentItem == null ? "null" : DependentItem.Id.ToString())} rec={(RecommendedItem == null ? "null" : RecommendedItem.Id.ToString())}");
        }

        /// <summary>
        ///     The pair on screen, whole. The text under the covers comes from here rather than from a
        ///     separate copy of the description on each of the two entries.
        /// </summary>
        public RecommendationData CurrentRecommendation { get; private set; }

        /// <summary>
        ///     Whether there is anything to show at all, which is what the covers, the text and the
        ///     arrows hang off. A count of zero is its own case and the page shows an empty state
        ///     instead of a pair of empty covers.
        /// </summary>
        public bool HasAny => DependentItem != null && RecommendedItem != null;

        /// <summary>
        ///     The "3 / 100" readout. Built here because a XAML StringFormat only ever takes the one
        ///     value it is bound to, and this needs both halves of the position.
        /// </summary>
        public string CounterText =>
            TotalCount == 0 ? string.Empty : $"{CurrentIndex + 1} / {TotalCount}";

        private void BuildPair(RecommendationData data)
        {
            DependentItem = ToItem(data, data.DependentId, data.DependentTitle, data.DependentImageUrl, true);
            RecommendedItem =
                ToItem(data, data.RecommendationId, data.RecommendationTitle, data.RecommendationImageUrl, false);
        }

        private AnimeItemViewModel ToItem(RecommendationData data, int id, string title, string imageUrl, bool isDependent)
        {
            if (id <= 0)
                return null;

            var type = isDependent ? data.DependentType : data.RecommendationType;
            var score = isDependent ? data.DependentScore : data.RecommendationScore;
            var episodes = isDependent ? data.DependentAllEpisodes : data.RecommendationAllEpisodes;

            var model = MALPlus.XShared.Utils.AnimeItemFactory.Create(
                id,
                title,
                imageUrl,
                type: type,
                score: score,
                allEpisodes: episodes,
                isManga: !data.IsAnime);

            if (model != null)
                model.UpdateButtonsVisibility = false;
            return model;
        }

        public List<AnimeItemViewModel> PersonalizedAnimeItems
        {
            get { return _personalizedAnimeItems; }
            set
            {
                _personalizedAnimeItems = value;
                RaisePropertyChanged(() => PersonalizedAnimeItems);
            }
        }

        public List<AnimeItemViewModel> PersonalizedMangaItems
        {
            get { return _personalizedMangaItems; }
            set
            {
                _personalizedMangaItems = value;
                RaisePropertyChanged(() => PersonalizedMangaItems);
            }
        }

        public bool Loading
        {
            get { return _loading; }
            set
            {
                _loading = value;
                RaisePropertyChanged(() => Loading);
            }
        }

        public int PivotItemIndex
        {
            get { return _pivotItemIndex; }
            set
            {
                _pivotItemIndex = value;
                if (!Loading)
                    RaisePropertyChanged(() => PivotItemIndex);
            }
        }

        public RecommendationsPageWorkMode CurrentWorkMode
        {
            get { return _currentWorkMode; }
            set
            {
                // The mode is the page's trigger for loading, so writing the same mode has to load the
                // same as writing a different one. Returning early here meant an initial assign of Anime
                // never reached PopulateData, and since the field already defaults to Anime, opening the
                // page showed an empty list. Tapping Manga worked only because that _was_ a new value,
                // which made the first tab look like the broken one.
                _currentWorkMode = value;
                RaisePropertyChanged(() => CurrentWorkMode);
                PopulateData();
            }
        }

        public ICommand SwitchWorkModeCommand => _switchWorkModeCommand ?? (_switchWorkModeCommand =
                                                     new RelayCommand<RecommendationsPageWorkMode>(
                                                         mode => CurrentWorkMode = mode));

        public double MaxWidth => AnimeItemViewModel.MaxWidth;

        public async void PopulateData(bool force = false)
        {
            ViewModelLocator.GeneralMain.CurrentStatus = CurrentWorkMode.GetDescription();

            if (Loading)
                return;

            if (!force)
            {
                switch (CurrentWorkMode)
                {
                    case RecommendationsPageWorkMode.TopAnime:
                        if (_topAnimeItems != null)
                        {
                            CurrentIndex = 0;
                            RaiseCurrentPair();
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    case RecommendationsPageWorkMode.Anime:
                        if (_recentAnimeItems != null)
                        {
                            CurrentIndex = 0;
                            RaiseCurrentPair();
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    case RecommendationsPageWorkMode.TopManga:
                        if (_topMangaItems != null)
                        {
                            CurrentIndex = 0;
                            RaiseCurrentPair();
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    case RecommendationsPageWorkMode.Manga:
                        if (_recentMangaItems != null)
                        {
                            CurrentIndex = 0;
                            RaiseCurrentPair();
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    case RecommendationsPageWorkMode.PersonalizedAnime:
                        if (PersonalizedAnimeItems != null)
                        {
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    case RecommendationsPageWorkMode.PersonalizedManga:
                        if (PersonalizedMangaItems != null)
                        {
                            RaisePropertyChanged(() => CurrentWorkMode);
                            return;
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                RaisePropertyChanged(() => PivotItemIndex);
            }

            Loading = true;
            try
            {
                if (CurrentWorkMode == RecommendationsPageWorkMode.TopAnime ||
                    CurrentWorkMode == RecommendationsPageWorkMode.Anime ||
                    CurrentWorkMode == RecommendationsPageWorkMode.TopManga ||
                    CurrentWorkMode == RecommendationsPageWorkMode.Manga)
                {
                    var isAnime = CurrentWorkMode == RecommendationsPageWorkMode.TopAnime ||
                                  CurrentWorkMode == RecommendationsPageWorkMode.Anime;
                    var isTop = CurrentWorkMode == RecommendationsPageWorkMode.TopAnime ||
                                CurrentWorkMode == RecommendationsPageWorkMode.TopManga;

                    List<RecommendationData> data = null;
                    await Task.Run(async () => data =
                        await new AnimeRecomendationsQuery(isAnime, isTop)
                            .GetRecomendationsData());

                    // Reached only when the page waits for the list to appear, and it clears the
                    // busy state on the way out. Returning from inside the try used to leave the
                    // overlay up over a searchable page, with the flag nothing was left to reset.
                    if (data == null)
                    {
                        data = new List<RecommendationData>();
                    }

                    switch (CurrentWorkMode)
                    {
                        case RecommendationsPageWorkMode.TopAnime:
                            _topAnimeItems = data;
                            break;
                        case RecommendationsPageWorkMode.Anime:
                            _recentAnimeItems = data;
                            break;
                        case RecommendationsPageWorkMode.TopManga:
                            _topMangaItems = data;
                            break;
                        case RecommendationsPageWorkMode.Manga:
                            _recentMangaItems = data;
                            break;
                    }

                    CurrentIndex = 0;
                    RaiseCurrentPair();

                    Console.WriteLine($"MALPLUS recsVM: modo={CurrentWorkMode} pares={(CurrentItems == null ? -1 : CurrentItems.Count)}");
                    RaisePropertyChanged(() => PivotItemIndex);
                }
                else
                {
                    var data =
                        await new AnimePersonalizedRecommendationsQuery(CurrentWorkMode ==
                                                                        RecommendationsPageWorkMode.PersonalizedAnime)
                            .GetPersonalizedRecommendations();
                    if (data == null)
                        return;
                    if (CurrentWorkMode == RecommendationsPageWorkMode.PersonalizedAnime)
                    {
                        PersonalizedAnimeItems =
                            data.Select(recommendationData => new AnimeItemAbstraction(false, new AnimeLibraryItemData
                            {
                                Title = recommendationData.Title,
                                Id = recommendationData.Id,
                                ImgUrl = recommendationData.ImgUrl
                            }).ViewModel).ToList();
                        PersonalizedAnimeItems.ForEach(model =>
                        {
                            model.UpdateButtonsVisibility = false;
                        });
                    }
                    else
                    {
                        PersonalizedMangaItems =
                            data.Select(recommendationData => new AnimeItemAbstraction(false, new MangaLibraryItemData
                            {
                                Title = recommendationData.Title,
                                Id = recommendationData.Id,
                                ImgUrl = recommendationData.ImgUrl
                            }).ViewModel).ToList();
                        PersonalizedMangaItems.ForEach(model =>
                        {
                            model.UpdateButtonsVisibility = false;
                        });
                    }
                }
            }
            finally
            {
                // Loading doubles as the re-entrancy guard above, so leaving it
                // set would block every later attempt.
                Loading = false;
            }

        }
    }
}