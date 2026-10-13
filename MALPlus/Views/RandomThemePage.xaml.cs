using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MALPlus.Services;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;
using Microsoft.Maui.Controls;

namespace MALPlus.Views;

/// <summary>
///     One theme, shown with the entry it belongs to.
/// </summary>
/// <remarks>
///     Its own page rather than a row in the shared results list, because what is shown here is a single
///     thing with a video under it, and a list of one item is a list pretending there could have been
///     more. The hero is the details page's own so the entry reads as the entry, and its name opens the
///     entry, which is where the rest of it lives.
/// </remarks>
[QueryProperty(nameof(Slug), "slug")]
[QueryProperty(nameof(Anime), "anime")]
[QueryProperty(nameof(Song), "song")]
[QueryProperty(nameof(Slot), "slot")]
[QueryProperty(nameof(Url), "url")]
public partial class RandomThemePage : ContentPage, INotifyPropertyChanged
{
    /// <summary>
    ///     Raised when a property this page binds changes.
    /// </summary>
    /// <remarks>
    ///     Declared and raised by hand. BindableObject.OnPropertyChanged only notifies for something
    ///     registered as a BindableProperty, so calling it for an ordinary property does nothing at all:
    ///     the entry's details arrived, the poster URL was set, and the hero never repainted because the
    ///     binding was never told.
    /// </remarks>
    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private ICommand _themeRowCommand;

    public RandomThemePage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public string Slug { get; set; }

    public string Anime { get; set; }

    public string Song { get; set; }

    public string Slot { get; set; }

    public string Url { get; set; }

    public string AnimeName => string.IsNullOrWhiteSpace(Anime)
        ? (Slug ?? "Theme").Replace('_', ' ')
        : Anime;

    public string AnimeYear => _year > 0 ? _year.ToString() : string.Empty;

    public bool HasYear => _year > 0;

    public string AnimeSynopsis => _synopsis ?? string.Empty;

    public string MediaFormat => _mediaFormat ?? string.Empty;

    public bool HasMediaFormat => !string.IsNullOrWhiteSpace(_mediaFormat);

    public bool IsOpening => (Slot ?? string.Empty).StartsWith("OP", StringComparison.OrdinalIgnoreCase);

