using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.Views;

namespace MALPlus;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // The flyout is declared disabled in AppShell.xaml, next to NavBarIsVisible, and not here.
        //
        // Hiding the nav bar only removes the hamburger button: the gesture that opens the flyout by
        // dragging from the left edge belongs to Android's DrawerLayout, which MAUI inflates for the
        // flyout, and it stayed armed the whole time. That is why a sidebar kept reappearing long
        // after it was meant to be gone.
        //
        // It used to be off for free. While the root item was a TabBar the Shell worked out that a
        // flyout could not apply and disabled it by itself; building the sections in code, which the
        // tabs need so they stop drawing side by side, makes the root items ShellItems, that shortcut
        // stops applying and the default Flyout came back.
        //
        // It has to be an attribute rather than an assignment in this constructor. Setting the property
        // here, before the platform view exists, made every pushed page initialise and then draw
        // nothing at all: the page view model ran and its tabs were built, but the entry came up black.
        BuildSections();


        // Push routes
        Routing.RegisterRoute("animedetails", typeof(AnimeDetailsPage));
        // A second entry point for the same page, so a genre or studio filter can be PUSHED on top
        // of the details page. Navigating to the "///anime" section instead pops back down to the
        // section root, which is why pressing Back after filtering by genre used to close the app:
        // there was nothing left on the Shell stack to go back to.
        Routing.RegisterRoute("animelist", typeof(AnimeListPage));
        Routing.RegisterRoute("login", typeof(LogInPage));
        Routing.RegisterRoute("search", typeof(SearchPage));
        Routing.RegisterRoute("calendar", typeof(CalendarPage));
        Routing.RegisterRoute("profile", typeof(ProfilePage));

        // All other routes are placeholders for now and will be replaced
        // as the corresponding MAUI pages are built.
        Routing.RegisterRoute("settings", typeof(SettingsPage));
        Routing.RegisterRoute("articles", typeof(ArticlesPage));
        Routing.RegisterRoute("videos", typeof(VideosPage));
        Routing.RegisterRoute("forums", typeof(ForumsIndexPage));
        Routing.RegisterRoute("forumboard", typeof(ForumsBoardPage));
        Routing.RegisterRoute("forumtopic", typeof(ForumsTopicPage));
        Routing.RegisterRoute("forumnewtopic", typeof(ForumNewTopicPage));
        Routing.RegisterRoute("forumreply", typeof(ForumReplyPage));
        Routing.RegisterRoute("history", typeof(HistoryPage));
        Routing.RegisterRoute("feeds", typeof(FeedsPage));
        Routing.RegisterRoute("friends", typeof(FriendsPage));
        Routing.RegisterRoute("wallpapers", typeof(WallpapersPage));
        Routing.RegisterRoute("messaging", typeof(MessagingPage));
        Routing.RegisterRoute("messagedetails", typeof(MessageDetailsPage));
        Routing.RegisterRoute("clubs", typeof(ClubsIndexPage));
        Routing.RegisterRoute("clubdetails", typeof(ClubDetailsPage));
        Routing.RegisterRoute("listcomparison", typeof(ListComparisonPage));
        Routing.RegisterRoute("notifications", typeof(NotificationsHubPage));
        Routing.RegisterRoute("recommendations", typeof(RecommendationsPage));
        Routing.RegisterRoute("character", typeof(CharacterDetailsPage));
        Routing.RegisterRoute("staff", typeof(StaffDetailsPage));
        Routing.RegisterRoute("discoveryresults", typeof(DiscoveryResultsPage));
    Routing.RegisterRoute("seasonthemes", typeof(SeasonThemesPage));
    Routing.RegisterRoute("randomtheme", typeof(RandomThemePage));

        // Signing out from anywhere must land on the login page. The view model that drops the
        // session cannot navigate itself, so it raises the request and this is the only place that
        // can act on it. Registered once here, unsubscribed in OnNavigated when the login page
        // takes over, so it cannot fire twice into a dead page.
        ViewModelLocator.LogIn.LogOutRequested += OnLogOutRequested;
    }

    private async void OnLogOutRequested(object sender, EventArgs e)
    {
        try
        {
            if (Credentials.Authenticated)
                return;

            // Pushed as a route, not a section. The four signed-in sections are ShellItems, so
            // there is no //login section to go to, and this is the same single-page route the
            // startup check and the settings sign-out already use.
            await Shell.Current?.GoToAsync("login");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS sign-out navigation failed: " + ex.Message);
        }
    }

    // The Shell still switches sections through a ViewPager2, so the scroll itself is
    // there but no longer draws two pages side by side. What is left is a brief smear:
    // the pages really do travel for ~125ms (RecyclerView's LinearSmoothScroller floor)
    // over the app background.
    //
    // So hide the movement instead of cancelling it. Cancelling it (forcing
    // SetCurrentItem from onPageSelected) re-enters MAUI's selection sync and sends
    // every tab to the wrong page, so it is not an option.
    //
    // This callback only watches the scroll state and toggles an opaque shield over
    // the pager. It never touches CurrentItem, the adapter or MAUI's navigation.
    private sealed class PagerShield : global::AndroidX.ViewPager2.Widget.ViewPager2.OnPageChangeCallback
    {
        private const int ScrollStateIdle = 0;
        private const int ScrollStateSettling = 2;

        private readonly global::AndroidX.ViewPager2.Widget.ViewPager2 _viewPager;
        private readonly global::Android.Graphics.Color _color;
        private global::Android.Views.View _shield;
        private int _hideGeneration;

        public PagerShield(global::AndroidX.ViewPager2.Widget.ViewPager2 viewPager, global::Android.Graphics.Color color)
        {
            _viewPager = viewPager;
            _color = color;
        }

        public override void OnPageScrollStateChanged(int state)
        {
            if (state == ScrollStateSettling)
                Show();
            else if (state == ScrollStateIdle)
                Hide();
        }

        private void Show()
        {
            try
            {
                // Invalidates any queued hide: a new scroll started.
                _hideGeneration++;

                if (_shield is null)
                {
                    if (_viewPager.Parent is not global::Android.Views.ViewGroup parent)
                        return;

                    _shield = new global::Android.Views.View(_viewPager.Context)
                    {
                        // Never intercept taps: the shield must not swallow input if it
                        // ever fails to be hidden.
                        Clickable = false,
                        Focusable = false,
                        ImportantForAccessibility = global::Android.Views.ImportantForAccessibility.No
                    };
                    _shield.SetBackgroundColor(_color);
                    _shield.LayoutParameters = new global::Android.Views.ViewGroup.LayoutParams(
                        global::Android.Views.ViewGroup.LayoutParams.MatchParent,
                        global::Android.Views.ViewGroup.LayoutParams.MatchParent);
                    parent.AddView(_shield);
                }

                _shield.BringToFront();
                _shield.Visibility = global::Android.Views.ViewStates.Visible;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("MALPLUS", $"PagerShield.Show failed: {ex.Message}");
            }
        }

        private void Hide()
        {
            try
            {
                if (_shield is null)
                    return;

                var generation = ++_hideGeneration;
                // Safety net: if IDLE never arrives the shield must not stay on screen.
                _viewPager.PostDelayed(() =>
                {
                    if (generation != _hideGeneration || _shield is null)
                        return;

                    _shield.Visibility = global::Android.Views.ViewStates.Gone;
                }, 400);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("MALPLUS", $"PagerShield.Hide failed: {ex.Message}");
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        global::AndroidX.ViewPager2.Widget.ViewPager2, PagerShield> Shields = new();

    private bool _sharedBarInstalled;
    private global::Android.Views.View _sharedBarView;
    private string _lastNavigatedSection;
    private readonly Dictionary<string, WeakReference<Views.AnimeListPage>> _rootListPages = new();

    private void PaintPagers()
    {
        var background = ResolveBackground();
        if (background is not global::Android.Graphics.Color color)
            return;

        // Root panels are the only surface the section pager is for: anything above them
        // in the navigation stack is served by the shellitem pager instead.
        var stack = Navigation?.NavigationStack;
        var rootInput = stack == null || stack.Count <= 1;

        if (Handler?.PlatformView is global::Android.Views.View root)
            PaintPagers(root, color, rootInput);
    }

    // The four roots share one section pager, so each page used to carry its own
    // navigation strip and the strip travelled with the finger. One bar is pulled out
    // of a page and hosted under the pager instead: the pager keeps the remaining
    // height, every page bar is collapsed, and the strip never moves horizontally.
    private void InstallSharedBar()
    {
        if (_sharedBarInstalled || Handler?.PlatformView is not global::Android.Views.View root)
            return;

        var pager = FindSectionPager(root);
        if (pager?.Parent is not global::Android.Widget.LinearLayout coordinator)
            return;

        var bar = Controls.BottomNavBar.TakeSharedHostBar();
        if (bar?.Handler?.PlatformView is not global::Android.Views.View barView)
            return;

        var height = barView.Height;
        if (height <= 0)
            return;

        _sharedBarInstalled = true;
        Controls.BottomNavBar.MarkSharedHostInstalled();
        Controls.BottomNavBar.SetSharedBarView(barView);
        Controls.BottomNavBar.HideAllForSharedHost(barView);

        (barView.Parent as global::Android.Views.ViewGroup)?.RemoveView(barView);
        coordinator.AddView(barView,
            new global::Android.Widget.LinearLayout.LayoutParams(
                global::Android.Views.ViewGroup.LayoutParams.MatchParent, height));
        bar.Opacity = 1;
        barView.Visibility = global::Android.Views.ViewStates.Visible;

        pager.LayoutParameters = new global::Android.Widget.LinearLayout.LayoutParams(
            global::Android.Views.ViewGroup.LayoutParams.MatchParent, 0, 1f);

        _sharedBarView = barView;
        UpdateSharedBarVisibility();
        Controls.BottomNavBar.AttachBarWatch(root);

        global::Android.Util.Log.Info("MALPLUS",
            $"shared bar installed h={height} coordinator={coordinator.ChildCount}");
    }

    // Pushed pages own the full height, exactly like when every root page carried its
    // own bar. The strip only belongs to the four root panels.
    private void UpdateSharedBarVisibility()
    {
        UpdateRootPagerInput();

        if (_sharedBarView == null)
            return;

        var page = CurrentPage;
        var stack = Navigation.NavigationStack;
        var root = stack.Count <= 1 &&
            (page is Views.AnimeListPage || page is Views.DiscoverPage || page is Views.MorePage);

        var next = root
            ? global::Android.Views.ViewStates.Visible
            : global::Android.Views.ViewStates.Gone;

        if (_sharedBarView.Visibility != next)
        {
            _sharedBarView.Visibility = next;
            global::Android.Util.Log.Info("MALPLUS",
                $"BARWATCH shared visibility={next} root={root} stack={stack.Count} page={page?.GetType().Name}");
        }

        global::Android.Util.Log.Info("MALPLUS",
            $"shared bar root={root} page={page?.GetType().Name ?? "null"} stack={stack.Count}");

        // Safety net for pages the pager created after the last OnNavigated: a list page always
        // has to know whether it owns the shared view model.
        UpdateRootListModes(_lastNavigatedSection);
    }

    // The section pager is the root swipe surface and nothing else. A pushed page sits above
    // the section inside the shellitem pager, but the section pager stays alive underneath
    // and kept its input, so a sideways drag on Forum, Recommendations or Season OP/ED
    // moved the root section behind the page. The stack depth is the whole test, which is
    // why every pushed page is covered without a list of page names.
    private void UpdateRootPagerInput()
    {
        if (Handler?.PlatformView is not global::Android.Views.View root)
            return;

        var pager = FindSectionPager(root);
        if (pager == null)
            return;

        var stack = Navigation?.NavigationStack;
        var enabled = stack == null || stack.Count <= 1;
        if (pager.UserInputEnabled != enabled)
            pager.UserInputEnabled = enabled;
    }

    private static global::AndroidX.ViewPager2.Widget.ViewPager2 FindSectionPager(
        global::Android.Views.View view)
    {
        if (view is global::AndroidX.ViewPager2.Widget.ViewPager2 pager &&
            pager.Id == SectionPagerId(pager.Context))
            return pager;

        if (view is global::Android.Views.ViewGroup group)
        {
            for (int i = 0; i < group.ChildCount; i++)
            {
                var found = FindSectionPager(group.GetChildAt(i));
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static global::Android.Graphics.Color? ResolveBackground()
    {
        try
        {
            if (Application.Current?.Resources != null &&
                Application.Current.Resources.TryGetValue("EmBackground", out var value))
            {
                if (value is global::Android.Graphics.Color androidColor)
                    return androidColor;

                if (value is Color mauiColor)
                    return new global::Android.Graphics.Color(
                        (int)(mauiColor.Red * 255), (int)(mauiColor.Green * 255), (int)(mauiColor.Blue * 255),
                        (int)(mauiColor.Alpha * 255));
            }
        }
        catch { }

        return null;
    }

    private static int _sectionPagerId = -1;
    private static int _shellItemPagerId = -1;
    private static int _topTabsId = -1;

    private static int SectionPagerId(global::Android.Content.Context context)
    {
        if (_sectionPagerId <= 0)
        {
            _sectionPagerId = context.Resources?.GetIdentifier(
                "shellsection_viewpager", "id", context.PackageName) ?? 0;
        }

        return _sectionPagerId;
    }

    private static int ShellItemPagerId(global::Android.Content.Context context)
    {
        if (_shellItemPagerId <= 0)
        {
            _shellItemPagerId = context.Resources?.GetIdentifier(
                "shellitem_viewpager", "id", context.PackageName) ?? 0;
        }

        return _shellItemPagerId;
    }

    private static int TopTabsId(global::Android.Content.Context context)
    {
        if (_topTabsId <= 0)
        {
            _topTabsId = context.Resources?.GetIdentifier(
                "navigationlayout_toptabs", "id", context.PackageName) ?? 0;
        }

        return _topTabsId;
    }

    private static void PaintPagers(global::Android.Views.View view, global::Android.Graphics.Color color,
        bool rootInput)
    {
        // The platform overscroll stretch draws a band at the edge of the scrollable
        // right above the shared strip while a fling is settling, which reads as a
        // second bar appearing and vanishing. Edge effects add nothing here.
        if (view is global::AndroidX.RecyclerView.Widget.RecyclerView list)
            list.OverScrollMode = global::Android.Views.OverScrollMode.Never;
        else if (view is global::AndroidX.Core.Widget.NestedScrollView scroll)
            scroll.OverScrollMode = global::Android.Views.OverScrollMode.Never;

        if (view is global::AndroidX.ViewPager2.Widget.ViewPager2 viewPager)
        {
            viewPager.SetBackgroundColor(color);
            viewPager.OverScrollMode = global::Android.Views.OverScrollMode.Never;

            // Phase B: the four roots live in one ShellSection, so their pager is
            // shellsection_viewpager. MAUI only enables its input when the section
            // shows more than one item; this keeps it enabled for the roots without
            // touching the shellitem pager that hosts pushed pages. It is disabled
            // for every pushed page: the section pager stays alive underneath them
            // and was following sideways drags that are none of its business.
            if (viewPager.Id == SectionPagerId(viewPager.Context))
            {
                viewPager.UserInputEnabled = rootInput;
            }

            for (int i = 0; i < viewPager.ChildCount; i++)
            {
                if (viewPager.GetChildAt(i) is not global::AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
                    continue;

                recyclerView.SetBackgroundColor(color);
                recyclerView.SetItemAnimator(null);
            }

            // Only the shellitem pager gets a shield: it hosts pushed pages, while the
            // section pager is the swipe surface itself and must stay uncovered.
            if (viewPager.Id == ShellItemPagerId(viewPager.Context) &&
                !Shields.TryGetValue(viewPager, out _))
            {
                var shield = new PagerShield(viewPager, color);
                Shields.Add(viewPager, shield);
                viewPager.RegisterOnPageChangeCallback(shield);
            }
        }

        if (view is global::Android.Views.ViewGroup group)
        {
            if (view.Id == TopTabsId(view.Context))
                HideTopTabs(view);

            for (int i = 0; i < group.ChildCount; i++)
                PaintPagers(group.GetChildAt(i), color, rootInput);
        }
    }

    private static global::Android.Views.ViewGroup _topTabsGuardParent;

    // Shell re-applies the top tabs on every section change, which is exactly when a
    // root swipe settles. Hiding them once is not enough: the tab strip came back for
    // a beat on top of the page. The guard collapses it before every frame, so the
    // strip cannot draw even if Shell marks it visible again.
    private static void HideTopTabs(global::Android.Views.View view)
    {
        if (view.Visibility != global::Android.Views.ViewStates.Gone)
            view.Visibility = global::Android.Views.ViewStates.Gone;

        var parameters = view.LayoutParameters;
        if (parameters != null && parameters.Height != 0)
        {
            parameters.Height = 0;
            view.LayoutParameters = parameters;
        }

        if (view.Parent is global::Android.Views.ViewGroup parent &&
            !ReferenceEquals(parent, _topTabsGuardParent))
        {
            parent.ViewTreeObserver?.AddOnPreDrawListener(new TopTabsGuard(parent));
            _topTabsGuardParent = parent;
        }
    }

    private sealed class TopTabsGuard : global::Java.Lang.Object,
        global::Android.Views.ViewTreeObserver.IOnPreDrawListener
    {
        private readonly global::Android.Views.ViewGroup _parent;

        public TopTabsGuard(global::Android.Views.ViewGroup parent)
            => _parent = parent;

        public bool OnPreDraw()
        {
            for (int i = 0; i < _parent.ChildCount; i++)
            {
                var child = _parent.GetChildAt(i);
                if (child == null || child.Id != TopTabsId(child.Context))
                    continue;

                if (child.Visibility != global::Android.Views.ViewStates.Gone)
                {
                    child.Visibility = global::Android.Views.ViewStates.Gone;
                    global::Android.Util.Log.Info("MALPLUS", "BARWATCH toptabs forced gone");
                }

                var parameters = child.LayoutParameters;
                if (parameters != null && parameters.Height != 0)
                {
                    parameters.Height = 0;
                    child.LayoutParameters = parameters;
                }
            }

            return true;
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null || !OperatingSystem.IsAndroid())
            return;

        _sharedBarInstalled = false;
        _sharedBarView = null;
        _topTabsGuardParent = null;
        Controls.BottomNavBar.ResetSharedHost();

        PaintPagers();
        Dispatcher.Dispatch(PaintPagers);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), PaintPagers);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), PaintPagers);
        ScheduleSharedBar();
    }

    private void ScheduleSharedBar()
    {
        Dispatcher.Dispatch(InstallSharedBar);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), InstallSharedBar);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), InstallSharedBar);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2400), InstallSharedBar);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(3600), () =>
        {
            if (_sharedBarInstalled)
                return;

            if (CurrentPage is Views.AnimeListPage || CurrentPage is Views.DiscoverPage ||
                CurrentPage is Views.MorePage)
                Controls.BottomNavBar.ShowAllForFallback();
        });
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        PaintPagers();
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);

        // Last line of defence: the whole app is behind a sign-in, so any navigation that lands
        // anywhere else without a session is bounced back to the login page. The checks at
        // startup and on the login page already do this, but they only cover the paths they know
        // about: a deep link, a launcher shortcut or a restored navigation stack all arrive
        // straight through OnNavigated. Without this the app could be used signed out.
        if (!Credentials.Authenticated &&
            (args?.Current?.Location?.OriginalString?.Contains("login") ?? false) != true)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await GoToAsync("login");
                }
                catch (Exception)
                {
                }
            });
            return;
        }

        // ShellItems are created lazily as they are first shown, so their pagers only
        // appear after a navigation.
        PaintPagers();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), PaintPagers);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), PaintPagers);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), PaintPagers);
        ScheduleSharedBar();
        UpdateSharedBarVisibility();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), UpdateSharedBarVisibility);

        // A root page that is already alive does not receive the query of a new root
        // route: Shell switches to its section and drops the query, so tapping Anime
        // from More reopened the panel that had been borrowed by Seasonal/Top. Deliver
        // it to the live list page after the switch instead.
        string section = null;
        try
        {
            var location = args?.Current?.Location?.OriginalString;
            section = SectionOf(location);
            if (!string.IsNullOrEmpty(location) && CurrentPage is Views.AnimeListPage listPage)
            {
                if (location.Contains('?'))
                    listPage.ApplyRouteFromShell(location);
                else if (!string.Equals(section, _lastNavigatedSection, StringComparison.OrdinalIgnoreCase))
                    listPage.ApplyRootLocation(location);
            }

            _lastNavigatedSection = section;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS root route apply failed: " + ex.GetType().Name);
        }

        // After the route is applied, not before: the incoming page must own the shared view model
        // (so the query/mode it just applied is what its grid paints).
        UpdateRootListModes(section);
    }

    // Only the list root that the current location names may paint the shared view model. The other
    // one keeps its own mode's library, otherwise a drag towards it shows the tab it is leaving.
    private void UpdateRootListModes(string section)
    {
        if (string.IsNullOrEmpty(section))
            return;

        foreach (var (route, reference) in _rootListPages)
        {
            if (reference.TryGetTarget(out var page))
                page.SetLiveLibrary(string.Equals(route, section, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string SectionOf(string location)
    {
        if (string.IsNullOrEmpty(location))
            return null;

        var path = location.Split(new[] { '?' }, 2)[0];
        foreach (var segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment.ToLowerInvariant())
            {
                case "discover":
                case "anime":
                case "manga":
                case "more":
                    return segment.ToLowerInvariant();
            }
        }

        return null;
    }

    private void BuildSections()
    {
        // Phase B spike: one ShellItem -> one ShellSection -> four ShellContents.
        // ShellItem is the level that owns shellitem_viewpager; ShellSection owns
        // shellsection_viewpager. Keeping the four roots in one section is the only
        // arrangement where MAUI exposes them through a single root pager that can
        // follow the finger, instead of one pager per ShellItem.
        var section = new ShellSection();
        // The pager order IS the swipe order, and it has to match the visible bar
        // (Discover | Anime | Manga | More). Keeping Anime first made a right swipe
        // from More land on Discover instead of Manga.
        var contents = new List<ShellContent>();
        foreach (var (route, title, pageType) in new (string, string, Type)[]
        {
            ("discover", "Discover", typeof(Views.DiscoverPage)),
            ("anime", "Anime", typeof(Views.AnimeListPage)),
            ("manga", "Manga", typeof(Views.AnimeListPage)),
            ("more", "More", typeof(Views.MorePage))
        })
        {
            var content = Section(route, title, pageType, page =>
            {
                if (page is Views.AnimeListPage listPage)
                    _rootListPages[route] = new WeakReference<Views.AnimeListPage>(listPage);
            });
            contents.Add(content);
            section.Items.Add(content);
        }

        // Anime is the default start tab (Settings.DefaultMenuTab is applied later, in
        // App.OnStart). Select it before the pager exists so the first frame is the list
        // and not Discover while InitApp is still running.
        var currentItemProperty = typeof(ShellSection).GetProperty("CurrentItem",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        if (currentItemProperty != null && currentItemProperty.CanWrite)
            currentItemProperty.SetValue(section, contents[1]);

        var item = new ShellItem();
        item.Items.Add(section);
        Items.Add(item);
    }

    // No icons: Shell.TabBarIsVisible is false and every tab glyph comes from the
    // app's own BottomNavBar.
    private static ShellContent Section(string route, string title, Type pageType,
        Action<Page> onCreated = null)
    {
        var content = new ShellContent
        {
            Title = title,
            Route = route
        };

        var templateProperty = typeof(ShellContent).GetProperty("ContentTemplate",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (templateProperty != null && templateProperty.CanWrite)
        {
            templateProperty.SetValue(content, new DataTemplate(() =>
            {
                var page = (Page)Activator.CreateInstance(pageType);
                if (page is Views.AnimeListPage listPage)
                {
                    listPage.RootRoute = route;
                    // Both list roots share one view model, so the page has to start painting its
                    // own mode's rows. Whoever is the live root later swaps it back in the route.
                    listPage.SetLiveLibrary(false);
                }

                onCreated?.Invoke(page);
                return page;
            }));
        }

        var contentProperty = typeof(ShellContent).GetProperty("Content",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (contentProperty != null && contentProperty.CanWrite)
        {
            var page = (Page)Activator.CreateInstance(pageType);
            if (page is Views.AnimeListPage listPage)
            {
                listPage.RootRoute = route;
                listPage.SetLiveLibrary(false);
            }

            onCreated?.Invoke(page);
            contentProperty.SetValue(content, page);
            return content;
        }

        throw new InvalidOperationException("ShellContent exposes neither ContentTemplate nor Content");
    }

}