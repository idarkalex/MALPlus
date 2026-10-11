using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Comm.MalSpecific;
using MALPlus.XShared.NavArgs;

namespace MALPlus.XShared.ViewModels.Main
{
    public class PopularVideosViewModel : ViewModelBase
    {
        private ObservableCollection<AnimeVideoData> _videos;
        private bool _loading;
        private int _page = 1;

        /// <summary>
        ///     Whether there might be another page. False once a page comes back with nothing new, which
        ///     is the only signal the feed gives.
        /// </summary>
        private bool _hasMore = true;

        /// <summary>
        ///     Whether a page is already on the way, so scrolling to the end twice cannot ask twice.
        /// </summary>
        private bool _loadingMore;

        public ObservableCollection<AnimeVideoData> Videos
        {
            get { return _videos; }
            set
            {
                _videos = value;
                RaisePropertyChanged(() => Videos);
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

        /// <summary>
        ///     Whether the pull-to-refresh gesture is showing its own spinner.
        /// </summary>
        /// <remarks>
        ///     Deliberately separate from <see cref="Loading"/>. RefreshView's IsRefreshing is TwoWay, so
        ///     it writes back into whatever it is bound to when the gesture starts, and the control is
        ///     never told the refresh is over once the reader pulls again before the first page landed.
        ///     The old binding to Loading left it true forever, which meant both the RefreshView's own
        ///     spinner and the full-page scrim stayed up over a perfectly loaded list.
        /// </remarks>
        private bool _refreshing;

        public bool Refreshing
        {
            get { return _refreshing; }
            set
            {
                _refreshing = value;
                RaisePropertyChanged(() => Refreshing);
            }
        }

        public ICommand NavDetailsCommand => _navDetailsCommand ?? (_navDetailsCommand =
            new RelayCommand<AnimeVideoData>(item =>
        {
            if (ViewModelLocator.AnimeDetails.Id != item.AnimeId)
                ViewModelLocator.GeneralMain.Navigate(PageIndex.PageAnimeDetails,
                    new AnimeDetailsPageNavigationArgs(item.AnimeId, item.AnimeTitle, null, null)
                    {
                        Source = PageIndex.PagePopularVideos
                    });
        }));

        private ICommand _navDetailsCommand;

        /// <summary>
        ///     Whether the list should offer to keep going. Bound to the CollectionView's remaining items
        ///     threshold so scrolling to the end asks for the next page.
        /// </summary>
        public bool CanLoadMore => _hasMore;

        public double ItemWidth => AnimeItemViewModel.MaxWidth;

        /// <summary>
        ///     Whether the first request has been answered, for a page that otherwise has to poll.
        /// </summary>
        /// <remarks>
        ///     An empty answer counts as answered. The flag only exists to stop the page waiting on a
        ///     spinner, and a list that came back empty is the answer.
        /// </remarks>
        public bool HasLoadedOnce => _answeredOnce;

        private bool _answeredOnce;

        public async void Init()
        {
            // A refresh while a page is in flight is refused rather than queued: two answers to the same
            // page would put every entry on the list twice.
            if (_loadingMore)
                return;

            // A fresh start, so the held-back list is dropped and rebuilt from the first page. Clearing
            // to nothing first is what left the page blank when the request came back empty; replacing it
            // is what leaves the previous entries on screen until better ones arrive.
            _page = 1;
            _hasMore = true;
            Videos = new ObservableCollection<AnimeVideoData>();
            await LoadPage();
        }

        /// <summary>
        ///     Asks for the next page of the feed, appending what it does not have yet.
        /// </summary>
        public async Task<bool> LoadMore()
        {
            if (!_hasMore || _loadingMore)
                return false;

            var got = await LoadPage();
            if (!got)
                _hasMore = false;
            return got;
        }

                private async Task<bool> LoadPage()
        {
            _loadingMore = true;
            Loading = true;
            try
            {
                var videos = await new PopularVideosQuery(_page).GetVideos();
                if (videos == null || videos.Count == 0)
                    return false;

                // Grown in place rather than replaced. Every page used to come back as a brand new
                // ObservableCollection, and a CollectionView whose ItemsSource is swapped out from
                // under it throws away its scroll position and its realised cells, which put the
                // viewport back at the top. That re-armed RemainingItemsThreshold on the way, so
                // scrolling to the end re-asked for the next page forever: the list visibly reloaded
                // itself each time it got near the bottom.
                // The property is still published when the list is created, so the first read binds.
                if (Videos == null)
                    Videos = new ObservableCollection<AnimeVideoData>();

                // The same entry twice means the server ran out of pages and started over, which is how
                // a paged feed signals the end when it does not say so.
                //
                // The key is the anime id and the YouTube link, never the title: the title is the one
                // field that can legitimately arrive empty, and keying on it made a single nameless row
                // collapse the whole first page into "nothing new" and leave the list empty.
                var existing = Videos;
                var seen = new HashSet<string>(
                    existing.Select(v => v.AnimeId + "|" + (v.YtLink ?? "")), StringComparer.OrdinalIgnoreCase);
                var fresh = videos.Where(v => seen.Add(v.AnimeId + "|" + (v.YtLink ?? ""))).ToList();

                foreach (var v in fresh)
                    existing.Add(v);

                _page++;
                return true;
            }
            finally
            {
                _loadingMore = false;
                _answeredOnce = true;
                Loading = false;
                if (_refreshing)
                {
                    Refreshing = false;
                }
            }
        }
    }
}
