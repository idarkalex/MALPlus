using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.Services;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using Microsoft.Maui.Controls;

namespace MALPlus.Views;

[QueryProperty(nameof(RouteYear), "year")]
[QueryProperty(nameof(RouteSeason), "season")]
public partial class SeasonThemesPage : ContentPage, INotifyPropertyChanged
{
    /// <summary>
    ///     Raised when a property this page binds changes.
    /// </summary>
    /// <remarks>
    ///     Declared and raised by hand rather than through BindableObject.OnPropertyChanged, which does
    ///     nothing at all for an ordinary property: it only notifies for something registered as a
    ///     BindableProperty. The list loaded its rows, the count went up, and the CollectionView stayed
    ///     invisible because the one binding that mattered was never notified of anything.
    /// </remarks>
    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _loading;
    private WebView _videoWebView;
    private ICommand _themeRowCommand;

    public SeasonThemesPage()
    {
        InitializeComponent();
        BindingContext = this;
        RowsList.ItemsSource = Rows;
    }

    public ObservableCollection<SeasonThemeRow> Rows { get; } = new ObservableCollection<SeasonThemeRow>();

    public bool IsListVisible { get; private set; }

    public int RouteYear { get; set; }

    public string RouteSeason { get; set; }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        VideoWebViewHelper.PlaybackError -= OnVideoPlaybackError;
        VideoWebViewHelper.PlaybackStarted -= OnVideoPlaybackStarted;
        // Armed when the page appeared, so it has to be released when it goes: a callback left
        // registered would keep answering presses for a page nobody is looking at.
        MainActivity.DisarmMenuBackCallback();
        MainActivity.DisarmVideoBackCallback();
        // Same reasoning as closing the overlay by hand: a page that goes away still sideways stays
        // sideways, and the next page opens looking broken.
        FullscreenVideo.Exit();
        StopDirectMedia();
    }

    /// <summary>
    ///     Whether the route has been applied yet.
    /// </summary>
    /// <remarks>
    ///     Once, and only once. OnAppearing fires again every time this page comes back to the front,
    ///     and choosing a season pushes a modal page to ask: dismissing it raises OnAppearing here, so
    ///     re-applying the route on every appearance put the season the page was opened with back over
    ///     the one the reader had just picked. The reload that followed showed the original season and
    ///     the choice appeared to do nothing at all.
    /// </remarks>
    private bool _routeApplied;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Static events, so attached here and released on disappearing: a page that subscribed once
        // would go on answering for every overlay opened after it.
        VideoWebViewHelper.PlaybackError += OnVideoPlaybackError;
        VideoWebViewHelper.PlaybackStarted += OnVideoPlaybackStarted;

        if (_loading || _routeApplied)
            return;
        _routeApplied = true;
        try
        {
            // Detached BEFORE the route is applied, not after. SeasonSelector's Season and Year setters
            // both raise SelectionChanged, so applying the route while the handler was still attached
            // fired it once per assignment and each of those started a load of its own. That is three
            // concurrent loads of the same season, each clearing and refilling Rows and each asking
            // AnimeThemes for every theme in the season: on a cold start one of the three came back
            // without themes, and because the panel only draws the rows that have any, whichever landed
            // last decided what the reader saw. Coming back a minute later worked because the response
            // was cached by then and all three returned the same thing.
            SeasonSelector.SelectionChanged -= OnSelectionChanged;

            // The route names what it wants. Arriving with nothing means whatever is airing now, which
            // is what the selector paints on its first appearance, so the route only overrides when it
            // actually says something.
            if (!string.IsNullOrWhiteSpace(RouteSeason))
                SeasonSelector.Season = Parse(RouteSeason);
            if (RouteYear > 0)
                SeasonSelector.Year = RouteYear;

            await LoadAsync(SeasonSelector.Year, SeasonSelector.Season);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "load: " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            SeasonSelector.SelectionChanged += OnSelectionChanged;
            // Back leaves THIS page. The Shell's own handler treated the whole section as the thing to
            // leave, which dropped the reader back at the Discover hub instead of out of the season
            // they were reading, so the press is taken over for as long as the page is on screen.
            MainActivity.ArmMenuBackCallback(() => _ = Shell.Current.GoToAsync(".."));
        }
    }

    private static Season Parse(string season)
        => season?.Trim().ToLowerInvariant() switch
        {
            "winter" => Season.Winter,
            "spring" => Season.Spring,
            "summer" => Season.Summer,
            _ => Season.Fall
        };

    private async Task LoadAsync(int year, Season season)
    {
        // One load at a time. Anything else lets two of them run Rows.Clear() against each other and the
        // panel ends up showing whichever finished last, which is how the first opening came up empty:
        // the selector fired two loads of its own before this one even started.
        if (_loading)
            return;
        _loading = true;

        Busy.IsRunning = true;
        EmptyLabel.IsVisible = false;
        IsListVisible = false;
        Raise(nameof(IsListVisible));

        try
        {
            var rows = await SeasonThemesQuery.GetSeasonRowsAsync(year, SeasonYearSelector.SeasonName(season));

            // Only the entries that actually have themes. This panel is the season's OP/ED index and
            // nothing else: the season's full list of anime already lives on the Seasonal tab, and
            // keeping an entry here with no themes beside it would be the same list twice.
            Rows.Clear();
            foreach (var row in rows.Where(row => row.Themes.Count > 0))
                Rows.Add(row);
        }
        catch (Exception ex)
        {
            // Never a blank page. A load that dies here used to leave a spinner turning forever and
            // nothing else, which is indistinguishable on screen from having no data at all, so the
            // message goes where the reader is looking.
            global::Android.Util.Log.Warn("MALPlus SeasonThemes",
                "rows failed: " + ex.GetType().Name + " " + ex.Message);
            Rows.Clear();
            EmptyLabel.Text = "Could not load this season: " + ex.Message;
            EmptyLabel.IsVisible = true;
        }

        EmptyLabel.IsVisible = Rows.Count == 0 && EmptyLabel.Text == "Nothing for this season.";
        IsListVisible = Rows.Count > 0;
        Raise(nameof(IsListVisible));
        Busy.IsRunning = false;
        _loading = false;
    }

    public ICommand ThemeRowCommand => _themeRowCommand ?? (_themeRowCommand =
        new Command<AnimeThemeRow>(async theme => await PlayThemeAsync(theme)));

    /// <summary>
    ///     Plays one OP or one ED, by the chain the details page uses.
    /// </summary>
    /// <remarks>
    ///     The video came with the row, so that is the first thing tried and nothing is spent looking
    ///     it up. Only a row that arrived without one goes looking, and then it goes exactly as far as
    ///     details goes: AnimeThemes by title, then a YouTube search, and failing both the YouTube app
    ///     so the query at least stays usable.
    /// </remarks>
    private async Task PlayThemeAsync(AnimeThemeRow theme)
    {
        if (theme == null)
            return;
        try
        {
            // The video usually came with the row, and then nothing is spent looking it up.
            global::Android.Util.Log.Info("MALPlus SeasonThemes",
                $"play mal={theme.MalId} slot={theme.Slot} url={(string.IsNullOrEmpty(theme.VideoUrl) ? "-" : "si")} text={theme.Text}");

            if (!string.IsNullOrEmpty(theme.VideoUrl))
            {
                ShowVideo(theme.VideoUrl);
                return;
            }

            var url = await Task.Run(async () =>
            {
                // By the entry's own MAL id. The row already carries it, so there is no name to compare
                // and nothing that can drift between the two sources.
                var themes = await AnimeThemesHelper.GetThemesAsync(theme.MalId);
                var match = AnimeThemesHelper.FindMatch(themes, theme.IsOpening, theme.Sequence,
                    AnimeThemesHelper.BuildOpEdSearchQuery(theme.Text),
                    AnimeThemesHelper.ExtractOpEdSong(theme.Text), theme.Text);
                global::Android.Util.Log.Info("MALPlus SeasonThemes",
                    $"lookup mal={theme.MalId} themes={themes?.Count ?? 0} match={(string.IsNullOrEmpty(match?.Url) ? "no" : "si")}");

                if (!string.IsNullOrEmpty(match?.Url))
                    return match.Url;

                var videoId = await VideoWebViewHelper.SearchYouTubeVideoId(theme.Text);
                if (!string.IsNullOrEmpty(videoId))
                    return $"https://www.youtube.com/watch?v={videoId}";

                return null;
            });

            if (!string.IsNullOrEmpty(url))
            {
                ShowVideo(url);
                return;
            }

            await Launcher.Default.OpenAsync(new Uri("https://www.youtube.com/results?search_query=" +
                                                     System.Net.WebUtility.UrlEncode(theme.Text)));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "play: " + ex.GetType().Name);
        }
    }

    private void ShowVideo(string url)
    {
        if (VideoOverlay == null || string.IsNullOrWhiteSpace(url))
            return;

        VideoOverlay.IsVisible = true;
        // Only the close button is placed now. Full screen lives on the player's own control bar,
        // which the shared WebChromeClient answers for every video surface in the app.
        VideoOverlayInsets.Apply(null, CloseButton);
        VideoWebViewHelper.BackHandler = OnVideoBackPressed;
        MainActivity.ArmVideoBackCallback();

        // One player for everything, direct media included, on Chromium's native controls. The old
        // comment here claimed ExoPlayer gave "a real scrub bar" while the WebView could not, and that
        // was backwards: the hand written bar was a four pixel strip with an onclick and no thumb, which
        // is why it was unreadable and undraggable on touch. Native controls have the real bar, and the
        // origin sees the same kind of request from either player.
        StopDirectMedia();
        var webView = EnsureVideoWebView();
        webView.IsVisible = true;
        VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);

        // Only when this theme follows another one closely, which is what flipping down the list
        // looks like. Opening a single theme is never delayed.
        var delay = VideoWebViewHelper.BurstDelay();
        _currentVideoUrl = url;
        if (delay <= TimeSpan.Zero)
        {
            webView.Source = VideoWebViewHelper.BuildSource(url);
            VideoWebViewHelper.ApplyMediaSettings(webView);
            return;
        }

        _ = PlayAfterBurstAsync(url, delay);
    }

    private async Task PlayAfterBurstAsync(string url, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            if (VideoOverlay == null || !VideoOverlay.IsVisible || _currentVideoUrl != url)
                return;
            var webView = EnsureVideoWebView();
            webView.IsVisible = true;
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
            webView.Source = VideoWebViewHelper.BuildSource(url);
            VideoWebViewHelper.ApplyMediaSettings(webView);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "PlayAfterBurst: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     A theme was refused. Asked for again after a wait instead of being left dead on screen.
    /// </summary>
    private void OnVideoPlaybackError(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                url = _currentVideoUrl;
            if (string.IsNullOrWhiteSpace(url) || url != _currentVideoUrl)
                return;

            _videoRetry.NoteFailed(url);
            var delay = _videoRetry.DelayFor(url);
            if (!delay.HasValue)
            {
                global::Android.Util.Log.Warn("MALPlus SeasonThemes", "giving up on " + url);
                // Nothing is coming, so the spinner has to go rather than sit there claiming otherwise.
                OnCloseVideoTapped(this, EventArgs.Empty);
                return;
            }

            global::Android.Util.Log.Warn("MALPlus SeasonThemes",
                $"refused, retrying in {delay.Value.TotalSeconds}s: {url}");
            CancelPendingRetries();
            _retryTokenSource = new CancellationTokenSource();
            var token = _retryTokenSource.Token;
            _ = RetryVideoAsync(url, delay.Value, token);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "OnVideoPlaybackError: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Stops a retry that has not fired yet.
    /// </summary>
    /// <remarks>
    ///     Without this a retry scheduled for a theme the reader already left survives the close, waits
    ///     out its delay and then puts that theme back on screen by itself.
    /// </remarks>
    private void CancelPendingRetries()
    {
        try
        {
            _retryTokenSource?.Cancel();
            _retryTokenSource?.Dispose();
        }
        catch
        {
        }

        _retryTokenSource = null;
    }

    private CancellationTokenSource _retryTokenSource;

    private void OnVideoPlaybackStarted(string url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url) && url == _currentVideoUrl)
                _videoRetry.NotePlaying(url);
        }
        catch
        {
        }
    }

    private async Task RetryVideoAsync(string url, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            // The reader may have closed the overlay, moved to another theme or left the page while
            // this was waiting, so all of it has to still hold for the retry to be wanted.
            if (token.IsCancellationRequested || VideoOverlay == null || !VideoOverlay.IsVisible ||
                _currentVideoUrl != url)
                return;
            var webView = EnsureVideoWebView();
            webView.IsVisible = true;
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
            webView.Source = VideoWebViewHelper.BuildSource(url);
            VideoWebViewHelper.ApplyMediaSettings(webView);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "RetryVideo: " + ex.GetType().Name);
        }
    }

    private string _currentVideoUrl;

    private readonly VideoRetryPolicy _videoRetry = new();

    private string _directMediaUrl;

    private WebView EnsureVideoWebView()
    {
        if (_videoWebView != null)
            return _videoWebView;

        _videoWebView = new WebView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Black
        };
        VideoOverlay.Add(_videoWebView);
