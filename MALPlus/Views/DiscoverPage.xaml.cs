using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Models.Models.Misc;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.Services;

namespace MALPlus.Views;

public partial class DiscoverPage : ContentPage
{
    private const string FilterPrefsKey = "MALPlus_DiscoveryRandomFilter_v1";

    /// <summary>
    ///     The options sheet currently open for the random filters, so the submenu back row can redraw
    ///     the filter list without being handed a sheet.
    /// </summary>
    private OptionsSheet _filterSheet;

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
        try { await Shell.Current.GoToAsync("search"); } catch { }
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
            // AniList's own trending metric, restricted to what is currently airing.
            // See AniListTrendingQuery for why that restriction is the whole trick.
            var items = await AniListTrendingQuery.GetAsync(TrendingResultCount);
            if (items.Count == 0)
            {
                global::Android.Util.Log.Warn("MALPlus Discover",
                    "trending failed: " + AniListTrendingQuery.LastError);
                await Alert("Could not load trending.");
            }
            else
            {
                await ShowResults("Trending now", items);
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
    private static List<AnimeItemAbstraction> MyAnimeLibrary()
        => ResourceLocator.AnimeLibraryDataStorage?.AllLoadedAnimeItemAbstractions
           ?? new List<AnimeItemAbstraction>();

    /// <summary>
    ///     Every id the reader already has, anime and manga alike, so recommendations leave it out.
    /// </summary>
    private static HashSet<int> MyAnimeLibraryIds()
    {
        var ids = new HashSet<int>();
        var storage = ResourceLocator.AnimeLibraryDataStorage;
        if (storage == null)
            return ids;

        foreach (var abstraction in storage.AllLoadedAnimeItemAbstractions
                                          ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null)
                ids.Add(abstraction.MalId);
        }

        foreach (var abstraction in storage.AllLoadedMangaItemAbstractions
                                          ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null)
                ids.Add(abstraction.MalId);
        }