    public string SongLine
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Slot))
                parts.Add(Slot);
            if (!string.IsNullOrWhiteSpace(Song))
                parts.Add("\"" + Song + "\"");
            return parts.Count == 0 ? "Theme" : string.Join("  ", parts);
        }
    }

    private int _year;
    private string _synopsis;
    private string _mediaFormat;

    public string HeroImage { get; private set; }

    public ICommand ThemeRowCommand => _themeRowCommand ?? (_themeRowCommand =
        new Command(async () => await LookUpAsync()));

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        foreach (var name in new[] { nameof(AnimeName), nameof(AnimeYear), nameof(HasYear),
                    nameof(AnimeSynopsis), nameof(MediaFormat), nameof(HasMediaFormat),
                    nameof(IsOpening), nameof(SongLine) })
            Raise(name);

        // The player is attached here rather than configured in place. Configuring a WebView that does
        // not have a handler yet is silently a no-op, which is why this page's full screen button used
        // to do nothing: the chrome client that answers it was never installed.
        _player ??= new VideoPlayerSession(ThemeVideo, OnVideoBackPressed);
        _player.Attach();
        _player.OnPlaybackError(OnVideoPlaybackError);
        _player.OnPlaybackStarted(OnVideoPlaybackStarted);

        // The slug is the only thing the random theme carries about the entry, and it is not something
        // to show anybody. One request turns it into the name, the year, the synopsis and the format,
        // which is everything the hero puts on screen.
        await LoadAnimeAsync();

        // AnimeThemes only, and the video came with the theme. There is no fallback: a search would put
        // a different thing on screen under this one's name.
        if (string.IsNullOrWhiteSpace(Url))
        {
            VideoFailed.IsVisible = true;
            return;
        }

        VideoFrame.IsVisible = true;
        _currentVideoUrl = Url;
        _player.Open(Url);
    }

    private VideoPlayerSession _player;

    /// <summary>
    ///     Keeps the player box at 16:9, which is the shape of every one of these files.
    /// </summary>
    /// <remarks>
    ///     A fixed height could only be right for one screen width. This is a percent rather than a
    ///     number so it survives a font scale and a tablet, and it is set after the fact because a
    ///     binding cannot ask the layout how wide it ended up.
    /// </remarks>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (VideoFrame == null || width <= 0 || double.IsNaN(width))
            return;

        var wanted = width * 9d / 16d;
        if (double.IsNaN(VideoFrame.HeightRequest) || Math.Abs(VideoFrame.HeightRequest - wanted) > 0.5)
            VideoFrame.HeightRequest = wanted;
    }

    /// <summary>
    ///     Back gesture while a theme is playing: it closes the video and stays on the page instead of
    ///     leaving it.
    /// </summary>
    private bool OnVideoBackPressed()
    {
        if (VideoFrame == null || !VideoFrame.IsVisible)
            return false;
        OnCloseTapped(this, EventArgs.Empty);
        return true;
    }

    private string _currentVideoUrl;

    private readonly VideoRetryPolicy _videoRetry = new();

    /// <summary>
    ///     A theme was refused. Asked for again after a wait, so a temporary refusal from the origin
    ///     resolves itself instead of leaving an empty frame behind a random theme.
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
                VideoFailed.IsVisible = true;
                return;
            }

            global::Android.Util.Log.Warn("MALPlus Video",
                $"refused, retrying in {delay.Value.TotalSeconds}s: {url}");
            CancelPendingRetries();
            _retryTokenSource = new CancellationTokenSource();
            var token = _retryTokenSource.Token;
            _ = RetryVideoAsync(url, delay.Value, token);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "OnVideoPlaybackError: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Stops a retry that has not fired yet, which otherwise wakes up after the page was closed and
    ///     puts the theme back on screen.
    /// </summary>
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
            // The page may have been closed or moved on while this waited.
            if (token.IsCancellationRequested || _currentVideoUrl != url || VideoFrame == null ||
                !VideoFrame.IsVisible)
                return;
            // Opened through the session rather than by hand: the media settings have to be re-applied
            // after every source change or the element never starts, and that is the session's job now.
            _player.Open(url);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "RetryVideo: " + ex.GetType().Name);
        }
    }

    private async Task LoadAnimeAsync()
    {
        try
        {
            var json = await AnimeThemesHelper.GetRawAsync(
                $"https://api.animethemes.moe/anime/{Uri.EscapeDataString(Slug ?? string.Empty)}?include=resources");
            if (string.IsNullOrEmpty(json))
                return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var anime = doc.RootElement.TryGetProperty("anime", out var found) ? found : default;
            if (anime.ValueKind != System.Text.Json.JsonValueKind.Object)
                return;

            if (anime.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String)
                Anime = name.GetString();
            if (anime.TryGetProperty("year", out var year) && year.ValueKind == System.Text.Json.JsonValueKind.Number)
                _year = year.GetInt32();
            if (anime.TryGetProperty("synopsis", out var synopsis) && synopsis.ValueKind == System.Text.Json.JsonValueKind.String)
                _synopsis = synopsis.GetString();
            if (anime.TryGetProperty("media_format", out var format) && format.ValueKind == System.Text.Json.JsonValueKind.String)
                _mediaFormat = format.GetString();

            // The MAL id, read off the entry's own reference. The random theme hands over a slug and
            // nothing else, and until now the id was guessed by searching MAL for the name, which is
            // the one thing that differs between the two sources. With the id there is nothing to guess.
            if (anime.TryGetProperty("resources", out var resources) &&
                resources.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var resource in resources.EnumerateArray())
                {
                    var site = resource.TryGetProperty("site", out var siteProp) &&
                               siteProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? siteProp.GetString()
                        : string.Empty;
                    if (!string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (resource.TryGetProperty("external_id", out var idProp) &&
                        idProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        MalId = idProp.GetInt32();
                    break;
                }
            }

            foreach (var changed in new[] { nameof(AnimeName), nameof(AnimeYear), nameof(HasYear),
                        nameof(AnimeSynopsis), nameof(MediaFormat), nameof(HasMediaFormat) })
                Raise(changed);

            // The artwork is MAL's and it is read by the id, not by the name.
            HeroImage = string.Empty;
            if (MalId > 0)
            {
                try
                {
                    var details = await new MALPlus.XShared.Comm.Anime.AnimeGeneralDetailsQuery()
                        .GetAnimeDetails(false, MalId.ToString(), AnimeName, true);
                    HeroImage = details?.ImgUrl ?? string.Empty;
                }
                catch
                {
                }
            }

            Raise(nameof(HeroImage));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus RandomTheme", "anime: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Looks the theme up, for when nothing came with it.
    /// </summary>
    /// <remarks>
    ///     Same chain the details page uses, and the same one the season themes page uses: AnimeThemes
    ///     by MAL id first, then a YouTube search, and failing both the YouTube app. The chain ends
    ///     there because it has to end somewhere, and a query the reader can still use beats nothing.
    /// </remarks>
    private async Task LookUpAsync()
    {
        try
        {
            var url = await Task.Run(async () =>
            {
                var themes = await AnimeThemesHelper.GetThemesAsync(MalId);
                var match = AnimeThemesHelper.FindMatch(themes, IsOpening, 1,
                    AnimeThemesHelper.BuildOpEdSearchQuery(SongLine),
                    AnimeThemesHelper.ExtractOpEdSong(SongLine), SongLine);
                if (!string.IsNullOrEmpty(match?.Url))
                    return match.Url;

                var videoId = await VideoWebViewHelper.SearchYouTubeVideoId(SongLine);
                if (!string.IsNullOrEmpty(videoId))
                    return $"https://www.youtube.com/watch?v={videoId}";

                return null;
            });

            if (!string.IsNullOrEmpty(url))
            {
                VideoFrame.IsVisible = true;
                VideoFailed.IsVisible = false;
                _currentVideoUrl = url;
                _player.Open(url);
                return;
            }

            await Launcher.Default.OpenAsync(new Uri("https://www.youtube.com/results?search_query=" +
                                                    System.Net.WebUtility.UrlEncode(SongLine)));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus RandomTheme", "lookup: " + ex.GetType().Name);
        }
    }

    private void OnAnimeTapped(object sender, EventArgs e)
    {
        try
        {
            if (MalId <= 0)
                return;
            // The player stops with the page: a WebView left decoding behind a popped page keeps
            // spending on a surface nobody is looking at, and its audio keeps playing.
            _player?.Close();
            _ = Shell.Current.GoToAsync(
                $"animedetails?id={MalId}&title={Uri.EscapeDataString(AnimeName ?? string.Empty)}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus RandomTheme", "open: " + ex.GetType().Name);
        }
    }

    /// <summary>MAL id of the entry, 0 when the search never found one.</summary>
    public int MalId { get; private set; }

    private void OnCloseTapped(object sender, EventArgs e)
    {
        // Leaving the page ends any retry still waiting, or it would wake up afterwards and reopen the
        // theme the reader just closed.
        CancelPendingRetries();
        _player?.Close();
        _ = Shell.Current.GoToAsync("..");
    }

    /// <summary>
    ///     Releases the player when the page goes away without its close button having been used.
    /// </summary>
    /// <remarks>
    ///     The bottom navigation and a deep link both leave this page without going near
    ///     <see cref="OnCloseTapped" />, and a WebView left behind keeps decoding video and playing it
    ///     for as long as the process lives. Closing it here is what stops one visit from making the
    ///     next one slower. The static playback subscriptions go with it, or a page still in the
    ///     navigation stack keeps answering for a theme it is no longer showing.
    /// </remarks>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CancelPendingRetries();
        _player?.Close();
        _player?.Dispose();
    }
}
