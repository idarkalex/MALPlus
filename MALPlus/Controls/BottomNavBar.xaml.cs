namespace MALPlus.Controls;

public partial class BottomNavBar : ContentView
{
    // Fired after a successful tab navigation with the exact route used. MAUI does
    // not raise Navigated when only the query changes on an already-visible
    // ShellContent, so pages that need to react to a re-tap rely on this.
    public event Action<string> SectionActivated;

    // Fired the moment a tab is tapped, with the route that is about to be applied, BEFORE
    // GoToAsync. SectionActivated only arrives after the navigation resolves, which for the
    // pager is after its scroll, and anything that has to look instant (the status name on the
    // list page) used to arrive a visible beat late because of it.
    public event Action<string> SectionChanging;

    private static readonly Color SelectedColor = Color.FromArgb("#0066FF");
    private static readonly Color UnselectedColor = Color.FromArgb("#A0FFFFFF");
    private static readonly Color SelectedButtonColor = Color.FromArgb("#1F0066FF");
    private static readonly Color TransparentButtonColor = Color.FromArgb("#00000000");
    private static readonly string[] Sections = { "discover", "anime", "manga", "more" };
    private Shell _subscribedShell;
    private bool _navigationInFlight;
    private string _pendingRoute;
    private string _pendingSection;
    private bool _pendingAllowsCurrent;
    private long _requestVersion;
    private long _navigationVersion;

    public BottomNavBar()
    {
        InitializeComponent();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent == null)
        {
            UnsubscribeFromShell();
            InvalidateNavigation();
            // Dropped with the parent. Four of these exist, one per root page, and a stale override on
            // one that is no longer on screen would repaint a tab nobody is looking at the moment the
            // shell next navigates.
            _sectionOverride = null;
            _appliedSection = null;
            return;
        }

