using System;

namespace MALPlus.XShared.Utils;

/// <summary>
///     The one rule for what a card's episode half of the badge says.
/// </summary>
/// <remarks>
///     The hero, the poster cell and the recommendation row all bind a badge that has an episode half
///     in it, and there used to be a different answer in each place. An entry the user follows reads
///     "3/12", which is the status and the progress both. An entry they do not follow cannot read
///     that, because there is no watched half yet, so it reads the total on its own.
///     Every caller goes through here: changing the shape of the counter is one edit.
/// </remarks>
public static class AnimeCardBadge
{
    /// <summary>
    ///     "3/12" while the entry is on the list, and empty when it is not on the user's list.
    ///     Entries not followed have no watched progress or library status, so the corner badge
    ///     stays clean and doesn't display a floating total episode count.
    /// </summary>
    public static string Episodes(bool onList, int watched, int total)
    {
        if (!onList)
            return string.Empty;

        return $"{watched}/{(total == 0 ? "?" : total.ToString())}";
    }

    /// <summary>
    ///     The status half, blank for anything the user does not follow.
    /// </summary>
    public static string Status(bool onList, Func<string> status)
        => onList ? status?.Invoke() ?? string.Empty : string.Empty;

    /// <summary>
    ///     Meaningful accent color for each status code (W/R: Green, C: Sky Cyan, Re: Purple, H: Amber, D: Red, P: Silver).
    /// </summary>
    public static string StatusColor(bool onList, int status, bool rewatching = false)
    {
        if (!onList)
            return "#C0E0FF";

        if (rewatching)
            return "#C084FC"; // Rewatching (Purple)

        return status switch
        {
            1 => "#FFFFFF", // Watching / Reading: White
            2 => "#38BDF8", // Completed: Sky Blue / Cyan
            3 => "#FF9500", // On Hold: Vivid Orange
            4 => "#FF5252", // Dropped: Vivid Bright Red
            6 => "#C084FC", // Plan to Watch / Read: Purple
            _ => "#FFFFFF"
        };
    }

    public static string StatusColor(bool onList, string shortStatus)
    {
        if (!onList || string.IsNullOrEmpty(shortStatus))
            return "#C0E0FF";

        return shortStatus.Trim() switch
        {
            "W" or "R" => "#FFFFFF", // Watching / Reading: White
            "C" => "#38BDF8", // Completed: Sky Blue / Cyan
            "Re" => "#C084FC", // Rewatching: Purple
            "H" => "#FF9500", // On Hold: Vivid Orange
            "D" => "#FF5252", // Dropped: Vivid Bright Red
            "P" => "#C084FC", // Plan to Watch / Read: Purple
            _ => "#FFFFFF"
        };
    }
}
