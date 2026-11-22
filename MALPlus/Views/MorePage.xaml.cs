using MALPlus.Models.Enums;
using MALPlus.Controls;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Comm.Profile;

using System.Collections.Generic;
using System.Linq;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace MALPlus.Views;

public partial class MorePage : ContentPage
{
    private bool _initialized;
    private Color _lastAppliedThemeBg;

    private bool _animeListPanelExpanded;
    private bool _mangaListPanelExpanded;
    private bool _topAnimePanelExpanded;
    private bool _topMangaPanelExpanded;
    private bool _adaptedPanelExpanded;

    public MorePage()
    {
        InitializeComponent();
    }

    internal void Preload()
    {
        if (_initialized)
            return;
        _initialized = true;
        _appliedSettingsRevision = SettingsViewModelBase.SettingsRevision;
        _appliedContentTag = ContentFilter.CacheTag;
        LoadProfileDataAsync();
        RestorePanelState();
    }

    private string _appliedContentTag;
    private long _appliedSettingsRevision;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyTheme();
        if (_initialized)
        {
            if (_appliedContentTag != ContentFilter.CacheTag)
            {
                _appliedContentTag = ContentFilter.CacheTag;
                if (_topMangaPanelExpanded)
                    PopulateTopMangaPanel();
            }
            if (_appliedSettingsRevision != SettingsViewModelBase.SettingsRevision)
            {
                _appliedSettingsRevision = SettingsViewModelBase.SettingsRevision;
                if (SettingsViewModelBase.ResetDataOnNextAppearance)
                {
                    LoadProfileDataAsync();
                }
            }
            RestorePanelState();
            return;
        }
        _initialized = true;
        _appliedSettingsRevision = SettingsViewModelBase.SettingsRevision;
        _appliedContentTag = ContentFilter.CacheTag;

        Dispatcher.Dispatch(() => LoadProfileDataAsync());

