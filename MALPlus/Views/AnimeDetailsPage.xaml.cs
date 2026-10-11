using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Favourites;
using MALPlus.Models.Interfaces;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Details;
using MALPlus.Services;

namespace MALPlus.Views;

[QueryProperty(nameof(MalId), "id")]
[QueryProperty(nameof(AnimeTitle), "title")]
[QueryProperty(nameof(InitialIsManga), "manga")]
[QueryProperty(nameof(InitialTabIndex), "tab")]
public partial class AnimeDetailsPage : ContentPage
{
    //First paint shows five rows; the rest is already in the VM and gets appended as the user
    //scrolls. The number is deliberately small: a long-runner has well over a thousand episodes
    //and painting forty of them on the tap is what made the tab feel like it hung.
    private const int ProgressiveChunkSize = 5;
    private const int RecommendationChunkSize = 8;
    //Characters, staff and related were the three tabs that still froze on first paint. Their
    //CollectionViews sit inside the page ScrollView with a HeightRequest sized to the whole list,
    //so the viewport is as tall as the data (sixty character rows is 6480px) and MAUI realises
    //every row in one layout pass: sixty portraits plus sixty seiyuu avatars, all decoded on the
    //UI thread. Measured 3000ms frames at p95 on a cold open. Growing them from the page scroll is
    //not an option, the scroll threshold says nothing about where these lists are, and an earlier
    //attempt at it appended forty rows per frame and was reverted. So the height is bounded and
    //the rest is reached with the Show more button instead.
    private const int ImageTabChunkSize = 12;
    private double _lastProgressiveAppendScrollY = double.NaN;
    private int _charactersShown = ImageTabChunkSize;
    private int _mangaCharactersShown = ImageTabChunkSize;
    private int _staffShown = ImageTabChunkSize;
    private int _relatedShown = ImageTabChunkSize;
    /// <summary>
    ///     Related is shown in full. It used to be capped, first at twelve rows and then at forty,
    ///     and both were guesses rather than measurements: One Piece has thirty relations and a
    ///     handful of other franchises have more, so the tab looked permanently cut for them. A
    ///     hundred is a safety net rather than a limit, since nothing real comes close; past it
    ///     the Show N more button takes over, and the scroll driven growth keeps working so the
    ///     list never ends in a dead end.
    /// </summary>
    private const int RelatedShowCap = 100;
    private const double HeroExpandedHeight = 460;
    private const double HeroCollapsedHeight = 210;
    private const double HeroCollapseRange = HeroExpandedHeight - HeroCollapsedHeight;
    private const uint TabTransitionLength = 160;

    /// <summary>
    /// Height of one character/staff row: PanelStyle padding 10 top and bottom, the 88px
    /// image, and the 4px margin above and below the 100px row. Every row is built to this
    /// height so the virtualised lists get an exact HeightRequest instead of a guess.
    /// </summary>
    private const double CharacterRowHeight = 108;

    private bool _initialized;
    private int _initializedId;
    private bool _initializedAnimeMode;
    private int _entryVersion;
    private bool _isAppearing;
    private bool _episodesLoaded;
    private bool _reviewsLoaded;
    private bool _charactersLoaded;
    private bool _recommendationsLoaded;
    private bool _relatedLoaded;
    private readonly HashSet<(int Tab, int Version)> _loadingTabs = new();
    private AnimeDetailsPageViewModel _subscribedVm;
    private int _headerSpacerRetry;
    private double _headerPanLastY;
    private bool _headerPanActive;
    private double _lastViewportWidth = -1;
    private double _lastViewportHeight = -1;
    private string _generalError = string.Empty;
    private string _detailsError = string.Empty;
    private string _episodesError = string.Empty;
    private string _reviewsError = string.Empty;
    private string _recommendationsError = string.Empty;
    private string _relatedError = string.Empty;
    private string _charactersError = string.Empty;
    private string _staffError = string.Empty;
#if ANDROID
    private bool _videoHandlerSubscribed;
#endif
    private WebView _videoWebView;

    public string MalId { get; set; }
    public string AnimeTitle { get; set; }
    public string InitialIsManga { get; set; }
    public int InitialTabIndex { get; set; }

    public ObservableCollection<AnimeEpisode> DisplayedEpisodes { get; } = new();
    public ObservableCollection<AnimeReviewData> DisplayedReviews { get; } = new();
    public ObservableCollection<DirectRecommendationData> DisplayedRecommendations { get; } = new();
    public ObservableCollection<AnimeCharacterCard> DisplayedCharacters { get; } = new();
    public ObservableCollection<MangaCharacterCard> DisplayedMangaCharacters { get; } = new();
    public ObservableCollection<StaffCard> DisplayedStaff { get; } = new();
    public ObservableCollection<RelatedAnimeData> DisplayedRelated { get; } = new();

    public string GeneralErrorText => EmptyStateText(_generalError, "Unable to load general details.");
    public bool GeneralErrorVisible => !string.IsNullOrWhiteSpace(_generalError);
    public bool GeneralLoadingVisible => Vm?.LoadingGlobal == true;
    public bool GeneralContentVisible => Vm != null && !Vm.LoadingGlobal && !GeneralErrorVisible;
    public bool GeneralSynopsisEmptyVisible => GeneralContentVisible && string.IsNullOrWhiteSpace(Vm.Synopsis);

    public string DetailsErrorText => EmptyStateText(_detailsError, "Unable to load details.");
    public bool DetailsErrorVisible => !string.IsNullOrWhiteSpace(_detailsError);
    public bool DetailsEmptyVisible => Vm != null && !Vm.LoadingDetails && !DetailsErrorVisible && !HasDetailsContent();
    public bool DetailsContentVisible => Vm != null && !Vm.LoadingDetails && !DetailsErrorVisible && HasDetailsContent();

    public string EpisodesErrorText => EmptyStateText(_episodesError, "Unable to load episodes.");
    public bool EpisodesErrorVisible => !string.IsNullOrWhiteSpace(_episodesError);
    public bool EpisodesEmptyVisible => Vm != null && !Vm.LoadingEpisodes && !EpisodesErrorVisible && Vm.Episodes.Count == 0;
    public bool EpisodesContentVisible => Vm != null && !EpisodesErrorVisible && Vm.Episodes.Count > 0;

    public string ReviewsErrorText => EmptyStateText(_reviewsError, "Unable to load reviews.");
    public bool ReviewsErrorVisible => !string.IsNullOrWhiteSpace(_reviewsError);
    public bool ReviewsEmptyVisible => Vm != null && !Vm.LoadingReviews && !ReviewsErrorVisible && Vm.Reviews.Count == 0;
    public bool ReviewsContentVisible => Vm != null && !ReviewsErrorVisible && Vm.Reviews.Count > 0;

    public string RecommendationsErrorText => EmptyStateText(_recommendationsError, "Unable to load recommendations.");
    public bool RecommendationsErrorVisible => !string.IsNullOrWhiteSpace(_recommendationsError);
    public bool RecommendationsEmptyVisible => Vm != null && !Vm.LoadingRecommendations && !RecommendationsErrorVisible && Vm.Recommendations.Count == 0;
    public bool RecommendationsContentVisible => Vm != null && !RecommendationsErrorVisible && Vm.Recommendations.Count > 0;

    public string RelatedErrorText => EmptyStateText(_relatedError, "Unable to load related titles.");
    public bool RelatedErrorVisible => !string.IsNullOrWhiteSpace(_relatedError);
    public bool RelatedEmptyVisible => Vm != null && !Vm.LoadingRelated && !RelatedErrorVisible && Vm.RelatedAnime.Count == 0;
    public bool RelatedContentVisible => Vm != null && !RelatedErrorVisible && Vm.RelatedAnime.Count > 0;

    /// <summary>
    /// Related normally shows everything, so this is only a guard for a pathological list.
    /// The old growth path relied on the page reaching its bottom, which a short grid never
    /// does, and there was no visible control at all, so it just looked cut.
    /// </summary>
    public int RelatedRemainingCount =>
        Vm == null ? 0 : Math.Max(0, Vm.RelatedAnime.Count - Math.Min(Vm.RelatedAnime.Count, RelatedShowCap));

    public bool RelatedMoreVisible => RelatedRemainingCount > 0;

    public string RelatedMoreText => $"Show {RelatedRemainingCount} more";

    public string CharactersErrorText => EmptyStateText(_charactersError, "Unable to load characters.");
    public bool CharactersErrorVisible => !string.IsNullOrWhiteSpace(_charactersError);
    public bool CharactersEmptyVisible => Vm != null && !Vm.LoadingCharactersVisibility && !CharactersErrorVisible &&
        (Vm.AnimeMode ? Vm.AnimeStaffData?.AnimeCharacterPairs?.Count > 0 : Vm.MangaCharacterData?.Count > 0) == false;
    public bool CharactersContentVisible => Vm != null && !Vm.LoadingCharactersVisibility && !CharactersErrorVisible &&
        (Vm.AnimeMode ? Vm.AnimeStaffData?.AnimeCharacterPairs?.Count > 0 : Vm.MangaCharacterData?.Count > 0) == true;

    public string StaffErrorText => EmptyStateText(_staffError, "Unable to load staff.");
    public bool StaffErrorVisible => !string.IsNullOrWhiteSpace(_staffError);
    public bool StaffEmptyVisible => Vm != null && !Vm.LoadingCharactersVisibility && !StaffErrorVisible && (Vm.AnimeStaffData?.AnimeStaff?.Count ?? 0) == 0;
    public bool StaffContentVisible => Vm != null && !Vm.LoadingCharactersVisibility && !StaffErrorVisible && (Vm.AnimeStaffData?.AnimeStaff?.Count ?? 0) > 0;

    /// <summary>
    /// A CollectionView living inside the page's own ScrollView has to be told how tall it
    /// is or it collapses. The rows are a fixed height by design, so the total is exact.
    /// </summary>
    public double CharactersListHeight => CharacterRowHeight * DisplayedCharacters.Count;

    public double MangaCharactersListHeight => CharacterRowHeight * DisplayedMangaCharacters.Count;

    /// <summary>
    ///     Row count times row height, where the rows are three across. The grid lives inside the
    ///     page ScrollView, so without an explicit height it collapses to nothing.
    /// </summary>
    public double StaffListHeight
    {
        get
        {
            var rows = (DisplayedStaff.Count + StaffColumns - 1) / StaffColumns;
            return rows * StaffRowHeight;
        }
    }

    /// <summary>Three across, matching the Span on the staff grid in the XAML.</summary>
    private const int StaffColumns = 3;

    /// <summary>
    ///     96 image + name + up to two role lines + padding and margins. Slightly taller than
    ///     <see cref="CharacterRowHeight" /> because the card is a column, not a row.
    /// </summary>
    private const double StaffRowHeight = 156;

    private double _tabListHeight = 520;
    private bool _tabListHeightSet;

    /// <summary>
    /// Bounded viewport for the character and staff lists. Sizing these to their content
    /// (row height times count) is what stopped them virtualising: the viewport then IS the
    /// list, so MAUI realises every row at once. A shorter box than the content is the whole
    /// trick - the CollectionView recycles, only the visible rows exist, and the memory stays
    /// flat no matter how far the user scrolls. It also means these two lists no longer grow
    /// by chunks, so the scroll driven append in AppendNextProgressiveChunk is not used for
    /// them; the list scrolls itself.
    /// </summary>
    public double TabListHeight
    {
        get => _tabListHeight;
        private set
        {
            if (value <= 0 || Math.Abs(value - _tabListHeight) < 0.5)
                return;
            _tabListHeight = value;
            OnPropertyChanged(nameof(TabListHeight));
        }
    }

    private void UpdateTabListHeight()
    {
        if (PageScroll == null)
            return;
        var viewport = PageScroll.Height;
        if (viewport <= 0 || double.IsNaN(viewport))
            return;
        // Big enough to be worth scrolling, small enough to leave the page something to scroll
        // for the hero. The floor keeps a short list from becoming a two row window.
        const double floor = 320;
        const double heroRoom = 120;
        const double minimumUsable = 160;

        var preferred = viewport * 0.72;
        var ceiling = viewport - heroRoom;

        // On a short viewport the floor and the room left for the hero collide, and
        // Math.Clamp throws Argument_MinMaxValue as soon as min > max. The page has to fit
        // first, so on those viewports the floor is dropped instead of the viewport.
        TabListHeight = ceiling > floor
            ? Math.Clamp(preferred, floor, ceiling)
            : Math.Max(minimumUsable, Math.Min(preferred, ceiling));
        _tabListHeightSet = true;
    }