#if ANDROID
        _videoWebView.HandlerChanged += (s, e) =>
        {
            var pv = _videoWebView?.Handler?.PlatformView as global::Android.Views.View;
            if (pv != null)
                VideoWebViewHelper.ConfigurePlatformView(pv);
        };
        // Adding the view can make MAUI attach the handler right away, and HandlerChanged does not
        // fire again for a handler that is already there. Configuring only from that event is why the
        // player sometimes reported nothing at all: no logging and no error reporting, so a refused
        // theme looked like a frozen screen. Configuring here as well is harmless when the event still
        // arrives, and the only thing that works when it does not.
        try
        {
            VideoWebViewHelper.ConfigurePlatformView(
                _videoWebView.Handler?.PlatformView as global::Android.Views.View);
        }
        catch
        {
        }
#endif
        return _videoWebView;
    }

    private void StopDirectMedia()
    {
        // Closing ends any retry still waiting its turn, and forgets what was playing. A retry that
        // survived the close would wake up later and put that theme back on screen by itself, which is
        // what left the overlay showing again after the reader had already dismissed it.
        CancelPendingRetries();
        _currentVideoUrl = null;
        try
        {
            if (DirectVideoPlayer != null)
            {
                DirectVideoPlayer.Source = null;
                DirectVideoPlayer.IsVisible = false;
            }
            if (_videoWebView != null)
                _videoWebView.IsVisible = false;
        }
        catch
        {
        }
    }

    /// <summary>
    ///     ExoPlayer could not play the file. Asks the source again before treating it as final,
    ///     because the refusals here are a moment and not a verdict.
    /// </summary>
    /// <remarks>
    ///     The waits are seconds, not the 700/1400ms this first used: a refusal means the origin's
    ///     budget is spent, and an early retry spends it again.
    /// </remarks>
    private void OnDirectVideoFailed(object sender, string reason)
    {
        var url = _directMediaUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Only a refusal by the source is worth another attempt. A codec failure is the device's
        // answer, and asking it twice more will produce the same answer three times.
        //
        // Nothing falls back to the WebView player for a direct file. That fallback only ever fired on
        // refusals, which is what the retry below is for, and letting it through meant the old WebView
        // player and its white flash replacing playback the moment the source blinked.
        if (reason.IndexOf("BAD_HTTP_STATUS", StringComparison.Ordinal) >= 0 && RetryDirectMedia(url))
            return;

        global::Android.Util.Log.Warn("MALPlus SeasonThemes", "direct media failed: " + reason);
        _directMediaUrl = null;
        StopDirectMedia();
    }

    private readonly Dictionary<string, int> _directMediaRetries = new();

    /// <summary>
    ///     The URL a retry is waiting to serve. Kept apart from <c>_directMediaUrl</c> because stopping
    ///     the player clears that one, and the pending retry has to outlive the stop or it would cancel
    ///     itself before it ever fired.
    /// </summary>
    private string _pendingRetryUrl;

    private bool RetryDirectMedia(string url)
    {
        var attempts = _directMediaRetries.TryGetValue(url, out var seen) ? seen : 0;
        if (attempts >= 2)
            return false;
        _directMediaRetries[url] = attempts + 1;

        // Seconds, not the 700/1400ms this started with: a refusal means the origin's budget is spent,
        // and asking again inside that window spends it again.
        var wait = TimeSpan.FromSeconds(2 + 2 * attempts);
        _pendingRetryUrl = url;
        global::Android.Util.Log.Warn("MALPlus SeasonThemes",
            $"direct media retry {attempts + 1} in {wait.TotalSeconds}s: {url}");
        StopDirectMedia();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(wait);
            if (VideoOverlay == null || !VideoOverlay.IsVisible || _pendingRetryUrl != url)
                return;
            _pendingRetryUrl = null;
            _directMediaUrl = url;
            DirectVideoPlayer.IsVisible = true;
            DirectVideoPlayer.Source = url;
        });
        return true;
    }

    private void OnCloseVideoTapped(object sender, EventArgs e)
    {
        // Leaving full screen belongs to closing: an overlay that went away while the activity was
        // still landscape left the whole app sideways.
        FullscreenVideo.Exit();
        StopDirectMedia();
        // Source = null on its own leaves the media element running, so the overlay went away and the
        // audio carried on. StopMediaAndClear pauses it and navigates the WebView away.
        if (_videoWebView != null)
            VideoWebViewHelper.StopMediaAndClear(_videoWebView);
        VideoOverlay.IsVisible = false;
        VideoWebViewHelper.BackHandler = null;
        MainActivity.DisarmVideoBackCallback();
    }


    /// <summary>
    ///     The back gesture while a video is up: it closes the video, and only the video.
    /// </summary>
    /// <remarks>
    ///     Without this the press went to the Shell, which treated the whole page as the thing to
    ///     leave and dropped the reader out of the season instead of out of the video they were
    ///     watching. Returning true says the press was handled here and stops there.
    /// </remarks>
    private bool OnVideoBackPressed()
    {
        if (VideoOverlay == null || !VideoOverlay.IsVisible)
            return false;
        OnCloseVideoTapped(this, EventArgs.Empty);
        return true;
    }

    private void OnCloseTapped(object sender, EventArgs e)
    {
        _ = Shell.Current.GoToAsync("..");
    }

    /// <summary>
    ///     Tapping an entry opens it, like every other surface in the app.
    /// </summary>
    /// <remarks>
    ///     The row itself is the target and not just the poster, so the recogniser sits on the panel
    ///     rather than on any one part of it. It carries the row with it through CommandParameter,
    ///     because a recogniser with no parameter has nothing to act on.
    ///     <para>
    ///     A tap on a theme row is claimed by that row first: the recogniser on it is a child of this
    ///     one and Android delivers the touch to the deepest view that wants it, so opening the video
    ///     does not also open the entry.
    ///     </para>
    /// </remarks>
    private async void OnRowTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SeasonThemeRow row || row.Id <= 0)
            return;
        try
        {
            await Shell.Current.GoToAsync(
                $"animedetails?id={row.Id}&title={Uri.EscapeDataString(row.Title ?? string.Empty)}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus SeasonThemes", "row nav failed: " + ex.GetType().Name);
        }
    }

    private void OnSeasonRequested(object sender, EventArgs e)
    {
        // Labelled and kept in step with the value, so the answer comes back as a season rather than
        // as a string to be searched for in a list of labels.
        var choices = SeasonYearSelector.SeasonChoices(SeasonSelector.Season)
            .Select(choice => (
                Label: SeasonYearSelector.SeasonName(choice.Season) + (choice.IsNow ? " (Current)" : ""),
                choice.Season,
                choice.IsChosen))
            .ToList();
        _ = PickSeasonAsync(choices);
    }

    private async Task PickSeasonAsync(
        List<(string Label, Season Season, bool IsChosen)> choices)
    {
        var picked = await OptionsSheetHost.PickAsync("Season", null, choices.Select(choice =>
                new OptionsSheetItem(choice.Label, null, value: choice.IsChosen ? "shown" : null)).ToList(),
            null);

        var chosen = choices.FirstOrDefault(choice => choice.Label == picked);
        if (chosen != default)
            SeasonSelector.Season = chosen.Season;
    }

    private void OnYearRequested(object sender, EventArgs e)
    {
        _ = PickYearAsync();
    }

    private async Task PickYearAsync()
    {
        var years = await SeasonYearSelector.YearsAsync();
        var picked = await OptionsSheetHost.PickAsync("Year", null,
            years.Select(year => new OptionsSheetItem(year.ToString(), null,
                value: year == SeasonSelector.Year ? "shown" : null))
                .ToList(), null);
        if (int.TryParse(picked, out var year))
            SeasonSelector.Year = year;
    }

    private void OnSelectionChanged(object sender, EventArgs e)
    {
        _ = LoadAsync(SeasonSelector.Year, SeasonSelector.Season);
    }
}