        // Restore panel state from saved UI state (lazy populates any expanded panel)
        RestorePanelState();
    }

    private async void LoadProfileDataAsync()
    {
        try
        {
            if (Credentials.Authenticated)
            {
                ProfileUsername.Text = Credentials.UserName;
                ProfileCompleted.IsVisible = false;

                // The profile HTML is the only reliable source: an account with no
                // picture has no <img> in user-image, and the CDN id URL 404s.
                SetProfileImage(null);

                // Load profile data from cache
                try
                {
                    var data = await DataCache.RetrieveProfileData(Credentials.UserName);
                    if (data?.User?.ImgUrl != null)
                    {
                        SetProfileImage(data.User.ImgUrl);
                        if (data.AnimeCompleted > 0 || data.MangaCompleted > 0)
                        {
                            ProfileCompleted.Text = $"{data.AnimeCompleted} Completed";
                            ProfileCompleted.IsVisible = true;
                        }
                    }
                    else
                    {
                        RefreshProfileCacheInBackground();
                    }
                }
                catch
                {
                    RefreshProfileCacheInBackground();
                }
            }
            else
            {
                ProfileUsername.Text = "Log in";
                ProfileCompleted.IsVisible = false;
                SetProfileImage(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MALPLUS MorePage LoadProfileDataAsync error: {ex.Message}");
        }
    }

    private void SetProfileImage(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            ProfileImage.Source = new UriImageSource
            {
                Uri = new Uri(url),
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromMinutes(10)
            };
            ProfileImage.IsVisible = true;
            ProfileFallbackIcon.IsVisible = false;
        }
        else
        {
            ProfileImage.Source = null;
            ProfileImage.IsVisible = false;
            ProfileFallbackIcon.IsVisible = true;
        }
    }

    private async void RefreshProfileCacheInBackground()
    {
        try
        {
            var fresh = await new ProfileQuery(Credentials.UserName).GetProfileData(false);
            if (!string.IsNullOrEmpty(fresh?.User?.ImgUrl))
                SetProfileImage(fresh.User.ImgUrl);
            if (fresh != null && (fresh.AnimeCompleted > 0 || fresh.MangaCompleted > 0))
            {
                ProfileCompleted.Text = $"{fresh.AnimeCompleted} Completed";
                ProfileCompleted.IsVisible = true;
            }
        }
        catch (Exception) { }
    }

    #region Navigation Handlers

    private void OnAnimeListTapped(object sender, TappedEventArgs e)
    {
        EnsureAnimeListPanel();
        TogglePanel(AnimeListPanel, AnimeListArrow, AnimeListDivider, v => _animeListPanelExpanded = v, _animeListPanelExpanded);
    }

    private void OnMangaListTapped(object sender, TappedEventArgs e)
    {
        EnsureMangaListPanel();
        TogglePanel(MangaListPanel, MangaListArrow, MangaListDivider, v => _mangaListPanelExpanded = v, _mangaListPanelExpanded);
    }

    private void OnTopAnimeTapped(object sender, TappedEventArgs e)
    {
        EnsureTopAnimePanel();
        TogglePanel(TopAnimePanel, TopAnimeArrow, TopAnimeDivider, v => _topAnimePanelExpanded = v, _topAnimePanelExpanded);
    }

    private void OnTopMangaTapped(object sender, TappedEventArgs e)
    {
        EnsureTopMangaPanel();
        TogglePanel(TopMangaPanel, TopMangaArrow, TopMangaDivider, v => _topMangaPanelExpanded = v, _topMangaPanelExpanded);
    }

    private void OnAdaptedTapped(object sender, TappedEventArgs e)
    {
        EnsureAdaptedPanel();
        TogglePanel(AdaptedPanel, AdaptedArrow, null, v => _adaptedPanelExpanded = v, _adaptedPanelExpanded);
    }

    private void OnSeasonalTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageSeasonal, AnimeListPageNavigationArgs.Seasonal);
    }

    private void OnSearchTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageSearch, new SearchPageNavigationArgs());
    }

    private void OnRecommendationsTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageRecomendations, null);
    }

    private async void OnGlobalCalendarTapped(object sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync("calendar?mode=global"); } catch { }
    }

    private async void OnMyScheduleTapped(object sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync("calendar?mode=mine"); } catch { }
    }

    private void OnCalendarTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageCalendar, null);
    }

    private void OnArticlesTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageArticles, new MalArticlesPageNavigationArgs { WorkMode = ArticlePageWorkMode.Articles, Source = PageIndex.PageMore });
    }

    private void OnVideosTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PagePopularVideos, null);
    }

    private void OnForumsTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageForumIndex, null);
    }

    private void OnClubsTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageClubIndex, null);
    }

    private void OnMessagingTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageMessanging, null);
    }

    private void OnNotificationsTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageNotificationHub, null);
    }

    private void OnFriendsTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageFriends, null);
    }

    private void OnFeedsTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageFeeds, null);
    }

    private void OnHistoryTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageHistory, null);
    }

    private void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        NavigateTo(PageIndex.PageSettings, null);
    }

    private void OnProfileHeaderTapped(object sender, TappedEventArgs e)
    {
        if (!Credentials.Authenticated) { NavigateToLogin(); return; }
        NavigateTo(PageIndex.PageProfile, new ProfilePageNavigationArgs { TargetUser = Credentials.UserName });
    }

    private void OnLogOutTapped(object sender, TappedEventArgs e)
    {
        try
        {
            // The shared command, not a private Credentials.Reset(). This page used to reset the
            // credentials and navigate on its own, which left the library in memory, the list cache
            // and the http context populated: a sign-out from here was a half sign-out. The command
            // is also what raises LogOutRequested, and AppShell is what navigates, so signing out
            // from here lands exactly like signing out from Settings.
            ViewModelLocator.LogIn.LogOutCommand.Execute(null);
        }
        catch (Exception ex) { Console.WriteLine("MALPLUS logout failed: " + ex); }
    }

    private void NavigateToLogin()
    {
        _ = NavigateToLoginAsync();
    }

    // "login" is a global route, so it must be addressed relatively. Absolute
    // routing ("//login") throws inside ShellNavigationManager and the faulted
    // task was being discarded, which left logout and every auth-gated row as
    // a silent no-op.
    private static async Task NavigateToLoginAsync()
    {
        var shell = Shell.Current;
        if (shell == null)
            return;
        try
        {
            await shell.GoToAsync("login");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS login navigation failed: " + ex);
        }
    }

    #endregion

    #region Panel Animation

    private void TogglePanel(Grid panel, Image arrow, BoxView divider, System.Action<bool> setExpanded, bool currentExpanded)
    {
        if (currentExpanded)
        {
            AnimateCollapse(panel, arrow, divider);
            setExpanded(false);
        }
        else
        {
            AnimateExpand(panel, arrow, divider);
            setExpanded(true);
        }
    }

    private static void ApplyPanelState(Grid panel, Image arrow, BoxView divider, bool expanded)
    {
        panel.IsVisible = expanded;
        panel.Opacity = expanded ? 1 : 0;
        panel.TranslationY = 0;
        arrow.Rotation = expanded ? 180 : 0;
        if (divider != null)
            divider.IsVisible = expanded;
    }

    private void AnimateExpand(Grid panel, Image arrow, BoxView divider)
    {
        panel.IsVisible = true;
        if (divider != null)
            divider.IsVisible = true;
        panel.Opacity = 0;
        panel.TranslationY = -20;
        
        var animation = new Animation(
            d => panel.Opacity = d, 0, 1, Easing.CubicOut);
        animation.Commit(panel, "Expand", 16, 180, Easing.CubicOut);

        var translateAnim = new Animation(
            d => panel.TranslationY = d, -20, 0, Easing.CubicOut);
        translateAnim.Commit(panel, "TranslateExpand", 16, 180, Easing.CubicOut);

        var rotateAnim = new Animation(d => arrow.Rotation = d, 0, 180, Easing.CubicOut);
        rotateAnim.Commit(arrow, "RotateExpand", 16, 180, Easing.CubicOut);
    }

    private void AnimateCollapse(Grid panel, Image arrow, BoxView divider)
    {
        var animation = new Animation(
            d => panel.Opacity = d, 1, 0, Easing.CubicIn);
        animation.Commit(panel, "Collapse", 16, 160, Easing.CubicIn, finished: (d, b) =>
        {
            panel.IsVisible = false;
            if (divider != null)
                divider.IsVisible = false;
        });

        var translateAnim = new Animation(
            d => panel.TranslationY = d, 0, -20, Easing.CubicIn);
        translateAnim.Commit(panel, "TranslateCollapse", 16, 160, Easing.CubicIn);

        var rotateAnim = new Animation(d => arrow.Rotation = d, 180, 0, Easing.CubicIn);
        rotateAnim.Commit(arrow, "RotateCollapse", 16, 160, Easing.CubicIn);
    }

    #endregion

    #region Panel Population

    private void PopulateStatusPanel(Grid panel, bool manga)
    {
        var statusValues = new[] { AnimeStatus.Watching, AnimeStatus.Completed, AnimeStatus.OnHold, AnimeStatus.Dropped, AnimeStatus.PlanToWatch };
        var workMode = manga ? AnimeListWorkModes.Manga : AnimeListWorkModes.Anime;
        var items = new List<(string Label, Action OnClick)>
        {
            ("All", () => NavigateTo(PageIndex.PageAnimeList, new AnimeListPageNavigationArgs(5, workMode)))
        };

        for (int i = 0; i < statusValues.Length; i++)
        {
            var status = statusValues[i];
            var index = i;
            var label = MALPlus.XShared.Utils.Utilities.StatusToString((int)status, manga);
            items.Add((label, (Action)(() => NavigateTo(PageIndex.PageAnimeList, new AnimeListPageNavigationArgs(index, workMode)))));
        }

        PopulateTypePanel(panel, items);
    }

    private void ApplyTheme()
    {
        try
        {
            var bg = MALPlus.Services.ThemeService.CurrentBackground;
            if (_lastAppliedThemeBg == bg)
                return;
            _lastAppliedThemeBg = bg;
            BackgroundColor = bg;
        }
        catch { }
    }

    private void EnsureAnimeListPanel()
    {
        if (AnimeListPanel.Children.Count == 0)
            PopulateStatusPanel(AnimeListPanel, false);
    }

    private void EnsureMangaListPanel()
    {
        if (MangaListPanel.Children.Count == 0)
            PopulateStatusPanel(MangaListPanel, true);
    }

    private void EnsureTopAnimePanel()
    {
        if (TopAnimePanel.Children.Count == 0)
        {
            PopulateTypePanel(TopAnimePanel,
                Enum.GetValues(typeof(TopAnimeType)).Cast<TopAnimeType>().Select(t => (PrettyTopAnime(t), (Action)(() => NavigateTo(PageIndex.PageTopAnime, AnimeListPageNavigationArgs.TopAnime(t))))).ToList());
        }
    }

    private void EnsureTopMangaPanel()
    {
        if (TopMangaPanel.Children.Count == 0)
        {
            PopulateTopMangaPanel();
        }
    }

    private void PopulateTopMangaPanel()
    {
        PopulateTypePanel(TopMangaPanel,
            Enum.GetValues(typeof(MangaTopType)).Cast<MangaTopType>()
                .Where(ContentFilter.AllowsMangaSubcategory)
                .Select(t => (PrettyTopManga(t), (Action)(() => NavigateTo(PageIndex.PageTopManga, AnimeListPageNavigationArgs.TopMangaCategory(t))))).ToList());
    }

    private void EnsureAdaptedPanel()
    {
        if (AdaptedPanel.Children.Count == 0)
        {
            PopulateTypePanel(AdaptedPanel,
                Enum.GetValues(typeof(MangaAdaptedType)).Cast<MangaAdaptedType>().Select(t => (AnimeAdaptedToAnimeQuery.ToDisplayName(t), (Action)(() => NavigateTo(PageIndex.PageMangaAdapted, AnimeListPageNavigationArgs.MangaAdapted(t))))).ToList());
        }
    }

    private void PopulateTypePanel(Grid panel, List<(string Label, Action OnClick)> items)
    {
        panel.RowDefinitions.Clear();
        panel.ColumnDefinitions.Clear();
        panel.Children.Clear();

        var sunkenBg = MALPlus.Services.ThemeService.CurrentSunkenBg;
        var sunkenStroke = MALPlus.Services.ThemeService.CurrentSunkenBorder;

        // Inset sub-panel: a sunken container with rounded corners and subtle border,
        // clearly nesting the submenu under its parent row.
        var container = new Border
        {
            BackgroundColor = sunkenBg,
            Stroke = sunkenStroke,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Margin = new Thickness(14, 6, 14, 8),
            Padding = new Thickness(0)
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 0
        };

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            var row = new Grid
            {
                HeightRequest = 42,
                BackgroundColor = sunkenBg,
                Padding = new Thickness(16, 0, 14, 0),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            row.Add(new Label
            {
                Text = item.Label,
                FontFamily = "Inter",
                FontSize = 14.5,
                TextColor = Color.FromArgb("#DCE7F2"),
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            row.Add(new Image
            {
                Source = "icon_chevron_right",
                WidthRequest = 14,
                HeightRequest = 14,
                Opacity = 0.35,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End,
                InputTransparent = true
            }, 1, 0);

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => item.OnClick();
            row.GestureRecognizers.Add(tapGesture);

            stack.Add(row);

            if (i < items.Count - 1)
            {
                stack.Add(new BoxView
                {
                    HeightRequest = 1,
                    Color = Color.FromArgb("#0DFFFFFF"),
                    Margin = new Thickness(16, 0, 14, 0)
                });
            }
        }

        container.Content = stack;
        panel.Children.Add(container);
    }

    private static string PrettyTopAnime(TopAnimeType type) => type switch
    {
        TopAnimeType.General => "All time",
        TopAnimeType.Airing => "Airing",
        TopAnimeType.Upcoming => "Upcoming",
        TopAnimeType.Tv => "TV",
        TopAnimeType.Movies => "Movies",
        TopAnimeType.Ovas => "OVAs",
        TopAnimeType.Popular => "Popular",
        TopAnimeType.Favourited => "Favourited",
        _ => type.ToString()
    };

    private static string PrettyTopManga(MangaTopType type) => type switch
    {
        MangaTopType.All => "All time",
        MangaTopType.Manga => "Manga",
        MangaTopType.Novels => "Novels",
        MangaTopType.LightNovels => "Light Novels",
        MangaTopType.OneShots => "One-shots",
        MangaTopType.Doujinshi => "Doujinshi",
        MangaTopType.Manhwa => "Manhwa",
        MangaTopType.Manhua => "Manhua",
        MangaTopType.Popular => "Popular",
        MangaTopType.Favourited => "Favourited",
        _ => type.ToString()
    };

    #endregion

    #region Navigation

    private void NavigateTo(PageIndex page, object args = null)
    {
        if (args is AnimeListPageNavigationArgs animeArgs)
        {
            animeArgs.FromMore = true;
            animeArgs.ResetBackNav = false;
        }
        ViewModelLocator.GeneralMain.Navigate(page, args);

        // Only arm BackRouter for root panels that borrow the anime/manga tabs without pushing
        // onto the navigation stack. Pushed pages (Profile, Settings, Forums, etc.) naturally pop
        // through Shell's back dispatcher and must NOT trigger BackRouter.
        if (page == PageIndex.PageSeasonal || page == PageIndex.PageTopAnime ||
            page == PageIndex.PageTopManga || page == PageIndex.PageMangaAdapted)
        {
            ViewModelLocator.NavMgr.DeregisterBackNav();
            MALPlus.Services.Navigation.BackRouter.Register(PageIndex.PageMore);
#if ANDROID
            global::MALPlus.MainActivity.ArmBackNavCallback();
#endif
        }
    }

    #endregion

    #region State Persistence

    private void RestorePanelState()
    {
        try
        {
            var ui = MALPlus.XShared.ViewModels.Main.FragmentUiState.More;
            if (ui == null)
                return;

            if (ui.TryGetValue("AnimeList", out var a) && a is bool ab && ab)
            {
                _animeListPanelExpanded = true;
                EnsureAnimeListPanel();
            }
            if (ui.TryGetValue("MangaList", out var ml) && ml is bool mlb && mlb)
            {
                _mangaListPanelExpanded = true;
                EnsureMangaListPanel();
            }
            if (ui.TryGetValue("TopAnime", out var ta) && ta is bool tab && tab)
            {
                _topAnimePanelExpanded = true;
                EnsureTopAnimePanel();
            }
            if (ui.TryGetValue("TopManga", out var tm) && tm is bool tmb && tmb)
            {
                _topMangaPanelExpanded = true;
                EnsureTopMangaPanel();
            }
            if (ui.TryGetValue("Adapted", out var ad) && ad is bool adb && adb)
            {
                _adaptedPanelExpanded = true;
                EnsureAdaptedPanel();
            }

            ApplyPanelState(AnimeListPanel, AnimeListArrow, AnimeListDivider, _animeListPanelExpanded);
            ApplyPanelState(MangaListPanel, MangaListArrow, MangaListDivider, _mangaListPanelExpanded);
            ApplyPanelState(TopAnimePanel, TopAnimeArrow, TopAnimeDivider, _topAnimePanelExpanded);
            ApplyPanelState(TopMangaPanel, TopMangaArrow, TopMangaDivider, _topMangaPanelExpanded);
            ApplyPanelState(AdaptedPanel, AdaptedArrow, null, _adaptedPanelExpanded);
        }
        catch { }
    }

    protected override void OnDisappearing()
    {
        // A back destination is only meaningful while the page it was registered for is still on
        // screen. Back at More, there is nowhere to go but the Shell's own handling, so the
        // registration is dropped and the armed callback disarms.
        MALPlus.Services.Navigation.BackRouter.Clear();
#if ANDROID
        global::MALPlus.MainActivity.DisarmBackNavCallback();
#endif
        base.OnDisappearing();
        SavePanelState();
    }

    private void SavePanelState()
    {
        try
        {
            var ui = MALPlus.XShared.ViewModels.Main.FragmentUiState.More;
            if (ui != null)
            {
                ui["AnimeList"] = _animeListPanelExpanded;
                ui["MangaList"] = _mangaListPanelExpanded;
                ui["TopAnime"] = _topAnimePanelExpanded;
                ui["TopManga"] = _topMangaPanelExpanded;
                ui["Adapted"] = _adaptedPanelExpanded;
            }
        }
        catch { }
    }

    #endregion
}