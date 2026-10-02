using System.Diagnostics;

namespace MALPlus.Services;

// Startup timings, printed to logcat under MALPLUSStartup so the splash can be judged
// on numbers instead of vibes. This is the same trick that pinned down the details
// navigation: log the milestones, read the gaps.
//
// Nothing here is a gate. It only records, so a slow start can be attributed to a
// phase rather than guessed at.
public static class StartupTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<(string Name, long Ms)> Marks = new();
    private static readonly object Gate = new();
    private static bool _logged;

    public static void Mark(string name)
    {
        lock (Gate)
        {
            Marks.Add((name, Clock.ElapsedMilliseconds));

            var since = Marks.Count > 1 ? Marks[^2].Ms : 0;
            var delta = Marks[^1].Ms - since;

            try
            {
                Android.Util.Log.Info("MALPLUSStartup",
                    $"{Marks[^1].Ms,6}ms  (+{delta,5}ms)  {name}");
            }
            catch { }
        }
    }

    // Called once the first list has something in it: the moment the user can finally
    // see data instead of an empty grid.
    public static void LogSummary()
    {
        lock (Gate)
        {
            if (_logged)
                return;
            _logged = true;

            try
            {
                var ordered = string.Join(" -> ", Marks.Select(m => $"{m.Name}@{m.Ms}ms"));
                Android.Util.Log.Info("MALPLUSStartup", "summary: " + ordered);
            }
            catch { }
        }
    }
}