    private AnimeDetailsPageViewModel Vm => BindingContext as AnimeDetailsPageViewModel;

    public AnimeDetailsPage()
    {
InitializeComponent();
            // Set here rather than as a <BindableLayout.ItemsLayout> property element in the XAML:
            // on this RC the compiler rejects that form with MAUIX2002.
            BindingContext = ViewModelLocator.AnimeDetails;
            DetailsSheet.Closed += OnDetailsSheetClosed;
        if (RelatedGrid?.Inner != null)
            RelatedGrid.Inner.SelectionChanged += OnRelatedSelectionChanged;
        ApplyTabActiveState();
        UpdateHeroMetaLayout();
    }

    private void AttachSubscriptions()
    {
        DetachVmSubscriptions();
        if (BindingContext is AnimeDetailsPageViewModel vm)
        {
            _subscribedVm = vm;
            vm.ShowMoreRequested += OnShowMoreRequested;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.Episodes.CollectionChanged += OnEpisodesSourceChanged;
            vm.Reviews.CollectionChanged += OnReviewsSourceChanged;
            vm.LeftGenres.CollectionChanged += OnDetailsSourceChanged;
            vm.RightGenres.CollectionChanged += OnDetailsSourceChanged;
            vm.Information.CollectionChanged += OnDetailsSourceChanged;
            vm.Stats.CollectionChanged += OnDetailsSourceChanged;
            vm.OPs.CollectionChanged += OnDetailsSourceChanged;
            vm.EDs.CollectionChanged += OnDetailsSourceChanged;
            vm.Recommendations.CollectionChanged += OnRecommendationsSourceChanged;
            vm.RelatedAnime.CollectionChanged += OnRelatedSourceChanged;
            vm.RequestVideoPlayback += ShowVideoOverlay;
            RefreshDisplayedEpisodes(false);
            RefreshDisplayedReviews(false);
            RefreshDisplayedRecommendations(false);
        }
#if ANDROID
        if (!_videoHandlerSubscribed)
        {
            _videoHandlerSubscribed = true;
        }
#endif
    }

    private void DetachSubscriptions()
    {
        DetachVmSubscriptions();
#if ANDROID
        if (_videoHandlerSubscribed)
        {
            if (VideoWebView != null)
                VideoWebView.HandlerChanged -= OnVideoWebViewHandlerChanged;
            _videoHandlerSubscribed = false;
        }
#endif
    }

    private void DetachVmSubscriptions()
    {
        if (_subscribedVm == null)
            return;
        _subscribedVm.ShowMoreRequested -= OnShowMoreRequested;
        _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm.Episodes.CollectionChanged -= OnEpisodesSourceChanged;
        _subscribedVm.Reviews.CollectionChanged -= OnReviewsSourceChanged;
        _subscribedVm.LeftGenres.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.RightGenres.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.Information.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.Stats.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.OPs.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.EDs.CollectionChanged -= OnDetailsSourceChanged;
        _subscribedVm.Recommendations.CollectionChanged -= OnRecommendationsSourceChanged;
        _subscribedVm.RelatedAnime.CollectionChanged -= OnRelatedSourceChanged;
        _subscribedVm.RequestVideoPlayback -= ShowVideoOverlay;
        _subscribedVm = null;
    }

#if ANDROID
    private void OnVideoWebViewHandlerChanged(object sender, EventArgs e)
    {
        try
        {
            var platformView = VideoWebView?.Handler?.PlatformView as global::Android.Views.View;
            if (platformView == null)
                return;
            VideoWebViewHelper.ConfigurePlatformView(platformView);
            VideoWebViewHelper.Resume(platformView);
        }
        catch { }
    }

    private static void SetSystemBars(bool video)
    {
        try
        {
            var window = Platform.CurrentActivity?.Window;
            if (window == null)
                return;
            window.SetStatusBarColor(video
                ? global::Android.Graphics.Color.Black
                : global::Android.Graphics.Color.ParseColor("#051522"));
            window.SetNavigationBarColor(global::Android.Graphics.Color.Black);
        }
        catch { }
    }
#else
    private static void SetSystemBars(bool video) { }
#endif

