using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MALPlus.XShared.Interfaces
{
    public interface IAiringInfoProvider
    {
        Task Init(bool cacheOnly, bool force = false);
        bool TryGetCurrentEpisode(int id, out int episode, DateTime? forDay = null);
        bool TryGetLastEpisode(int id, out int ep);
        bool TryGetNextAirDate(int id, DateTime forDay, out DateTime date);
        bool TryGetAiringDay(int id,out DayOfWeek day);
        bool HasAiringEntry(int id);
        IEnumerable<int> GetAllAiringIds();
        bool TryGetEntry(int id, out MALPlus.XShared.BL.AiringInfoProvider.AiringData entry);

        /// <summary>
        ///     The next episode date the external schedule declares for a series. Absent means no
        ///     chapter is announced, which is a different answer from the weekly broadcast slot.
        /// </summary>
        bool TryGetDeclaredNextAirDate(int malId, out DateTime dateUtc);

        /// <summary>
        ///     Refreshes the declared next air dates, at most once per cache window. The result is
        ///     how many dates were learned, which a caller waiting with a bound uses to decide
        ///     whether a rebuild is worth it.
        /// </summary>
        Task<int> RefreshDeclaredNextAirAsync(IEnumerable<int> malIds, bool force = false);

        /// <summary>
        ///     Says a series is wanted, batching the requests that arrive together. Screens call this
        ///     instead of fetching, so a grid or a season turns into one request instead of one per
        ///     entry, which is also the difference between being answered and being refused.
        ///     <para>
        ///     The returned task completes once this series has been asked for, so a caller can read
        ///     the table knowing its own answer is in it rather than hoping it arrived in time.
        ///     </para>
        /// </summary>
        Task RequestDeclaredNextAir(int malId);

        bool InitializationSuccess { get; }
        event Action AiringsUpdated;
        void NotifyUpdated();

    }
}
