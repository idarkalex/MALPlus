using MALPlus.Controls;
using MALPlus.Services;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

[QueryProperty(nameof(InitialMode), "mode")]
public partial class CalendarPage : ContentPage
{
    private bool _initialized;
    private string _initialMode;
    private bool _subscribed;

    /// <summary>
    ///     True from the moment a poster opens until this page is visible again. The carousel raises
    ///     an item-change with position 0 while the details page is being pushed over this one, and
    ///     that write landed in the view model, so coming back dropped the reader on the first day
    ///     instead of the one they had picked.
    /// </summary>
    private bool _posterOpening;

    /// <summary>
    ///     "mine" or "global", set by the two Discover calendar tiles or More page.
    /// </summary>
    public string InitialMode
    {
        get => _initialMode;
        set
        {
            _initialMode = value;
            if (string.IsNullOrEmpty(value))
                return;
            SetCalendarMode(string.Equals(value, "global", StringComparison.OrdinalIgnoreCase));
        }
    }

    private CalendarPageViewModel Vm => (CalendarPageViewModel)BindingContext;

    public CalendarPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.CalendarPage;
        UpdateHeaderUI();
        // The pager and the day strip have to agree at all times, and the two-way Position
        // binding on its own left the strip lagging on the first swipe.
        CalendarCarousel.PositionChanged += OnCarouselPositionChanged;
        CalendarCarousel.CurrentItemChanged += OnCarouselCurrentItemChanged;
    }

    private void OnCarouselCurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        try
        {
            if (_posterOpening || !_initialized || Vm?.CalendarData == null || Vm.CalendarData.Count == 0)
                return;
            var position = CalendarCarousel.Position;
            global::Android.Util.Log.Info("MALPlus Calendar", $"current item -> {position}");
            if (position >= 0 && position < Vm.CalendarData.Count && position != Vm.CalendarPivotIndex)
            {
                Vm.CalendarPivotIndex = position;
                HighlightActiveTab();
            }
            ScheduleStripResync();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "current item sync failed: " + ex.GetType().Name);
        }
    }

    private int _stripSyncGeneration;

    private void ScheduleStripResync()
    {
        var generation = ++_stripSyncGeneration;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(350), () =>
        {
            try
            {
                if (generation != _stripSyncGeneration || Vm?.CalendarData == null || Vm.CalendarData.Count == 0 || _posterOpening || !_initialized)
                    return;
                var position = CalendarCarousel.Position;
                if (position >= 0 && position < Vm.CalendarData.Count && position != Vm.CalendarPivotIndex)
                    Vm.CalendarPivotIndex = position;
                HighlightActiveTab();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("MALPlus Calendar", "strip resync failed: " + ex.GetType().Name);
            }
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _posterOpening = false;
        ThemeService.ThemeChanged += OnThemeChanged;
        OnThemeChanged();
        UpdateHeaderUI();
        ArmBackCallback();
        // Re-subscribe on every appearance: the page instance outlives a navigation, and the day
        // strip has to be rebuilt whenever the view model produces a fresh CalendarData.
        BuildTabStrip();
        try
        {
            // A rebuild is needed when the data on screen was produced with different settings:
            // the mode (Current Season / My List), the first day of the week, or anything else
            // that goes into the build. Comparing the whole set is what makes a settings change
            // applied while this page was away take effect on the way back.
            var force = Vm.CalendarBuiltKey != CalendarPageViewModel.CurrentSettingsKey();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_initialized)
            {
                _initialized = true;
                await Vm.Init(force);
                global::Android.Util.Log.Info("MALPlus Calendar", $"init first build done in {sw.ElapsedMilliseconds}ms");
            }
            else if (force)
            {
                await Vm.Init(true);
                global::Android.Util.Log.Info("MALPlus Calendar", $"init rebuild done in {sw.ElapsedMilliseconds}ms");
            }
            SyncCarouselPosition();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "Init failed: " + ex.GetType().Name);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeService.ThemeChanged -= OnThemeChanged;
        if (_subscribed)
        {
            Vm.PropertyChanged -= OnVmPropertyChanged;
            _subscribed = false;
        }
#if ANDROID
        if (_backArmed)
        {
            _backArmed = false;
            MainActivity.DisarmMenuBackCallback();
        }
#endif
    }

    private void OnThemeChanged()
    {
        try
        {
            BackgroundColor = ThemeService.CurrentBackground;
            HighlightActiveTab();
        }
        catch { }
    }

    private void OnBackTapped(object sender, TappedEventArgs e)
    {
        _ = PopBackAsync();
    }

    // ---------- back gesture ----------

    private bool _backArmed;

    /// <summary>
    ///     The way back from this page and from the details page it opens. A literal pop names the
    ///     page below instead of letting the shell unwind to its route parent, which dropped both
    ///     the details and this page and left the reader on the tab root.
    /// </summary>
    private void ArmBackCallback()
    {
#if ANDROID
        _backArmed = true;
        MainActivity.ArmMenuBackCallback(() => _ = PopBackAsync());
        global::Android.Util.Log.Info("MALPlus Calendar", "back armed");
#endif
    }

    private async Task PopBackAsync()
    {
        global::Android.Util.Log.Info("MALPlus Calendar", "pop-back begin");
        try
        {
            await Shell.Current.Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Info("MALPlus Calendar", "pop failed: " + ex.GetType().Name);
            await Shell.Current.GoToAsync("..");
        }
        try
        {
            global::Android.Util.Log.Info("MALPlus Calendar", $"back -> {Shell.Current.CurrentState.Location}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Info("MALPlus Calendar", "back log failed: " + ex.GetType().Name);
        }
    }

    // ---------- day strip / pager sync ----------

    private void OnCarouselPositionChanged(object sender, PositionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentPosition < 0 || _posterOpening || !_initialized || Vm?.CalendarData == null || Vm.CalendarData.Count == 0)
                return;
            var changed = e.CurrentPosition < Vm.CalendarData.Count && Vm != null && e.CurrentPosition != Vm.CalendarPivotIndex;
            if (changed)
            {
                global::Android.Util.Log.Info("MALPlus Calendar", $"day -> {e.CurrentPosition}");
                Vm.CalendarPivotIndex = e.CurrentPosition;
            }
            HighlightActiveTab();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "position sync failed: " + ex.GetType().Name);
        }
    }

    private void SyncCarouselPosition()
    {
        try
        {
            if (Vm?.CalendarData != null && Vm.CalendarData.Count > 0 &&
                Vm.CalendarPivotIndex >= 0 && Vm.CalendarPivotIndex < Vm.CalendarData.Count)
            {
                if (CalendarCarousel.Position != Vm.CalendarPivotIndex)
                    CalendarCarousel.Position = Vm.CalendarPivotIndex;
            }
            // The day change can also arrive through the Position binding (the view model picks
            // the landing day while it builds), which moves the element but not necessarily the
            // pager: the platform sync below is what makes the move real.
            SyncPagerToElement();
            // The landing runs after this wait (wherever it came from), so this is the point
            // where the pager has settled with its pages attached: re-arm its swipe.
            ReassertCarouselSwipe();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "carousel sync failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Moves the pager to the page the element points at, without going through MAUI's own
    ///     scroll machinery. The element's Position mapper arms a goto token and starts a queued
    ///     scroll that, depending on layout timing, can never apply: from then on the token stays
    ///     set and every element-driven move (landing, strip tap) silently does nothing while the
    ///     element says it moved. Clearing the token first and then scrolling the platform
    ///     directly is what makes the move real; the ScrollBy also raises the platform scroll
    ///     events that keep the element itself in sync afterwards.
    /// </summary>
    private void SyncPagerToElement()
    {
        try
        {
            ResetGotoToken();
            var target = Vm?.CalendarPivotIndex ?? -1;
#if ANDROID
            if (target < 0 || CalendarCarousel.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView platform)
                return;
            if (platform.GetAdapter() == null || target >= platform.GetAdapter().ItemCount)
                return;
            var lm = platform.GetLayoutManager() as AndroidX.RecyclerView.Widget.LinearLayoutManager;
            var current = lm?.FindFirstCompletelyVisibleItemPosition() ?? -1;
            if (current < 0)
                current = lm?.FindFirstVisibleItemPosition() ?? -1;
            if (current < 0 || current == target)
                return;

            if (CalendarCarousel.Position != target)
                CalendarCarousel.Position = target;

            platform.ScrollToPosition(target);
#else
            if (target >= 0 && CalendarCarousel.Position != target)
                CalendarCarousel.Position = target;
#endif
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "pager sync failed: " + ex.GetType().Name);
        }
    }

    private void ResetGotoToken()
    {
        try
        {
            var platform = CalendarCarousel.Handler?.PlatformView;
            if (platform == null)
                return;
            var type = platform.GetType();
            while (type != null)
            {
                var field = type.GetField("_gotoPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(platform, -1);
                    return;
                }
                type = type.BaseType;
            }
        }
        catch
        {
        }
    }

    // ---------- header & mode switch ----------

    private void UpdateHeaderUI()
    {
        try
        {
            var isGlobal = Settings.CalendarShowAllAiring;
            if (CalendarTitleLabel != null)
                CalendarTitleLabel.Text = isGlobal ? "Broadcast Calendar" : "My Schedule";
            if (CalendarSubtitleLabel != null)
                CalendarSubtitleLabel.Text = isGlobal ? "Weekly TV & ONA anime schedule" : "Airing episodes from your list";
            if (ModeSwitchButton != null)
                ModeSwitchButton.Text = isGlobal ? "My Schedule" : "Broadcast";
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "UpdateHeaderUI: " + ex.GetType().Name);
        }
    }

    private void OnToggleModeClicked(object sender, EventArgs e)
    {
        SetCalendarMode(!Settings.CalendarShowAllAiring);
    }

    private void SetCalendarMode(bool allAiring)
    {
        if (Settings.CalendarShowAllAiring == allAiring && _initialized)
            return;
        Settings.CalendarShowAllAiring = allAiring;
        UpdateHeaderUI();
        if (!_initialized)
            return;
        _ = RebuildAsync();
    }

    private async Task RebuildAsync()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Vm.Init(true);
            global::Android.Util.Log.Info("MALPlus Calendar", $"mode rebuild done in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "mode rebuild failed: " + ex.GetType().Name);
        }
    }

    // ---------- tab strip ----------

    private void BuildTabStrip()
    {
        if (Vm == null)
            return;
        if (!_subscribed)
        {
            Vm.PropertyChanged += OnVmPropertyChanged;
            _subscribed = true;
        }
        // Back-nav re-entry: the data is already there, so redraw without rebuilding it.
        if (Vm.CalendarData != null && Vm.CalendarData.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildDayStrip();
                HighlightActiveTab();
            });
        }
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarPageViewModel.CalendarPivotIndex))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                HighlightActiveTab();
                if (Vm?.CalendarData != null && Vm.CalendarData.Count > 0 &&
                    Vm.CalendarPivotIndex >= 0 && Vm.CalendarPivotIndex < Vm.CalendarData.Count)
                {
                    if (CalendarCarousel.Position != Vm.CalendarPivotIndex)
                        CalendarCarousel.Position = Vm.CalendarPivotIndex;
                    SyncPagerToElement();
                }
            });
            return;
        }
        if (e.PropertyName != nameof(CalendarPageViewModel.CalendarData)) return;
        if (Vm?.CalendarData == null || Vm.CalendarData.Count == 0) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ReassertCarouselSwipe();
            BuildDayStrip();
            SyncCarouselPosition();
        });
    }

    /// <summary>
    ///     Re-applies the pager's swipe flag after every publish. A publish replaces the
    ///     carousel's items and the platform pager must come out of that swap with user input
    ///     on; the flag is set directly on the platform view as well as through the element.
    /// </summary>
    private void ReassertCarouselSwipe()
    {
        try
        {
            CalendarCarousel.IsSwipeEnabled = false;
            CalendarCarousel.IsSwipeEnabled = true;
#if ANDROID
            if (CalendarCarousel.Handler?.PlatformView is Microsoft.Maui.Controls.Handlers.Items.MauiCarouselRecyclerView platform)
            {
                platform.IsSwipeEnabled = true;
                platform.SetItemViewCacheSize(8);
            }
            ArmCarouselTouchShield();
#endif
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "swipe re-assert failed: " + ex.GetType().Name);
        }
    }

