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

        public const string AppWebsiteUrl = "https://malplus.vercel.app/";

        public static string FormatShareUrls(string malUrl)
        {
            if (string.IsNullOrWhiteSpace(malUrl))
                return AppWebsiteUrl;
            if (malUrl.Contains(AppWebsiteUrl))
                return malUrl;
            return $"{malUrl.Trim()}\n{AppWebsiteUrl}";
        }

        public static string AppendSuffix(string msg)
        {
            if (string.IsNullOrEmpty(msg))
                return msg;

            var suffix = !string.IsNullOrWhiteSpace(Settings.ShareCustomSuffix)
                ? Settings.ShareCustomSuffix.Trim()
                : Settings.DefaultShareCustomSuffix;

            if (!string.IsNullOrWhiteSpace(suffix) && !msg.Contains(suffix))
            {
                return $"{msg}\n\n{suffix}";
            }

            return msg;
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
                var msg = GenerateMessage(clear: true);
                if (string.IsNullOrEmpty(msg))
                    return null;

                return AppendSuffix(msg);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public ShareCardModel GetCurrentShareCardModel()
        {
            try
            {
                if (_lastEvents.Count == 0)
                    return null;

                var epDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeEpisodesChanged);
                var scoreDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeScoreChanged);
                var statusDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeStatusChanged);
                var rewatchDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.StartedRewatching || tuple.e == ShareEvent.ChangedRewatchingCount);

                var primaryTuple = _lastEvents.LastOrDefault();
                var primaryDiff = primaryTuple.diff;
                if (primaryDiff == null)
                    return null;

                var rawMsg = GenerateMessage(clear: false);
                var msg = AppendSuffix(rawMsg);

                string statusText = null;
                string progressText = null;

                if (statusDiff.diff != null)
                {
                    statusText = FormatStatus(statusDiff.diff.NewStatus, statusDiff.diff.IsAnime);
                }
                else if (rewatchDiff.diff != null)
                {
                    if (rewatchDiff.e == ShareEvent.StartedRewatching)
                        statusText = rewatchDiff.diff.IsAnime ? "Rewatching" : "Rereading";
                    else
                        statusText = $"Rewatched {rewatchDiff.diff.RewatchCount}×";
                }
                else if (epDiff.diff != null)
                {
                    statusText = epDiff.diff.IsAnime ? "Watching" : "Reading";
                }

                if (epDiff.diff != null)
                {
                    var eps = epDiff.diff.TotalEpisodes > 0 ? epDiff.diff.TotalEpisodes.ToString() : "?";
                    var unit = epDiff.diff.IsAnime ? "ep" : (epDiff.diff.IsVolumes ? "vol" : "ch");
                    progressText = $"{epDiff.diff.NewEpisodes} / {eps} {unit}";
                }
                else if (rewatchDiff.diff != null && rewatchDiff.diff.RewatchCount > 0)
                {
                    var times = rewatchDiff.diff.RewatchCount == 1 ? "time" : "times";
                    progressText = $"{rewatchDiff.diff.RewatchCount} {times}";
                }

                int score = scoreDiff.diff?.NewScore ?? statusDiff.diff?.NewScore ?? epDiff.diff?.NewScore ?? 0;

                int watched = epDiff.diff?.NewEpisodes 
                    ?? statusDiff.diff?.NewEpisodes 
                    ?? scoreDiff.diff?.NewEpisodes 
                    ?? primaryDiff.NewEpisodes;

                int total = epDiff.diff?.TotalEpisodes 
                    ?? statusDiff.diff?.TotalEpisodes 
                    ?? scoreDiff.diff?.TotalEpisodes 
                    ?? primaryDiff.TotalEpisodes;

                float globalScore = primaryDiff.GlobalScore;
                if (globalScore <= 0 && statusDiff.diff?.GlobalScore > 0)
                    globalScore = statusDiff.diff.GlobalScore;
                if (globalScore <= 0 && epDiff.diff?.GlobalScore > 0)
                    globalScore = epDiff.diff.GlobalScore;
                if (globalScore <= 0 && scoreDiff.diff?.GlobalScore > 0)
                    globalScore = scoreDiff.diff.GlobalScore;

                if (statusText != null && statusText.Equals("completed", StringComparison.OrdinalIgnoreCase) && total > 0 && watched == 0)
                {
                    watched = total;
                }

                return new ShareCardModel
                {
                    Id = primaryDiff.Id,
                    Title = primaryDiff.Title,
                    IsAnime = primaryDiff.IsAnime,
                    ImageUrl = primaryDiff.ImageUrl,
                    Type = primaryDiff.Type ?? (primaryDiff.IsAnime ? "Anime" : "Manga"),
                    SeasonTag = primaryDiff.SeasonTag,
                    StatusText = statusText,
                    ProgressText = progressText,
                    Score = score,
                    GlobalScore = globalScore,
                    WatchedEpisodes = watched,
                    TotalEpisodes = total,
                    RewatchCount = rewatchDiff.diff?.RewatchCount ?? 0,
                    ShareText = msg,
                    Url = primaryDiff.Url
                };
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
                var rawUrl = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                var url = FormatShareUrls(rawUrl);
                var scoreStr = score > 0 ? score.ToString("0.00") : "N/A";
                var totalStr = totalEpisodes > 0 ? totalEpisodes.ToString() : "?";

                var msg = template
                    .Replace("{title}", title ?? string.Empty)
                    .Replace("{url}", url)
                    .Replace("{score}", scoreStr)
                    .Replace("{type}", type ?? (isAnime ? "Anime" : "Manga"))
                    .Replace("{total}", totalStr);

                return AppendSuffix(msg);
            }
            catch
            {
                var rawUrl = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                return AppendSuffix($"Check out {title} on MyAnimeList! {FormatShareUrls(rawUrl)}");
            }
        }

        public string GenerateMyEntryMessage(string title, int id, bool isAnime, string status, int myScore, int watched, int totalEpisodes)
        {
            try
            {
                var rawUrl = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                var url = FormatShareUrls(rawUrl);
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

                return AppendSuffix(msg);
            }
            catch
            {
                var scorePart = myScore > 0 ? $" ({myScore}/10)" : string.Empty;
                var rawUrl = $"https://myanimelist.net/{(isAnime ? "anime" : "manga")}/{id}";
                return AppendSuffix($"{title} - {status}{scorePart} {FormatShareUrls(rawUrl)}");
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

        private string GenerateMessage(bool clear = true)
        {
            var events = _lastEvents.Aggregate(ShareEvent.None, (t, x) => t | x.e);

            var epDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeEpisodesChanged);
            var scoreDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeScoreChanged);
            var statusDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.AnimeStatusChanged);
            var rewatchDiff = _lastEvents.FirstOrDefault(tuple => tuple.e == ShareEvent.StartedRewatching || tuple.e == ShareEvent.ChangedRewatchingCount);

            if (clear)
            {
                _lastEvents.Clear();
                StopTimer();
            }

            // 1. Completion (with or without score)
            if (statusDiff.diff != null && statusDiff.diff.NewStatus == AnimeStatus.Completed)
            {
                var template = Settings.ShareTemplateCompleted;
                var scoreVal = scoreDiff.diff != null ? scoreDiff.diff.NewScore : statusDiff.diff.NewScore;
                var msg = template
                    .Replace("{title}", statusDiff.diff.Title)
                    .Replace("{url}", FormatShareUrls(statusDiff.diff.Url))
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
                        .Replace("{url}", FormatShareUrls(epDiff.diff.Url))
                        .Replace("{ep}", epDiff.diff.NewEpisodes.ToString())
                        .Replace("{total}", epDiff.diff.TotalEpisodes > 0 ? epDiff.diff.TotalEpisodes.ToString() : "?");

                    msg = CleanScorePlaceholder(msg, scoreDiff.diff.NewScore);

                    if (!template.Contains("{score}") && scoreDiff.diff.NewScore > 0)
                        msg = $"{msg} (Scored {scoreDiff.diff.NewScore}/10)";

                    return msg;
                }
                return
                    $"I've {WatchedOrRead()} {epDiff.diff.NewEpisodes}{FormatAllEpisodes(epDiff.diff.TotalEpisodes)} {EpisodesOrChapters()} of {epDiff.diff.Title} and scored it {scoreDiff.diff.NewScore}/10.\n" +
                    $"{FormatShareUrls(epDiff.diff.Url)}";
            }

            // 3. Episode progress changed
            if (events.HasFlag(ShareEvent.AnimeEpisodesChanged) && epDiff.diff != null)
            {
                var template = epDiff.diff.IsAnime ? Settings.ShareTemplateWatched : Settings.ShareTemplateRead;
                var msg = template
                    .Replace("{title}", epDiff.diff.Title)
                    .Replace("{url}", FormatShareUrls(epDiff.diff.Url))
                    .Replace("{ep}", epDiff.diff.NewEpisodes.ToString())
                    .Replace("{total}", epDiff.diff.TotalEpisodes > 0 ? epDiff.diff.TotalEpisodes.ToString() : "?");

                return CleanScorePlaceholder(msg, epDiff.diff.NewScore);
            }

            if (events.HasFlag(ShareEvent.AnimeScoreChanged) &&
                events.HasFlag(ShareEvent.AnimeStatusChanged))
            {
                return
                    $"I've {FormatStatus(statusDiff.diff.NewStatus, statusDiff.diff.IsAnime)} {statusDiff.diff.Title} with score of {scoreDiff.diff.NewScore}/10.\n" +
                    $"{FormatShareUrls(statusDiff.diff.Url)}";
            }

            if (events.HasFlag(ShareEvent.AnimeStatusChanged))
            {
                return
                    $"I've {FormatStatus(statusDiff.diff.NewStatus, statusDiff.diff.IsAnime)} {statusDiff.diff.Title}.\n" +
                    $"{FormatShareUrls(statusDiff.diff.Url)}";
            }

            if (events.HasFlag(ShareEvent.AnimeScoreChanged))
            {
                return
                    $"I've scored {scoreDiff.diff.Title} {scoreDiff.diff.NewScore}/10.\n" +
                    $"{FormatShareUrls(scoreDiff.diff.Url)}";
            }

            if (events.HasFlag(ShareEvent.StartedRewatching) && rewatchDiff.diff != null)
            {
                return
                    $"I've started {ReWatchingOrRereading()} {rewatchDiff.diff.Title}.\n" +
                    $"{FormatShareUrls(rewatchDiff.diff.Url)}";
            }

            if (events.HasFlag(ShareEvent.ChangedRewatchingCount) && rewatchDiff.diff != null)
            {
                var times = rewatchDiff.diff.RewatchCount == 1 ? "time" : "times";
                return
                    $"I've {ReWatchedOrReread()} {rewatchDiff.diff.Title} {rewatchDiff.diff.RewatchCount} {times}.\n" +
                    $"{FormatShareUrls(rewatchDiff.diff.Url)}";
            }

            return null;

            string WatchedOrRead() => epDiff.diff.IsAnime ? "watched" : "read";
            string ReWatchingOrRereading() => rewatchDiff.diff?.IsAnime == false ? "re-reading" : "re-watching";
            string ReWatchedOrReread() => rewatchDiff.diff?.IsAnime == false ? "re-read" : "re-watched";
            string EpisodesOrChapters() => epDiff.diff.IsAnime ? "episodes" :  (epDiff.diff.IsVolumes ? "volumes" : "chapters");
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
