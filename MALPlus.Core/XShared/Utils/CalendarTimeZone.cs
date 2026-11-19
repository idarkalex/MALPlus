using System;
using System.Collections.Generic;

namespace MALPlus.XShared.Utils
{
    //Airing instants are absolute, so a countdown needs no time zone at all: "3H" is the same in
    //Madrid and in Tokyo. What does need one is the weekday column. MAL publishes the broadcast
    //slot in JST, so the provider always reports the Japanese weekday, which puts a 07:30 JST
    //Tuesday show under Tuesday even though anyone west of Tokyo watches it Monday night.
    public static class CalendarTimeZone
    {
        //A real class, not a value tuple: reflection only sees Item1/Item2 on a tuple, so a
        //Picker ItemDisplayBinding of "Label" silently renders nothing.
        public sealed class Option
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public string Subtitle { get; set; }
            public string ShortLabel { get; set; }
            public override string ToString() => Label;
        }

        private static readonly List<Option> _options = new List<Option>
        {
            new Option { Id = "", Label = "System default", Subtitle = "Device local time", ShortLabel = "System default" },
            new Option { Id = "Pacific/Pago_Pago", Label = "UTC-11:00", Subtitle = "Pago Pago, Midway", ShortLabel = "UTC-11:00" },
            new Option { Id = "Pacific/Honolulu", Label = "UTC-10:00 — Honolulu", Subtitle = "Hawaii, Papeete", ShortLabel = "Honolulu (UTC-10)" },
            new Option { Id = "America/Anchorage", Label = "UTC-09:00 — Anchorage", Subtitle = "Alaska Standard Time", ShortLabel = "Anchorage (UTC-9)" },
            new Option { Id = "America/Los_Angeles", Label = "UTC-08:00 — Los Angeles", Subtitle = "Vancouver, Tijuana (PT)", ShortLabel = "Los Angeles (PT)" },
            new Option { Id = "America/Denver", Label = "UTC-07:00 — Denver", Subtitle = "Phoenix, Salt Lake City (MT)", ShortLabel = "Denver (MT)" },
            new Option { Id = "America/Chicago", Label = "UTC-06:00 — Chicago", Subtitle = "Mexico City, Dallas (CT)", ShortLabel = "Chicago (CT)" },
            new Option { Id = "America/New_York", Label = "UTC-05:00 — New York", Subtitle = "Toronto, Bogota, Miami (ET)", ShortLabel = "New York (ET)" },
            new Option { Id = "America/Santiago", Label = "UTC-04:00 — Santiago", Subtitle = "Caracas, Halifax, La Paz", ShortLabel = "Santiago (UTC-4)" },
            new Option { Id = "America/Argentina/Buenos_Aires", Label = "UTC-03:00 — Buenos Aires", Subtitle = "Sao Paulo, Montevideo", ShortLabel = "Buenos Aires (UTC-3)" },
            new Option { Id = "America/Noronha", Label = "UTC-02:00 — Noronha", Subtitle = "Fernando de Noronha, South Georgia", ShortLabel = "Noronha (UTC-2)" },
            new Option { Id = "Atlantic/Cape_Verde", Label = "UTC-01:00 — Cape Verde", Subtitle = "Azores, Ponta Delgada", ShortLabel = "Cape Verde (UTC-1)" },
            new Option { Id = "UTC", Label = "UTC+00:00 — London, UTC", Subtitle = "Lisbon, Dublin, Casablanca", ShortLabel = "London / UTC" },
            new Option { Id = "Europe/Madrid", Label = "UTC+01:00 — Madrid", Subtitle = "Paris, Berlin, Rome (CET)", ShortLabel = "Madrid (CET)" },
            new Option { Id = "Europe/Athens", Label = "UTC+02:00 — Athens", Subtitle = "Cairo, Kyiv, Helsinki (EET)", ShortLabel = "Athens (EET)" },
            new Option { Id = "Europe/Moscow", Label = "UTC+03:00 — Moscow", Subtitle = "Istanbul, Riyadh, Baghdad", ShortLabel = "Moscow (UTC+3)" },
            new Option { Id = "Asia/Dubai", Label = "UTC+04:00 — Dubai", Subtitle = "Baku, Tbilisi, Yerevan", ShortLabel = "Dubai (UTC+4)" },
            new Option { Id = "Asia/Karachi", Label = "UTC+05:00 — Karachi", Subtitle = "Tashkent, Islamabad", ShortLabel = "Karachi (UTC+5)" },
            new Option { Id = "Asia/Kolkata", Label = "UTC+05:30 — New Delhi", Subtitle = "Mumbai, Kolkata (IST)", ShortLabel = "New Delhi (IST)" },
            new Option { Id = "Asia/Dhaka", Label = "UTC+06:00 — Dhaka", Subtitle = "Almaty, Astana, Omsk", ShortLabel = "Dhaka (UTC+6)" },
            new Option { Id = "Asia/Bangkok", Label = "UTC+07:00 — Bangkok", Subtitle = "Jakarta, Hanoi, Ho Chi Minh", ShortLabel = "Bangkok (UTC+7)" },
            new Option { Id = "Asia/Singapore", Label = "UTC+08:00 — Singapore", Subtitle = "Beijing, Perth, Manila, Taipei", ShortLabel = "Singapore / Perth" },
            new Option { Id = "Asia/Tokyo", Label = "UTC+09:00 — Tokyo", Subtitle = "Seoul, Osaka (JST)", ShortLabel = "Tokyo (JST)" },
            new Option { Id = "Australia/Sydney", Label = "UTC+10:00 — Sydney", Subtitle = "Melbourne, Brisbane, Guam", ShortLabel = "Sydney (AEST)" },
            new Option { Id = "Pacific/Noumea", Label = "UTC+11:00 — Noumea", Subtitle = "Solomon Islands, Vladivostok", ShortLabel = "Noumea (UTC+11)" },
            new Option { Id = "Pacific/Auckland", Label = "UTC+12:00 — Auckland", Subtitle = "Wellington, Suva, Fiji", ShortLabel = "Auckland (UTC+12)" },
            new Option { Id = "Pacific/Tongatapu", Label = "UTC+13:00 — Tonga", Subtitle = "Nuku'alofa, Apia, Samoa", ShortLabel = "Tonga (UTC+13)" },
            new Option { Id = "Pacific/Kiritimati", Label = "UTC+14:00 — Kiritimati", Subtitle = "Line Islands", ShortLabel = "Kiritimati (UTC+14)" },
        };

