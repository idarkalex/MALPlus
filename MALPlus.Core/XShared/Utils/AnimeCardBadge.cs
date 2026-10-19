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
    ///     "3/12" while the entry is on the list, "12" when it is not but the total is known, and
    ///     nothing at all when it is neither followed nor measured.
    /// </summary>
    public static string Episodes(bool onList, int watched, int total)
    {
        if (onList)
            return $"{watched}/{(total == 0 ? "?" : total.ToString())}";

        // Nothing has been watched, so "0/12" is a fact about nothing. The total is the one half of
        // this that is worth saying about an entry the user has not started.
        return total == 0 ? string.Empty : total.ToString();
    }

    /// <summary>
    ///     The status half, blank for anything the user does not follow.
    /// </summary>
    public static string Status(bool onList, Func<string> status)
        => onList ? status?.Invoke() ?? string.Empty : string.Empty;
}
