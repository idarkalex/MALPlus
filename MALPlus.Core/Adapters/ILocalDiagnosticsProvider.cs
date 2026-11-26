using System;
using System.Runtime.CompilerServices;
using MALPlus.Models.Enums;

namespace MALPlus.Adapters
{
    /// <summary>
    /// Provides 100% local, privacy-first diagnostic logging and error tracking on device.
    /// Zero data is transmitted externally.
    /// </summary>
    public interface ILocalDiagnosticsProvider
    {
        void Init();
        void TrackEvent(DiagnosticsEvent @event);
        void TrackEvent(DiagnosticsEvent @event, params (string Key, string Param)[] args);
        void TrackNavigation(PageIndex page);
        void TrackNavigation(ForumsPageIndex page);
        void LogEvent(string @event);
        void TrackExceptionWithMessage(Exception e, string message);
        void TrackException(Exception e, [CallerMemberName] string caller = null);
        void TrackExceptionWithAttachment(Exception e, string attachment = null, [CallerMemberName] string caller = null);
    }
}
