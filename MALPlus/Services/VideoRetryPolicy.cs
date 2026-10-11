using System;
using System.Collections.Generic;

namespace MALPlus.Services;

/// <summary>
///     Asks a refused theme for again, on its own, and gives up only after several tries.
/// </summary>
/// <remarks>
///     The origin lets a handful of requests through per IP and then refuses the rest for a while.
///     Refused does not mean broken: the same file answers a moment later, which is why the themes used
///     to stop dead on the ninth and left the overlay on a black splash with nothing trying any more.
///     <para>
///     The waits grow on every attempt, and they grow for a reason rather than for taste: the budget is
///     spent when the refusal arrives, so asking again inside that window spends it again and earns
///     another refusal. Waiting past it is what makes the retry different from the attempt that failed.
///     </para>
///     <para>
///     One instance per overlay. It exists so every page that plays an AnimeThemes file gets the same
///     behaviour, including pages added later, instead of each one reimplementing the waits.
///     </para>
/// </remarks>
public sealed class VideoRetryPolicy
{
    /// <summary>
    ///     Waits before each further attempt, in seconds. Long enough to be a different answer rather
    ///     than the same one, which a fraction of a second would not be.
    /// </summary>
    private static readonly int[] BackoffSeconds = { 3, 6, 11 };

    private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>();

    /// <summary>
    ///     Attempts a URL has already used. A URL that fails every time is never attempted again, so
    ///     a genuinely broken file cannot hold the overlay open forever.
    /// </summary>
    /// <remarks>
    ///     A counter and not a set of names. The first version kept a HashSet and used membership as the
    ///     count, which meant a URL that had failed ONCE already read as past the last wait, so the first
    ///     refusal reported "giving up" in the same millisecond it arrived and none of the retries ever
    ///     ran. A set can only answer yes or no, and the difference between one failure and three is
    ///     exactly what this class exists to know.
    /// </remarks>
    public int AttemptsFor(string url) =>
        !string.IsNullOrWhiteSpace(url) && _attempts.TryGetValue(url, out var used) ? used : 0;

    /// <summary>
    ///     How long to wait before asking for this URL again, or null when there is nothing left to try.
    /// </summary>
    public TimeSpan? DelayFor(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var used = AttemptsFor(url);
        if (used <= 0)
            return TimeSpan.Zero;
        if (used > BackoffSeconds.Length)
            return null;
        return TimeSpan.FromSeconds(BackoffSeconds[used - 1]);
    }

    /// <summary>
    ///     Records that a URL has been refused, which is what moves it one step along the waits.
    /// </summary>
    public void NoteFailed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        _attempts.TryGetValue(url, out var used);
        _attempts[url] = used + 1;
    }

    /// <summary>
    ///     Clears the count for a URL once it plays, so a later refusal of the same file starts from the
    ///     short wait rather than from the long one.
    /// </summary>
    public void NotePlaying(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            _attempts.Remove(url);
    }
}