    /// <summary>
    ///     The overflow menu, as the shared options sheet rather than a native action sheet.
    ///     <para>
    ///     It is the original's nine options in the original's order. The sheet it used to be was three
    ///     of them, and one of those three opened a URL hardcoded to /anime/, which is not a page on
    ///     MAL for a manga. OpenInMalCommand already knows the difference.
    ///     </para>
    /// </summary>
    private void OnShowMoreRequested()
    {
        try
        {
            var vm = Vm;
            if (vm == null)
                return;

            var onList = !vm.AddAnimeVisibility;
            var rows = new List<MALPlus.Controls.OptionsSheetItem>
            {
                new("Forum board", "icon_forum",
                    action: () => OpenForumBoard(vm))
            };

            if (vm.AnimeMode)
                rows.Add(new MALPlus.Controls.OptionsSheetItem("Promotional videos", "icon_video",
                    action: () => ShowVideoOverlay(vm.TrailerUrl)));

            // Only offered while the entry can actually be rewatching, same as the original.
            if (vm.IsRewatchingButtonVisibility)
                rows.Add(new MALPlus.Controls.OptionsSheetItem(
                    vm.AnimeMode ? "Rewatching" : "Rereading", "icon_history",
                    submenu: sheet => OpenRewatchSubmenu(sheet, vm)));

            if (onList)
                rows.Add(new MALPlus.Controls.OptionsSheetItem(
                    vm.AnimeMode ? "Set rewatch count" : "Set reread count", "icon_episodes",
                    submenu: sheet => OpenRewatchSubmenu(sheet, vm)));

            rows.Add(new MALPlus.Controls.OptionsSheetItem("Copy title", "icon_copy",
                vm.CopyTitleToClipboardCommand));
            rows.Add(new MALPlus.Controls.OptionsSheetItem("Copy link", "icon_list",
                vm.CopyToClipboardCommand));
            rows.Add(new MALPlus.Controls.OptionsSheetItem("Open in browser", "icon_search",
                vm.OpenInMalCommand));

            if (onList)
                rows.Add(new MALPlus.Controls.OptionsSheetItem("Remove from my list", "icon_list",
                    vm.RemoveAnimeCommand, destructive: true));

            DetailsSheet.Open("More", vm.Title, rows);
            MainActivity.ArmMenuBackCallback(() => DetailsSheet.Dismiss());
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnShowMoreRequested failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Rewatch count, 1 to 5 plus none, which is the range the original offered. A submenu rather
    ///     than a picker dialog because it belongs to the panel the user is already in, and a dialog on
    ///     top of a sheet is two dismissible surfaces stacked.
    /// </summary>
    /// <summary>
    ///     This entry's own discussion board.
    ///     <para>
    ///     Straight to the route rather than through <c>NavigateForumBoardCommand</c>: that command goes
    ///     out to the platform's PageForumIndex, which in MAUI is the global forum index with no id, so
    ///     it opened the boards list instead of the entry's board.
    ///     </para>
    /// </summary>
    private static async void OpenForumBoard(
        MALPlus.XShared.ViewModels.Details.AnimeDetailsPageViewModel vm)
    {
        try
        {
            var title = Uri.EscapeDataString(vm.Title ?? "");
            var manga = vm.AnimeMode ? "0" : "1";
            await Shell.Current.GoToAsync($"forumboard?anime={vm.Id}&title={title}&manga={manga}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("MALPLUS forum board navigation failed: " + ex.Message);
        }
    }

    private bool OpenRewatchSubmenu(MALPlus.Controls.OptionsSheet sheet,
        MALPlus.XShared.ViewModels.Details.AnimeDetailsPageViewModel vm)
    {
        var rows = new List<MALPlus.Controls.OptionsSheetItem>
        {
            new("None", "icon_episodes",
                action: () => vm.SetRewatchCount(0))
        };
        for (var count = 1; count <= 5; count++)
        {
            var picked = count;
            rows.Add(new MALPlus.Controls.OptionsSheetItem(picked.ToString(), "icon_episodes",
                action: () => vm.SetRewatchCount(picked)));
        }

        sheet.OpenSubmenu(vm.AnimeMode ? "Rewatching" : "Rereading", rows, () => OnShowMoreRequested());
        return true;
    }

    private void OnDetailsSheetClosed(object sender, EventArgs e)
    {
        MainActivity.DisarmMenuBackCallback();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isAppearing = true;
        // Attached here and released on disappearing, because these are static events: a page that
        // subscribed once and never unsubscribed would answer for every overlay opened afterwards.
        VideoWebViewHelper.PlaybackError += OnVideoPlaybackError;
        VideoWebViewHelper.PlaybackStarted += OnVideoPlaybackStarted;
        AttachSubscriptions();
        try
        {
            if (!int.TryParse(MalId, out var id) || id <= 0)
                return;

            var isManga = string.Equals(InitialIsManga, "true", StringComparison.OrdinalIgnoreCase);
            var handoff = MauiDetailsNavigationHandoff.Take(id, !isManga);
            var args = handoff ?? new AnimeDetailsPageNavigationArgs(id, AnimeTitle, null, null, null)
            {
                SourceTabIndex = Math.Clamp(InitialTabIndex, 0, 7)
            };
            if (isManga)
                args.AnimeMode = false;
            if (handoff != null)
                args.AnimeMode = handoff.AnimeMode;

            var entryChanged = !_initialized || _initializedId != args.Id || _initializedAnimeMode != args.AnimeMode ||
                               Vm.Id != args.Id || Vm.AnimeMode != args.AnimeMode;
            if (!entryChanged)
                return;

            _initialized = true;
            _initializedId = args.Id;
            _initializedAnimeMode = args.AnimeMode;
            _entryVersion++;
            ResetTabLoadedFlags();
            ResetPageStateForEntry();
            _loadingTabs.Clear();
            ApplyTabStripVisibilityFromParams(args.AnimeMode);
            ApplyCharacterStaffBindings();
            _ = InitAsyncSafe(Vm, args);
        }
        catch (Exception ex)
        {
            _generalError = "Unable to open this title.";
            NotifyStateProperties();
            System.Diagnostics.Debug.WriteLine("MALPLUS Details Init failed: " + ex);
        }
    }

    private async Task InitAsyncSafe(AnimeDetailsPageViewModel vm, AnimeDetailsPageNavigationArgs args)
    {
        try
        {
            _generalError = string.Empty;
            NotifyStateProperties();
            await vm.InitAsync(args, fakeDelay: false);
            while (vm.LoadingGlobal)
                await Task.Delay(100);
            if (PageScroll != null)
                await PageScroll.ScrollToAsync(0, 0, false);
            ApplyHeroState(0);
            var initialTab = Math.Clamp(args.SourceTabIndex, 0, 7);
            vm.DetailsPivotSelectedIndex = IsTabVisible(initialTab) ? initialTab : 0;
            ApplyTabStripVisibility();
            ApplyTabActiveState();
            ApplyCharacterStaffBindings();
            Dispatcher.Dispatch(UpdateHeaderSpacer);
            NotifyStateProperties();
            System.Diagnostics.Debug.WriteLine("MALPLUS Details Init returned title=" + vm.Title
                + " rank=" + vm.GeneralRank + " pop=" + vm.GeneralPopularity
                + " studios=" + vm.GeneralStudios + " animemode=" + vm.AnimeMode);
        }
        catch (Exception ex)
        {
            _generalError = "Unable to load this title.";
            NotifyStateProperties();
            System.Diagnostics.Debug.WriteLine("MALPLUS Details Init failed: " + ex);
        }
    }

    /// <summary>
    ///     Manga gets Reviews, Recs and Related too. The three queries behind them all take an
    ///     animeMode flag and already handle manga: AnimeReviewsQuery builds its url as
    ///     /manga/{id}/whatever/reviews, and the other two pass the flag straight through. They
    ///     were hidden from the manga strip on the assumption the manga scrapes came back empty,
    ///     which is not true. Staff stays hidden because MAL's staff table is anime-only.
    /// </summary>
    private void ApplyTabStripVisibilityFromParams(bool isManga)
    {
        TabGeneral.IsVisible = true;
        TabEpisodes.IsVisible = !isManga;
        TabOpEd.IsVisible = !isManga && Vm?.OpEdVisible == true;
        TabReviews.IsVisible = true;
        TabRecs.IsVisible = true;
        TabRelated.IsVisible = true;
        TabCharacters.IsVisible = true;
        TabStaff.IsVisible = !isManga;
        ApplyTabActiveState();
    }

    private void ApplyTabStripVisibility()
    {
        TabGeneral.IsVisible = true;
        TabEpisodes.IsVisible = true;
        TabOpEd.IsVisible = true;
        TabReviews.IsVisible = true;
        TabRecs.IsVisible = true;
        TabRelated.IsVisible = true;
        TabCharacters.IsVisible = true;
        TabStaff.IsVisible = true;
        if (Vm == null)
            return;
        if (!Vm.AnimeMode)
        {
            // Episodes is anime-only (manga has chapters and volumes, not episodes) and OP/ED are
            // anime-only. Reviews, Recs and Related are NOT: their queries carry the animeMode flag
            // and serve manga as well, so they stay on the strip.
            TabEpisodes.IsVisible = false;
            TabOpEd.IsVisible = false;
            TabStaff.IsVisible = false;
        }
        else if (Vm.Type?.Equals("Movie", StringComparison.OrdinalIgnoreCase) == true)
        {
            TabEpisodes.IsVisible = false;
        }

        // OP/ED only earns its place once the entry actually has themes, which arrives with the
        // details payload and can be after the strip was first laid out.
        TabOpEd.IsVisible = Vm.OpEdVisible;
        ApplyTabActiveState();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isAppearing = false;
        VideoWebViewHelper.PlaybackError -= OnVideoPlaybackError;
        VideoWebViewHelper.PlaybackStarted -= OnVideoPlaybackStarted;
        DetachSubscriptions();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        DetachVmSubscriptions();
        if (_isAppearing)
            AttachSubscriptions();
    }

    private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.LastAired) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.Status) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.GlobalScoreBind) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.MyStatusBind))
        {
            UpdateHeroMetaLayout();
        }
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.DetailsPivotSelectedIndex))
        {
            ApplyTabActiveState();
            if (Vm.Id == _initializedId && Vm.AnimeMode == _initializedAnimeMode)
                LoadTabData(Vm.DetailsPivotSelectedIndex);
            return;
        }
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.AnimeMode) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.AnimeStaffData) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.MangaCharacterData))
        {
            ApplyTabStripVisibility();
            ApplyCharacterStaffBindings();
        }
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.OpEdVisible))
        {
            ApplyTabStripVisibility();
            if (!IsTabVisible(Vm.DetailsPivotSelectedIndex))
                Vm.DetailsPivotSelectedIndex = FindAdjacentVisibleTab(Vm.DetailsPivotSelectedIndex, 1) ?? 0;
        }
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.Type))
        {
            ApplyTabStripVisibility();
            if (!IsTabVisible(Vm.DetailsPivotSelectedIndex))
                Vm.DetailsPivotSelectedIndex = FindAdjacentVisibleTab(Vm.DetailsPivotSelectedIndex, 1) ?? 0;
        }
        if (e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingGlobal) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingDetails) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingEpisodes) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingReviews) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingRecommendations) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingRelated) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.LoadingCharactersVisibility) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.Synopsis) ||
            e.PropertyName == nameof(AnimeDetailsPageViewModel.DetailedDataVisibility))
        {
            NotifyStateProperties();
        }
    }

    private void ApplyCharacterStaffBindings()
    {
        try
        {
            if (Vm == null)
                return;
            var animeMode = Vm.AnimeMode;
            var pairs = animeMode ? Vm.AnimeStaffData?.AnimeCharacterPairs : null;
            var mangaCharacters = animeMode ? null : Vm.MangaCharacterData;
            var staff = animeMode ? Vm.AnimeStaffData?.AnimeStaff : null;
            var cap = MALPlus.XShared.Comm.Anime.AnimeCharactersStaffQuery.MaxCharacterPairs;
            var staffCap = MALPlus.XShared.Comm.Anime.AnimeCharactersStaffQuery.MaxStaff;
            // Characters and staff are sized to their content and live inside the page ScrollView, so they
            // scroll with the page and the hero reacts to them the way it does everywhere else.
            // That means the height does track the count, so they are filled a chunk at a time and
            // the rest arrives as the page reaches its bottom, exactly like Episodes and Reviews.
            // Filling a two hundred strong cast in one pass is what pinned a gigabyte of views
            // before this list was ever given a bound of its own.
            var charTarget = Math.Min(cap, Math.Min(pairs?.Count ?? 0, _charactersShown));
            var mangaTarget = Math.Min(cap, Math.Min(mangaCharacters?.Count ?? 0, _mangaCharactersShown));
            var staffTarget = Math.Min(staffCap, Math.Min(staff?.Count ?? 0, _staffShown));
            var relatedTarget = Math.Min(Vm.RelatedAnime.Count, RelatedShowCap);
            DisplayedCharacters.Clear();
            DisplayedMangaCharacters.Clear();
            DisplayedStaff.Clear();
            DisplayedRelated.Clear();
            foreach (var pair in pairs?.Take(charTarget) ?? Enumerable.Empty<AnimeDetailsPageViewModel.AnimeStaffDataViewModels.AnimeCharacterStaffModelViewModel>())
                DisplayedCharacters.Add(new AnimeCharacterCard(pair));
            foreach (var character in mangaCharacters?.Take(mangaTarget) ?? Enumerable.Empty<FavouriteViewModel>())
                DisplayedMangaCharacters.Add(new MangaCharacterCard(character));
            foreach (var person in staff?.Take(staffTarget) ?? Enumerable.Empty<FavouriteViewModel>())
                DisplayedStaff.Add(new StaffCard(person));
            foreach (var item in Vm.RelatedAnime.Take(relatedTarget))
                DisplayedRelated.Add(item);
            NotifyStateProperties();
            if (!_tabListHeightSet)
                UpdateTabListHeight();
            OnPropertyChanged(nameof(TabListHeight));
            OnPropertyChanged(nameof(CharactersListHeight));
            OnPropertyChanged(nameof(MangaCharactersListHeight));
            OnPropertyChanged(nameof(StaffListHeight));
            Console.WriteLine($"MALPlusTabs rows pairs={pairs?.Count ?? 0}/{DisplayedCharacters.Count} manga={mangaCharacters?.Count ?? 0}/{DisplayedMangaCharacters.Count} staff={staff?.Count ?? 0}/{DisplayedStaff.Count} related={Vm.RelatedAnime.Count}/{DisplayedRelated.Count} tabListH={TabListHeight}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS char/staff binding failed: " + ex.Message);
        }
    }

    private void ResetTabLoadedFlags()
    {
        _episodesLoaded = false;
        _reviewsLoaded = false;
        _charactersLoaded = false;
        _recommendationsLoaded = false;
        // A new entry starts at the first chunk again; carrying the grown count over would
        // hand the next anime a list that is already past its own first paint.
        _charactersShown = ImageTabChunkSize;
        _mangaCharactersShown = ImageTabChunkSize;
        _staffShown = ImageTabChunkSize;
        _relatedShown = ImageTabChunkSize;
        _relatedLoaded = false;
        ResetReviewExpansionState();
    }

    private void ResetPageStateForEntry()
    {
        _generalError = string.Empty;
        _detailsError = string.Empty;
        _episodesError = string.Empty;
        _reviewsError = string.Empty;
        _recommendationsError = string.Empty;
        _relatedError = string.Empty;
        _charactersError = string.Empty;
        _staffError = string.Empty;
        ResetProgressiveCollections();
        ResetHeaderLayoutState();
        NotifyStateProperties();
    }

    private void ResetProgressiveCollections()
    {
        DisplayedEpisodes.Clear();
        DisplayedReviews.Clear();
        DisplayedRecommendations.Clear();
        DisplayedCharacters.Clear();
        DisplayedMangaCharacters.Clear();
        DisplayedStaff.Clear();
    }

    private async void LoadTabData(int tabIndex)
    {
        var entryVersion = _entryVersion;
        var loadingKey = (tabIndex, entryVersion);
        if (!_loadingTabs.Add(loadingKey))
            return;

        // Breadcrumb: "warm" means the background chain had already filled this tab before the
        // user touched it, "cold" means the tap is what triggered the fetch. This is how we tell
        // a preload that works from one that silently never ran.
        global::Android.Util.Log.Info("MALPlusTabs",
            $"tap tab={tabIndex} warm={IsTabWarm(tabIndex)} entry={_initializedId}");
        LogMemory($"tab{tabIndex}-tap");

        var entryId = _initializedId;
        var entryAnimeMode = _initializedAnimeMode;
        try
        {
            SetTabError(tabIndex, null);
            switch (tabIndex)
            {
                // OP/ED used to be a section of the Details tab, so tapping it still has to make
                // sure the payload that carries the themes has actually been asked for.
                case 1:
                    if (!_episodesLoaded)
                    {
                        await Vm.LoadEpisodes(false);
                        if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
                        {
                            _episodesLoaded = Vm.EpisodesLoaded;
                            RefreshDisplayedEpisodes(false);
                        }
                    }
                    break;
                case 2:
                    if (!Vm.DetailsLoaded)
                        await Vm.LoadDetails(false);
                    break;
                case 3:
                    if (!_reviewsLoaded)
                    {
                        await Vm.LoadReviews(false);
                        if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
                        {
                            _reviewsLoaded = Vm.ReviewsLoaded;
                            ResetReviewExpansionState();
                            RefreshDisplayedReviews(false);
                        }
                    }
                    break;
                case 4:
                    if (!_recommendationsLoaded)
                    {
                        await Vm.LoadRecommendations(false);
                        if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
                        {
                            _recommendationsLoaded = Vm.RecommendationsLoaded;
                            RefreshDisplayedRecommendations(false);
                        }
                    }
                    break;
                case 5:
                    if (!_relatedLoaded)
                    {
                        await Vm.LoadRelatedAnime(false);
                        if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
                            _relatedLoaded = Vm.RelatedLoaded;
                    }
                    break;
                case 6:
                case 7:
                    if (!_charactersLoaded)
                    {
                        await Vm.LoadCharacters(false);
                        if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
                        {
                            _charactersLoaded = Vm.CharactersLoaded;
                            ApplyCharacterStaffBindings();
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            if (entryId == _initializedId && entryAnimeMode == _initializedAnimeMode)
            {
                ResetTabLoadedFlags();
                SetTabError(tabIndex, ex);
            }
            System.Diagnostics.Debug.WriteLine("MALPLUS LoadTabData failed: " + ex);
        }
        finally
        {
            _loadingTabs.Remove(loadingKey);
        }
    }

    private void OnEpisodesSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDisplayedEpisodes(false);
        NotifyStateProperties();
    }

    private void OnReviewsSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            ResetReviewExpansionState();
        RefreshDisplayedReviews(false);
        NotifyStateProperties();
    }

    private void OnDetailsSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
        => NotifyStateProperties();

    private void OnRecommendationsSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDisplayedRecommendations(false);
        NotifyStateProperties();
    }

    private void OnRelatedSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // The grid binds to the paged DisplayedRelated, not straight to the source, so it has to
        // be filled here. The load path clears the VM collection and then adds the entries one by
        // one, so this runs once per item and appends whatever the tab is allowed to show.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            DisplayedRelated.Clear();
        var source = Vm?.RelatedAnime;
        if (source != null)
        {
            var target = Math.Min(source.Count, RelatedShowCap);
            for (var index = DisplayedRelated.Count; index < target; index++)
                DisplayedRelated.Add(source[index]);
        }
        NotifyStateProperties();
    }

    private void RefreshDisplayedEpisodes(bool reset)
    {
        var source = Vm?.Episodes;
        if (source == null)
            return;
        if (reset || source.Count < DisplayedEpisodes.Count)
            DisplayedEpisodes.Clear();
        var target = Math.Min(source.Count, Math.Max(ProgressiveChunkSize, DisplayedEpisodes.Count));
        for (var index = DisplayedEpisodes.Count; index < target; index++)
            DisplayedEpisodes.Add(source[index]);
    }

    private void RefreshDisplayedReviews(bool reset)
    {
        var source = Vm?.Reviews;
        if (source == null)
            return;
        if (reset || source.Count < DisplayedReviews.Count)
            DisplayedReviews.Clear();
        var target = Math.Min(source.Count, Math.Max(ProgressiveChunkSize, DisplayedReviews.Count));
        for (var index = DisplayedReviews.Count; index < target; index++)
            DisplayedReviews.Add(source[index]);
    }

    private void ResetReviewExpansionState()
    {
        if (Vm == null)
            return;
        foreach (var review in Vm.Reviews)
            review.IsExpanded = false;
    }

    private void RefreshDisplayedRecommendations(bool reset)
    {
        var source = Vm?.Recommendations;
        if (source == null)
            return;
        if (reset || source.Count < DisplayedRecommendations.Count)
            DisplayedRecommendations.Clear();
        var target = Math.Min(source.Count, Math.Max(RecommendationChunkSize, DisplayedRecommendations.Count));
        for (var index = DisplayedRecommendations.Count; index < target; index++)
            DisplayedRecommendations.Add(source[index]);
    }

    private async void OnEpisodeTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is not AnimeEpisode episode || string.IsNullOrWhiteSpace(episode.ForumUrl))
                return;
            var match = System.Text.RegularExpressions.Regex.Match(
                episode.ForumUrl,
                @"(?:topicid|topic)=([0-9]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
                return;
            await Shell.Current.GoToAsync($"forumtopic?id={match.Groups[1].Value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnEpisodeTapped failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Splits the recommendation blurb so it really wraps around the poster: the first slice sits
    ///     beside the artwork, whatever is left flows underneath it at full card width. The only honest
    ///     budget comes from the measured title height, so this runs on SizeChanged and memoizes its
    ///     inputs on the item.
    /// </summary>
    private void OnRecommendationTitleSized(object sender, EventArgs e)
    {
        if (sender is not Label title || title.BindingContext is not DirectRecommendationData item)
            return;

        var width = title.Width;
        var titleHeight = title.Height;
        if (width <= 1 || titleHeight <= 0)
            return;

        var unchanged = Math.Abs(item.SplitWidth - width) < 1 &&
                        Math.Abs(item.SplitTitleHeight - titleHeight) < 1 &&
                        string.Equals(item.DescriptionSplitSource, item.Description, StringComparison.Ordinal);
        if (unchanged)
            return;

        item.SplitWidth = width;
        item.SplitTitleHeight = titleHeight;

        var full = item.Description;
        if (string.IsNullOrWhiteSpace(full))
        {
            item.SetDescriptionSplit(null, null);
            return;
        }

        const double lineHeight = 13 * 1.35;
        const double rowSpacing = 8;
        const double charWidth = 6.6;
        const double posterHeight = 195;

        var available = Math.Max(0, posterHeight - titleHeight - rowSpacing);
        var lines = (int)Math.Floor(available / lineHeight);
        // Keep one line of slack: measured line height drifts from the nominal 13 * 1.35 and an
        // over-eager budget is what makes the blurb run over the artwork.
        var budget = (int)Math.Floor(width / charWidth) * Math.Max(0, lines - 1);
        if (budget <= 8)
        {
            // Nothing fits beside the poster, so the blurb flows underneath it at full width.
            item.SetDescriptionSplit(null, full);
            return;
        }

        if (full.Length <= budget)
        {
            item.SetDescriptionSplit(full, null);
            return;
        }

        var cut = full.LastIndexOf(' ', Math.Min(full.Length, budget), Math.Min(full.Length, budget));
        if (cut <= 0)
            cut = Math.Min(full.Length, budget);
        item.SetDescriptionSplit(full.Substring(0, cut).TrimEnd(), full.Substring(cut).TrimStart());
    }

    private async void OnRecommendationTapped(object sender, TappedEventArgs e)
        => await NavigateToRelatedItem(e.Parameter as IDetailsPageArgs);

    private async void OnRelatedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (RelatedGrid?.Inner == null)
                return;
            // Clear it straight away, otherwise the same card stays selected and tapping it again
            // raises no SelectionChanged.
            RelatedGrid.Inner.SelectedItem = null;
            if (e.CurrentSelection.Count == 0)
                return;
            await NavigateToRelatedItem(e.CurrentSelection[0] as IDetailsPageArgs);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnRelatedSelectionChanged failed: " + ex.GetType().Name);
        }
    }

    private async Task NavigateToRelatedItem(IDetailsPageArgs item)
    {
        try
        {
            if (item == null || item.Id <= 0)
                return;
            var animeMode = item.Type != RelatedItemType.Manga;
            var args = new AnimeDetailsPageNavigationArgs(item.Id, item.Title, null, null, null)
            {
                AnimeMode = animeMode,
                Source = PageIndex.PageAnimeDetails,
                SourceTabIndex = Vm.DetailsPivotSelectedIndex
            };
            MauiDetailsNavigationHandoff.Set(args);
            var title = Uri.EscapeDataString(item.Title ?? string.Empty);
            var route = animeMode
                ? $"animedetails?id={item.Id}&title={title}"
                : $"animedetails?id={item.Id}&title={title}&manga=true";
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnRecommendationTapped failed: " + ex.GetType().Name);
        }
    }

    private void OnReviewMoreClicked(object sender, EventArgs e)
    {
        try
        {
            // CommandParameter already carries the exact review instance bound to the row,
            // so it is the only one that may flip. Walking the collection by index used to
            // expand every review from this one downwards.
            if (sender is Button button && button.CommandParameter is AnimeReviewData review)
                review.IsExpanded = !review.IsExpanded;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnReviewMoreClicked failed: " + ex.GetType().Name);
        }
    }

    private async void OnStatusClicked(object sender, EventArgs e)
    {
        try
        {
            var options = new[]
            {
                AnimeStatus.Watching, AnimeStatus.Completed, AnimeStatus.OnHold,
                AnimeStatus.Dropped, AnimeStatus.PlanToWatch
            };
            var rows = options.Select(status => new OptionsSheetItem(
                MALPlus.XShared.Utils.Utilities.StatusToString((int)status, !Vm.AnimeMode, false),
                "icon_status")).ToArray();
            var choice = await OptionsSheetHost.PickAsync("Set status", null, rows);
            if (string.IsNullOrEmpty(choice))
                return;
            var index = Array.IndexOf(options.Select(status =>
                MALPlus.XShared.Utils.Utilities.StatusToString((int)status, !Vm.AnimeMode, false)).ToArray(), choice);
            if (index >= 0)
                Vm.ChangeStatus(options[index]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnStatusClicked failed: " + ex.GetType().Name);
        }
    }

    private async void OnScoreClicked(object sender, EventArgs e)
    {
        try
        {
            var rows = Enumerable.Range(0, 11)
                .Select(score => new OptionsSheetItem(score == 0 ? "0 (clear)" : score.ToString(), "icon_score"))
                .Reverse()
                .ToArray();
            var choice = await OptionsSheetHost.PickAsync("Set score", null, rows);
            if (string.IsNullOrEmpty(choice))
                return;
            var number = choice.Split(' ')[0];
            if (int.TryParse(number, out var score))
                Vm.ChangeScoreCommand.Execute(score.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnScoreClicked failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// The old client let you type the watched count straight into the flyout; the model still
    /// carries WatchedEpsInput and ChangeWatchedEps for it, only the input surface was missing.
    /// </summary>
    /// <summary>
    ///     The hero stepper's pill while the entry is off the list. It adds the title to the
    ///     library instead of incrementing a counter that does not exist yet: the old code hid the
    ///     whole stepper in that state, so the only way to start an entry was the separate Add to
    ///     List button, and the counter itself looked absent rather than empty.
    /// </summary>
    private async void OnStepperStartTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null || !vm.AddAnimeVisibility)
                return;

            vm.AddAnimeCommand?.Execute(null);

            // The command is what puts it on the list; the counter is only useful once that has
            // happened, and the view model's own notifications redraw the pill.
            if (!vm.AddAnimeVisibility)
                return;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnStepperStartTapped failed: " + ex.GetType().Name);
        }
    }

    private async void OnEpisodesCounterTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null)
                return;

            int.TryParse(vm.MyEpisodesBind?.Split('/')[0], out var current);
            int.TryParse(vm.MyEpisodesBind?.Split('/').LastOrDefault(), out var max);

            var entered = await OptionsSheetHost.PromptAsync(
                vm.AnimeMode ? "Episodes watched" : "Chapters read",
                max > 0 ? $"Max {max}" : string.Empty,
                current.ToString(),
                max > 0 ? $"Max {max}" : string.Empty,
                maxLength: 6);

            if (entered == null)
                return;

            // An empty field clears the count, which is what the prompt says it does, so it is read as
            // zero rather than ignored. Anything that is not a number is ignored instead of being pushed
            // at the model, which used to be the only thing that could come back.
            vm.WatchedEpsInput = int.TryParse(entered.Trim(), out var watched) ? watched.ToString() : "0";
            vm.ChangeWatchedCommand.Execute(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus OnEpisodesCounterTapped failed: " + ex.GetType().Name);
        }
    }

    private void OnTrailerClicked(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(Vm.TrailerUrl))
                return;
            ShowVideoOverlay(Vm.TrailerUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnTrailerClicked failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     The synopsis is capped at four lines with a Show more underneath. Same reason as the
    ///     moreinfo note: the full text is printed by nobody on purpose, it just pushes the rest of
    ///     the tab below a wall of MAL Rewrite prose.
    /// </summary>
    private void OnSynopsisTapped(object sender, TappedEventArgs e)
    {
        Vm?.SetSynopsisExpanded();
    }

    /// <summary>
    ///     Expands the moreinfo note in place. The preview is capped at two lines because Tenrai's
    ///     field is AniDB's note dump: on a long show it is pages of crossover trivia and schedule
    ///     history that nobody wants pushed in front of them unasked.
    /// </summary>
    private void OnMoreInfoTapped(object sender, TappedEventArgs e)
    {
        Vm?.SetMoreInfoExpanded();
    }

    /// <summary>
    ///     An off-site reference chip. MAL is a list, not a browser, so these leave the app rather
    ///     than try to render inside it.
    /// </summary>
    private async void OnExternalLinkTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string url || string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            await global::Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnExternalLinkTapped failed: " + ex.GetType().Name);
        }
    }

    private async void OnOpEdTapped(object sender, TappedEventArgs e)
        => await ResolveOpEdAsync(e.Parameter as string);

    /// <summary>
    ///     The row's own command, so the extracted rows share this handler with the rows that used to
    ///     own it themselves.
    /// </summary>
    /// <remarks>
    ///     Held in a field rather than built on each read: an inline RelayCommand leaves the delegate
    ///     with nothing rooting it and Execute becomes a silent no-op that still answers CanExecute
    ///     true, which is a row that highlights and does nothing.
    /// </remarks>
    public ICommand OpEdRowCommand => _opEdRowCommand ?? (_opEdRowCommand =
        new Command<string>(async text => await ResolveOpEdAsync(text)));

    private ICommand _opEdRowCommand;

    private async Task ResolveOpEdAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Vm == null)
            return;
        try
        {
            // The OP and ED lists share one handler, so recover which list the row came from.
            var isOp = Vm.OPs.Contains(text);
            var sequence = AnimeThemesHelper.ParseSequence(text);
            var song = AnimeThemesHelper.ExtractOpEdSong(text);
            var query = AnimeThemesHelper.BuildOpEdSearchQuery(text);
            var entryId = Vm.Id;
            var entryTitle = Vm.Title;
            var animeMode = Vm.AnimeMode;

            ShowVideoLoading();

            var url = await Task.Run(async () =>
            {
                // 1) AnimeThemes: direct WebM, exact match, plays in the overlay. Resolved by the
                // entry's own MAL id, which is what makes it work for a title the two sources spell
                // differently.
                var themes = Vm.OpEdThemes;
                if (themes == null || themes.Count == 0)
                    themes = await AnimeThemesHelper.GetThemesAsync(entryId);
                var match = AnimeThemesHelper.FindMatch(themes, isOp, sequence, query, song, text);
                global::Android.Util.Log.Info("MALPlus Themes",
                    $"id={entryId} isOp={isOp} seq={sequence} themes={themes?.Count ?? 0} match={(string.IsNullOrEmpty(match?.Url) ? "no" : "si")} text={text}");
                if (!string.IsNullOrEmpty(match?.Url))
                    return match.Url;

                // 2) YouTube search scrape.
                var videoId = await VideoWebViewHelper.SearchYouTubeVideoId(query);
                if (!string.IsNullOrEmpty(videoId))
                    return $"https://www.youtube.com/watch?v={videoId}";

                return null;
            });

            if (!string.IsNullOrEmpty(url))
            {
                ShowVideoOverlay(url);
                return;
            }

            // 3) Nothing resolved: hand the search to the YouTube app.
            HideVideoLoading();
            await global::Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(
                new Uri("https://www.youtube.com/results?search_query=" +
                        System.Net.WebUtility.UrlEncode(query)));
        }
        catch (Exception ex)
        {
            HideVideoLoading();
            global::Android.Util.Log.Warn("MALPlus Video", "ResolveOpEdAsync: " + ex.GetType().Name);
        }
    }

    private void OnCharacterItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0)
            return;
        var id = ResolveCharacterId(e.CurrentSelection[0]);
        ClearSenderSelection(sender);
        if (id > 0)
            NavigateToCharacter(id);
    }

