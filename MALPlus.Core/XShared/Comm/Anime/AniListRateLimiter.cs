using System;
using System.Threading.Tasks;

namespace MALPlus.XShared.Comm.Anime
{
    /// <summary>
    ///     One request budget for every call this app makes to AniList.
    ///     <para>
    ///     There used to be two clients with two independent limiters: the countdown query, which
    ///     asks for many series in one document, and the schedule client, which asks for one series at
    ///     a time. Both were perfectly behaved on their own and together they spent twice what
    ///     AniList allows, so the API started answering 429 while each of them still believed it had
    ///     budget left. A limit nobody can observe from outside its own object is not a limit.
    ///     </para>
    ///     <para>
    ///     The bucket refills one request every two seconds, thirty a minute, under AniList's
    ///     documented ninety but inside the reduced allowance it falls back to when it is under load.
    ///     Sitting on the documented number is how a client gets itself throttled.
    ///     </para>
    /// </summary>
    public static class AniListRateLimiter
    {
        private const int Capacity = 8;
        private static readonly TimeSpan RefillInterval = TimeSpan.FromSeconds(2);
        private static readonly object Gate = new object();
        private static int _tokens = Capacity;
        private static DateTime _lastRefill = DateTime.UtcNow;

        private static bool TryTakeToken()
        {
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                var refill = (int)((now - _lastRefill).TotalSeconds / RefillInterval.TotalSeconds);
                if (refill > 0)
                {
                    _tokens = Math.Min(Capacity, _tokens + refill);
                    _lastRefill = now;
                }
                if (_tokens <= 0)
                    return false;
                _tokens--;
                return true;
            }
        }

        /// <summary>
        ///     Waits for the next request slot. The sleep is one interval rather than a poll loop
        ///     because polling made every waiting caller wake on the same tick and race for the single
        ///     token that had come back, which is a busy loop wearing a delay as a disguise.
        /// </summary>
        public static async Task WaitAsync()
        {
            while (!TryTakeToken())
                await Task.Delay(RefillInterval);
        }
    }
}
