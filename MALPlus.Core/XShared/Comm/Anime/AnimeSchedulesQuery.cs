using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.XShared.BL;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    public class AnimeSchedulesQuery
    {
        private static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

/// <summary>
        ///     The series MyAnimeList currently lists as airing, with their weekly broadcast slot.
        ///     <para>
        ///     Deliberately NOT a list of episodes. This used to take the weekly slot and invent 52
        ///     weekly dates from it, which is the source of every wrong countdown in the app: it
        ///     announced chapters that were never scheduled, and it kept announcing them for a series
        ///     whose real last episode was weeks old, and for a series that had moved to a different
        ///     broadcast day it kept pointing at the old one. What a weekly slot can honestly answer
        ///     is "this is on the air, and this is the slot it normally uses", and that is all this
        ///     returns now. The next air date comes from AniListAiringQuery.
        ///     </para>
        /// </summary>
        public async Task<List<AiringInfoProvider.AiringData>> GetScheduleAsync()
        {
            var output = new List<AiringInfoProvider.AiringData>();
            try
            {
                var items = await TenraiClient.GetAllPagesAsync(p => ContentFilter.Query($"schedules?page={p}"), 12);
                var nowUtc = DateTime.UtcNow;

                foreach (var entry in items)
                {
                    try
                    {
                        var malId = GetInt(entry, "mal_id");
                        if (malId <= 0)
                            continue;

                        var dayOfWeek = GetBroadcastDay(entry);
                        var time = GetBroadcastTime(entry);
                        if (dayOfWeek == null || time == null)
                            continue;

                        var airingData = new AiringInfoProvider.AiringData
                        {
                            MalId = malId,
                            // Deliberately empty. This used to hold fifty-two weekly slots rolled
                            // forward from today, and those fabrications are what put a countdown on
                            // One Piece months after its last real episode and on Bleach on the old
                            // Saturday after it had moved. What a weekly slot can honestly answer is
                            // "this is on the air", and the dates now come from AniList.
                            Episodes = new List<AiringInfoProvider.Episode>(),
                            Title = GetString(entry, "title"),
                            ImgUrl = GetNestedImageUrl(entry),
                            Type = (int)MalTypeParser.ParseAnimeType(GetString(entry, "type")),
                            AllEpisodes = GetInt(entry, "episodes"),
                            BroadcastDay = dayOfWeek,
                            BroadcastTime = time
                        };

                        output.Add(airingData);
                    }
                    catch (Exception)
                    {
                        // skip malformed entry
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return output.Count > 0 ? output : null;
        }


        private static DayOfWeek? GetBroadcastDay(JsonElement entry)
        {
            if (!entry.TryGetProperty("broadcast", out var bc) || bc.ValueKind != JsonValueKind.Object)
                return null;
            if (!bc.TryGetProperty("day", out var dayProp) || dayProp.ValueKind != JsonValueKind.String)
                return null;

            var dayStr = dayProp.GetString();
            if (string.IsNullOrEmpty(dayStr))
                return null;

            for (int i = 0; i < DayNames.Length; i++)
            {
                if (dayStr.StartsWith(DayNames[i], StringComparison.OrdinalIgnoreCase))
                    return (DayOfWeek)i;
            }
            return null;
        }

        private static TimeSpan? GetBroadcastTime(JsonElement entry)
        {
            if (!entry.TryGetProperty("broadcast", out var bc) || bc.ValueKind != JsonValueKind.Object)
                return null;
            if (!bc.TryGetProperty("time", out var timeProp) || timeProp.ValueKind != JsonValueKind.String)
                return null;

            var timeStr = timeProp.GetString();
            if (string.IsNullOrEmpty(timeStr))
                return null;

            var parts = timeStr.Split(':');
            if (parts.Length != 2)
                return null;
            if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
                return null;

            return TimeSpan.FromMinutes(hours * 60 + minutes);
        }

        private static int GetInt(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

        private static string GetString(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            return "";
        }

        private static string GetNestedImageUrl(JsonElement entry)
        {
            if (!entry.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object)
                return null;
            if (!images.TryGetProperty("jpg", out var jpg) || jpg.ValueKind != JsonValueKind.Object)
                return null;
            if (!jpg.TryGetProperty("image_url", out var url) || url.ValueKind != JsonValueKind.String)
                return null;
            return url.GetString();
        }
    }
}