private void OnStaffItemSelected(object sender, SelectionChangedEventArgs e)
      {
          if (e.CurrentSelection.Count == 0)
              return;
          var id = ResolveStaffId(e.CurrentSelection[0]);
          ClearSenderSelection(sender);
          if (id > 0)
              NavigateToStaff(id);
      }

private async void OnJapaneseVoiceClicked(object sender, EventArgs e)
    {
        try
        {
            if (TryGetNavigationId((sender as ImageButton)?.CommandParameter, out var id))
                await Shell.Current.GoToAsync($"staff?id={id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus OnJapaneseVoiceClicked failed: " + ex.Message);
        }
    }

    private static int ResolveCharacterId(object item) => item switch
    {
        AnimeCharacterCard card => ResolveStringId(card.AnimeCharacter?.Data?.Id),
        MangaCharacterCard mangaCard => ResolveStringId(mangaCard.Id),
        _ => 0
    };

    private static int ResolveStaffId(object item) => item switch
    {
        StaffCard card => ResolveStringId(card.Id),
        _ => 0
    };

    private static int ResolveStringId(string value) => int.TryParse(value, out var id) ? id : 0;

    private static void ClearSenderSelection(object sender)
    {
        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;
    }

    private static async Task NavigateToCharacter(int id)
    {
        try
        {
            await Shell.Current.GoToAsync($"character?id={id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus character nav failed: " + ex.Message);
        }
    }

    private static async Task NavigateToStaff(int id)
    {
        try
        {
            await Shell.Current.GoToAsync($"staff?id={id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus staff nav failed: " + ex.Message);
        }
    }

    private static bool TryGetNavigationId(object value, out int id)
    {
        if (value is int numericId)
        {
            id = numericId;
            return id > 0;
        }
        return int.TryParse(value as string, out id) && id > 0;
    }

    private WebView VideoWebView => _videoWebView ??= EnsureVideoWebView();

    /// <summary>
    ///     URL handed to ExoPlayer, kept so a playback failure can fall back to the WebView with the
    ///     same file instead of leaving a black rectangle.
    /// </summary>
    private string _directMediaUrl;

    private WebView EnsureVideoWebView()
    {
        if (_videoWebView != null)
            return _videoWebView;

            _videoWebView = new WebView();
            // Spans rows 0 and 1, so it covers the WHOLE overlay instead of only the area below
            // the button row. Row 0 is left empty precisely so the buttons can float over the
            // video; sizing the video to row 1 left a dead band across the top of the picture.
            Grid.SetRow(_videoWebView, 0);
            Grid.SetRowSpan(_videoWebView, 2);
            VideoOverlay.Add(_videoWebView);
#if ANDROID
        _videoWebView.HandlerChanged += OnVideoWebViewHandlerChanged;
        // Adding the view can make MAUI attach the handler immediately, and HandlerChanged does not
        // fire again for a handler that is already there. Configuring only from that event is why the
        // chrome client was sometimes never installed and the player reported nothing: no logging, no
        // error reporting, and a refused file looked like a broken screen. The handler is therefore
        // configured here as well, which is harmless when the event still arrives and the only thing
        // that works when it does not.
        try
        {
            VideoWebViewHelper.ConfigurePlatformView(
                _videoWebView.Handler?.PlatformView as global::Android.Views.View);
        }
        catch
        {
        }
#endif
        return _videoWebView;
    }

    /// <summary>
    ///     One overlay, two players, and this is the only place that decides between them.
    ///
    ///     Direct media (AnimeThemes WebM) goes to ExoPlayer through <see cref="NativeVideoView"/>,
    ///     which finally gives a real scrub bar, a play button that agrees with what you can hear,
    ///     a start that does not wait on preload='auto', and a stop that actually silences it.
    ///     YouTube, the OP/ED search results page and the YouTube fallback stay on the WebView,
    ///     because a watch page is HTML with an iframe and ExoPlayer cannot load one.
    ///
    ///     MAUI 11 RC1 ships no MediaElement at all, which is why this is a hand written handler
    ///     over AndroidX Media3 rather than the built in control.
    /// </summary>
    private void ShowVideoOverlay(string url)
    {
        try
        {
            if (VideoOverlay == null || string.IsNullOrWhiteSpace(url))
                return;
            VideoWebViewHelper.BackHandler = OnVideoBackPressed;
            MainActivity.ArmVideoBackCallback();
            VideoOverlay.IsVisible = true;
            SetSystemBars(true);

            // Everything plays on the WebView now, direct media included. ExoPlayer is still wired up
            // below but no URL is routed to it, so this branch reads as what it is: one player.
            StopDirectMedia();
            var webView = EnsureVideoWebView();
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);

            // Only when this theme is asked for right after the last one was. Tapping down the list
            // spends the origin's per-IP allowance in a burst and the refusals start at the third
            // theme; a single theme opened normally is never delayed.
            var delay = VideoWebViewHelper.BurstDelay();
            if (delay <= TimeSpan.Zero)
            {
                _currentVideoUrl = url;
                webView.Source = VideoWebViewHelper.BuildSource(url);
                VideoWebViewHelper.ApplyMediaSettings(webView);
                return;
            }

            _pendingBurstUrl = url;
            _currentVideoUrl = url;
            ShowVideoLoading();
            _ = PlayAfterBurstAsync(url, delay);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "ShowVideoOverlay: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Waits out a burst and then plays, giving up quietly if the overlay is gone or the reader
    ///     has already moved on to another theme.
    /// </summary>
    private async Task PlayAfterBurstAsync(string url, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            if (VideoOverlay == null || !VideoOverlay.IsVisible || _pendingBurstUrl != url)
                return;
            _pendingBurstUrl = null;
            var webView = EnsureVideoWebView();
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
            webView.Source = VideoWebViewHelper.BuildSource(url);
            VideoWebViewHelper.ApplyMediaSettings(webView);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "PlayAfterBurst: " + ex.GetType().Name);
        }
    }

    private string _pendingBurstUrl;

    /// <summary>
    ///     How many times a refused theme has been asked for again, so one stubborn file cannot hold the
    ///     overlay open and a temporary refusal recovers by itself.
    /// </summary>
    private readonly VideoRetryPolicy _videoRetry = new();

    private string _currentVideoUrl;

    /// <summary>
    ///     A theme was refused. Asked for again after a wait, up to a point, and then left alone: the
    ///     origin's refusals are a full budget rather than a broken file, and stopping at the first one
    ///     is what left the ninth theme dead on a black splash.
    /// </summary>
    private void OnVideoPlaybackError(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                url = _currentVideoUrl;
            if (string.IsNullOrWhiteSpace(url) || url != _currentVideoUrl)
                return;

            _videoRetry.NoteFailed(url);
            var delay = _videoRetry.DelayFor(url);
            if (!delay.HasValue)
            {
                global::Android.Util.Log.Warn("MALPlus Video", "giving up on " + url);
                // Nothing is coming, so the spinner must go: left up it reads as a player still trying
                // long after it stopped, and it is the one thing still on screen.
                HideVideoLoading();
                return;
            }

            // Only for a retry the reader is still waiting on. ShowVideoLoading reopens the overlay, so
            // calling it after the overlay was closed put a spinner back over a video already dismissed.
            if (VideoOverlay == null || !VideoOverlay.IsVisible)
                return;

            global::Android.Util.Log.Warn("MALPlus Video",
                $"refused, retrying in {delay.Value.TotalSeconds}s: {url}");
            ShowVideoLoading();
            CancelPendingRetries();
            _retryTokenSource = new CancellationTokenSource();
            var token = _retryTokenSource.Token;
            _ = RetryVideoAsync(url, delay.Value, token);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "OnVideoPlaybackError: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Stops any retry that has not fired yet.
    /// </summary>
    /// <remarks>
    ///     A retry scheduled for a theme the reader has already left is worse than no retry: it waits out
    ///     its delay and then puts that theme back on screen by itself, which is what left the loading
    ///     spinner showing over a video that had been dismissed.
    /// </remarks>
    private void CancelPendingRetries()
    {
        try
        {
            _retryTokenSource?.Cancel();
            _retryTokenSource?.Dispose();
        }
        catch
        {
        }

        _retryTokenSource = null;
        _pendingBurstUrl = null;
    }

    private CancellationTokenSource _retryTokenSource;

    private void OnVideoPlaybackStarted(string url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url) && url == _currentVideoUrl)
                _videoRetry.NotePlaying(url);
        }
        catch
        {
        }
    }

    private async Task RetryVideoAsync(string url, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            // Everything has to still line up: the reader may have closed the overlay, moved to another
            // theme, or left the page entirely while this was waiting.
            if (token.IsCancellationRequested || VideoOverlay == null || !VideoOverlay.IsVisible ||
                _currentVideoUrl != url)
                return;
            var webView = EnsureVideoWebView();
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
            webView.Source = VideoWebViewHelper.BuildSource(url);
            VideoWebViewHelper.ApplyMediaSettings(webView);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "RetryVideo: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Starts direct media on ExoPlayer immediately, and nothing checks the file first.
    /// </summary>
    /// <remarks>
    ///     A probe was tried here, asking for a kilobyte with a range header to discover a refusal
    ///     cheaply. It was wrong twice over: the Range header is a restricted one and has to be set with
    ///     AddRange, so the request either threw or, when the header went ignored, downloaded the whole
    ///     file it was supposed to sample. Either way playback stopped for reasons unrelated to the
    ///     file, and the themes that used to play stopped playing.
    ///     <para>
    ///     So the source is asked once, by the player, which is the one client that needs the answer.
    ///     A refusal surfaces as a player error and is retried from <see cref="OnDirectVideoFailed"/>,
    ///     which is the only place that answers one.
    ///     </para>
    /// </remarks>
    private void StartDirectMedia(string url)
    {
        var player = DirectVideoPlayer;
        if (player == null)
            return;
        _directMediaUrl = url;
        if (_videoWebView != null)
            _videoWebView.IsVisible = false;
        player.IsVisible = true;
        player.Source = url;
    }

    /// <summary>
    ///     ExoPlayer could not play the file. Asks the source again before treating it as final,
    ///     because the refusals here are a moment and not a verdict.
    /// </summary>
    /// <remarks>
    ///     The origin refuses bursts. One file answered 206, then 503 twice, then 206 twice across a
    ///     handful of requests minutes apart, so a 503 on the third theme in a row is not that theme
    ///     being broken, it is the source being unhappy at that instant, and asking again a couple of
    ///     seconds later gets the file. Reporting it as final instead is what made playback stop after
    ///     two.
    ///     <para>
    ///     The waits are seconds rather than the 700/1400ms this first used. A refusal means the
    ///     budget is spent, and retrying inside that window spends it again: each early retry is one
    ///     more request against the same exhausted allowance, which is what turned a hiccup into
    ///     playback that never recovers.
    ///     </para>
    ///     <para>
    ///     Nothing falls back to the WebView player for a direct file. That fallback was the answer to
    ///     a codec the device cannot read, which is a real thing on some hardware, but it was also
    ///     firing on every refusal —and a refusal is what the retry above is for. Letting it through
    ///     meant the old WebView player and its white flash replacing playback the moment the source
    ///     blinked.
    ///     </para>
    /// </remarks>
    private void OnDirectVideoFailed(object sender, string reason)
    {
        var url = _directMediaUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Only a refusal by the source is worth another attempt. A codec failure is the device's
        // answer, and asking it twice more will produce the same answer three times.
        if (reason.IndexOf("BAD_HTTP_STATUS", StringComparison.Ordinal) >= 0 && RetryDirectMedia(url))
            return;

        global::Android.Util.Log.Warn("MALPlus Video", "direct media failed: " + reason);
        _directMediaUrl = null;
        StopDirectMedia();
    }

    /// <summary>
    ///     How many times each URL has been asked again, so one stubborn file cannot loop forever and
    ///     a refusal does not spend the origin's budget twice for the same theme.
    /// </summary>
    private readonly Dictionary<string, int> _directMediaRetries = new();

    /// <summary>
    ///     The URL a retry is waiting to serve, kept apart from <see cref="_directMediaUrl"/> on purpose.
    ///     Stopping the player clears the current URL, and the pending retry has to outlive that or it
    ///     would cancel itself before it ever fired.
    /// </summary>
    private string _pendingRetryUrl;

    private bool RetryDirectMedia(string url)
    {
        var attempts = _directMediaRetries.TryGetValue(url, out var seen) ? seen : 0;
        if (attempts >= 2)
            return false;
        _directMediaRetries[url] = attempts + 1;

        // Seconds, not the 700/1400ms this started with. The origin refuses bursts, so arriving while
        // the previous refusal is still settling spends the budget again and earns another refusal.
        var wait = TimeSpan.FromSeconds(2 + 2 * attempts);
        _pendingRetryUrl = url;
        global::Android.Util.Log.Warn("MALPlus Video",
            $"direct media retry {attempts + 1} in {wait.TotalSeconds}s: {url}");
        StopDirectMedia();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(wait);
            // The reader may have moved on or closed the overlay while waiting, and a newer retry
            // for another theme supersedes this one.
            if (VideoOverlay == null || !VideoOverlay.IsVisible || _pendingRetryUrl != url)
                return;
            _pendingRetryUrl = null;
            _directMediaUrl = url;
            DirectVideoPlayer.IsVisible = true;
            DirectVideoPlayer.Source = url;
        });
        return true;
    }

    private void StopDirectMedia()
    {
        // Closing is also the end of any retry that has not fired yet, and of the record of what was
        // playing. Without both, a scheduled retry survives the close, reopens the overlay by itself a
        // few seconds later and puts a spinner over a video the reader already dismissed.
        CancelPendingRetries();
        _currentVideoUrl = null;
        var player = DirectVideoPlayer;
        _directMediaUrl = null;
        if (player == null)
            return;
        try
        {
            // Source = null makes the handler Stop and Release, which is what guarantees the audio
            // is gone. Pause alone would leave the track running behind the closed overlay.
            player.IsPlaying = false;
            player.Source = null;
            player.IsVisible = false;
        }
        catch (Exception)
        {
        }
    }

    private void ShowVideoLoading()
    {
        try
        {
            if (VideoOverlay == null)
                return;
        var webView = EnsureVideoWebView();
        VideoOverlay.IsVisible = true;
        VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
                webView.Source = new HtmlWebViewSource
                {
                    Html = VideoWebViewHelper.BuildLoadingHtml(),
                    BaseUrl = "https://myanimelist.net"
                };
                VideoWebViewHelper.ApplyMediaSettings(webView);
                SetSystemBars(true);
        }
        catch { }
    }

    /// <summary>
    ///     Tears the overlay down, and this is where the "the music keeps playing after I close it"
    ///     bug lived. Setting Source = null unloads the WebView's document but does not stop what
    ///     is already playing inside it, and a quick close gesture races that unload entirely. The
    ///     media has to be paused explicitly first, and only then pointed at about:blank, which
    ///     tears the document down rather than just navigating it. The native side is paused and
    ///     stopped too, so either player goes quiet.
    /// </summary>
