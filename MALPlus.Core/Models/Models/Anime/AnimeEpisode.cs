using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MALPlus.Models.Models.Anime
{

    public class AnimeEpisode : INotifyPropertyChanged
    {
        [JsonProperty("episode_id")] public long EpisodeId { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("title_japanese")] public string TitleJapanese { get; set; }
        [JsonProperty("title_romanji")] public string TitleRomanji { get; set; }
        [JsonProperty("filler")] public bool Filler { get; set; }
        [JsonProperty("recap")] public bool Recap { get; set; }
        [JsonProperty("video_url")] public string VideoUrl { get; set; }
        [JsonProperty("forum_url")] public string ForumUrl { get; set; }
        [JsonProperty("aired")] public DateTime? AiredDate { get; set; }

        [JsonIgnore] private bool _isWatched;
        [JsonIgnore]
        public bool IsWatched
        {
            get => _isWatched;
            set
            {
                if (_isWatched == value) return;
                _isWatched = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string AiredLabel
        {
            get
            {
                if (!AiredDate.HasValue) return "—";
                var local = AiredDate.Value.Kind == DateTimeKind.Utc
                    ? AiredDate.Value.ToLocalTime()
                    : AiredDate.Value;
                // The UI is English regardless of the device locale, so do not let the
                // Spanish/Portuguese short month names leak in ("26 sept", "29 ago").
                //
                // The two digit year is there because an episode list without one is genuinely
                // ambiguous: One Piece is a fifty episode row of "27 Sep" values across four
                // different years, and a two digit year costs four characters to disambiguate
                // them. Rendered as 25 rather than 2025 to keep the card from wrapping.
                return $"{local.Day} {InvariantMonths[local.Month - 1]} {local.Year % 100:00}";
            }
        }

        private static readonly string[] InvariantMonths =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        [JsonIgnore]
        public bool HasAiredLabel => AiredDate.HasValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseAiredLabelChanged()
        {
            OnPropertyChanged(nameof(AiredLabel));
            OnPropertyChanged(nameof(HasAiredLabel));
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