        try
        {
            var shell = Shell.Current;
            if (shell == null)
            {
                UnsubscribeFromShell();
                return;
            }

            if (!ReferenceEquals(_subscribedShell, shell))
            {
                UnsubscribeFromShell();
                _subscribedShell = shell;
                _subscribedShell.Navigated += OnShellNavigated;
            }
            RefreshSelection();
        }
        catch { }
    }

    private void UnsubscribeFromShell()
    {
        var shell = _subscribedShell;
        _subscribedShell = null;
        if (shell == null)
            return;

        try
        {
            shell.Navigated -= OnShellNavigated;
        }
        catch { }
    }

    private void InvalidateNavigation()
    {
        _navigationVersion++;
        _requestVersion++;
        _pendingRoute = null;
        _pendingSection = null;
        _pendingAllowsCurrent = false;
    }

    private void OnShellNavigated(object sender, ShellNavigatedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshSelection);
    }

    private string _appliedSection;

    private string _sectionOverride;

    /// <summary>
    ///     Marks a different tab than the one on screen, for a page that is still inside a section it
    ///     was reached from elsewhere.
    /// </summary>
    /// <remarks>
    ///     A Seasonal or a Top list is opened from More but lives in the anime section, so the bar said
    ///     "Anime" while the reader was nowhere near their library: the tabs disagreed with the page and
    ///     there was nothing on screen saying where they actually were. Passing "more" marks the tab the
    ///     panel was reached from, which is also the tab back leads to. Passing null gives the bar back
    ///     to the section the shell reports.
    ///     <para>
    ///     Kept as an override rather than folded into <see cref="CurrentSection" /> because the shell's
    ///     own answer is not wrong, only incomplete: the list really is in the anime section, and it is
    ///     the reader's mental model that needs correcting.
    ///     </para>
    /// </remarks>
    public void SetSectionOverride(string section)
    {
        if (string.Equals(_sectionOverride, section, StringComparison.Ordinal))
            return;
        _sectionOverride = section;
        _appliedSection = null;
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        try
        {
            var section = string.IsNullOrEmpty(_sectionOverride) ? CurrentSection() : _sectionOverride;
            // Shell.Navigated fires on every navigation anywhere, and there are 4
            // BottomNavBar instances alive (one per root page). Re-writing the 20
            // bindable properties each time dirtied 4 buttons for no visual change.
            if (string.Equals(section, _appliedSection, StringComparison.Ordinal))
                return;
            _appliedSection = section;
            SetSelectedSection(section);
        }
        catch { }
    }

    private static string CurrentLocation()
        => Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;

    private static string CurrentSection()
    {
        var location = CurrentLocation();
        var path = location.Split(new[] { '?' }, 2)[0];
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            foreach (var section in Sections)
            {
                if (string.Equals(segment, section, StringComparison.OrdinalIgnoreCase))
                    return section;
            }
        }

        return Shell.Current?.CurrentItem?.Route ?? string.Empty;
    }

    private static bool OnSection(string section)
        => string.Equals(CurrentSection(), section, StringComparison.OrdinalIgnoreCase);

    private void SetSelectedSection(string section)
    {
        SetSelected(DiscoverButton, DiscoverIcon, DiscoverLabel, "discover_compass",
            string.Equals(section, "discover", StringComparison.OrdinalIgnoreCase));
        SetSelected(AnimeButton, AnimeIcon, AnimeLabel, "anime_list",
            string.Equals(section, "anime", StringComparison.OrdinalIgnoreCase));
        SetSelected(MangaButton, MangaIcon, MangaLabel, "manga_books",
            string.Equals(section, "manga", StringComparison.OrdinalIgnoreCase));
        SetSelected(MoreButton, MoreIcon, MoreLabel, "more_dots",
            string.Equals(section, "more", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The selected tab is marked by tint alone: accent coloured icon, accent coloured label.
    ///
    ///     It used to be a tinted Button background, which filled the whole grid cell, and then a
    ///     Border pill sized 72x42, and both were wrong in the same way. Any background that
    ///     belongs to the tab also belonged to the overflow dots sitting beside the label, so the
    ///     dots either fell outside it or carried a second, different background of their own. The
    ///     dots are gone now, moved to the status bar on the list page, so a pill could be brought
    ///     back if the tint ever stops being enough.
    /// </summary>
    private static void SetSelected(Button button, Image icon, Label label, string iconName, bool selected)
    {
        try
        {
            button.BackgroundColor = TransparentButtonColor;
            label.TextColor = selected ? SelectedColor : UnselectedColor;
            // Image has no tint in MAUI 7, so the selected tab swaps to an accent-coloured
            // copy of the same glyph instead of being marked with an outline.
            icon.Source = ImageSource.FromFile(selected ? $"{iconName}_accent" : iconName);
            icon.Opacity = selected ? 1.0 : 0.55;
        }
        catch { }
    }

    private void OnDiscoverTapped(object sender, EventArgs e)
        => NavigateToSection("discover");

    private void OnAnimeTapped(object sender, EventArgs e)
        => NavigateToSection("anime");

    private void OnMangaTapped(object sender, EventArgs e)
        => NavigateToSection("manga");

    private void OnMoreTapped(object sender, EventArgs e)
        => NavigateToSection("more");

    private static bool IsListSection(string section)
        => string.Equals(section, "anime", StringComparison.OrdinalIgnoreCase)
           || string.Equals(section, "manga", StringComparison.OrdinalIgnoreCase);

    // The list tabs must always carry an explicit work mode and status. A bare
    // "///anime" would let the page keep whatever [QueryProperty] the previous
    // route left behind, which is how "Anime" ended up showing the Top list.
    private static string SectionRoute(string section, bool reset)
    {
        var query = SectionQuery(section, reset);
        if (query == null)
            return $"///{section}";
        return $"///{section}?{query}";
    }

    private static string SectionQuery(string section, bool reset)
    {
        var query = section switch
        {
            "anime" => "mode=0&status=0",
            "manga" => "mode=2&status=0",
            _ => null
        };
        if (query == null)
            return null;
        return reset ? $"{query}&reset=1" : query;
    }

    private void NavigateToSection(string section)
        => NavigateToRoute(SectionRoute(section, OnSection(section) && IsListSection(section)),
            section, IsListSection(section));

    private void NavigateToRoute(string route, string section, bool allowCurrent)
    {
        var requestId = ++_requestVersion;
        SetSelectedSection(section);
        if (!allowCurrent && OnSection(section))
        {
            _pendingRoute = null;
            _pendingSection = null;
            _pendingAllowsCurrent = false;
            RefreshSelection();
            return;
        }

        // Synchronous and before GoToAsync on purpose: the caller repaints from this, so the
        // label follows the finger instead of trailing the pager's scroll.
        try { SectionChanging?.Invoke(route); }
        catch { }

        var navigationVersion = _navigationVersion;
        _ = NavigateToRouteAsync(route, section, allowCurrent, requestId, navigationVersion);
    }

    private async Task NavigateToRouteAsync(string route, string section, bool allowCurrent,
        long requestId, long navigationVersion)
    {
        var ownsNavigation = false;
        try
        {
            if (requestId != _requestVersion || navigationVersion != _navigationVersion)
                return;

            if (_navigationInFlight)
            {
                _pendingRoute = route;
                _pendingSection = section;
                _pendingAllowsCurrent = allowCurrent;
                return;
            }

            if (!allowCurrent && OnSection(section))
            {
                _pendingRoute = null;
                _pendingSection = null;
                _pendingAllowsCurrent = false;
                MainThread.BeginInvokeOnMainThread(RefreshSelection);
                return;
            }

            var shell = Shell.Current;
            if (shell == null)
                return;

            _navigationInFlight = true;
            ownsNavigation = true;
            await shell.GoToAsync(route);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { SectionActivated?.Invoke(route); }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("MALPLUS", $"BottomNav GoToAsync failed '{route}': {ex}");
        }
        finally
        {
            if (ownsNavigation && navigationVersion == _navigationVersion)
            {
                _navigationInFlight = false;
                MainThread.BeginInvokeOnMainThread(CompleteNavigation);
            }
        }
    }

    private void CompleteNavigation()
    {
        if (Parent == null)
            return;

        RefreshSelection();
        var route = _pendingRoute;
        var section = _pendingSection;
        var allowCurrent = _pendingAllowsCurrent;
        _pendingRoute = null;
        _pendingSection = null;
        _pendingAllowsCurrent = false;
        if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(section))
            NavigateToRoute(route, section, allowCurrent);
    }
}
