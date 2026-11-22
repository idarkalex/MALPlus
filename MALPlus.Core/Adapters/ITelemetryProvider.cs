using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Enums;

namespace MALPlus.Adapters
{
    /// <summary>
    /// Provides 100% local, privacy-first diagnostic logging and error tracking on device.
    /// Zero data is transmitted to external telemetry servers.
    /// </summary>
    public interface ILocalDiagnosticsProvider
    {
        void Init();
        void TrackEvent(TelemetryTrackedEvents @event);
        void TrackEvent(TelemetryTrackedEvents @event, params (string Key, string Param)[] args);
        void TrackNavigation(PageIndex page);
        void TrackNavigation(ForumsPageIndex page);
        void LogEvent(string @event);
        void TrackExceptionWithMessage(Exception e, string message);
        void TrackException(Exception e, [CallerMemberName] string caller = null);
        void TrackExceptionWithAttachment(Exception e, string attachment = null, [CallerMemberName] string caller = null);

        // Backward-compatibility aliases
        void TelemetryTrackEvent(TelemetryTrackedEvents @event) => TrackEvent(@event);
        void TelemetryTrackEvent(TelemetryTrackedEvents @event, params (string Key, string Param)[] args) => TrackEvent(@event, args);
        void TelemetryTrackNavigation(PageIndex page) => TrackNavigation(page);
        void TelemetryTrackNavigation(ForumsPageIndex page) => TrackNavigation(page);
    }

    [Obsolete("Use ILocalDiagnosticsProvider instead")]
    public interface ITelemetryProvider : ILocalDiagnosticsProvider
    {
    }
}