        return ids;
    }

    private async void OnBecauseYouLikedTapped(object sender, EventArgs e)
    {
        if (!await GuardAsync()) return;
        try
        {
            var anchors = new List<Tuple<int, string, int>>();
            var inLibrary = MyAnimeLibraryIds();
            foreach (var abstraction in MyAnimeLibrary())
            {
                if (abstraction?.EntryData is not MALPlus.Models.Models.Library.AnimeLibraryItemData data)
                    continue;
                if (data.MyStatus == MALPlus.Models.Enums.AnimeStatus.Completed && data.MyScore >= 8)
                    anchors.Add(Tuple.Create(abstraction.MalId, data.Title, (int)data.MyScore));
            }
            if (anchors.Count == 0)
            {
                Console.WriteLine($"MALPLUS foryou: sin anclas (biblioteca={MyAnimeLibrary().Count} items)");
                await Alert("You need at least one completed anime scored 8 or more in your list.");
                return;
            }

            var result = await BecauseYouLikedQuery.GetAsync(anchors, inLibrary);
            Console.WriteLine($"MALPLUS foryou: anclas={anchors.Count} biblioteca={inLibrary.Count} resultado={result.Count}");
            if (result.Count == 0)
                await Alert("No recommendations found for your list.");
            else
                await ShowResults("For you", result);
        }
        catch { }
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
    ///     One hidden gem per week rather than a reshuffle on every tap. The id is stored with the
    ///     week it was drawn in, so the tile shows the same title all week; the draw itself is
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
                await Alert("No gems matched those filters this week.");
                return;
            }
            await ShowResults("Gem of the week", new List<DiscoveryItem> {gem});
        }
        catch { }
        finally { Release(); }
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

    private async void OnRandomAnimeFiltersTapped(object sender, EventArgs e) => await EditFilterAsync(false);

    private async void OnRandomMangaFiltersTapped(object sender, EventArgs e) => await EditFilterAsync(true);

    private async Task JumpToRandomAsync(bool manga)
    {
        var filter = await LoadFilterAsync(manga);
        var items = await DiscoveryRandomQuery.GetAsync(manga, filter, 0, 0, 1);
        if (items.Count == 0)
        {
            await Alert("Nothing matched those filters.");
            return;
        }
        var qs = manga ? "&manga=true" : "";
        await Shell.Current.GoToAsync($"animedetails?id={items[0].Id}&title={Uri.EscapeDataString(items[0].Title ?? "")}{qs}");
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

    // ---------- random filters ----------

    /// <summary>
    ///     The filter panel for a random capsule: one sheet that owns its own sub levels.
    /// </summary>
    /// <remarks>
    ///     Each subfilter used to be a second sheet pushed from the first, and choosing one ended the
    ///     flow: the panel closed and the reader was dropped back on Discover, so setting a second
    ///     filter meant starting again from the tile. They are submenus now, so the panel stays and a
    ///     pick comes back to this same list with the new value on it. The check marks and the summary
    ///     are rebuilt on every redraw for that reason.
    /// </remarks>
    private async Task EditFilterAsync(bool manga)
    {
        var filter = await LoadFilterAsync(manga);
        var title = manga ? "Manga filters" : "Anime filters";

        var formats = manga ? RandomFilter.MangaFormats : RandomFilter.AnimeFormats;
        var formatLabels = formats
            .Select(f => string.IsNullOrEmpty(f) ? "All" : char.ToUpperInvariant(f[0]) + f.Substring(1))
            .ToArray();

        // "All" is a shortcut over the real formats, not one of them. It reads as marked when every
        // format below it is, tapping it with anything cleared marks them all, and tapping it while
        // everything is marked clears them all. Every row is therefore an independent toggle.
        var chosen = new HashSet<string>(filter.Formats ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (chosen.Count == 0 && !string.IsNullOrEmpty(filter.Format))
            chosen.Add(filter.Format);

        bool AllMarked()
        {
            var real = formats.Where(f => !string.IsNullOrEmpty(f)).ToList();
            return real.Count > 0 && real.All(chosen.Contains);
        }

        // Committed by a submenu row and read when the parent list is drawn again. A row tapped here
        // does not close anything: the submenu hook returns true, which is what tells the sheet it
        // handled the row itself.
        string pending = null;

        // Redraws the filter rows over whatever sheet is up. The submenu back row is handed no sheet
        // because it belongs to the level already on screen, so the reference kept from open time is
        // what lets it come back up.
        void BackToFilters() =>
            _filterSheet?.OpenCompactWithSubtitle(title, Summarize(filter), BuildFilterRows(), null);

        List<OptionsSheetItem> ScoreRows()
            => new List<OptionsSheetItem>
            {
                new("0 (any)", null, submenu: s =>
                {
                    filter.MinScore = 0;
                    pending = "0";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("6", null, submenu: s =>
                {
                    filter.MinScore = 6;
                    pending = "6";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("7", null, submenu: s =>
                {
                    filter.MinScore = 7;
                    pending = "7";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("7.5", null, submenu: s =>
                {
                    filter.MinScore = 7.5;
                    pending = "7.5";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("8", null, submenu: s =>
                {
                    filter.MinScore = 8;
                    pending = "8";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("8.5", null, submenu: s =>
                {
                    filter.MinScore = 8.5;
                    pending = "8.5";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("9", null, submenu: s =>
                {
                    filter.MinScore = 9;
                    pending = "9";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                })
            };

        List<OptionsSheetItem> YearRows() => new List<OptionsSheetItem>
        {
            new("Any", null, submenu: s =>
            {
                filter.MinYear = 0;
                filter.MaxYear = 0;
                pending = "Any";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2015+", null, submenu: s =>
            {
                filter.MinYear = 2015;
                filter.MaxYear = 0;
                pending = "2015+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2010+", null, submenu: s =>
            {
                filter.MinYear = 2010;
                filter.MaxYear = 0;
                pending = "2010+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2000+", null, submenu: s =>
            {
                filter.MinYear = 2000;
                filter.MaxYear = 0;
                pending = "2000+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("Before 2000", null, submenu: s =>
            {
                filter.MinYear = 1960;
                filter.MaxYear = 0;
                pending = "Before 2000";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            })
        };

        List<OptionsSheetItem> GenreRows()
        {
            var all = Enum.GetValues(typeof(MALPlus.Models.Enums.AnimeGenreSearch)).Cast<int>().ToList();
            var rows = new List<OptionsSheetItem>();
            foreach (var id in all)
            {
                var captured = id;
                var on = filter.GenreIds.Contains(id);
                rows.Add(new OptionsSheetItem(captured == 0 ? "Any" : DescribeGenre(captured),
                    on ? "icon_check" : null,
                    submenu: s =>
                    {
                        // Marks rather than commits, and the panel is drawn again from the new
                        // selection so the check marks follow the taps.
                        if (captured == 0)
                            filter.GenreIds.Clear();
                        else if (!filter.GenreIds.Remove(captured))
                            filter.GenreIds.Add(captured);
                        _ = SaveFilterAsync(manga, filter);
                        s.OpenSubmenu("Genres", GenreRows(), BackToFilters);
                        return true;
                    }));
            }

            rows.Add(new OptionsSheetItem("Done", "icon_check", submenu: s =>
            {
                BackToFilters();
                return true;
            }));
            return rows;
        }

        List<OptionsSheetItem> BuildFilterRows()
        {
            var rows = new List<OptionsSheetItem>();
            var allMarked = AllMarked();

            rows.Add(new OptionsSheetItem("All", allMarked ? "icon_check" : null, hideChevron: true, submenu: s =>
            {
                if (allMarked)
                    chosen.Clear();
                else
                    foreach (var f in formats.Where(f => !string.IsNullOrEmpty(f)))
                        chosen.Add(f);
                filter.Formats = chosen.ToList();
                filter.Format = "";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }));

            for (var i = 0; i < formatLabels.Length; i++)
            {
                var value = formats[i];
                if (string.IsNullOrEmpty(value))
                    continue;
                // No chevron: these rows commit on tap and stay on the same level, so an arrow would
                // promise a sub panel that does not exist.
                rows.Add(new OptionsSheetItem(formatLabels[i], chosen.Contains(value) ? "icon_check" : null,
                    hideChevron: true,
                    submenu: s =>
                    {
                        if (!chosen.Remove(value))
                            chosen.Add(value);
                        filter.Formats = chosen.ToList();
                        filter.Format = "";
                        _ = SaveFilterAsync(manga, filter);
                        BackToFilters();
                        return true;
                    }));
            }

                        rows.Add(new OptionsSheetItem("Min score", null,
                value: filter.MinScore <= 0
                    ? "any"
                    : filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                showChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Minimum score", ScoreRows(), BackToFilters);
                    return true;
                }));
            rows.Add(new OptionsSheetItem("Years", null, value: YearLabel(filter), showChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Year range", YearRows(), BackToFilters);
                    return true;
                }));
            rows.Add(new OptionsSheetItem("Genres", null,
                value: filter.GenreIds.Count == 0 ? "any" : filter.GenreIds.Count + " chosen",
                showChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Genres", GenreRows(), BackToFilters);
                    return true;
                }));
            rows.Add(new OptionsSheetItem("Clear all", "icon_close", destructive: true,
                hideChevron: true,
                submenu: s =>
                {
                    chosen.Clear();
                    filter.Format = "";
                    filter.Formats = new List<string>();
                    filter.MinScore = 0;
                    filter.MinYear = 0;
                    filter.MaxYear = 0;
                    filter.GenreIds.Clear();
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }));
            // No submenu, so choosing it is what finishes: PickAsync hands the label back and the panel closes.
            // Without it the only way out was the close button, which reads as abandoning the panel
            // rather than as being done with the filters.
            rows.Add(new OptionsSheetItem("Done", "icon_check"));
            return rows;
        }

        // The close button at the top is what abandoning the panel looks like; Done is what finishing
        // it looks like, and both end up here.
        _filterSheet = null;
        await OptionsSheetHost.PickAsync(title, Summarize(filter), BuildFilterRows(), null,
            sheet => _filterSheet = sheet);
    }

    private static string YearLabel(RandomFilter filter) =>
        filter.MinYear <= 0 ? "any" : filter.MinYear + "+";

    private static string DescribeGenre(int id)
    {
        try
        {
            var info = System.Enum.GetName(typeof(MALPlus.Models.Enums.AnimeGenreSearch), id);
            if (info == null)
                return "Genre " + id;
            var attr = typeof(MALPlus.Models.Enums.AnimeGenreSearch)
                .GetField(info)?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
            if (attr is { Length: > 0 } && attr[0] is System.ComponentModel.DescriptionAttribute d)
                return d.Description;
            return info.Replace("_", " ");
        }
        catch
        {
            return "Genre " + id;
        }
    }

    private async Task<RandomFilter> LoadFilterAsync(bool manga)
    {
        var key = FilterPrefsKey + (manga ? "_manga" : "_anime");
        try
        {
            var stored = await ResourceLocator.DataCacheService
                .RetrieveDataRoaming<RoamingData<RandomFilter>>(key, -1);
            return stored?.Data ?? new RandomFilter();
        }
        catch
        {
            return new RandomFilter();
        }
    }

    private async Task SaveFilterAsync(bool manga, RandomFilter filter)
    {
        var key = FilterPrefsKey + (manga ? "_manga" : "_anime");
        try
        {
            await ResourceLocator.DataCacheService.SaveDataRoaming(new RoamingData<RandomFilter>
            {
                Data = filter
            }, key);
        }
        catch
        {
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
    }

    private static string Summarize(RandomFilter filter)
    {
        var parts = new List<string>();
        var formats = filter.FormatsQuery();
        if (!string.IsNullOrEmpty(formats))
        {
            var names = formats.Split(',')
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => char.ToUpperInvariant(f[0]) + f.Substring(1))
                .ToList();
            if (names.Count > 0)
                parts.Add(string.Join("/", names));
        }
        if (filter.MinScore > 0)
            parts.Add("★" + filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        if (filter.MinYear > 0)
            parts.Add(filter.MinYear + "+");
        if (filter.GenreIds.Count > 0)
            parts.Add(filter.GenreIds.Count + " genres");
        return parts.Count == 0 ? "No filters" : string.Join(" · ", parts);
    }

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