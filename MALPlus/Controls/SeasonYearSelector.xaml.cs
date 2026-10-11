using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Comm;

namespace MALPlus.Controls;

/// <summary>
///     Which season and which year, as one control.
/// </summary>
/// <remarks>
///     The two words and the data behind them, in one place so the anime list page and the season
///     themes page cannot drift apart. The dropdown itself is asked for through
///     <see cref="SeasonRequested"/> and <see cref="YearRequested"/> rather than drawn here, for the
///     reason the XAML spells out: a panel belonging to a control this short would be clipped to the
///     control's own bounds on Android and appear behind the bar.
/// </remarks>
public partial class SeasonYearSelector : ContentView
{
    private Season _season;
    private int _year;

    public SeasonYearSelector()
    {
        InitializeComponent();
        _season = AiringSeasonNow();
        _year = DateTime.UtcNow.Year;
        Paint();
    }

    /// <summary>The host should show the season choices for this control.</summary>
    public event EventHandler SeasonRequested;

    /// <summary>The host should show the year choices for this control.</summary>
    public event EventHandler YearRequested;

    /// <summary>Raised after a choice changes, so the host can reload whatever it was showing.</summary>
    public event EventHandler SelectionChanged;

    public Season Season
    {
        get => _season;
        set
        {
            if (value == _season)
                return;
            _season = value;
            Paint();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int Year
    {
        get => _year;
        set
        {
            if (value == _year)
                return;
            _year = value;
            Paint();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SeasonText => SeasonName(_season);

    public string YearText => _year.ToString();

    /// <summary>
    ///     The season that is airing right now.
    /// </summary>
    /// <remarks>
    ///     Deliberately the same rule the query and the view model use, and not the calendar months
    ///     this would otherwise reach for. If the two disagree, the "(Current)" mark ends up naming a
    ///     season current while the grid under it was filled from the other one.
    /// </remarks>
    public static Season AiringSeasonNow()
        => DateTime.UtcNow.Month switch
        {
            <= 3 => Season.Winter,
            <= 6 => Season.Spring,
            <= 9 => Season.Summer,
            _ => Season.Fall
        };

    public static string SeasonName(Season season)
        => season switch
        {
            Season.Winter => "Winter",
            Season.Spring => "Spring",
            Season.Summer => "Summer",
            _ => "Fall"
        };

    /// <summary>
    ///     The seasons to offer, in the order the eye expects: the chosen one first, then the rest of
    ///     the year in order.
    /// </summary>
    public static IEnumerable<(Season Season, bool IsChosen, bool IsNow)> SeasonChoices(Season chosen)
    {
        var now = AiringSeasonNow();
        var ordered = new List<Season> { chosen };
        foreach (var season in new[] { Season.Winter, Season.Spring, Season.Summer, Season.Fall })
            if (!ordered.Contains(season))
                ordered.Add(season);

        return ordered.Select(season =>
            (season, season == chosen, season == now));
    }

    /// <summary>
    ///     The years to offer, newest first, read from the source and then shared.
    /// </summary>
    /// <remarks>
    ///     Property of the catalogue rather than of a page, and read once per session. The anime list
    ///     page used to hold this in its own view model and refused to load it unless that page was on
    ///     the seasonal work mode, so any other page asking for it got nothing.
    ///     <para>
        /// The current year is always present even if the source never names it, because a selector
        ///     that cannot express "this year" is useless on the first launch and after a failure.
        /// </para>
        /// </remarks>
    public static async Task<IReadOnlyList<int>> YearsAsync()
    {
        var years = new List<int> { DateTime.UtcNow.Year };

        try
        {
            var (items, _) = await TenraiClient.GetPaginatedAsync("seasons");
            foreach (var item in items.Take(4))
            {
                if (!item.TryGetProperty("year", out var yearProp) ||
                    yearProp.ValueKind != System.Text.Json.JsonValueKind.Number)
                    continue;
                var year = yearProp.GetInt32();
                if (!years.Contains(year))
                    years.Add(year);
            }
        }
        catch
        {
            // The current year is enough to be usable, and the source will be asked again next time.
        }

        years.Sort();
        years.Reverse();
        return years;
    }

    private void OnSeasonTapped(object sender, EventArgs e)
        => SeasonRequested?.Invoke(this, EventArgs.Empty);

    private void OnYearTapped(object sender, EventArgs e)
        => YearRequested?.Invoke(this, EventArgs.Empty);

    private void Paint()
    {
        SeasonLabel.Text = SeasonText;
        YearLabel.Text = YearText;
    }
}