        public static IReadOnlyList<Option> Options => _options;

        public static string LabelFor(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "System default";
            foreach (var option in _options)
            {
                if (option.Id == id)
                    return option.Label;
            }
            return id;
        }

        public static TimeZoneInfo Resolve()
        {
            var id = Settings.CalendarTimeZoneId;
            if (string.IsNullOrEmpty(id))
                return TimeZoneInfo.Local;
            try
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var winId))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
                }
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static string _cachedId;
        private static TimeZoneInfo _cachedZone;
        private static bool _cachedResolved;

        /// <summary>
        ///     Memoised zone. This is read once per item in hot paths (AnimeItemAbstraction
        ///     constructors), and FindSystemTimeZoneById is far too expensive for that.
        /// </summary>
        public static TimeZoneInfo Resolved
        {
            get
            {
                var id = Settings.CalendarTimeZoneId ?? "";
                if (_cachedResolved && id == _cachedId && _cachedZone != null)
                    return _cachedZone;
                _cachedZone = Resolve();
                _cachedId = id;
                _cachedResolved = true;
                return _cachedZone;
            }
        }

        public static void Invalidate() => _cachedResolved = false;

        //NextAirUtc values come out of ConvertFromUnixTimestamp as Kind=Unspecified, but they are
        //UTC instants, so anything Local has to be normalised before converting.
        public static DateTime ToZone(DateTime utc, TimeZoneInfo zone)
        {
            var target = zone ?? TimeZoneInfo.Local;
            DateTime asUtc;
            if (utc.Kind == DateTimeKind.Local)
                asUtc = utc.ToUniversalTime();
            else
                asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(asUtc, target);
        }

        public static DayOfWeek ToZoneDay(DateTime utc, TimeZoneInfo zone) => ToZone(utc, zone).DayOfWeek;

        //The day-of-year key used to match a day cell. Has to be computed in the same zone as the
        //day column, otherwise an entry can never find its own cell around midnight.
        public static int ToZoneDayOfYear(DateTime utc, TimeZoneInfo zone) => ToZone(utc, zone).DayOfYear;
    }
}
