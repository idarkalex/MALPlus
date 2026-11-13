using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Comm.Profile;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.ViewModels.Main
{
    public class HistoryViewModel : ViewModelBase
    {
        private readonly IAnimeLibraryDataStorage _animeLibraryDataStorage;

        public HistoryViewModel(IAnimeLibraryDataStorage animeLibraryDataStorage)
        {
            _animeLibraryDataStorage = animeLibraryDataStorage;
            LoadMoreCommand = new RelayCommand(async () => await LoadMoreHistory());
        }

        private Dictionary<string, List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>> _history;

        public Dictionary<string, List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>> History
        {
            get { return _history; }
            set
            {
                _history = value;
                RaisePropertyChanged(() => History);
            }
        }

        private bool _loadingVisibility;

        public bool LoadingVisibility
        {
            get { return _loadingVisibility; }
            set
            {
                _loadingVisibility = value;
                RaisePropertyChanged(() => LoadingVisibility);
            }
        }

        private bool _historyEmptyNoticeVisibility = false;

        public bool HistoryEmptyNoticeVisibility
        {
            get { return _historyEmptyNoticeVisibility; }
            set
            {
                _historyEmptyNoticeVisibility = value;
                RaisePropertyChanged(() => HistoryEmptyNoticeVisibility);
            }
        }

        private HistoryNavigationArgs _prevArgs;

        /// <summary>
        ///     Whether another page of history may exist, for the "load more" button.
        /// </summary>
        private bool _historyHasMore = true;

        /// <summary>
        ///     Number of entries already read, which is what the next page starts from.
        /// </summary>
        private int _historyOffset;

        /// <summary>
        ///     Whether there is more history to read. Bound by the page to decide whether to offer it.
        /// </summary>
        public bool HistoryHasMore => _historyHasMore;

        /// <summary>
        ///     Whether a page is on its way, for the footer spinner only.
        /// </summary>
        /// <remarks>
        ///     Deliberately not <see cref="LoadingVisibility" />. That one is bound to the full page
        ///     overlay and to the pull to refresh's own spinner, so reading another page through it put
        ///     a black scrim over a list that was already on screen and fully readable, once per page.
        /// </remarks>
        public bool LoadingMore
        {
            get { return _loadingMore; }
            private set
            {
                _loadingMore = value;
                RaisePropertyChanged(() => LoadingMore);
            }
        }

        private bool _loadingMore;

        /// <summary>
        ///     Reads the next page. Bound to the list's own remaining items threshold, and also called
        ///     from the page's scroll handler, because that threshold fires once per session on a
        ///     collection that is still growing and never re-arms on its own.
        /// </summary>
        public ICommand LoadMoreCommand { get; }

        public async void Init(HistoryNavigationArgs args, bool force = false)
        {
            if (args == null && force)
                args = _prevArgs;

            args = args ?? new HistoryNavigationArgs { Source = Credentials.UserName };

            if (!force && _prevArgs?.Source == args.Source)
                return;

            _prevArgs = args;
            _historyOffset = 0;
            _historyHasMore = true;
            History = null;

            LoadingVisibility = true;
            try
            {
                var data = await BuildHistoryPage(args, 0);
                History = data;
                _historyOffset = CountEntries(data);
                // MyAnimeList history endpoint /history/{user} returns all activity within its 3-week window.
                _historyHasMore = false;
                RaisePropertyChanged(() => HistoryHasMore);
            }
            finally
            {
                LoadingVisibility = false;
            }
        }

        /// <summary>
        ///     Reads the next page of history and adds it to what is already shown.
        /// </summary>
        public async Task<bool> LoadMoreHistory()
        {
            if (!_historyHasMore || LoadingMore)
                return false;

            LoadingMore = true;
            try
            {
                var args = _prevArgs ?? new HistoryNavigationArgs { Source = Credentials.UserName };
                var page = await BuildHistoryPage(args, _historyOffset);
                if (page == null || page.Count == 0)
                {
                    _historyHasMore = false;
                    RaisePropertyChanged(() => HistoryHasMore);
                    return false;
                }

                var merged = History;
                bool anyNew = false;
                if (merged == null)
                {
                    merged = page;
                    anyNew = true;
                }
                else
                {
                    foreach (var pair in page)
                    {
                        if (merged.TryGetValue(pair.Key, out var existing))
                        {
                            var existingIds = new HashSet<int>(existing.Where(x => x?.Item1 != null).Select(x => x.Item1.Id));
                            foreach (var item in pair.Value)
                            {
                                if (item?.Item1 != null && existingIds.Add(item.Item1.Id))
                                {
                                    existing.Add(item);
                                    anyNew = true;
                                }
                            }
                        }
                        else
                        {
                            merged[pair.Key] = pair.Value;
                            anyNew = true;
                        }
                    }
                }

                if (!anyNew)
                {
                    _historyHasMore = false;
                    RaisePropertyChanged(() => HistoryHasMore);
                    return false;
                }

                _historyOffset += CountEntries(page);
                RaisePropertyChanged(() => History);
                RaisePropertyChanged(() => HistoryHasMore);
                return true;
            }
            finally
            {
                LoadingMore = false;
            }
        }

        /// <summary>
        ///     Reads one page of history into the shape the page binds.
        /// </summary>
        private async Task<Dictionary<string,
            List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>>> BuildHistoryPage(
            HistoryNavigationArgs args, int offset)
        {
            Dictionary<string, List<MalProfileHistoryEntry>> history = null;
            await Task.Run(async () =>
                history = await new ProfileHistoryQuery(args.Source, offset).GetProfileHistory());

            var data = new Dictionary<string,
                List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>>();

            if (history == null)
                return data;

            if (args.Source == Credentials.UserName)
            {
                foreach (var key in history.Keys)
                {
                    try
                    {
                        List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>> entries =
                            new List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>();
                        var distinctIds = history[key].Select(entry => entry.Id).Distinct();
                        foreach (var distinctId in distinctIds)
                        {
                            var firstEntry = history[key].First(entry => entry.Id == distinctId);
                            var vm = await ViewModelLocator.AnimeList.TryRetrieveAuthenticatedAnimeItem(distinctId,
                                firstEntry.IsAnime) as AnimeItemViewModel;

                            if (vm == null)
                            {
                                var seasonal = new SeasonalAnimeData
                                {
                                    Id = distinctId,
                                    Title = firstEntry.Title ?? $"Title #{distinctId}",
                                    ImgUrl = firstEntry.ImgUrl ?? ""
                                };
                                var abstraction = new AnimeItemAbstraction(seasonal, firstEntry.IsAnime);
                                vm = new AnimeItemViewModel(seasonal, abstraction);
                            }

                            entries.Add(new Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>(vm,
                                history[key].Where(entry => entry.Id == distinctId).ToList()));
                        }
                        data.Add(key, entries);
                    }
                    catch (Exception)
                    {
                        //
                    }
                }
            }
            else
            {
                try
                {
                    await _animeLibraryDataStorage.EnsureOthersLibraryLoadedAsync(args.Source);
                    var others = _animeLibraryDataStorage.OthersAbstractions[args.Source];
                    foreach (var key in history.Keys)
                    {
                        List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>> entries =
                            new List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>();
                        var distinctIds = history[key].Select(entry => entry.Id).Distinct();
                        foreach (var distinctId in distinctIds)
                        {
                            var firstEntry = history[key].First(entry => entry.Id == distinctId);
                            bool anime = firstEntry.IsAnime;
                            var vm = anime
                                ? others.Item1.FirstOrDefault(abstraction => abstraction.Id == distinctId)?.ViewModel
                                : others.Item2.FirstOrDefault(abstraction => abstraction.Id == distinctId)?.ViewModel;

                            if (vm == null)
                            {
                                var seasonal = new SeasonalAnimeData
                                {
                                    Id = distinctId,
                                    Title = firstEntry.Title ?? $"Title #{distinctId}",
                                    ImgUrl = firstEntry.ImgUrl ?? ""
                                };
                                var abstraction = new AnimeItemAbstraction(seasonal, anime);
                                vm = new AnimeItemViewModel(seasonal, abstraction);
                            }

                            entries.Add(new Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>(
                                vm, history[key].Where(entry => entry.Id == distinctId).ToList()));
                        }
                        data.Add(key, entries);
                    }
                }
                catch (Exception)
                {
                    //
                }
            }

            return data;
        }

        /// <summary>
        ///     How many entries the given history holds.
        /// </summary>
        private static int CountEntries(Dictionary<string,
            List<Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>> history)
        {
            if (history == null)
                return 0;
            var total = 0;
            foreach (var day in history.Values)
                total += day.Count;
            return total;
        }
    }
}
