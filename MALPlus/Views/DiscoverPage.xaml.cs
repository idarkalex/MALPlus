using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Misc;
using MALPlus.Services;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Views;

public partial class DiscoverPage : ContentPage
{
    private bool _busy;

    public DiscoverPage()
    {
        InitializeComponent();
    }

    private async Task<bool> GuardAsync()
    {
        if (_busy)
            return false;
        _busy = true;
        HeaderSpinner.IsRunning = true;
        return true;
    }

    private void Release()
    {
        _busy = false;
        HeaderSpinner.IsRunning = false;
    }

    // ---------- simple destinations ----------

    private async void OnSearchTapped(object sender, EventArgs e)
    {
        NavigateSearchWithCurrentQuery();
    }

    private void OnSearchInputCompleted(object sender, EventArgs e)
    {
        NavigateSearchWithCurrentQuery();
    }

    private void OnSearchInputHandlerChanged(object sender, EventArgs e)
    {
#if ANDROID
        StripEntryUnderline();
#endif
    }

    private async void NavigateSearchWithCurrentQuery()
    {
        var text = SearchInputField?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            try { await Shell.Current.GoToAsync($"search?query={Uri.EscapeDataString(text)}"); } catch { }
        }
        else
        {
            try { await Shell.Current.GoToAsync("search"); } catch { }
        }
    }

    private async void OnNewsTapped(object sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("articles?mode=News"); } catch { }
    }

    private async void OnUpcomingAllTapped(object sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("calendar?mode=global"); } catch { }
    }

    private async void OnUpcomingMineTapped(object sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("calendar?mode=mine"); } catch { }
    }

    // ---------- multi result sheets ----------

    private async void OnTrendingTapped(object sender, EventArgs e)
    {
        if (!await GuardAsync()) return;
        try
        {
            // Native Tenrai weekly momentum: airing anime sorted by net members gained over the past 7 days.
            var items = await DiscoveryTrendingQuery.GetAsync(TrendingResultCount);
            if (items.Count == 0)
            {
                global::Android.Util.Log.Warn("MALPlus Discover",
                    "trending failed: " + DiscoveryTrendingQuery.LastError);
                await Alert("Could not load trending.");
            }
            else
            {
                await ShowResults("Trending this week", items);
            }
        }
        catch { }
        finally { Release(); }
    }

    /// <summary>Rows on the trending sheet. The old feed asked for twenty and the screen holds five.</summary>
    private const int TrendingResultCount = 10;

    /// <summary>
    ///     The reader's own anime, as the app already holds it.
    /// </summary>
    /// <remarks>
    ///     <c>AllLoadedAuthAnimeItems</c> used to be read here and it is the wrong collection: it only
    ///     starts pointing at the real library once the anime list page has fetched it, because that is
    ///     what hands the list over. Until then it is the empty list the storage was constructed with, so
    ///     Discover saw nothing and the tile reported that the reader had no entry scored 8 or higher
    ///     when their list was full of them.
    /// </remarks>
    /// <summary>
    ///     The reader's own anime, ensuring the storage is loaded if opening Discover on cold start.
    /// </summary>
    private static async Task<List<AnimeItemAbstraction>> EnsureAnimeLibraryAsync()
    {
        var storage = ResourceLocator.AnimeLibraryDataStorage;
        var items = storage?.AllLoadedAnimeItemAbstractions;
        if (items != null && items.Count > 0)
            return items;

        if (Credentials.Authenticated && !string.IsNullOrWhiteSpace(Credentials.UserName))
        {
            try
            {
                await ViewModelLocator.AnimeList.FetchData(false, MALPlus.Models.Enums.AnimeListWorkModes.Anime);
                items = ResourceLocator.AnimeLibraryDataStorage?.AllLoadedAnimeItemAbstractions;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("MALPlus Discover", "EnsureAnimeLibraryAsync: " + ex.Message);
            }
        }

        return items ?? new List<AnimeItemAbstraction>();
    }

    /// <summary>
    ///     Every id the reader already has, anime and manga alike, so recommendations leave it out.
    /// </summary>
    private static HashSet<int> MyAnimeLibraryIds()
        => RandomDiscoveryService.MyAnimeLibraryIds();

    private async void OnBecauseYouLikedTapped(object sender, EventArgs e)
    {
        if (!await GuardAsync()) return;
        try
        {
            var library = await EnsureAnimeLibraryAsync();
            var inLibrary = MyAnimeLibraryIds();
            var anchors = new List<RecommendationAnchor>();

            foreach (var abstraction in library)
            {
                if (abstraction?.EntryData is not MALPlus.Models.Models.Library.AnimeLibraryItemData data)
                    continue;

                // Never recommend based on shows the user dropped or rated poorly
                if (data.MyScore > 0 && data.MyScore <= 5)
                    continue;
                if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.Dropped)
                    continue;
                if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.PlanToWatch)
                    continue;

                int weight = 0;
                // Tier 1: User rated 8, 9, or 10 -> Top priority
                if (data.MyScore >= 8)
                {
                    weight = (int)(data.MyScore * 3); // 24 to 30
                }
                // Tier 2: User rated 6 or 7
                else if (data.MyScore >= 6)
                {
                    weight = (int)(data.MyScore * 2); // 12 to 14
                }
                // Tier 3: Completed without numeric score -> High positive weight!
                else if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.Completed)
                {
                    weight = 20;
                }
                // Tier 4: Watching / Rewatching with progress
                else if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.Watching || data.IsRewatching)
                {
                    if (data.MyEpisodes >= 2)
                        weight = 15;
                }
                // Tier 5: On hold with progress
                else if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.OnHold && data.MyEpisodes >= 3)
                {
                    weight = 10;
                }

                if (weight > 0 && abstraction.MalId > 0)
                {
                    anchors.Add(new RecommendationAnchor
                    {
                        MalId = abstraction.MalId,
                        Title = data.Title ?? abstraction.Title ?? string.Empty,
                        Score = (int)data.MyScore,
                        Weight = weight
                    });
                }
            }

            if (anchors.Count == 0)
            {
                Console.WriteLine($"MALPLUS foryou: sin anclas (biblioteca={library.Count} items)");
                await Alert("Add or complete some anime in your list to get recommendations.");
                return;
            }

            var result = await BecauseYouLikedQuery.GetAsync(anchors, inLibrary);
            Console.WriteLine($"MALPLUS foryou: anclas={anchors.Count} biblioteca={inLibrary.Count} resultado={result.Count}");
            if (result.Count == 0)
                await Alert("No recommendations found for your list.");
            else
                await ShowResults("For you", result);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Discover", "OnBecauseYouLikedTapped: " + ex.Message);
        }
        finally { Release(); }
    }

    private async void OnSeasonThemesTapped(object sender, EventArgs e)
    {
        // Its own page rather than the shared results list: this one carries the season and year
        // bar at the top and shows one anime per row with its themes beside it, which is a different
        // shape from the single-item rows the results list draws.
        if (!await GuardAsync()) return;
        try
        {
            var now = MALPlus.Controls.SeasonYearSelector.AiringSeasonNow();
            await Shell.Current.GoToAsync(
                $"seasonthemes?year={DateTime.UtcNow.Year}&season={MALPlus.Controls.SeasonYearSelector.SeasonName(now).ToLowerInvariant()}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Discover", "season themes nav: " + ex.GetType().Name);
        }
        finally { Release(); }
    }

    /// <summary>
    ///     One hidden gem per day rather than a reshuffle on every tap. The id is stored with the
    ///     day it was drawn in, so the tile shows the same title all day; the draw itself is
    ///     score >= 7.5, popularity between #1500 and #15000, under 60k members, from 2010 on, and
    ///     not already on the user's list. Tenrai filters the score but ignores the member and
    ///     popularity bounds, and has no "not in my list" filter, so both are applied here.
    /// </summary>
    private async void OnGemTapped(object sender, EventArgs e)
    {
        if (!await GuardAsync()) return;
        try
        {
            // Same wrong collection as Because you liked: reading the auth list left the "not already
            // on your list" filter matching nothing, so the gem was free to be something the reader
            // already had.
            var inLibrary = MyAnimeLibraryIds();

            var gem = await DiscoveryGemQuery.GetAsync(inLibrary);
            if (gem == null)
            {
                await Alert("No gems matched those filters today.");
                return;
            }

            UpdateDailyGemTile();
            MALPlus.Services.MauiDetailsNavigationHandoff.Set(
                new AnimeDetailsPageNavigationArgs(gem.Id, gem.Title, null, null, null)
                {
                    AnimeMode = true,
                    Source = PageIndex.PageDiscover
                });
            var title = Uri.EscapeDataString(gem.Title ?? string.Empty);
            await Shell.Current.GoToAsync($"animedetails?id={gem.Id}&title={title}");
        }
        catch { }
        finally { Release(); }
    }

    private void UpdateDailyGemTile()
    {
        if (DailyGemSubLabel == null)
            return;
        var cached = DiscoveryGemQuery.GetCachedGem();
        if (cached != null)
        {
            var scorePart = cached.Score > 0
                ? "★ " + cached.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : "Today's pick";
            DailyGemSubLabel.Text = string.IsNullOrEmpty(cached.Title)
                ? scorePart
                : $"{scorePart} · {cached.Title}";
        }
        else
        {
            DailyGemSubLabel.Text = "High score · Hidden find";
        }
    }

    // ---------- single item jumps ----------

    /// <summary>
    ///     The picture of a random capsule: go straight to an entry.
    /// </summary>
    /// <remarks>
    ///     This used to open a sheet with Go / Adjust filters / Clear filters first, so picking a
    ///     random one cost a dialog before anything happened. The gear on the right quarter of the same
    ///     capsule now opens the filters, which leaves this side free to just do the thing.
    /// </remarks>
    private async void OnRandomAnimeTapped(object sender, EventArgs e) => await JumpToRandomAsync(false);

    private async void OnRandomMangaTapped(object sender, EventArgs e) => await JumpToRandomAsync(true);

    private async void OnRandomAnimeFiltersTapped(object sender, EventArgs e) => await RandomDiscoveryService.EditFilterAsync(false);

    private async void OnRandomMangaFiltersTapped(object sender, EventArgs e) => await RandomDiscoveryService.EditFilterAsync(true);

    private async Task JumpToRandomAsync(bool manga)
    {
        if (!await GuardAsync()) return;
        try
        {
            var item = await RandomDiscoveryService.PickRandomAsync(manga);
            if (item == null)
            {
                await Alert("Nothing matched those filters.");
                return;
            }
            var qs = manga ? "&manga=true&random=true" : "&random=true";
            await Shell.Current.GoToAsync($"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}{qs}");
        }
        finally { Release(); }
    }

    private async void OnRandomThemeTapped(object sender, EventArgs e)
    {
        if (!await GuardAsync()) return;
        try
        {
            // AnimeThemes only, with no fallback anywhere. If it has nothing to offer the reader is
            // told and that is the end of it: a YouTube search in its place would put a video of a
            // different thing on screen under the name of a thing that was never drawn.
            var theme = await AnimeThemesHelper.GetRandomThemeAsync();
            if (string.IsNullOrEmpty(theme?.Url))
            {
                await Alert("AnimeThemes returned no theme.");
                return;
            }

            // The anime's name came with the theme, so one lookup is enough to reach MAL, which is the
            // only side of this that has a poster.
            var animeName = await AnimeThemesHelper.GetAnimeNameAsync(theme.AnimeSlug);

            await Shell.Current.GoToAsync(
                $"randomtheme?slug={Uri.EscapeDataString(theme.AnimeSlug ?? "")}" +
                $"&anime={Uri.EscapeDataString(animeName ?? "")}" +
                $"&song={Uri.EscapeDataString(theme.SongTitle ?? "")}" +
                $"&slot={Uri.EscapeDataString(theme.Type + " " + theme.Sequence)}" +
                $"&url={Uri.EscapeDataString(theme.Url)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS random theme nav failed: " + ex.GetType().Name);
        }
        finally { Release(); }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (Shell.Current as AppShell)?.UpdateSharedBarVisibility();
        UpdateDailyGemTile();
#if ANDROID
        StripEntryUnderline();
#endif
    }

#if ANDROID
    private void StripEntryUnderline()
    {
        try
        {
            if (SearchInputField?.Handler?.PlatformView is global::Android.Widget.EditText edit)
            {
                edit.Background = null;
                edit.SetPadding(0, 0, 0, 0);
                edit.Gravity = global::Android.Views.GravityFlags.CenterVertical |
                               global::Android.Views.GravityFlags.Start;
            }
        }
        catch { }
    }
#endif

    // ---------- helpers ----------

    private async Task ShowResults(string header, List<DiscoveryItem> items)
    {
        try
        {
            var json = JsonSerializer.Serialize(items);
            var page = new DiscoveryResultsPage
            {
                HeaderText = header,
                ItemsJson = json
            };
            await Navigation.PushModalAsync(page);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Discover", "show results: " + ex.GetType().Name);
        }
    }

    private Task Alert(string message)
    {
        try
        {
            return OptionsSheetHost.ConfirmAsync("", message, "OK");
        }
        catch
        {
            return Task.CompletedTask;
        }
    }
}