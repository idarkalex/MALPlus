using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Misc;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Services;

/// <summary>
///     Shared random-picker service for both Discover tiles and the Random Anime / Manga
///     floating controls inside AnimeDetailsPage (filter sheet, persistence, and roll query).
/// </summary>
public static class RandomDiscoveryService
{
    private const string FilterPrefsKey = "MALPlus_DiscoveryRandomFilter_v1";

    public static HashSet<int> MyAnimeLibraryIds()
    {
        var ids = new HashSet<int>();
        var storage = ResourceLocator.AnimeLibraryDataStorage;
        if (storage == null)
            return ids;

        foreach (var abstraction in storage.AllLoadedAnimeItemAbstractions ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null && abstraction.MalId > 0)
                ids.Add(abstraction.MalId);
        }

        foreach (var abstraction in storage.AllLoadedMangaItemAbstractions ?? new List<AnimeItemAbstraction>())
        {
            if (abstraction != null && abstraction.MalId > 0)
                ids.Add(abstraction.MalId);
        }

        return ids;
    }

    public static async Task<RandomFilter> LoadFilterAsync(bool manga)
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

    public static async Task SaveFilterAsync(bool manga, RandomFilter filter)
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

    public static async Task<DiscoveryItem> PickRandomAsync(bool manga, int excludeId = 0)
    {
        var filter = await LoadFilterAsync(manga);
        var exclude = MyAnimeLibraryIds();
        if (excludeId > 0)
            exclude.Add(excludeId);

        var items = await DiscoveryRandomQuery.GetAsync(manga, filter, 0, 0, 1, excludeIds: exclude);
        return items.Count > 0 ? items[0] : null;
    }

    public static async Task EditFilterAsync(bool manga)
    {
        var filter = await LoadFilterAsync(manga);
        var title = manga ? "Manga filters" : "Anime filters";

        var formats = manga ? RandomFilter.MangaFormats : RandomFilter.AnimeFormats;
        var formatLabels = formats
            .Select(f => string.IsNullOrEmpty(f) ? "All" : char.ToUpperInvariant(f[0]) + f.Substring(1))
            .ToArray();

        var chosen = new HashSet<string>(filter.Formats ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (chosen.Count == 0 && !string.IsNullOrEmpty(filter.Format))
            chosen.Add(filter.Format);

        bool AllMarked()
        {
            var real = formats.Where(f => !string.IsNullOrEmpty(f)).ToList();
            return real.Count > 0 && real.All(chosen.Contains);
        }

        OptionsSheet filterSheet = null;
        string pending = null;

        void BackToFilters() =>
            filterSheet?.OpenCompactWithSubtitle(title, Summarize(filter), BuildFilterRows(), null);

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
                        ? $"{filter.MinScore.ToString("0.0", CultureInfo.InvariantCulture)} - {filter.MaxScore.ToString("0.0", CultureInfo.InvariantCulture)}"
                        : filter.MinScore > 0
                            ? filter.MinScore.ToString("0.0", CultureInfo.InvariantCulture)
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
            var all = Enum.GetValues(typeof(AnimeGenreSearch)).Cast<int>().ToList();
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
                        if (captured == 0)
                            filter.GenreIds.Clear();
                        else if (!filter.GenreIds.Remove(captured))
                            filter.GenreIds.Add(captured);
                        _ = SaveFilterAsync(manga, filter);
                        s.OpenSubmenu("Genres", GenreRows(), BackToFilters);
                        return true;
                    }));
            }

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

            return rows;
        }

        List<OptionsSheetItem> BuildFilterRows()
        {
            var rows = new List<OptionsSheetItem>
            {
                new("Type", null,
                    value: TypeLabel(),
                    hideChevron: true,
                    submenu: s =>
                    {
                        s.OpenSubmenu("Type", TypeRows(), BackToFilters);
                        return true;
                    }),
                new("Score", null,
                    value: ScoreLabel(filter),
                    hideChevron: true,
                    submenu: s =>
                    {
                        s.OpenSubmenu("Score range", ScoreRows(), BackToFilters);
                        return true;
                    }),
                new("Years", null, value: YearLabel(filter), hideChevron: true,
                    submenu: s =>
                    {
                        s.OpenSubmenu("Year range", YearRows(), BackToFilters);
                        return true;
                    }),
                new("Genres", null,
                    value: filter.GenreIds.Count == 0 ? "any" : filter.GenreIds.Count + " chosen",
                    hideChevron: true,
                    submenu: s =>
                    {
                        s.OpenSubmenu("Genres", GenreRows(), BackToFilters);
                        return true;
                    })
            };

            var clearAllItem = new OptionsSheetItem("Clear all", "icon_close", destructive: true,
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
                });
            var doneItem = new OptionsSheetItem("Done", "icon_check");
            rows.Add(OptionsSheetItem.Pair(clearAllItem, doneItem));
            return rows;
        }

        await OptionsSheetHost.PickAsync(title, Summarize(filter), BuildFilterRows(), null,
            sheet => filterSheet = sheet);
    }

    private static string ScoreLabel(RandomFilter filter)
    {
        if (filter.MinScore <= 0 && filter.MaxScore <= 0)
            return "any";
        if (filter.MinScore > 0 && filter.MaxScore > 0)
            return $"{filter.MinScore.ToString("0.0", CultureInfo.InvariantCulture)} - {filter.MaxScore.ToString("0.0", CultureInfo.InvariantCulture)}";
        if (filter.MinScore > 0)
            return $"{filter.MinScore.ToString("0.0", CultureInfo.InvariantCulture)}+";
        return $"< {filter.MaxScore.ToString("0.0", CultureInfo.InvariantCulture)}";
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
        clean = Regex.Replace(clean, @"\s+(a|to)\s+", "-", RegexOptions.IgnoreCase);
        clean = clean.Replace('/', '-');

        if (clean.Contains('-'))
        {
            var parts = clean.Split('-', 2);
            if (double.TryParse(parts[0].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v1))
                minScore = Math.Clamp(v1, 0, 10);
            if (parts.Length > 1 && double.TryParse(parts[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v2))
                maxScore = Math.Clamp(v2, 0, 10);

            if (minScore > 0 && maxScore > 0 && minScore > maxScore)
                (minScore, maxScore) = (maxScore, minScore);
            return;
        }

        clean = clean.TrimStart('>', '=', ' ').TrimEnd('+').Trim().Replace(',', '.');
        if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
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
            var numStr = Regex.Match(clean, @"\d{4}").Value;
            if (int.TryParse(numStr, out var beforeYear))
            {
                minYear = 0;
                maxYear = beforeYear - 1;
                return;
            }
        }

        clean = Regex.Replace(clean, @"\s+(a|to)\s+", "-");
        clean = clean.Replace('/', '-');

        var matches = Regex.Matches(clean, @"\b(19\d\d|20\d\d)\b");
        if (matches.Count >= 2)
        {
            minYear = int.Parse(matches[0].Value);
            maxYear = int.Parse(matches[1].Value);
            if (minYear > maxYear)
                (minYear, maxYear) = (maxYear, minYear);
            return;
        }
        if (matches.Count == 1)
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

        var anyDigits = Regex.Matches(clean, @"\d+");
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
            var info = Enum.GetName(typeof(AnimeGenreSearch), id);
            if (info == null)
                return "Genre " + id;
            var attr = typeof(AnimeGenreSearch)
                .GetField(info)?.GetCustomAttributes(typeof(DescriptionAttribute), false);
            if (attr is { Length: > 0 } && attr[0] is DescriptionAttribute d)
                return d.Description;
            return info.Replace("_", " ");
        }
        catch
        {
            return "Genre " + id;
        }
    }

    public static string Summarize(RandomFilter filter)
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
}
