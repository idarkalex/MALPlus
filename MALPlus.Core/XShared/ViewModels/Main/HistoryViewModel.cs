using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
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
                // A page with nothing on it means there is nothing after it either, and the button is
                // then dead rather than fetching the same empty page again for ever.
                _historyHasMore = _historyOffset > 0;
            }
            finally
            {
                LoadingVisibility = false;
            }
        }

        /// <summary>
        ///     Reads the next page of history and adds it to what is already shown.
        /// </summary>
        /// <remarks>
        ///     MAL's own history page has no documented limit, so this asks for the entries after the ones
        ///     already on screen and reports what came back. If the server ignores the offset, the page
        ///     simply shows the same first entries again and the reader is told there is nothing more,
        ///     rather than the app pretending it paginated.
        /// </remarks>
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

                // Appended to the days already on screen rather than replacing them, which is the whole
                // point of asking for another page. The dictionary instance and the lists inside it are
                // left as they are: the page reads this straight after, and handing it a brand new
                // dictionary would only make the next rebuild start from a different object.
                var merged = History;
                if (merged == null)
                {
                    merged = page;
                }
                else
                {
                    foreach (var pair in page)
                    {
                        if (merged.TryGetValue(pair.Key, out var existing))
                            existing.AddRange(pair.Value);
                        else
                            merged[pair.Key] = pair.Value;
                    }
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
                            var vm = await ViewModelLocator.AnimeList.TryRetrieveAuthenticatedAnimeItem(distinctId,
                                history[key].First(entry => entry.Id == distinctId).IsAnime) as AnimeItemViewModel;

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
                            bool anime =
                                history[key].
                                    First(entry => entry.Id == distinctId).IsAnime;
                            var vm = anime
                                ? others.Item1.FirstOrDefault(abstraction => abstraction.Id == distinctId)
                                : others.Item2.FirstOrDefault(abstraction => abstraction.Id == distinctId);

                            if (vm != null)
                            {
                                entries.Add(new Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>(
                                    vm.ViewModel, history[key].Where(entry => entry.Id == distinctId).ToList()));
                            }
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
