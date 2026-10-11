using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Utils;

/// <summary>
///     Warms the seasonal cache with the seasons that can never change.
/// </summary>
/// <remarks>
///     <para>
///     A finished season is a fixed list: nothing is added to Winter 2025 now, and nothing will be.
///     Yet it cost eight or nine round trips every single time it was picked, and it is exactly the
///     kind of thing someone browses repeatedly. Fetching it once means the picker is instant.
///     </para>
///     <para>
///     Only seasons whose last broadcast has already passed are pulled, and never the current one:
///     that one changes week to week, so a stale copy would be worse than none. Nothing here blocks
///     anything. It runs once, off the startup path, one season at a time, and every step is a cache
///     write that a later pick finds and returns from in milliseconds.
///     </para>
/// </remarks>
public static class SeasonCacheWarmer
{
    private const int MaxSeasonsToWarm = 8;

    /// <summary>
    ///     Seasons still running, so the warmer leaves them alone.
    /// </summary>
    /// <remarks>
    ///     Deliberately generous: a season is treated as finished only a few weeks after it ended,
    ///     because a still-updating entry would be cached for a week and then be quietly wrong.
    /// </remarks>
    private static readonly TimeSpan GraceAfterSeasonEnd = TimeSpan.FromDays(21);

    private static bool _started;

    public static void WarmInBackground()
    {
        if (_started)
            return;
        _started = true;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var season in FinishedSeasonsToWarm())
                {
                    // Present at any age counts. A finished season does not change, so an existing
                    // entry is as good as a fresh one and rewriting it would be the very download
                    // this exists to avoid. Read past the expiry for the same reason.
                    var existing = await DataCache.RetrieveSeasonalDataIgnoringExpiry(CacheTagFor(season));
                    if (existing != null && existing.Count != 0)
                        continue;

                    // Sequential and unhurried on purpose: this is a background nicety, so it never
                    // competes with what the user is actually waiting for. One cancellation, checked
                    // between seasons rather than mid-flight, because the smallest possible unit to
                    // abandon is a whole season.
                    if (WarmupCancelled)
                        return;

                    await new AnimeSeasonalQuery(season).GetSeasonalAnime(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS season warmup stopped: " + ex.Message);
            }
        });
    }

    /// <summary>
    ///     True while the warmup should stop. Set by the user opening Seasonal, so warming never keeps
    ///     the source busy behind the request they are actually waiting on.
    /// </summary>
    public static volatile bool WarmupCancelled;

    public static void StopWarming() => WarmupCancelled = true;

    /// <summary>
    ///     The seasons worth having before they are asked for: the most recent finished ones, newest
    ///     first, capped so a first run cannot turn into a long download nobody asked for.
    /// </summary>
    private static IEnumerable<AnimeSeason> FinishedSeasonsToWarm()
    {
        var now = DateTime.UtcNow;
        var cutoff = now - GraceAfterSeasonEnd;

        // Walking back from the current season and stopping as soon as one is still live keeps this to
        // finished seasons by construction, without needing the source's own index to be fetched first.
        var order = new[] { Season.Winter, Season.Spring, Season.Summer, Season.Fall };
        var currentIndex = Array.IndexOf(order, CurrentSeason(now));
        var currentYear = now.Year;

        var result = new List<AnimeSeason>();
        for (var back = 1; back <= MaxSeasonsToWarm && result.Count < MaxSeasonsToWarm; back++)
        {
            var index = currentIndex - back;
            var year = currentYear;
            while (index < 0)
            {
                index += order.Length;
                year--;
            }

            var season = order[index];
            if (SeasonEnd(season, year) > cutoff)
                break;

            result.Add(new AnimeSeason
            {
                Season = season,
                Year = year,
                // Name is not read by the query, which keys on year and season, but composing it the
                // same way keeps a warmed season indistinguishable from a picked one.
                Name = season.ToString() + " " + year
            });
        }

        return result;
    }

    private static Season CurrentSeason(DateTime now)
        => now.Month switch
        {
            <= 3 => Season.Winter,
            <= 6 => Season.Spring,
            <= 9 => Season.Summer,
            _ => Season.Fall
        };

    /// <summary>
    ///     When a season is over. The source counts Winter as January to March, Spring as April to
    ///     June, Summer as July to September and Fall as October to December, which is what the season
    ///     index reports and therefore what the grace period has to be measured against.
    /// </summary>
    private static DateTime SeasonEnd(Season season, int year)
        => season switch
        {
            Season.Winter => new DateTime(year, 3, 31),
            Season.Spring => new DateTime(year, 6, 30),
            Season.Summer => new DateTime(year, 9, 30),
            _ => new DateTime(year, 12, 31)
        };

    private static string CacheTagFor(AnimeSeason season)
        => AnimeSeasonalQuery.CacheTagFor(season.Year, season.Season);

    /// <summary>
    ///     True when that season is over, given a season name off the wire ("winter") rather than the
    ///     enum, so the query can ask about a season it is fetching without converting twice.
    /// </summary>
    public static bool IsFinished(int year, string lowercaseSeason)
        => SeasonEnd(Parse(lowercaseSeason), year) <= DateTime.UtcNow - GraceAfterSeasonEnd;

    private static Season Parse(string lowercaseSeason) =>
        lowercaseSeason?.ToLowerInvariant() switch
        {
            "winter" => Season.Winter,
            "spring" => Season.Spring,
            "summer" => Season.Summer,
            _ => Season.Fall
        };
}