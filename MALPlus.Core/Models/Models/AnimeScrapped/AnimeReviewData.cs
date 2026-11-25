using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class ReviewScore
    {
        public string Field { get; set; }
        public string Score { get; set; }
    }

    public class AnimeReviewData : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private string _review;

        public string Id { get; set; }

        public string Review
        {
            get => _review;
            set
            {
                if (_review == value)
                    return;
                _review = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReviewToggleVisible));
            }
        }

        public string Author { get; set; }
        public string Date { get; set; }
        public string AuthorAvatar { get; set; }
        public string OverallRating { get; set; }
        public string EpisodesSeen { get; set; }
        public string HelpfulCount { get; set; }
        public bool HasSpoilers { get; set; }
        public bool IsPreliminary { get; set; }
        public List<ReviewScore> Score { get; set; } = new List<ReviewScore>();

        private const int ReviewCollapsedLines = 6;
        private const int ReviewCollapseThreshold = 220;

        public static bool ShouldShowReviewToggle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.Length > ReviewCollapseThreshold)
                return true;

            var lines = text.Split('\n');
            if (lines.Length > ReviewCollapsedLines)
                return true;

            int estimatedLines = 0;
            foreach (var line in lines)
            {
                estimatedLines += Math.Max(1, (int)Math.Ceiling(line.Length / 42.0));
                if (estimatedLines > ReviewCollapsedLines)
                    return true;
            }

            return false;
        }

        [Newtonsoft.Json.JsonIgnore]
        [JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReviewMaxLines));
                OnPropertyChanged(nameof(ReviewToggleText));
            }
        }

        [Newtonsoft.Json.JsonIgnore]
        [JsonIgnore]
        public int ReviewMaxLines => IsExpanded ? 10000 : ReviewCollapsedLines;

        [Newtonsoft.Json.JsonIgnore]
        [JsonIgnore]
        public string ReviewToggleText => IsExpanded ? "Show less" : "Show more";

        [Newtonsoft.Json.JsonIgnore]
        [JsonIgnore]
        public bool ReviewToggleVisible => ShouldShowReviewToggle(Review);

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
