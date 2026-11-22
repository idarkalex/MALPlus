using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.BL
{
    public class ShareManager : IShareManager
    {
        private Timer _countdownTimer;

        private List<(ShareEvent e, AnimeShareDiff diff)> _lastEvents
            = new List<(ShareEvent e, AnimeShareDiff diff)>();

        private bool _shareTimerRunning;

        public event EventHandler<bool> TimerStateChanged;

        public bool ShareTimerRunning
        {
            get => _shareTimerRunning;
            set
            {
                _shareTimerRunning = value;
                TimerStateChanged?.Invoke(this,value);
            }
        }

        public void EnqueueEvent(ShareEvent action, AnimeShareDiff diff)
        {
            if(!Settings.EnableShareButton)
                return;

            if (_lastEvents.Any(tuple => tuple.diff.Id != diff.Id))
                _lastEvents.Clear();

            var entry = _lastEvents.FirstOrDefault(tuple => tuple.e == action);

            if (entry != default)
                _lastEvents.Remove(entry);

            switch (action)
            {
                case ShareEvent.AnimeScoreChanged when diff.NewScore == 0:
                case ShareEvent.AnimeEpisodesChanged when diff.NewEpisodes == 0:
                    return;
            }

            RestartTimer();

            _lastEvents.Add((action, diff));
        }

        public void Dismiss()
        {
            _lastEvents.Clear();
            StopTimer();
        }

        string IShareManager.GenerateMessage()
        {
            try
            {
                var msg = GenerateMessage();
                if (string.IsNullOrEmpty(msg))
                    return null;

                if (!string.IsNullOrWhiteSpace(Settings.ShareCustomSuffix))
                    return $"{msg}\n\n{Settings.ShareCustomSuffix.Trim()}";

                return msg;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string GenerateRecommendMessage(string title, int id, bool isAnime, float score, string type, int totalEpisodes)
        {
            try
            {
                var template = Settings.ShareTemplateRecommend;
                var url = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                var scoreStr = score > 0 ? score.ToString("0.00") : "N/A";
                var totalStr = totalEpisodes > 0 ? totalEpisodes.ToString() : "?";

                var msg = template
                    .Replace("{title}", title ?? string.Empty)
                    .Replace("{url}", url)
                    .Replace("{score}", scoreStr)
                    .Replace("{type}", type ?? (isAnime ? "Anime" : "Manga"))
                    .Replace("{total}", totalStr);

                if (!string.IsNullOrWhiteSpace(Settings.ShareCustomSuffix))
                    return $"{msg}\n\n{Settings.ShareCustomSuffix.Trim()}";

                return msg;
            }
            catch
            {
                return $"Check out {title} on MyAnimeList! https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
            }
        }

        public string GenerateMyEntryMessage(string title, int id, bool isAnime, string status, int myScore, int watched, int totalEpisodes)
        {
            try
            {
                var url = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                string template;
                if (status != null && status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    template = Settings.ShareTemplateCompleted;
                }
                else
                {
                    template = isAnime ? Settings.ShareTemplateWatched : Settings.ShareTemplateRead;
                }

                var msg = template
                    .Replace("{title}", title ?? string.Empty)
                    .Replace("{url}", url)
                    .Replace("{ep}", watched.ToString())
                    .Replace("{total}", totalEpisodes > 0 ? totalEpisodes.ToString() : "?")
                    .Replace("{status}", status ?? (isAnime ? "Watching" : "Reading"));

                msg = CleanScorePlaceholder(msg, myScore);

                if (!string.IsNullOrWhiteSpace(Settings.ShareCustomSuffix))
                    return $"{msg}\n\n{Settings.ShareCustomSuffix.Trim()}";

                return msg;
            }
            catch
            {
                var scorePart = myScore > 0 ? $" ({myScore}/10)" : string.Empty;
                return $"{title} - {status}{scorePart} https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
            }
        }

        private static string CleanScorePlaceholder(string text, int score)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (score > 0)
                return text.Replace("{score}", score.ToString());

            return text
                .Replace(" with score of {score}/10.", ".")
                .Replace(" with score of {score}/10", string.Empty)
                .Replace(" scored {score}/10.", ".")
                .Replace(" scored {score}/10", string.Empty)
                .Replace(" ({score}/10)", string.Empty)
                .Replace(" {score}/10", string.Empty)
                .Replace("{score}", string.Empty);
        }

        private string GenerateMessage()
        {
            var events = _lastEvents.Aggregate(ShareEvent.None, (t, x) => t | x.e);

            var epDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeEpisodesChanged);
            var scoreDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeScoreChanged);
            var statusDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeStatusChanged);
            var rewatchDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.StartedRewatching);

            _lastEvents.Clear();
            StopTimer();

            // 1. Completion (with or without score)
            if (statusDiff.diff != null && statusDiff.diff.NewStatus == AnimeStatus.Completed)
            {
                var template = Settings.ShareTemplateCompleted;
                var scoreVal = scoreDiff.diff != null ? scoreDiff.diff.NewScore : statusDiff.diff.NewScore;
                var msg = template
                    .Replace("{title}", statusDiff.diff.Title)
                    .Replace("{url}", statusDiff.diff.Url)
                    .Replace("{status}", "completed");

                return CleanScorePlaceholder(msg, scoreVal);
            }

            // 2. Both score and episodes changed
            if (events.HasFlag(ShareEvent.AnimeScoreChanged) &&
                events.HasFlag(ShareEvent.AnimeEpisodesChanged) && epDiff.diff != null && scoreDiff.diff != null)
            {
                var template = epDiff.diff.IsAnime ? Settings.ShareTemplateWatched : Settings.ShareTemplateRead;
                if (template.Contains("{ep}"))
                {
                    var msg = template
                        .Replace("{title}", epDiff.diff.Title)
                        .Replace("{url}", epDiff.diff.Url)
                        .Replace("{ep}", epDiff.diff.NewEpisodes.ToString())
                        .Replace("{total}", epDiff.diff.TotalEpisodes > 0 ? epDiff.diff.TotalEpisodes.ToString() : "?");

                    msg = CleanScorePlaceholder(msg, scoreDiff.diff.NewScore);

                    if (!template.Contains("{score}") && scoreDiff.diff.NewScore > 0)
                        msg = $"{msg} (Scored {scoreDiff.diff.NewScore}/10)";

                    return msg;
                }
                return
                    $"I've {WatchedOrRead()} {epDiff.diff.NewEpisodes}{FormatAllEpisodes(epDiff.diff.TotalEpisodes)} {EpisodesOrChapters()} of {epDiff.diff.Title} and scored it {scoreDiff.diff.NewScore}/10.\n" +
                    $"{epDiff.diff.Url}";
            }

            // 3. Episode progress changed
            if (events.HasFlag(ShareEvent.AnimeEpisodesChanged) && epDiff.diff != null)
            {
                var template = epDiff.diff.IsAnime ? Settings.ShareTemplateWatched : Settings.ShareTemplateRead;
                var msg = template
                    .Replace("{title}", epDiff.diff.Title)
                    .Replace("{url}", epDiff.diff.Url)
                    .Replace("{ep}", epDiff.diff.NewEpisodes.ToString())
                    .Replace("{total}", epDiff.diff.TotalEpisodes > 0 ? epDiff.diff.TotalEpisodes.ToString() : "?");

                return CleanScorePlaceholder(msg, epDiff.diff.NewScore);
            }

            if (events.HasFlag(ShareEvent.AnimeScoreChanged) &&
                events.HasFlag(ShareEvent.AnimeStatusChanged))
            {
                return
                    $"I've {FormatStatus(statusDiff.diff.NewStatus, statusDiff.diff.IsAnime)} {statusDiff.diff.Title} with score of {scoreDiff.diff.NewScore}/10.\n" +
                    $"{statusDiff.diff.Url}";
            }

            if (events.HasFlag(ShareEvent.AnimeStatusChanged))
            {
                return
                    $"I've {FormatStatus(statusDiff.diff.NewStatus, statusDiff.diff.IsAnime)} {statusDiff.diff.Title}.\n" +
                    $"{statusDiff.diff.Url}";
            }

            if (events.HasFlag(ShareEvent.AnimeScoreChanged))
            {
                return
                    $"I've scored {scoreDiff.diff.Title} {scoreDiff.diff.NewScore}/10.\n" +
                    $"{scoreDiff.diff.Url}";
            }

            if (events.HasFlag(ShareEvent.StartedRewatching))
            {
                return
                    $"I've started {ReWatchingOrRereading()} {rewatchDiff.diff.Title} \n" +
                    $"{rewatchDiff.diff.Url}";
            }

            if (events.HasFlag(ShareEvent.ChangedRewatchingCount))
            {
                return
                    $"I've {ReWatchedOrReread()} {rewatchDiff.diff.Title} {rewatchDiff.diff.RewatchCount} \n" +
                    $"{rewatchDiff.diff.Url}";
            }

            return null;

            string WatchedOrRead() => epDiff.diff.IsAnime ? "watched" : "read";
            string ReWatchingOrRereading() => rewatchDiff.diff.IsAnime ? "re-watching" : "re-reading";
            string ReWatchedOrReread() => rewatchDiff.diff.IsAnime ? "re-watched" : "re-reread";
            string EpisodesOrChapters() => epDiff.diff.IsAnime ? "episodes" :  (epDiff.diff.IsVolumes ? "volumes" : "chapters");
            string EpisodeOrChapter() => epDiff.diff.IsAnime ? "episode" : (epDiff.diff.IsVolumes ? "volume" : "chapter");
        }

        private void CountdownTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            _lastEvents.Clear();
            StopTimer();
        }

        private string FormatAllEpisodes(int eps) => eps <= 0 ? "" : $"/{eps.ToString()}";

        private string FormatEpisode(int ep)
        {
            switch (ep)
            {
                case 1:
                    return "1st";
                case 2:
                    return "2nd";
                case 3:
                    return "3rd";
                default:
                    return $"{ep}th";
            }
        }

        private string FormatStatus(AnimeStatus status, bool isAnime)
        {
            switch (status)
            {
                case AnimeStatus.Watching:
                    return isAnime ? "started watching" : "started reading";
                case AnimeStatus.Completed:
                    return "completed";
                case AnimeStatus.OnHold:
                    return "put on hold";
                case AnimeStatus.Dropped:
                    return "dropped";
                case AnimeStatus.PlanToWatch:
                    return isAnime ? "added to PTW" : "added to PTR";
            }

            return null;
        }


        private void RestartTimer()
        {
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer.Close();
                _countdownTimer = null;
            }

            var durationSeconds = Math.Max(5, Settings.ShareTimerDuration);
            _countdownTimer = new Timer(durationSeconds * 1000);
            _countdownTimer.Elapsed += CountdownTimerOnElapsed;
            _countdownTimer.Start();
            if(!ShareTimerRunning)
                ShareTimerRunning = true;
        }

        private void StopTimer()
        {
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer.Close();
                _countdownTimer = null;
                ShareTimerRunning = false;
            }
        }
    }
}