#if ANDROID
    private global::Android.Views.View _touchShieldArmedFor;
    private CarouselTouchListener _touchListener;
    private bool _moveDisallowed;
    private readonly System.Collections.Generic.HashSet<global::Android.Views.View> _gridArmed = new();

    // The day page hosts a vertical poster grid whose MauiRecyclerView carries a gesture
    // dispatcher that intercepts a horizontal drag and replays it to the carousel through
    // OnTouchEvent. That replay bypasses the carousel's own intercept phase, so the pager
    // never enters its dragging state and the swipe is silently swallowed. A descendant
    // calling requestDisallowInterceptTouchEvent makes the dispatcher abandon that replay
    // and let the normal touch chain (and therefore the carousel) handle the gesture.
    private void OnDayPageLoaded(object sender, EventArgs e)
    {
        try
        {
            if (sender is not Microsoft.Maui.IVisualTreeElement node)
                return;
            foreach (var grid in FindPosterGrids(node))
                ArmGridPlatform(grid);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "day page arm failed: " + ex.GetType().Name);
        }
    }

    private void DisallowInnerGrids()
    {
        var sent = 0;
        foreach (var gridPlatform in _gridArmed)
        {
            try
            {
                if (gridPlatform is global::Android.Views.IViewParent parentGrid)
                {
                    parentGrid.RequestDisallowInterceptTouchEvent(true);
                    sent++;
                }
            }
            catch
            {
            }
        }

        if (sent > 0 && CalendarCarousel.Handler?.PlatformView is global::Android.Views.IViewParent carouselParent)
        {
            try
            {
                carouselParent.RequestDisallowInterceptTouchEvent(false);
            }
            catch
            {
            }
        }
    }

    private void ArmGridPlatform(Controls.AnimePosterGrid grid)
    {
        if (grid.Handler?.PlatformView is not global::Android.Views.View platform)
            return;
        _gridArmed.Add(platform);
    }

    private static System.Collections.Generic.List<Controls.AnimePosterGrid> FindPosterGrids(Microsoft.Maui.IVisualTreeElement node)
    {
        var found = new System.Collections.Generic.List<Controls.AnimePosterGrid>();
        var stack = new System.Collections.Generic.Stack<Microsoft.Maui.IVisualTreeElement>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is Controls.AnimePosterGrid grid)
            {
                found.Add(grid);
                continue;
            }

            foreach (var child in current.GetVisualChildren())
                stack.Push(child);
        }
        return found;
    }

    /// <summary>
    ///     Arms a non-consuming touch listener on the carousel so every new gesture re-asserts
    ///     that the nested poster grids never hand a horizontal drag to their parent scroller.
    /// </summary>
    private void ArmCarouselTouchShield()
    {
        try
        {
            if (CalendarCarousel.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView platform)
                return;
            if (ReferenceEquals(_touchShieldArmedFor, platform))
                return;

            _touchListener = new CarouselTouchListener(this);
            platform.AddOnItemTouchListener(_touchListener);
            _touchShieldArmedFor = platform;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "touch shield failed: " + ex.GetType().Name);
        }
    }

    private void OnCarouselItemTouch(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, global::Android.Views.MotionEvent e)
    {
        try
        {
            switch (e.ActionMasked)
            {
                case global::Android.Views.MotionEventActions.Down:
                    _moveDisallowed = false;
                    DisallowInnerGrids();
                    break;
                case global::Android.Views.MotionEventActions.Move:
                    if (!_moveDisallowed)
                    {
                        _moveDisallowed = true;
                        DisallowInnerGrids();
                    }
                    break;
                case global::Android.Views.MotionEventActions.Up:
                    recyclerView.PostDelayed(() => SyncSettledPage(recyclerView), 450);
                    break;
            }
        }
        catch
        {
        }
    }

    /// <summary>
    ///     Reads the page the pager physically settled on and, when the platform did not sync it
    ///     back to the element, sets the element position itself. MAUI's own scroll-to-position
    ///     sync is gated by a goto-token that a programmatic landing can leave set, and from then
    ///     on swipe settles are swallowed: the pager moves, the strip and the view model do not.
    /// </summary>
    private void SyncSettledPage(AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
    {
        try
        {
            var settled = -1;
            if (recyclerView.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
                settled = lm.FindFirstCompletelyVisibleItemPosition();
            if (recyclerView.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                return;
            if (settled >= 0 && settled < (Vm?.CalendarData?.Count ?? 0) && CalendarCarousel.Position != settled)
            {
                CalendarCarousel.Position = settled;
                ResetGotoToken();
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "settle sync failed: " + ex.GetType().Name);
        }
    }

    private sealed class CarouselTouchListener : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
    {
        readonly CalendarPage _page;

        public CarouselTouchListener(CalendarPage page)
        {
            _page = page;
        }

        public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, global::Android.Views.MotionEvent e)
        {
            _page.OnCarouselItemTouch(recyclerView, e);
            return false;
        }

        public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, global::Android.Views.MotionEvent e)
        {
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
        {
        }
    }
#else
    private void OnDayPageLoaded(object sender, EventArgs e) { }
#endif

    /// <summary>
    ///     Builds Option A Squircle Tab Chips for days of the week in a horizontal scroller.
    ///     Centrally highlights and tracks active day with smooth scrolling.
    /// </summary>
    private void BuildDayStrip()
    {
        try
        {
            if (DayChipsLayout == null || Vm?.CalendarData == null)
                return;

            DayChipsLayout.Children.Clear();
            var count = Vm.CalendarData.Count;
            var activeIndex = Vm.CalendarPivotIndex;

            for (int i = 0; i < count; i++)
            {
                var page = Vm.CalendarData[i];
                var index = i;
                var title = page is CalendarSummaryPivotPage ? "All" : page.Header;
                var itemCount = page.Items?.Count ?? 0;
                var text = itemCount > 0 ? $"{title} ({itemCount})" : title;
                var isActive = index == activeIndex;

                var chip = TabChipHelper.CreateChip(text, index, OnDayChipClicked, isActive);
                DayChipsLayout.Children.Add(chip);
            }

            HighlightActiveTab();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "BuildDayStrip: " + ex.GetType().Name);
        }
    }

    private void OnDayChipClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not int index)
            return;
        if (Vm?.CalendarData == null || index < 0 || index >= Vm.CalendarData.Count)
            return;

        if (Vm.CalendarPivotIndex != index)
        {
            Vm.CalendarPivotIndex = index;
            CalendarCarousel.Position = index;
            SyncPagerToElement();
        }
        HighlightActiveTab();
    }

    private void HighlightActiveTab()
    {
        try
        {
            var pages = Vm?.CalendarData;
            if (pages == null || DayChipsLayout == null) return;
            var activeIndex = Vm.CalendarPivotIndex;

            for (int i = 0; i < DayChipsLayout.Children.Count; i++)
            {
                if (DayChipsLayout.Children[i] is Button btn)
                {
                    var isActive = i == activeIndex;
                    TabChipHelper.ApplyStyle(btn, isActive);
                    if (isActive && DayChipsScroll != null)
                    {
                        _ = DayChipsScroll.ScrollToAsync(btn, ScrollToPosition.Center, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "HighlightActiveTab: " + ex.GetType().Name);
        }
    }

    // ---------- card navigation ----------

    /// <summary>
    ///     The calendar is a browsing surface: a tap opens the entry, like everywhere else in the
    ///     app. The per-row actions of the old list layout have no room on a poster card, and a
    ///     press-and-hold is not available on Android in MAUI 8, so "Add to List" and
    ///     "Export to calendar" both live on the details page instead.
    /// </summary>
    private async void OnGridItemTapped(object sender, object itemContext)
    {
        AnimeItemViewModel item = itemContext as AnimeItemViewModel;
        if (item == null && itemContext is AnimeItemAbstraction abs)
            item = abs.ViewModel;
        if (item == null)
            return;
        try
        {
            HighlightActiveTab();
            _backArmed = false;
#if ANDROID
            MainActivity.ArmMenuBackCallback(() => _ = PopBackAsync());
#endif
            _posterOpening = true;
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? string.Empty)}");
        }
        catch (Exception ex)
        {
            _posterOpening = false;
            global::Android.Util.Log.Warn("MALPlus Calendar", "grid item nav failed: " + ex.GetType().Name);
        }
    }

    private async void OnPosterTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not AnimeItemViewModel item)
            return;
        try
        {
            HighlightActiveTab();
            // The callback that answers the way back has to be the most recently added one to see
            // the press at all, and the one armed on arrival was overtaken by the hub's own (MorePage
            // arms after this page's OnAppearing, measured on device). Re-arming here puts this page
            // back in front for as long as the details page is on top, so the press lands on the pop
            // and returns to the calendar instead of unwinding past it to the tab root. The flag
            // stays down so this page's OnDisappearing does not disarm what was just armed.
            _backArmed = false;
#if ANDROID
            MainActivity.ArmMenuBackCallback(() => _ = PopBackAsync());
#endif
            _posterOpening = true;
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? string.Empty)}");
        }
        catch (Exception ex)
        {
            _posterOpening = false;
            global::Android.Util.Log.Warn("MALPlus Calendar", "poster nav failed: " + ex.GetType().Name);
        }
    }
}