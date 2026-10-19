using System.Windows.Input;
using MALPlus.Models.Enums;
using MALPlus.Models.Models;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Delegates;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Services;

public static class MauiDetailsNavigationHandoff
{
    private static readonly object Sync = new();
    private static readonly Dictionary<(int Id, bool AnimeMode), AnimeDetailsPageNavigationArgs> Pending = new();

        public static void Set(AnimeDetailsPageNavigationArgs args)
        {
            if (args == null)
                return;
            lock (Sync)
                Pending[(args.Id, args.AnimeMode)] = args;
        }


    public static AnimeDetailsPageNavigationArgs Take(int id, bool animeMode)
    {
        lock (Sync)
        {
            if (!Pending.TryGetValue((id, animeMode), out var args))
                return null;
            Pending.Remove((id, animeMode));
            return args;
        }
    }
}

public class MauiMainViewModel : MainViewModelBase
{
    protected override void CurrentStatusStoryboardBegin()
    {
    }

    protected override void CurrentOffSubStatusStoryboardBegin()
    {
    }

    protected override void CurrentOffStatusStoryboardBegin()
    {
    }

    public override void Navigate(PageIndex index, object args = null)
    {
        string route = null;
        string query = null;

        switch (index)
        {
            // Tab roots
            case PageIndex.PageAnimeList:
            case PageIndex.PageSeasonal:
            case PageIndex.PageTopAnime:
            case PageIndex.PageMangaList:
            case PageIndex.PageTopManga:
            case PageIndex.PageMangaAdapted:
                route = IsMangaWorkMode(index, args) ? "///manga" : "///anime";
                query = BuildAnimeListQuery(index, args);
                break;
            case PageIndex.PageDiscover:
                route = "///discover";
                break;
            case PageIndex.PageMore:
                // The "more" tab now hosts the real MorePage hub directly.
                route = "///more";
                break;

            // Off-page (push) routes
            case PageIndex.PageAnimeDetails:
                if (args is AnimeDetailsPageNavigationArgs ad)
                {
                    MauiDetailsNavigationHandoff.Set(ad);
                    query = $"?id={ad.Id}&title={Uri.EscapeDataString(ad.Title ?? "")}";
                    if (!ad.AnimeMode)
                        query += "&manga=true";
                    if (ad.SourceTabIndex > 0)
                        query += $"&tab={ad.SourceTabIndex}";
                }
                else if (args is int idd)
                    query = $"?id={idd}";
                route = "animedetails";
                break;
            case PageIndex.PageSearch:
            case PageIndex.PageMangaSearch:
            case PageIndex.PageCharacterSearch:
            case PageIndex.PageSearchEverywhere:
                route = "search";
                if (args is SearchPageNavigationArgs sa)
                    query = $"?query={Uri.EscapeDataString(sa.Query ?? "")}&mode={(sa.Anime ? "anime" : "manga")}";
                break;
            case PageIndex.PageCalendar:
                route = "calendar";
                break;
            case PageIndex.PageProfile:
                route = "profile";
                if (args is ProfilePageNavigationArgs pa)
                    query = $"?user={Uri.EscapeDataString(pa.TargetUser ?? "")}";
                break;
            case PageIndex.PageLogIn:
                route = "login";
                break;
            case PageIndex.PageSettings:
                route = "settings";
                break;
            case PageIndex.PageRecomendations:
                route = "recommendations";
                break;
            case PageIndex.PageArticles:
            case PageIndex.PageNews:
                route = "articles";
                var articleMode = index == PageIndex.PageNews ? "News" : "Articles";
                if (args is MalArticlesPageNavigationArgs ma)
                {
                    articleMode = ma.WorkMode switch
                    {
                        ArticlePageWorkMode.News => "News",
                        ArticlePageWorkMode.AnnNews => "AnnNews",
                        _ => "Articles"
                    };
                }
                query = $"?mode={articleMode}";
                break;
            case PageIndex.PagePopularVideos:
                route = "videos";
                break;
            case PageIndex.PageForumIndex:
                if (args is ForumsTopicNavigationArgs ftn && !string.IsNullOrEmpty(ftn.TopicId))
                {
                    route = "forumtopic";
                    query = $"?id={Uri.EscapeDataString(ftn.TopicId)}";
                }
                else if (args is ForumsBoardNavigationArgs fbn)
                {
                    route = "forumboard";
                    if (fbn.AnimeId > 0)
                        query = $"?anime={Uri.EscapeDataString(fbn.AnimeId.ToString())}&title={Uri.EscapeDataString(fbn.AnimeTitle ?? "")}&manga={(fbn.IsAnimeBoard ? "false" : "true")}&page={fbn.PageNumber}";
                    else if (fbn.TargetBoard > (ForumBoards)0)
                        query = $"?board={Uri.EscapeDataString(((int)fbn.TargetBoard).ToString())}&page={fbn.PageNumber}";
                    else if (fbn.ClubId != null)
                        query = $"?club={Uri.EscapeDataString(fbn.ClubId)}&title={Uri.EscapeDataString(fbn.ClubName ?? "")}&page={fbn.PageNumber}";
                    else if (!string.IsNullOrWhiteSpace(fbn.Query))
                        query = $"?board={Uri.EscapeDataString(((int)(fbn.Scope ?? fbn.TargetBoard)).ToString())}&page={fbn.PageNumber}";
                }
                else
                {
                    route = "forums";
                }
                break;
            case PageIndex.PageHistory:
                route = "history";
                break;
            case PageIndex.PageFeeds:
                route = "feeds";
                break;
            case PageIndex.PageFriends:
                route = "friends";
                break;
            case PageIndex.PageWallpapers:
                route = "wallpapers";
                if (args is WallpaperPageNavigationArgs wp)
                {
                    var q = Uri.EscapeDataString(wp.Query ?? "");
                    query = $"?url={q}&title={q}";
                }
                break;
            case PageIndex.PageMessanging:
                route = "messaging";
                break;
            case PageIndex.PageMessageDetails:
                route = "messagedetails";
                if (args is MalMessageDetailsNavArgs mdn)
                {
                    switch (mdn.Arg)
                    {
                        case MalMessageModel thread when !string.IsNullOrEmpty(thread.ThreadId ?? thread.Id):
                            query = $"?id={Uri.EscapeDataString(thread.ThreadId ?? thread.Id)}" +
                                    $"&subject={Uri.EscapeDataString(thread.Subject ?? "")}";
                            break;
                        case MalComment comment:
                            // Profile comment: open the com-to-com thread, which the
                            // page rebuilds from ?id + ?to + ?comments=1.
                            query = $"?id={Uri.EscapeDataString(comment.ComToCom ?? string.Empty)}&subject=" +
                                    $"&to={Uri.EscapeDataString(comment.User?.Name ?? string.Empty)}&comments=1";
                            break;
                        default:
                            query = $"?id=0&subject={Uri.EscapeDataString(mdn.NewMessageTarget ?? "")}" +
                                    $"&to={Uri.EscapeDataString(mdn.NewMessageTarget ?? "")}";
                            break;
                    }
                }
                else
                {
                    query = "?id=0&subject=";
                }
                break;
            case PageIndex.PageClubIndex:
                route = "clubs";
                break;
            case PageIndex.PageClubDetails:
                route = "clubdetails";
                if (args is ClubDetailsPageNavArgs cdn)
                    query = $"?id={Uri.EscapeDataString(cdn.Id ?? string.Empty)}";
                break;
            case PageIndex.PageListComparison:
                route = "listcomparison";
                if (args is ListComparisonPageNavigationArgs lcn && lcn.CompareWith?.Name != null)
                    query = $"?user={Uri.EscapeDataString(lcn.CompareWith.Name)}";
                break;
            case PageIndex.PageNotificationHub:
                route = "notifications";
                break;
            case PageIndex.PageCharacterDetails:
                route = "character";
                if (args is CharacterDetailsNavigationArgs cn)
                    query = $"?id={cn.Id}";
                else if (args is int cid)
                    query = $"?id={cid}";
                break;
            case PageIndex.PageStaffDetails:
                route = "staff";
                if (args is StaffDetailsNaviagtionArgs sn)
                    query = $"?id={sn.Id}";
                else if (args is int sid)
                    query = $"?id={sid}";
                break;
            // PageAbout and PageMessageDetails: leave as no-op for now (settings/about opens via Settings page)
        }

        if (string.IsNullOrEmpty(route))
        {
            // A PageIndex without a case here is a silent no-op: the user taps
            // something and nothing happens. Make it loud instead.
            Console.WriteLine($"MALPLUS Navigate: no route mapped for PageIndex {index} (args={args?.GetType().Name ?? "null"})");
            return;
        }

        var final = string.IsNullOrEmpty(query) ? route : route + query;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current != null)
                    await Shell.Current.GoToAsync(final);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALPLUS Navigate failed for '{final}': {ex.GetType().Name} {ex.Message}");
            }
        });
    }

    private static bool IsMangaWorkMode(PageIndex index, object args)
    {
        if (args is AnimeListPageNavigationArgs a)
        {
            return a.WorkMode == AnimeListWorkModes.Manga ||
                   a.WorkMode == AnimeListWorkModes.TopManga ||
                   a.WorkMode == AnimeListWorkModes.MangaAdapted;
        }

        return index == PageIndex.PageMangaList ||
               index == PageIndex.PageTopManga ||
               index == PageIndex.PageMangaAdapted;
    }

    internal static string BuildAnimeListQuery(PageIndex index, object args)
    {
        // Anime list is a parameterised page (work mode, status, type).
        // Args may be AnimeListPageNavigationArgs (rich) or null.
        //
        // THIS IS THE ONLY PLACE THAT DECIDES WHAT STATE TRAVELS on a list route, and both
        // directions go through it. Opening a panel from More and coming back from a details page
        // both serialise here, so a field added below and on the args object round-trips without
        // anyone remembering a second place. Building the return query by hand is what dropped the
        // type and sent an Upcoming panel back to the General ranking.
        var sb = new System.Text.StringBuilder("?");
        if (args is AnimeListPageNavigationArgs a)
        {
            sb.Append("mode=").Append((int)a.WorkMode);
            // 'status' carries the StatusSelectorSelectedIndex (0=Watching..5=All);
            // only meaningful for the user Anime/Manga list modes.
            if (a.WorkMode == AnimeListWorkModes.Anime || a.WorkMode == AnimeListWorkModes.Manga)
            {
                if (a.StatusIndex.HasValue)
                    sb.Append("&status=").Append(a.StatusIndex.Value);
            }
            // Preserve the specific top/adapted type.
            string type = null;
            if (a.WorkMode == AnimeListWorkModes.TopAnime)
                type = a.TopWorkMode.ToString();
            else if (a.WorkMode == AnimeListWorkModes.TopManga)
                type = a.MangaTopWorkMode.ToString();
            else if (a.WorkMode == AnimeListWorkModes.MangaAdapted)
                type = a.MangaAdaptedWorkMode.ToString();
            if (type != null)
                sb.Append("&type=").Append(type);

            // The band, when this ranking has one. It was on the args and parsed back off the query,
            // but never emitted here, so it was lost on every trip through a route: pick Movies in
            // the ranking, open an entry, come back, and you were on the first band again. Only the
            // rankings that actually read bands carry it, which is why the args default to -1.
            if (a.TopRangeIndex >= 0)
                sb.Append("&range=").Append(a.TopRangeIndex);

            // Seasonal: name the season in the route. The details page registers this same args
            // object as its back navigation, so without it coming back from an anime has no idea
            // which season it was on and reopens whatever the current one is. Only carried when
            // there is one: entering Seasonal from the More menu passes none, and that is exactly
            // what makes it land on the current season.
            if (a.WorkMode == AnimeListWorkModes.SeasonalAnime && a.CurrSeason != null &&
                a.CurrSeason.Year > 0)
            {
                sb.Append("&year=").Append(a.CurrSeason.Year);
                sb.Append("&season=").Append(a.CurrSeason.Season);
            }
        }
        else
        {
            int mode = index switch
            {
                PageIndex.PageSeasonal => 1, // SeasonalAnime
                PageIndex.PageTopAnime => 3, // TopAnime
                PageIndex.PageMangaList => 2, // Manga
                PageIndex.PageTopManga => 4, // TopManga
                PageIndex.PageMangaAdapted => 7, // MangaAdapted
                _ => 0,
            };
            sb.Append("mode=").Append(mode);
        }
        return sb.ToString();
    }
}