private void HideVideoLoading()
    {
        try
        {
            // Leaving full screen is part of closing, not something to remember: an overlay that went
            // away while the activity was still landscape left the whole app sideways.
            FullscreenVideo.Exit();
            if (VideoOverlay != null && VideoOverlay.IsVisible)
                VideoWebViewHelper.BackHandler = null;

                MainActivity.DisarmVideoBackCallback();
                StopDirectMedia();
                if (_videoWebView != null)
                {
                    VideoWebViewHelper.StopMediaAndClear(_videoWebView);
                    _videoWebView.IsVisible = true;
                }
                if (VideoOverlay != null)
                    VideoOverlay.IsVisible = false;
                SetSystemBars(false);
            }
            catch { }
        }

    private void CloseVideoOverlay(object sender, EventArgs e)
    {
        HideVideoLoading();
    }

    /// <summary>
///     Toggles full screen and swaps the glyph so the button says what it will do next rather
    ///     than what it just did.
    /// </summary>
    private void OnToggleFullscreen(object sender, TappedEventArgs e)
    {
        try
        {
            if (FullscreenVideo.IsActive)
            {
                FullscreenVideo.Exit();
                // Restore overlay to normal layout position (inside safe area)
                if (VideoOverlay != null)
                    VideoOverlay.Margin = new Thickness(0);
            }
            else
            {
                FullscreenVideo.Enter();
                // Pull overlay up into status bar area and down past nav bar for true fullscreen
                // The window is now laid out behind bars (SetDecorFitsSystemWindows=false), but MAUI
                // still positions views inside safe area. Negative margins compensate.
                if (VideoOverlay != null)
                {
                    var insets = MALPlus.Services.WindowInsetsHelper.Insets();
                    VideoOverlay.Margin = new Thickness(-insets.Left, -insets.Top, -insets.Right, -insets.Bottom);
                }
            }

            if (FullscreenIcon != null)
                FullscreenIcon.Source = FullscreenVideo.IsActive ? "icon_exit_fullscreen" : "icon_fullscreen";

            // Re-inset buttons after rotation lands
            Device.StartTimer(TimeSpan.FromMilliseconds(400), () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    VideoOverlayInsets.Apply(FullscreenButton, VideoCloseButton);
                    if (FullscreenIcon != null)
                        FullscreenIcon.Source = FullscreenVideo.IsActive
                            ? "icon_exit_fullscreen"
                            : "icon_fullscreen";
                });
                return false;
            });
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Fullscreen toggle failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Opens the list filtered by the genre or studio of the chip that was tapped.
    ///     <para>
    ///     It goes to the "animelist" route rather than the "///anime" section on purpose. A section
    ///     route pops back down to the section root, so Back had nothing to return to and closed the
    ///     app; a registered route is pushed, and Back lands on this entry again.
    ///     </para>
    ///     <para>
    ///     Themes such as "Ensemble Cast" have no genre id and therefore no list to open, which is
    ///     why they are not navigable and never reach this handler.
    ///     </para>
    /// </summary>
    private async void OnChipTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if ((sender as Border)?.BindingContext is not DetailChipViewModel chip || !chip.IsNavigable)
                return;

            var mode = chip.Genre.HasValue
                ? (int)AnimeListWorkModes.AnimeByGenre
                : (int)AnimeListWorkModes.AnimeByStudio;
            var type = chip.Genre.HasValue
                ? chip.Genre.Value.ToString()
                : chip.Studio?.ToString() ?? string.Empty;

            await Shell.Current.GoToAsync(
                $"animelist?mode={mode}&type={Uri.EscapeDataString(type)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnChipTapped failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Back gesture / back button while the player is up closes it and cancels navigation, so
    ///     the swipe drops you back onto the details page instead of leaving the entry. Neither a
    ///     SwipeGestureRecognizer (the WebView child eats the drag) nor Page.BackButtonPressed
    ///     (not in MAUI 7) works: this is routed through MainActivity.OnBackPressed.
    /// </summary>
    private bool OnVideoBackPressed()
    {
        if (VideoOverlay == null || !VideoOverlay.IsVisible)
            return false;
        HideVideoLoading();
        return true;
    }

    private void OnTabTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string parameter || !int.TryParse(parameter, out var tabIndex))
            return;
        var direction = Vm != null && tabIndex >= Vm.DetailsPivotSelectedIndex ? 1 : -1;
        _ = SelectTabAsync(tabIndex, direction);
    }

    private bool IsTabWarm(int tabIndex)
    {
        if (Vm == null) return false;
        switch (tabIndex)
        {
            // The OP/ED tab rides on the details payload, so it is warm exactly when Overview is.
            case 2: return Vm.DetailsLoaded || Vm.DetailsRowsLoaded;
            case 1: return Vm.EpisodesLoaded;
            case 3: return Vm.ReviewsLoaded;
            case 4: return Vm.RecommendationsLoaded;
            case 5: return Vm.RelatedLoaded;
            case 6:
            case 7: return Vm.CharactersLoaded;
            default: return true;
        }
    }

    /// <summary>
    ///     Memory breadcrumb. The details page used to sit at ~900MB RSS on a long-runner and we
    ///     need to tell apart the three things that can dominate it: decoded artwork (Android
    ///     graphics), the managed heap (which lives in NATIVE memory under Mono/AOT, so it shows
    ///     up as Native Heap and not as a managed counter) and one Java peer object per view.
    /// </summary>
    public static void LogMemory(string label)
    {
        try
        {
            long managed = GC.GetTotalMemory(false);
            long committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
            global::Android.Util.Log.Info("MALPlusMem",
                $"{label} gcHeap={managed / 1048576}MB committed={committed / 1048576}MB");
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("MALPlusMem", "LogMemory failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    ///     Remembers the tab the user picked by hand. Only the START of the background warm-up
    ///     chain moves: the sequence itself is always General, Details, Episodes, Reviews,
    ///     Recommendations, Related, Characters, Staff.
    /// </summary>
    private void RememberPreferredTab(int tabIndex)
    {
        try { Settings.DetailsLastTab = tabIndex; }
        catch { }
    }

    private async Task SelectTabAsync(int tabIndex, int direction)
    {
        if (Vm == null || tabIndex < 0 || tabIndex > 7 || !IsTabVisible(tabIndex))
            return;
        var changed = Vm.DetailsPivotSelectedIndex != tabIndex;
        if (changed)
        {
            Vm.DetailsPivotSelectedIndex = tabIndex;
            RememberPreferredTab(tabIndex);
        }
        ApplyTabActiveState();
        if (changed)
            await AnimateTabTransitionAsync(direction);
        if (tabIndex >= 2)
            await TopUpTabUntilScrollable(tabIndex);
    }

    /// <summary>
    ///     A list tab opens with one chunk, and on a tall screen that chunk is shorter than the
    ///     viewport. A ScrollView with nothing to scroll raises no Scrolled event, so the scroll
    ///     driven growth in OnPageScrolled never fires and the rest of the list is unreachable.
    ///     This tops the list up in chunks, with a beat between them so each chunk paints on its
    ///     own, until it is tall enough to scroll; from there normal scrolling takes over and
    ///     keeps appending. Applies to every growing tab, not just the poster grids: they all
    ///     hang off the same page scroll and all of them used to stall at the first chunk.
    /// </summary>
    private async Task TopUpTabUntilScrollable(int tabIndex)
    {
        // Related is the one tab that cannot be left to the scroll. Its grid is a ContentView
        // wrapping a CollectionView inside the page ScrollView, and the inner list wins the
        // nested gesture arbitration: the page moves about forty pixels and then the drag is
        // gone, so anything past that point would be unreachable. It is also the shortest list
        // (thirty entries), so it is simply filled completely. The list tabs below are the ones
        // that keep loading as the user scrolls.
        var fillCompletely = tabIndex == 5;
        for (var pass = 0; pass < 12; pass++)
        {
            if (Vm == null || Vm.DetailsPivotSelectedIndex != tabIndex)
                return;
            if (!fillCompletely && IsTabContentScrollable())
            {
                Console.WriteLine($"MALPlusTabs topup tab={tabIndex} pass={pass} scrollable viewport={PageScroll?.Height} content={PageScroll?.ContentSize.Height}");
                return;
            }
            _lastProgressiveAppendScrollY = double.NaN;
            var before = DisplayedCountFor(tabIndex);
            AppendNextProgressiveChunk();
            if (fillCompletely && DisplayedCountFor(tabIndex) == before)
                return;
            // The HeightRequest is a binding, so the list has to remeasure before it can be
            // judged against the viewport.
            await Task.Delay(90);
        }
    }

    private int DisplayedCountFor(int tabIndex)
    {
        if (Vm == null)
            return 0;
        return tabIndex switch
        {
            2 => DisplayedEpisodes.Count,
            3 => DisplayedReviews.Count,
            4 => DisplayedRecommendations.Count,
            5 => DisplayedRelated.Count,
            6 => Vm.AnimeMode ? DisplayedCharacters.Count : DisplayedMangaCharacters.Count,
            7 => DisplayedStaff.Count,
            _ => 0
        };
    }

    private bool IsTabContentScrollable()
    {
        if (PageScroll == null)
            return false;
        var viewport = PageScroll.Height;
        if (viewport <= 0 || double.IsNaN(viewport))
            return false;
        var content = PageScroll.ContentSize.Height;
        if (content <= 0 || double.IsNaN(content))
            return false;
        // Two screens, not one. A single screen of content is technically scrollable, but the
        // Related grid's inner CollectionView wins the nested gesture arbitration and the page
        // stops after a few pixels, so on that tab the list has to already be long enough to
        // browse without the scroll carrying the load.
        return content > viewport * 2;
    }

    private async Task AnimateTabTransitionAsync(int direction)
    {
        if (TabContentHost == null)
            return;
        //Dimming the whole host before switching (Opacity 0.35 + a 28px slide) was the visible
        //half of "tapping a tab takes ages": the fade only starts once the new tab has already
        //been laid out, so the screen sat dimmed and offset for the whole layout pass. A short
        //slide alone reads as responsive and does not hide the content while it measures.
        var offset = direction < 0 ? -20 : 20;
        TabContentHost.TranslationX = offset;
        TabContentHost.Opacity = 1;
        await Task.WhenAll(
            TabContentHost.TranslateTo(0, 0, TabTransitionLength, Easing.CubicOut),
            TabContentHost.FadeTo(1, TabTransitionLength / 2));
    }

    private int? FindAdjacentVisibleTab(int current, int direction)
    {
        var next = direction < 0 ? current - 1 : current + 1;
        while (next >= 0 && next <= 7)
        {
            if (IsTabVisible(next))
                return next;
            next += direction < 0 ? -1 : 1;
        }
        return null;
    }

/// <summary>
        ///     Which tabs exist for the current entry. General and Details merged into one Overview,
        ///     so the indices shifted: 0 Overview, 1 Episodes, 2 OP/ED, 3 Reviews, 4
        ///     Recommendations, 5 Related, 6 Characters, 7 Staff. Movies have no episode list and
        ///     OP/ED hides itself until the entry actually has themes, which is why both of those
        ///     answer to the view model instead of to the media type alone.
        /// </summary>
        private bool IsTabVisible(int index)
        {
            if (Vm == null)
                return true;
            // Manga keeps Reviews (3), Recs (4) and Related (5); only Episodes (1), OP/ED (2)
            // and Staff (7) are anime-only. This has to agree with
            // ApplyTabStripVisibility*, or a tab answers as visible while its label is gone.
            if (!Vm.AnimeMode)
                return index is not (1 or 2 or 7);
            if (index == 2)
                return Vm.OpEdVisible;
            if (Vm.Type?.Equals("Movie", StringComparison.OrdinalIgnoreCase) == true)
                return index != 1;
            return true;
        }

        private void ApplyTabActiveState()
        {
            if (TabGeneral == null || TabEpisodes == null || TabOpEd == null || TabReviews == null ||
                TabRecs == null || TabRelated == null || TabCharacters == null || TabStaff == null)
                return;
            var selected = Vm?.DetailsPivotSelectedIndex ?? 0;
            var labels = new[] { TabGeneral, TabEpisodes, TabOpEd, TabReviews, TabRecs, TabRelated, TabCharacters, TabStaff };
            for (var index = 0; index < labels.Length; index++)
                labels[index].TextColor = index == selected ? Color.FromArgb("#0066FF") : Color.FromArgb("#B3FFFFFF");
        }

    /// <summary>
    /// LAST AIRED only exists while something is actually airing, so the row can end up with a
    /// single field. The fields are packed to the left with a fixed gap so SCORE and STATUS stay
    /// together, and when only one is left it is stretched so the row keeps its shape.
    /// </summary>
    private void UpdateHeroMetaLayout()
    {
        if (HeroMetaLayout == null || ScoreMetaGroup == null || StatusMetaGroup == null ||
            LastAiredMetaGroup == null)
            return;

        var vm = Vm;
        bool showLastAired = vm != null && AirTimeUtils.IsCurrentlyAiringStatus(vm.Status) &&
                             !string.IsNullOrWhiteSpace(vm.LastAired);
        // The last aired block is one Grid spanning both rows, so hiding it hides its header and
        // its value together and leaves the row spacing to the Grid rather than to the items.
        LastAiredMetaGroup.IsVisible = showLastAired;

        // When it is the only thing left the row would otherwise sit at the start of a wide grid
        // and the stepper would drift away from it.
        var solo = showLastAired ? null : new View[] { ScoreHeaderGroup, ScoreMetaGroup, StatusHeaderGroup, StatusMetaGroup };
        if (solo != null)
            foreach (var view in solo)
                view.HorizontalOptions = LayoutOptions.Start;
    }

    private void OnPageViewportSizeChanged(object sender, EventArgs e)
    {
        if (PageScroll == null)
            return;
        var width = PageScroll.Width;
        var height = PageScroll.Height;
        if (Math.Abs(width - _lastViewportWidth) < 0.5 && Math.Abs(height - _lastViewportHeight) < 0.5)
            return;
        _lastViewportWidth = width;
        _lastViewportHeight = height;
        ApplyHeroState(PageScroll.ScrollY);
        UpdateTabListHeight();
        Dispatcher.Dispatch(UpdateHeaderSpacer);
    }

    private void OnHeaderPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _headerPanActive = true;
                _headerPanLastY = e.TotalY;
                break;
            case GestureStatus.Running:
                if (!_headerPanActive || PageScroll == null)
                    return;
                var currentY = e.TotalY;
                var deltaY = currentY - _headerPanLastY;
                _headerPanLastY = currentY;
                if (Math.Abs(deltaY) < 0.5 || Math.Abs(e.TotalX) > Math.Abs(deltaY) * 1.25)
                    return;
                var maxScroll = Math.Max(0, PageScroll.ContentSize.Height - PageScroll.Height);
                var target = Math.Clamp(PageScroll.ScrollY - deltaY, 0, maxScroll);
                _ = PageScroll.ScrollToAsync(0, target, false);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _headerPanActive = false;
                _headerPanLastY = 0;
                break;
        }
    }

    public void OnBoundedListScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        // Characters and staff are sized to their content again and sit inside the page
        // ScrollView, so the page moves when they are scrolled and this handler has nothing left
        // to do. It is kept only as a spare hook: wiring it again would be a mistake, because the
        // hero has to follow the page scroll and nothing else.
    }

    private void OnPageScrolled(object sender, ScrolledEventArgs e)
    {
        ApplyHeroState(e.ScrollY);
        var near = IsNearPageBottom(e.ScrollY);
        if (!near)
        {
            // Away from the tail, so the next approach to the bottom may append again.
            _lastProgressiveAppendScrollY = double.NaN;
            return;
        }
        // IsNearPageBottom is deliberately generous (three quarters of the viewport), so a single
        // fling fires it on many consecutive frames. Without this guard each of those frames
        // appended a chunk, which is what made this tab unusable when it was tried before: forty
        // rows per frame. One append per 120px of real scrolling is the same endless list with
        // none of the stampede.
        if (!double.IsNaN(_lastProgressiveAppendScrollY) &&
            e.ScrollY - _lastProgressiveAppendScrollY < 120)
            return;
        _lastProgressiveAppendScrollY = e.ScrollY;
        AppendNextProgressiveChunk();
    }

    private void ResetHeaderLayoutState()
    {
        _headerSpacerRetry = 0;
        if (PageScroll != null)
            _ = PageScroll.ScrollToAsync(0, 0, false);
        if (TabStripScroll != null)
            _ = TabStripScroll.ScrollToAsync(0, 0, false);
        if (HeaderFlow != null)
            HeaderFlow.TranslationY = 0;
        if (TabContentHost != null)
            TabContentHost.TranslationX = 0;
        if (TabContentHost != null)
            TabContentHost.TranslationY = 0;
        ApplyHeroState(0);
        Dispatcher.Dispatch(UpdateHeaderSpacer);
    }

    private void UpdateHeaderSpacer()
    {
        if (HeaderSpacer == null)
            return;
        var bar = (ActionBar?.Height ?? 0) + (TabStripScroll?.Height ?? 0);
        if (bar <= 0)
        {
            if (_headerSpacerRetry++ < 5)
                Dispatcher.Dispatch(UpdateHeaderSpacer);
            return;
        }
        _headerSpacerRetry = 0;
        var height = HeroExpandedHeight + bar;
        if (Math.Abs(HeaderSpacer.HeightRequest - height) > 0.5)
            HeaderSpacer.HeightRequest = height;
    }

    private static double GetDisplayDensity()
    {
#if ANDROID
        return global::Android.Content.Res.Resources.System.DisplayMetrics.Density;
#else
        return 1;
#endif
    }

    private void ApplyHeroState(double scrollY)
    {
        if (HeroContainer == null || HeroVisual == null || PosterContainer == null || HeroScrim == null)
            return;
        var progress = GetCollapseProgress(scrollY);
        var targetHeight = HeroExpandedHeight - HeroCollapseRange * progress;
        if (Math.Abs(HeroContainer.HeightRequest - targetHeight) > 0.5)
        {
            HeroContainer.HeightRequest = targetHeight;
            HeroVisual.HeightRequest = targetHeight;
        }
        var targetScale = GetPosterTargetScale();
        var scale = 1 + (targetScale - 1) * progress;
        PosterContainer.ScaleX = scale;
        PosterContainer.ScaleY = scale;
        HeroScrim.Opacity = progress;
    }

    private static double GetCollapseProgress(double scrollY)
    {
        if (HeroCollapseRange <= 0)
            return 1;
        return Math.Clamp(Math.Max(0, scrollY) / HeroCollapseRange, 0, 1);
    }

    private double GetPosterTargetScale()
    {
        var availableWidth = PageScroll?.Width ?? 0;
        var posterWidth = PosterContainer?.Width ?? 0;
        if (availableWidth <= 0 || posterWidth <= 0)
            return 1;
        return Math.Max(1, availableWidth / posterWidth);
    }

    private bool IsNearPageBottom(double scrollY)
    {
        if (PageScroll == null)
            return false;
        var contentHeight = PageScroll.ContentSize.Height;
        if (contentHeight <= 0 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            return false;
        var remaining = contentHeight - scrollY - Math.Max(0, PageScroll.Height);
        return remaining <= Math.Max(600, PageScroll.Height * 0.75);
    }

    private void AppendNextProgressiveChunk()
    {
        var tab = Vm?.DetailsPivotSelectedIndex ?? -1;
        // Only the active tab grows, so scrolling a long Episodes list never quietly appends
        // rows to a list nobody is looking at. OnPageScrolled keeps one append per 120px of
        // scroll, which is what makes this safe to run for the three grid tabs as well: their
        // rows are a portrait each, so an unbounded append is a stutter, not a few lines of text.
        switch (tab)
        {
            case 1:
                if (Vm.Episodes.Count > DisplayedEpisodes.Count)
                {
                    var target = Math.Min(Vm.Episodes.Count, DisplayedEpisodes.Count + ProgressiveChunkSize);
                    for (var index = DisplayedEpisodes.Count; index < target; index++)
                        DisplayedEpisodes.Add(Vm.Episodes[index]);
                }
                break;
            case 3:
                if (Vm.Reviews.Count > DisplayedReviews.Count)
                {
                    var target = Math.Min(Vm.Reviews.Count, DisplayedReviews.Count + ProgressiveChunkSize);
                    for (var index = DisplayedReviews.Count; index < target; index++)
                        DisplayedReviews.Add(Vm.Reviews[index]);
                }
                break;
            case 4:
                if (Vm.Recommendations.Count > DisplayedRecommendations.Count)
                {
                    var target = Math.Min(Vm.Recommendations.Count, DisplayedRecommendations.Count + RecommendationChunkSize);
                    for (var index = DisplayedRecommendations.Count; index < target; index++)
                        DisplayedRecommendations.Add(Vm.Recommendations[index]);
                }
                break;
            case 5:
                GrowImageTab(Vm.RelatedAnime, DisplayedRelated, ref _relatedShown, MakeRelatedCard, int.MaxValue);
                break;
            case 6:
                if (!Vm.AnimeMode)
                    GrowImageTab(Vm.MangaCharacterData, DisplayedMangaCharacters,
                        ref _mangaCharactersShown, MakeMangaCharacterCard);
                else
                    GrowImageTab(Vm.AnimeStaffData?.AnimeCharacterPairs, DisplayedCharacters,
                        ref _charactersShown, MakeCharacterCard);
                break;
            case 7:
                GrowImageTab(Vm.AnimeStaffData?.AnimeStaff, DisplayedStaff,
                    ref _staffShown, MakeStaffCard);
                break;
        }

        // A row added to one of these lists changes the height that list is asked to occupy, and
        // that height is a plain number read through a binding, so it does not follow the
        // collection on its own. Without this the list stayed at its old height while its content
        // grew, and the page had empty space to scroll through at the end of it.
        if (tab >= 6 && tab <= 7)
        {
            OnPropertyChanged(nameof(CharactersListHeight));
            OnPropertyChanged(nameof(MangaCharactersListHeight));
            OnPropertyChanged(nameof(StaffListHeight));
        }
    }

    private void OnRelatedMoreTapped(object sender, EventArgs e)
    {
        try
        {
            // Only ever reached past RelatedShowCap, so the character pair limit that the other
            // tabs share does not apply here: it exists for cast lists, not for a grid of posters.
            _relatedShown = Math.Min(Vm.RelatedAnime.Count, _relatedShown + ImageTabChunkSize);
            GrowImageTab(Vm.RelatedAnime, DisplayedRelated, ref _relatedShown, MakeRelatedCard, int.MaxValue);
            NotifyStateProperties();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Related grow failed: " + ex.GetType().Name);
        }
    }

    private delegate TCard TCardFactory<TSource, TCard>(TSource source);

    private static void GrowImageTab<TSource, TCard>(IReadOnlyList<TSource> source, ObservableCollection<TCard> target,
        ref int shown, TCardFactory<TSource, TCard> factory, int cap = MALPlus.XShared.Comm.Anime.AnimeCharactersStaffQuery.MaxCharacterPairs)
    {
        if (source == null || target == null)
            return;
        // One chunk per pass, and never past what the query already trimmed.
        var next = Math.Min(source.Count, Math.Min(cap, shown + ImageTabChunkSize));
        if (next <= target.Count)
            return;
        for (var index = target.Count; index < next; index++)
            target.Add(factory(source[index]));
        shown = next;
        Console.WriteLine($"MALPlusTabs grow rows={target.Count} of {source.Count} (shown={shown})");
    }

    private static AnimeCharacterCard MakeCharacterCard(
        AnimeDetailsPageViewModel.AnimeStaffDataViewModels.AnimeCharacterStaffModelViewModel pair)
        => new AnimeCharacterCard(pair);

    private static MangaCharacterCard MakeMangaCharacterCard(FavouriteViewModel character)
        => new MangaCharacterCard(character);

    private static StaffCard MakeStaffCard(FavouriteViewModel person)
        => new StaffCard(person);

    private static RelatedAnimeData MakeRelatedCard(RelatedAnimeData item)
        => item;

    private void SetTabError(int tabIndex, Exception exception)
    {
        var message = exception == null ? string.Empty : EmptyStateText(string.Empty, "Unable to load this section.");
        _detailsError = tabIndex == 1 ? message : _detailsError;
        _episodesError = tabIndex == 2 ? message : _episodesError;
        _reviewsError = tabIndex == 3 ? message : _reviewsError;
        _recommendationsError = tabIndex == 4 ? message : _recommendationsError;
        _relatedError = tabIndex == 5 ? message : _relatedError;
        _charactersError = tabIndex == 6 ? message : _charactersError;
        _staffError = tabIndex == 7 ? message : _staffError;
        NotifyStateProperties();
    }

    private bool HasDetailsContent()
    {
        return Vm.DetailedDataVisibility ||
               Vm.LeftGenres.Count > 0 ||
               Vm.RightGenres.Count > 0 ||
               Vm.Information.Count > 0 ||
               Vm.Stats.Count > 0 ||
               Vm.OPs.Count > 0 ||
               Vm.EDs.Count > 0;
    }

    private static string EmptyStateText(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(GeneralErrorText));
        OnPropertyChanged(nameof(GeneralErrorVisible));
        OnPropertyChanged(nameof(GeneralLoadingVisible));
        OnPropertyChanged(nameof(GeneralContentVisible));
        OnPropertyChanged(nameof(GeneralSynopsisEmptyVisible));
        OnPropertyChanged(nameof(DetailsErrorText));
        OnPropertyChanged(nameof(DetailsErrorVisible));
        OnPropertyChanged(nameof(DetailsEmptyVisible));
        OnPropertyChanged(nameof(DetailsContentVisible));
        OnPropertyChanged(nameof(EpisodesErrorText));
        OnPropertyChanged(nameof(EpisodesErrorVisible));
        OnPropertyChanged(nameof(EpisodesEmptyVisible));
        OnPropertyChanged(nameof(EpisodesContentVisible));
        OnPropertyChanged(nameof(ReviewsErrorText));
        OnPropertyChanged(nameof(ReviewsErrorVisible));
        OnPropertyChanged(nameof(ReviewsEmptyVisible));
        OnPropertyChanged(nameof(ReviewsContentVisible));
        OnPropertyChanged(nameof(RecommendationsErrorText));
        OnPropertyChanged(nameof(RecommendationsErrorVisible));
        OnPropertyChanged(nameof(RecommendationsEmptyVisible));
        OnPropertyChanged(nameof(RecommendationsContentVisible));
        OnPropertyChanged(nameof(RelatedErrorText));
        OnPropertyChanged(nameof(RelatedErrorVisible));
        OnPropertyChanged(nameof(RelatedEmptyVisible));
        OnPropertyChanged(nameof(RelatedContentVisible));
        OnPropertyChanged(nameof(RelatedRemainingCount));
        OnPropertyChanged(nameof(RelatedMoreVisible));
        OnPropertyChanged(nameof(RelatedMoreText));
        OnPropertyChanged(nameof(CharactersErrorText));
        OnPropertyChanged(nameof(CharactersErrorVisible));
        OnPropertyChanged(nameof(CharactersEmptyVisible));
        OnPropertyChanged(nameof(CharactersContentVisible));
        OnPropertyChanged(nameof(StaffErrorText));
        OnPropertyChanged(nameof(StaffErrorVisible));
        OnPropertyChanged(nameof(StaffEmptyVisible));
        OnPropertyChanged(nameof(StaffContentVisible));
    }

    public sealed class AnimeCharacterCard
    {
        public AnimeCharacterCard(AnimeDetailsPageViewModel.AnimeStaffDataViewModels.AnimeCharacterStaffModelViewModel pair)
        {
            AnimeCharacter = pair?.AnimeCharacter;
            AnimeStaffPerson = pair?.AnimeStaffPerson;
            var character = AnimeCharacter?.Data as MALPlus.Models.Models.Favourites.AnimeCharacter;
            Role = character?.Role ?? string.Empty;
            Favorites = character?.Favorites ?? 0;
            // Only the Japanese seiyuu. Tenrai hands over up to 32 voice actors per character
            // across ten languages, and the card has room for exactly one row.
            JapaneseVoice =
                (pair?.VoiceActors ?? new List<FavouriteViewModel>())
                .FirstOrDefault(actor => actor?.Data is MALPlus.Models.Models.Favourites.AnimeStaffPerson va &&
                                        string.Equals(va.Language, "Japanese", StringComparison.OrdinalIgnoreCase))
                ?? (pair?.VoiceActors ?? new List<FavouriteViewModel>()).FirstOrDefault(actor => actor?.Data != null);
            VoiceFavorites = (JapaneseVoice?.Data as MALPlus.Models.Models.Favourites.AnimeStaffPerson)?.Favorites ?? 0;
        }

        public FavouriteViewModel AnimeCharacter { get; }
        public FavouriteViewModel AnimeStaffPerson { get; }

        /// <summary>The Japanese voice actor, or null when the character has none.</summary>
        public FavouriteViewModel JapaneseVoice { get; }

        public bool HasJapaneseVoice => JapaneseVoice?.Data != null && !string.IsNullOrEmpty(JapaneseVoice.Data.Name);

        public string Role { get; }

        public bool HasRole => !string.IsNullOrEmpty(Role);

        public int Favorites { get; }

        public string FavoritesText => Favorites > 0 ? ShortNumber(Favorites) : string.Empty;

        public bool HasFavorites => Favorites > 0;

        public int VoiceFavorites { get; }

        public string VoiceFavoritesText => VoiceFavorites > 0 ? ShortNumber(VoiceFavorites) : string.Empty;

        public bool HasVoiceFavorites => VoiceFavorites > 0;

        internal static string ShortNumber(int value) =>
            value >= 1000 ? (value / 1000d).ToString("0.#") + "K" : value.ToString();
    }

    /// <summary>
    /// Staff and manga rows bind straight to FavouriteViewModel.Data, which is typed as the
    /// FavouriteBase and therefore has no Role/Favorites/PrimaryPosition. These wrappers
    /// unwrap the concrete type so the templates can bind real fields.
    /// </summary>
    public sealed class StaffCard
    {
        public StaffCard(FavouriteViewModel person)
        {
            Person = person;
            var staff = person?.Data as MALPlus.Models.Models.Favourites.AnimeStaffPerson;
            PrimaryPosition = staff?.PrimaryPosition ?? string.Empty;
            ExtraPositions = staff?.ExtraPositions ?? 0;
            // Up to two lines of roles, the rest folded into a count so a six role person
            // does not push the row past its height. "+2" on its own said nothing; this says
            // what the roles are.
            var roles = staff?.Positions;
            if (roles != null && roles.Count > 0)
            {
                RoleLineOne = roles[0];
                if (roles.Count > 1)
                    RoleLineTwo = roles.Count > 2
                        ? $"{roles[1]}  +{roles.Count - 2} more"
                        : roles[1];
            }
        }

        public FavouriteViewModel Person { get; }
        public string Name => Person?.Data?.Name ?? string.Empty;
        public string ImgUrl => Person?.Data?.ImgUrl ?? string.Empty;
        public string Id => Person?.Data?.Id ?? string.Empty;
        public string PrimaryPosition { get; }
        public bool HasPrimaryPosition => !string.IsNullOrEmpty(PrimaryPosition);
        public int ExtraPositions { get; }
        public bool HasExtraPositions => ExtraPositions > 0;
        public string RoleLineOne { get; }
        public string RoleLineTwo { get; }
        public bool HasRoleLineOne => !string.IsNullOrEmpty(RoleLineOne);
        public bool HasRoleLineTwo => !string.IsNullOrEmpty(RoleLineTwo);
    }

    public sealed class MangaCharacterCard
    {
        public MangaCharacterCard(FavouriteViewModel character)
        {
            Character = character;
            var data = character?.Data as MALPlus.Models.Models.Favourites.AnimeCharacter;
            Role = data?.Role ?? string.Empty;
            Favorites = data?.Favorites ?? 0;
        }

        public FavouriteViewModel Character { get; }
        public string Name => Character?.Data?.Name ?? string.Empty;
        public string ImgUrl => Character?.Data?.ImgUrl ?? string.Empty;
        public string Id => Character?.Data?.Id ?? string.Empty;
        public string Role { get; }
        public bool HasRole => !string.IsNullOrEmpty(Role);
        public int Favorites { get; }
        public string FavoritesText => Favorites > 0 ? AnimeCharacterCard.ShortNumber(Favorites) : string.Empty;
        public bool HasFavorites => Favorites > 0;
    }
}
