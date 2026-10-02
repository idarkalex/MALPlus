using MALPlus.Views;

namespace MALPlus;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        BuildSections();

        // Push routes
        Routing.RegisterRoute("animedetails", typeof(AnimeDetailsPage));
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

    private void PaintPagers()
    {
        var background = ResolveBackground();
        if (background is not global::Android.Graphics.Color color)
            return;

        if (Handler?.PlatformView is global::Android.Views.View root)
            PaintPagers(root, color);
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

    private static void PaintPagers(global::Android.Views.View view, global::Android.Graphics.Color color)
    {
        if (view is global::AndroidX.ViewPager2.Widget.ViewPager2 viewPager)
        {
            viewPager.SetBackgroundColor(color);

            for (int i = 0; i < viewPager.ChildCount; i++)
            {
                if (viewPager.GetChildAt(i) is not global::AndroidX.RecyclerView.Widget.RecyclerView recyclerView)
                    continue;

                recyclerView.SetBackgroundColor(color);
                recyclerView.SetItemAnimator(null);
            }

            // Only the root paging views get a shield. The per-section pager holds a
            // single page and is used for pushed pages, where a flash would be wrong.
            var name = viewPager.Resources?.GetResourceEntryName(viewPager.Id) ?? string.Empty;
            if (name.StartsWith("shellitem", StringComparison.OrdinalIgnoreCase) &&
                !Shields.TryGetValue(viewPager, out _))
            {
                var shield = new PagerShield(viewPager, color);
                Shields.Add(viewPager, shield);
                viewPager.RegisterOnPageChangeCallback(shield);
            }
        }

        if (view is global::Android.Views.ViewGroup group)
        {
            for (int i = 0; i < group.ChildCount; i++)
                PaintPagers(group.GetChildAt(i), color);
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null || !OperatingSystem.IsAndroid())
            return;

        PaintPagers();
        Dispatcher.Dispatch(PaintPagers);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);

        // ShellItems are created lazily as they are first shown, so their pagers only
        // appear after a navigation.
        PaintPagers();
    }

    private void BuildSections()
    {
        foreach (var (route, title, pageType) in new (string, string, Type)[]
        {
            ("anime", "Anime", typeof(Views.AnimeListPage)),
            ("manga", "Manga", typeof(Views.AnimeListPage)),
            ("discover", "Discover", typeof(Views.DiscoverPage)),
            ("more", "More", typeof(Views.MorePage))
        })
        {
            var content = Section(route, title, pageType);

            // One ShellItem per root section, exactly what "<TabBar> with four
            // ShellContents" produced. Kept as-is: every Shell arrangement with four
            // paging elements animates the switch in MAUI 11 RC1, so this is not the
            // place to fight that bug.
            var section = new ShellSection();
            section.Items.Add(content);

            var item = new ShellItem();
            item.Items.Add(section);

            Items.Add(item);
        }
    }

    // No icons: Shell.TabBarIsVisible is false and every tab glyph comes from the
    // app's own BottomNavBar.
    private static ShellContent Section(string route, string title, Type pageType)
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
            templateProperty.SetValue(content, new DataTemplate(pageType));
            return content;
        }

        var contentProperty = typeof(ShellContent).GetProperty("Content",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (contentProperty != null && contentProperty.CanWrite)
        {
            contentProperty.SetValue(content, (Page)Activator.CreateInstance(pageType));
            return content;
        }

        throw new InvalidOperationException("ShellContent exposes neither ContentTemplate nor Content");
    }
}