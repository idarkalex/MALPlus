using MALPlus.Controls;
using MALPlus.Services;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

[QueryProperty(nameof(InitialMode), "mode")]
public partial class CalendarPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private string _initialMode;
    private bool _subscribed;
    private int _displayedIndex = -1;
    private bool _isLoading;

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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ThemeService.ThemeChanged += OnThemeChanged;
        OnThemeChanged();
        UpdateHeaderUI();
        ArmBackCallback();
        if (Vm != null)
            Vm.IsPageActive = true;

        DayGrid?.InvalidateLayout();
        BuildTabStrip();
        try
        {
            var force = Vm.CalendarBuiltKey != CalendarPageViewModel.CurrentSettingsKey();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var shouldResetScroll = !_initialized || force;
            _isLoading = true;
            if (!_initialized)
            {
                _initialized = true;
                await Vm.InitAsync(force, resetToDesiredTab: true);
                global::Android.Util.Log.Info("MALPlus Calendar", $"init first build done in {sw.ElapsedMilliseconds}ms");
            }
            else if (force)
            {
                await Vm.InitAsync(true, resetToDesiredTab: true);
                global::Android.Util.Log.Info("MALPlus Calendar", $"init rebuild done in {sw.ElapsedMilliseconds}ms");
            }

            BuildDayStrip();
            SyncDisplayedDay(scrollToTop: shouldResetScroll);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "Init failed: " + ex.GetType().Name);
        }
        finally
        {
            _isLoading = false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeService.ThemeChanged -= OnThemeChanged;
        if (Vm != null)
            Vm.IsPageActive = false;
        if (_subscribed && Vm != null)
        {
            Vm.PropertyChanged -= OnVmPropertyChanged;
            Vm.DayStripChanged -= OnVmDayStripChanged;
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

    // ---------- single-grid day switch (matches AnimeListPage architecture) ----------

    private void SyncDisplayedDay(bool scrollToTop)
    {
        try
        {
            var pages = Vm?.CalendarData;
            if (pages == null || pages.Count == 0)
            {
                DayGrid.ItemsSource = null;
                _displayedIndex = -1;
                return;
            }

            var index = Vm.CalendarPivotIndex;
            if (index < 0 || index >= pages.Count)
            {
                index = 0;
                Vm.CalendarPivotIndex = 0;
            }

            var page = pages[index];
            var switchingDay = _displayedIndex != index;
            if (!ReferenceEquals(DayGrid.ItemsSource, page.Items))
            {
                DayGrid.ItemsSource = page.Items;
                if (scrollToTop || switchingDay)
                    ScrollGridToTop();
            }
            else if (scrollToTop && switchingDay)
            {
                ScrollGridToTop();
            }

            _displayedIndex = index;
            HighlightActiveTab();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "SyncDisplayedDay failed: " + ex.GetType().Name);
        }
    }

    private void ScrollGridToTop()
    {
        try
        {
#if ANDROID
            if (DayGrid?.Inner?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                rv.ScrollToPosition(0);
#else
            if (DayGrid?.ItemsSource is System.Collections.IList list && list.Count > 0)
                DayGrid.Inner.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
#endif
        }
        catch { }
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
            _isLoading = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Vm.InitAsync(true, resetToDesiredTab: true);
            BuildDayStrip();
            SyncDisplayedDay(scrollToTop: true);
            global::Android.Util.Log.Info("MALPlus Calendar", $"mode rebuild done in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "mode rebuild failed: " + ex.GetType().Name);
        }
        finally
        {
            _isLoading = false;
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
            Vm.DayStripChanged += OnVmDayStripChanged;
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

    private void OnVmDayStripChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildDayStrip();
            // If enrichment added an item to the currently visible day, refresh the grid source.
            var pages = Vm?.CalendarData;
            if (pages != null && _displayedIndex >= 0 && _displayedIndex < pages.Count)
            {
                var current = pages[_displayedIndex];
                if (ReferenceEquals(DayGrid.ItemsSource, current.Items))
                {
                    DayGrid.ItemsSource = null;
                    DayGrid.ItemsSource = current.Items;
                }
            }
        });
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading)
            return;

        if (e.PropertyName == nameof(CalendarPageViewModel.CalendarPivotIndex))
        {
            MainThread.BeginInvokeOnMainThread(() => SyncDisplayedDay(scrollToTop: true));
            return;
        }
        if (e.PropertyName != nameof(CalendarPageViewModel.CalendarData)) return;
        if (Vm?.CalendarData == null || Vm.CalendarData.Count == 0) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildDayStrip();
            SyncDisplayedDay(scrollToTop: true);
        });
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        if (Vm?.CalendarData == null || Vm.CalendarData.Count == 0)
            return false;

        var current = Vm.CalendarPivotIndex;
        var next = current + (direction > 0 ? 1 : -1);
        if (next < 0 || next >= Vm.CalendarData.Count)
            return false;

        SelectDay(next);
        return true;
    }

    private void SelectDay(int index)
    {
        if (Vm?.CalendarData == null || index < 0 || index >= Vm.CalendarData.Count)
            return;

        if (Vm.CalendarPivotIndex != index)
            Vm.CalendarPivotIndex = index;
        SyncDisplayedDay(scrollToTop: true);
    }

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
        SelectDay(index);
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
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? string.Empty)}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Calendar", "grid item nav failed: " + ex.GetType().Name);
        }
    }
}
