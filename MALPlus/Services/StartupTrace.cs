using System.Diagnostics;
using System.Text;

namespace MALPlus.Services;

// Startup timings, measured on-device to track launch latency milestones.
// 100% local, saved to local logs for diagnostics.
public static class StartupTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<(string Name, long Ms, long Delta)> Marks = new();
    private static readonly object Gate = new();
    private static bool _logged;
    private static long _startupCompletedMs;

    public static void Mark(string name)
    {
        lock (Gate)
        {
            if (name == "first-items-visible" && Marks.Any(m => m.Name == "first-items-visible"))
                return;

            var elapsed = Clock.ElapsedMilliseconds;
            var since = Marks.Count > 0 ? Marks[^1].Ms : 0;
            var delta = elapsed - since;
            Marks.Add((name, elapsed, delta));

            try
            {
                Android.Util.Log.Info("MALPLUSStartup",
                    $"{elapsed,6}ms  (+{delta,5}ms)  {name}");
            }
            catch { }

            try
            {
                AppLog.Debug("Startup", $"{name} @ {elapsed}ms (+{delta}ms)");
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
            _startupCompletedMs = Clock.ElapsedMilliseconds;

            try
            {
                var ordered = string.Join(" -> ", Marks.Select(m => $"{m.Name}@{m.Ms}ms"));
                Android.Util.Log.Info("MALPLUSStartup", "summary: " + ordered);
                AppLog.Info("Startup", $"Startup completed in {_startupCompletedMs}ms: {ordered}");
            }
            catch { }
        }
    }

    public static void AppendTimeline(StringBuilder sb)
    {
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                sb.AppendLine("No startup marks recorded.");
                return;
            }

            foreach (var (name, ms, delta) in Marks)
            {
                sb.AppendLine($"  {ms,6}ms  (+{delta,5}ms)  {name}");
            }
            var startupTime = _startupCompletedMs > 0 ? _startupCompletedMs : Marks[^1].Ms;
            sb.AppendLine($"Startup completed in: {startupTime}ms");
            sb.AppendLine($"App session uptime: {Clock.ElapsedMilliseconds / 1000}s");
        }
    }
}
