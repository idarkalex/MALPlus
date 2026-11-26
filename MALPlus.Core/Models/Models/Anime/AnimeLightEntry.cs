using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MALPlus.Models.Models.Anime
{
    public sealed class LightEntryChip
    {
        public string Text { get; init; }
        /// <summary>0 = role tag, 1 = ANIME badge, 2 = MANGA badge.</summary>
        public int Kind { get; init; }
    }

    public class AnimeLightEntry
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ImgUrl { get; set; }
        public string Notes { get; set; }
        public bool IsAnime { get; set; }

        private string _cachedNotes;
        private bool _cachedIsAnime;
        private IReadOnlyList<LightEntryChip> _cachedChips;

        [JsonIgnore]
        public IReadOnlyList<LightEntryChip> DisplayChips
        {
            get
            {
                if (_cachedChips != null && _cachedIsAnime == IsAnime && string.Equals(_cachedNotes, Notes, StringComparison.Ordinal))
                    return _cachedChips;

                var chips = new List<LightEntryChip>
                {
                    new LightEntryChip { Text = IsAnime ? "ANIME" : "MANGA", Kind = IsAnime ? 1 : 2 }
                };

                if (!string.IsNullOrWhiteSpace(Notes))
                {
                    foreach (var part in SplitTopLevelRoles(Notes))
                        chips.Add(new LightEntryChip { Text = part, Kind = 0 });
                }

                _cachedNotes = Notes;
                _cachedIsAnime = IsAnime;
                _cachedChips = chips;
                return _cachedChips;
            }
        }

        // Split roles only on top-level commas so parenthesised episode lists like
        // "Script (eps 10, 15, 18-21)" stay intact inside a single chip.
        private static IEnumerable<string> SplitTopLevelRoles(string notes)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < notes.Length; i++)
            {
                var c = notes[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && depth > 0)
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    var slice = notes.Substring(start, i - start).Trim();
                    if (!string.IsNullOrEmpty(slice))
                        yield return slice;
                    start = i + 1;
                }
            }

            var tail = notes.Substring(start).Trim();
            if (!string.IsNullOrEmpty(tail))
                yield return tail;
        }
    }
}
