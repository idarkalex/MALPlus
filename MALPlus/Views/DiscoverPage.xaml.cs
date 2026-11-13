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

public partial class DiscoverPage : ContentPage, ITabSwipeHost
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

    bool ITabSwipeHost.TrySwipe(int direction)
        => BottomNav?.StepSection(direction) == true;

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
    {
        var ids = new HashSet<int>();
        var storage = ResourceLocator.AnimeLibraryDataStorage;
        if (storage == null)
            return ids;

        foreach (var abstraction in storage.AllLoadedAnimeItemAbstractions
                                          ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null && abstraction.MalId > 0)
                ids.Add(abstraction.MalId);
        }

        foreach (var abstraction in storage.AllLoadedMangaItemAbstractions
                                          ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null && abstraction.MalId > 0)
                ids.Add(abstraction.MalId);
        }

        return ids;
    }

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
        var items = await DiscoveryRandomQuery.GetAsync(manga, filter, 0, 0, 1, excludeIds: MyAnimeLibraryIds());
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
                new("0 (any)", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 0;
                    filter.MaxScore = 0;
                    pending = "0";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("6+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 6;
                    filter.MaxScore = 0;
                    pending = "6+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("7+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 7;
                    filter.MaxScore = 0;
                    pending = "7+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("7.5+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 7.5;
                    filter.MaxScore = 0;
                    pending = "7.5+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("8+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 8;
                    filter.MaxScore = 0;
                    pending = "8+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("8.5+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 8.5;
                    filter.MaxScore = 0;
                    pending = "8.5+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("9+", null, hideChevron: true, submenu: s =>
                {
                    filter.MinScore = 9;
                    filter.MaxScore = 0;
                    pending = "9+";
                    _ = SaveFilterAsync(manga, filter);
                    BackToFilters();
                    return true;
                }),
                new("Custom range...", "icon_edit", hideChevron: true, submenu: s =>
                {
                    var initial = filter.MinScore > 0 && filter.MaxScore > 0
                        ? $"{filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} - {filter.MaxScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
                        : filter.MinScore > 0
                            ? filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                            : "";
                    s.OpenPrompt("Custom score", "e.g. 7.5 - 9.5 or 8.0", initial, "7.5 - 9.5", 20, true,
                        onSave: val =>
                        {
                            ParseScoreRange(val, out var min, out var max);
                            filter.MinScore = min;
                            filter.MaxScore = max;
                            _ = SaveFilterAsync(manga, filter);
                            BackToFilters();
                        },
                        onCancel: BackToFilters,
                        keyboard: Keyboard.Text,
                        closeOnSave: false);
                    return true;
                })
            };

        List<OptionsSheetItem> YearRows() => new List<OptionsSheetItem>
        {
            new("Any", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 0;
                filter.MaxYear = 0;
                pending = "Any";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2020+", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 2020;
                filter.MaxYear = 0;
                pending = "2020+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2015+", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 2015;
                filter.MaxYear = 0;
                pending = "2015+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2010+", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 2010;
                filter.MaxYear = 0;
                pending = "2010+";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("2000s", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 2000;
                filter.MaxYear = 2009;
                pending = "2000s";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("90s", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 1990;
                filter.MaxYear = 1999;
                pending = "90s";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("80s", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 1980;
                filter.MaxYear = 1989;
                pending = "80s";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("Before 2000", null, hideChevron: true, submenu: s =>
            {
                filter.MinYear = 0;
                filter.MaxYear = 1999;
                pending = "Before 2000";
                _ = SaveFilterAsync(manga, filter);
                BackToFilters();
                return true;
            }),
            new("Custom range...", "icon_edit", hideChevron: true, submenu: s =>
            {
                var initial = filter.MinYear > 0 && filter.MaxYear > 0
                    ? (filter.MinYear == filter.MaxYear ? $"{filter.MinYear}" : $"{filter.MinYear} - {filter.MaxYear}")
                    : filter.MinYear > 0
                        ? $"{filter.MinYear}+"
                        : filter.MaxYear > 0
                            ? $"< {filter.MaxYear + 1}"
                            : "";
                s.OpenPrompt("Custom year range", "e.g. 1995 - 2000 or 1998", initial, "1995 - 2000", 20, true,
                    onSave: val =>
                    {
                        ParseYearRange(val, out var min, out var max);
                        filter.MinYear = min;
                        filter.MaxYear = max;
                        _ = SaveFilterAsync(manga, filter);
                        BackToFilters();
                    },
                    onCancel: BackToFilters,
                    keyboard: Keyboard.Text,
                    closeOnSave: false);
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
                    hideChevron: true,
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

            rows.Add(new OptionsSheetItem("Done", "icon_check", hideChevron: true, submenu: s =>
            {
                BackToFilters();
                return true;
            }));
            return rows;
        }

        string TypeLabel()
        {
            if (chosen.Count == 0 || AllMarked())
                return "All";
            var names = chosen.Select(f => char.ToUpperInvariant(f[0]) + f.Substring(1)).ToList();
            if (names.Count <= 2)
                return string.Join(" / ", names);
            return $"{names.Count} chosen";
        }

        List<OptionsSheetItem> TypeRows()
        {
            var rows = new List<OptionsSheetItem>();
            var isAll = chosen.Count == 0 || AllMarked();

            rows.Add(new OptionsSheetItem("All", isAll ? "icon_check" : null, hideChevron: true, submenu: s =>
            {
                chosen.Clear();
                filter.Formats = new List<string>();
                filter.Format = "";
                _ = SaveFilterAsync(manga, filter);
                s.OpenSubmenu("Type", TypeRows(), BackToFilters);
                return true;
            }));

            for (var i = 0; i < formatLabels.Length; i++)
            {
                var value = formats[i];
                if (string.IsNullOrEmpty(value))
                    continue;

                var isChecked = !isAll && chosen.Contains(value);
                var label = formatLabels[i];

                rows.Add(new OptionsSheetItem(label, isChecked ? "icon_check" : null, hideChevron: true, submenu: s =>
                {
                    if (isAll)
                    {
                        chosen.Clear();
                        chosen.Add(value);
                    }
                    else
                    {
                        if (!chosen.Remove(value))
                            chosen.Add(value);

                        var realFormats = formats.Where(f => !string.IsNullOrEmpty(f)).ToList();
                        if (chosen.Count == 0 || realFormats.All(chosen.Contains))
                            chosen.Clear();
                    }

                    filter.Formats = chosen.ToList();
                    filter.Format = "";
                    _ = SaveFilterAsync(manga, filter);
                    s.OpenSubmenu("Type", TypeRows(), BackToFilters);
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

            rows.Add(new OptionsSheetItem("Type", null,
                value: TypeLabel(),
                hideChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Type", TypeRows(), BackToFilters);
                    return true;
                }));

            rows.Add(new OptionsSheetItem("Score", null,
                value: ScoreLabel(filter),
                hideChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Score range", ScoreRows(), BackToFilters);
                    return true;
                }));
            rows.Add(new OptionsSheetItem("Years", null, value: YearLabel(filter), hideChevron: true,
                submenu: s =>
                {
                    s.OpenSubmenu("Year range", YearRows(), BackToFilters);
                    return true;
                }));
            rows.Add(new OptionsSheetItem("Genres", null,
                value: filter.GenreIds.Count == 0 ? "any" : filter.GenreIds.Count + " chosen",
                hideChevron: true,
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
                    filter.MaxScore = 0;
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

    private static string ScoreLabel(RandomFilter filter)
    {
        if (filter.MinScore <= 0 && filter.MaxScore <= 0)
            return "any";
        if (filter.MinScore > 0 && filter.MaxScore > 0)
            return $"{filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} - {filter.MaxScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
        if (filter.MinScore > 0)
            return $"{filter.MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}+";
        return $"< {filter.MaxScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string YearLabel(RandomFilter filter)
    {
        if (filter.MinYear <= 0 && filter.MaxYear <= 0)
            return "any";
        if (filter.MinYear > 0 && filter.MaxYear > 0)
            return filter.MinYear == filter.MaxYear ? $"{filter.MinYear}" : $"{filter.MinYear} - {filter.MaxYear}";
        if (filter.MinYear > 0)
            return $"{filter.MinYear}+";
        return $"< {filter.MaxYear + 1}";
    }

    public static void ParseScoreRange(string input, out double minScore, out double maxScore)
    {
        minScore = 0;
        maxScore = 0;
        if (string.IsNullOrWhiteSpace(input))
            return;

        var clean = input.Trim();
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+(a|to)\s+", "-", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        clean = clean.Replace('/', '-');

        if (clean.Contains('-'))
        {
            var parts = clean.Split('-', 2);
            if (double.TryParse(parts[0].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v1))
                minScore = Math.Clamp(v1, 0, 10);
            if (parts.Length > 1 && double.TryParse(parts[1].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v2))
                maxScore = Math.Clamp(v2, 0, 10);

            if (minScore > 0 && maxScore > 0 && minScore > maxScore)
                (minScore, maxScore) = (maxScore, minScore);
            return;
        }

        clean = clean.TrimStart('>', '=', ' ').TrimEnd('+').Trim().Replace(',', '.');
        if (double.TryParse(clean, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var single))
        {
            minScore = Math.Clamp(single, 0, 10);
            maxScore = 0;
        }
    }

    public static void ParseYearRange(string input, out int minYear, out int maxYear)
    {
        minYear = 0;
        maxYear = 0;
        if (string.IsNullOrWhiteSpace(input))
            return;

        var clean = input.Trim().ToLowerInvariant();
        if (clean == "any" || clean == "0" || clean == "all")
            return;

        if (clean == "90s" || clean == "90's" || clean == "1990s")
        {
            minYear = 1990;
            maxYear = 1999;
            return;
        }
        if (clean == "80s" || clean == "80's" || clean == "1980s")
        {
            minYear = 1980;
            maxYear = 1989;
            return;
        }
        if (clean == "70s" || clean == "70's" || clean == "1970s")
        {
            minYear = 1970;
            maxYear = 1979;
            return;
        }
        if (clean == "2000s" || clean == "00s")
        {
            minYear = 2000;
            maxYear = 2009;
            return;
        }
        if (clean == "2010s" || clean == "10s")
        {
            minYear = 2010;
            maxYear = 2019;
            return;
        }

        if (clean.StartsWith("before ") || clean.StartsWith("<"))
        {
            var numStr = System.Text.RegularExpressions.Regex.Match(clean, @"\d{4}").Value;
            if (int.TryParse(numStr, out var beforeYear))
            {
                minYear = 0;
                maxYear = beforeYear - 1;
                return;
            }
        }

        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+(a|to)\s+", "-");
        clean = clean.Replace('/', '-');

        var matches = System.Text.RegularExpressions.Regex.Matches(clean, @"\b(19\d\d|20\d\d)\b");
        if (matches.Count >= 2)
        {
            minYear = int.Parse(matches[0].Value);
            maxYear = int.Parse(matches[1].Value);
            if (minYear > maxYear)
                (minYear, maxYear) = (maxYear, minYear);
            return;
        }
        else if (matches.Count == 1)
        {
            var y = int.Parse(matches[0].Value);
            if (clean.Contains("+") || clean.StartsWith("after") || clean.StartsWith(">"))
            {
                minYear = y;
                maxYear = 0;
            }
            else if (clean.StartsWith("-"))
            {
                minYear = 0;
                maxYear = y;
            }
            else
            {
                minYear = y;
                maxYear = y;
            }
            return;
        }

        var anyDigits = System.Text.RegularExpressions.Regex.Matches(clean, @"\d+");
        if (anyDigits.Count >= 2 && int.TryParse(anyDigits[0].Value, out var y1) && int.TryParse(anyDigits[1].Value, out var y2))
        {
            if (y1 < 100) y1 += (y1 > 30 ? 1900 : 2000);
            if (y2 < 100) y2 += (y2 > 30 ? 1900 : 2000);
            minYear = Math.Min(y1, y2);
            maxYear = Math.Max(y1, y2);
        }
        else if (anyDigits.Count == 1 && int.TryParse(anyDigits[0].Value, out var ySingle))
        {
            if (ySingle < 100) ySingle += (ySingle > 30 ? 1900 : 2000);
            minYear = ySingle;
            maxYear = ySingle;
        }
    }

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
        var scoreStr = ScoreLabel(filter);
        if (scoreStr != "any")
            parts.Add("★" + scoreStr);
        var yearStr = YearLabel(filter);
        if (yearStr != "any")
            parts.Add(yearStr);
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