public class MauiHamburgerViewModel : IHamburgerViewModel
{
    public Task UpdateProfileImg(bool dl = true)
    {
        return Task.CompletedTask;
    }

    public void SetActiveButton(HamburgerButtons val)
    {
    }

    public void UpdateApiDependentButtons()
    {
    }

    public void UpdateAnimeFiltersSelectedIndex()
    {
    }

    public void UpdateLogInLabel()
    {
    }

    public bool MangaSectionVisbility { get; set; }

    public void SetActiveButton(TopAnimeType topType)
    {
    }

    public void UpdatePinnedProfiles()
    {
    }

    public void UpdateBottomMargin()
    {
    }
}

public class MauiCssManager : MALPlus.XShared.Utils.CssManagerBase
{
    protected override string AccentColour => "#0066FF";
    protected override string AccentColourLight => "#4D94FF";
    protected override string AccentColourDark => "#0047B3";
    protected override string NotifyFunction => "function notify(msg){}";
    protected override string ShadowsDefinition => "";
}

public class MauiSettingsViewModel : MALPlus.XShared.ViewModels.SettingsViewModelBase
{
    public MauiSettingsViewModel() : base() { }

    public override event SettingsNavigationRequest NavigationRequest
    {
        add { }
        remove { }
    }

    public override ICommand ReviewCommand => null;
    public override ICommand RequestNavigationCommand => null;

    public override void LoadCachedEntries()
    {
    }
}
