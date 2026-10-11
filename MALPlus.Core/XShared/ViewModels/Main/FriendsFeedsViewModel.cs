using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Enums;
using MALPlus.Models.Models;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Comm.MalSpecific;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.ViewModels.Main
{
    public class FriendsFeedsViewModel : ViewModelBase
    {
        private List<UserFeedEntryModel> _feeds;
        private ICommand _navigateDeitalsCommand;
        private ICommand _navigateProfileCommand;
        private bool _loading;

        public double ItemWidth => AnimeItemViewModel.MaxWidth;

        public List<UserFeedEntryModel> Feeds
        {
            get { return _feeds; }
            set
            {
                _feeds = value;
                RaisePropertyChanged(() => Feeds);
            }
        }

        /// <summary>
        ///     Whether a profile read is already under way for this page.
        /// </summary>
        /// <remarks>
        ///     Init is fired by the page's appearing, by the pull to refresh, and by the tab's own
        ///     re-entrant refreshes. Each of those used to be able to start its own read, and a second
        ///     read entering while the first was still open had it re-asking for the same profile over
        ///     and over, which is what crashed the page when the answer kept coming back empty.
        ///     Guarding makes "empty again" simply give up rather than spin.
        /// </remarks>
        private bool _loadingProfile;

public async void Init(bool force = false)
        {
            // One at a time, covering the whole method. Init is fired by the page's appearing, by the
            // pull to refresh, and by the tab's own re-entrant refreshes; letting a second one in while
            // the first was still reading meant two answers landing on the same list, which is what the
            // flash was: the rows went away and came back a moment later on their own.
            if (_loadingProfile)
                return;
            _loadingProfile = true;
            // Raised once, here, and lowered once at the very end. The profile read used to raise and
            // lower it and so did the feed read, so one visit drove it three times and the page's own
            // spinner went up and down with it: that was the repeated reload, not a second request.
            Loading = true;
            try
            {
                // Only checked for null before, which never happens once the profile has been read: the
                // cached copy comes back as an empty list rather than null, so the condition was false
                // forever and the friend list was never rebuilt. An empty list is the normal result of
                // the cached profile having lost its friends, so that has to be allowed to re-read too.
                var friends = ViewModelLocator.ProfilePage.MyFriends;
                if (force || friends == null || friends.Count == 0)
                {
                    ViewModelLocator.GeneralMain.LockCurrentStatus = true;
                    try
                    {
                        // Always forced. LoadProfileData declines to refetch a profile it already holds
                        // and this is always the reader's own page, so without the flag a second visit
                        // sat on the stale list and kept reporting "no friends" behind friends the
                        // account does have.
                        await ViewModelLocator.ProfilePage.LoadProfileData(
                            new ProfilePageNavigationArgs { TargetUser = Credentials.UserName }, true);
                        friends = ViewModelLocator.ProfilePage.MyFriends;
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        // LockCurrentStatus stayed true on failure, permanently freezing
                        // the status bar text.
                        ViewModelLocator.GeneralMain.LockCurrentStatus = false;
                    }
                }

                if (Feeds != null && !force)
                    return;

                // The list is deliberately not emptied before the read. Clearing it first is what made
                // the page flash: rows, then nothing, then rows again. It now keeps what it has until
                // the answer arrives.
                var source =
                (friends ?? new List<MalUser>())
                .Concat(ResourceLocator.HandyDataStorage.PinnedUsers.StoredItems)
                .Distinct(MalUser.NameComparer)
                .ToList();

                if (source.Count == 0)
                {
                    Feeds = new List<UserFeedEntryModel>();
                    return;
                }

                var feeds = await new MalFriendsFeedsQuery(source).GetFeeds();
                Feeds = (feeds ?? new List<UserFeedEntryModel>()).OrderByDescending(
                    model => model.Date).ToList();
            }
            catch (Exception)
            {
                Feeds = new List<UserFeedEntryModel>();
            }
            finally
            {
                Loading = false;
                _loadingProfile = false;
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

        public ICommand NavigateDeitalsCommand
            => _navigateDeitalsCommand ?? (_navigateDeitalsCommand = new RelayCommand<UserFeedEntryModel>(
                   item =>
                   {
                       if (ViewModelLocator.Mobile)
                       {
                           ViewModelLocator.NavMgr.RegisterBackNav(PageIndex.PageFeeds,null);
                       }
                       if(ViewModelLocator.AnimeDetails.Id != item.Id)
                           ViewModelLocator.GeneralMain.Navigate
                           (PageIndex.PageAnimeDetails,
                               new AnimeDetailsPageNavigationArgs(item.Id, item.Title, null, null)
                               {
                                   Source = PageIndex.PageFeeds
                               });
                   }));

        public ICommand NavigateProfileCommand
            => _navigateProfileCommand ?? (_navigateProfileCommand = new RelayCommand<MalUser>(
                   user =>
                   {
                       ViewModelLocator.NavMgr.RegisterBackNav(PageIndex.PageFeeds,null);
                       ViewModelLocator.GeneralMain.Navigate(PageIndex.PageProfile,new ProfilePageNavigationArgs{TargetUser = user.Name});
                   }));

    }
}