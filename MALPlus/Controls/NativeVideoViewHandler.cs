using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.UI;
using Microsoft.Maui.Handlers;

namespace MALPlus.Controls;

/// <summary>
///     Hosts a PlayerView over an ExoPlayer instance.
/// </summary>
/// <remarks///     MAUI 11 RC1 removed the built in <c>MediaElement</c>: it is absent from
///     Microsoft.Maui.Controls on every target framework in the package, and there is no separate
///     media package to reference. ExoPlayer is the engine that control used, so hosting a
///     PlayerView directly is the only way to get it.
///
///     This exists for direct media only, meaning the AnimeThemes WebM files. YouTube never comes
///     here: a watch page or an embed is HTML with an iframe and a DRM script, which ExoPlayer
///     cannot load at all, so that stays on a WebView.
///
///     The difference from the WebView player it replaces is the whole point. The transport
///     controls are the platform's own, so seek is a real drag on the real timeline, play and
///     pause agree with what you can hear, and playback starts the moment the file is ready
///     instead of waiting on preload='auto' inside a document. Stopping is real as well: Stop
///     followed by Release is what guarantees the audio is gone, where unloading a WebView
///     document never did.
///
///     The player is built lazily. Every details page constructs this view even when nobody plays
///     anything, and an ExoPlayer that is created and thrown away costs a codec and a render
///     thread for nothing.
/// </remarks>
public class NativeVideoViewHandler : ViewHandler<NativeVideoView, FrameLayout>
{
    /// <summary>
    ///     Media3's <c>Player.STATE_READY</c>, which is 3. The binding does not surface that
    ///     constant anywhere reachable from here: it is not on <c>IPlayer</c>, and the Java
    ///     <c>PlaybackState</c> class is not bound at all in this version. It is spelled out once
    ///     here rather than guessed at every call site.
    /// </summary>
    private const int StateReady = 3;

    private IExoPlayer _player;
    private PlayerView _playerView;
    private string _loadingUrl;

    public static IPropertyMapper<NativeVideoView, NativeVideoViewHandler> Mapper =
        new PropertyMapper<NativeVideoView, NativeVideoViewHandler>(ViewMapper)
        {
            [nameof(NativeVideoView.Source)] = (h, v) => h.UpdateSource(v.Source),
            [nameof(NativeVideoView.IsPlaying)] = (h, v) => h.UpdateIsPlaying(v.IsPlaying),
            [nameof(NativeVideoView.IsMuted)] = (h, v) => h.UpdateIsMuted(v.IsMuted),
        };

    public NativeVideoViewHandler()
        : base(Mapper)
    {
    }

    protected override FrameLayout CreatePlatformView() =>
        // A FrameLayout rather than the PlayerView itself: the PlayerView is created and released
        // together with the player, so it cannot be the view the handler hands back for the whole
        // lifetime of the page.
        new(Context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
        };

    public void UpdateSource(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Release();
            return;
        }
        EnsurePlayer();
        _loadingUrl = url;
        _player.SetMediaItem(MediaItem.FromUri(url));
        _player.Prepare();
        // Prepare is asynchronous, so play straight away and let ExoPlayer start the moment it is
        // ready. Waiting for a ready callback first would add a round trip before any sound.
        _player.Play();
        VirtualView.IsPlaying = true;
        WatchForPlaybackStart(url);
    }

    /// <summary>
    ///     Reports whether ExoPlayer ever got the file off the ground, so the page can fall back to
    ///     the WebView player instead of leaving a black rectangle.
    ///     <para>
    ///     This polls <c>Error</c> and <c>PlaybackState</c> rather than registering an
    ///     <c>IPlayerListener</c>. That interface inherits <c>IJavaPeerable</c>, which drags in
    ///     Handle, JniIdentityHashCode, PeerReference, JniPeerMembers, JniManagedPeerState,
    ///     SetPeerReference, SetJniManagedPeerState, SetJniIdentityHashCode, Disposed, Finalized,
    ///     UnregisterFromRuntime, DisposeUnlessReferenced and Dispose, all of which are meaningless
    ///     for a listener that never crosses into Java. The binding's own base class for it,
    ///     IPlayerListenerImplementor, is internal and cannot be derived from here. Both properties
    ///     this needs are plain reads, and an unreadable WebM fails within a few hundred ms, so
    ///     polling gives the same answer without the JNI plumbing.
    ///     </para>
    /// </summary>
    private async void WatchForPlaybackStart(string url)
    {
        try
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                await Task.Delay(400);
                var player = _player;
                // The source changed or the player went away mid-wait: a different file owns the
                // outcome now, and this one must not report anything.
                if (player == null || _loadingUrl != url)
                    return;
                var error = player.PlayerError;
                if (error != null)
                {
                    var reason = error.ErrorCodeName + " " + error.Message;
                    global::Android.Util.Log.Warn("MALPlus Video", "ExoPlayer error: " + reason);
                    VirtualView?.RaisePlayFailed(reason);
                    return;
                }
                if (player.PlaybackState == StateReady)
                {
                    VirtualView?.RaiseMediaReady();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "WatchForPlaybackStart: " + ex.GetType().Name);
        }
    }

    public void UpdateIsPlaying(bool playing)
    {
        if (_player == null)
            return;
        if (playing)
            _player.Play();
        else
            _player.Pause();
    }

    public void UpdateIsMuted(bool muted)
    {
        // Volume is a property on IPlayer in this binding, there is no SetVolume.
        if (_player != null)
            _player.Volume = muted ? 0f : 1f;
    }

    private void EnsurePlayer()
    {
        if (_player != null)
            return;
        // The binding flattens Java's nested classes, so ExoPlayer.Builder arrives as
        // ExoPlayerBuilder. Looking for a type literally called ExoPlayer finds nothing.
        _player = new ExoPlayerBuilder(Context).Build();
        _playerView = new PlayerView(Context)
        {
            UseController = true,
            // The page draws its own chrome; the platform controller only has to be there.
            ControllerShowTimeoutMs = 2500,
            ContentDescription = "Video player",
        };
        _playerView.Player = _player;
        PlatformView.AddView(_playerView);
    }

    /// <summary>
    ///     Stops and releases ExoPlayer. Order matters: stop first so nothing is left decoding,
    ///     then release, which frees the codecs and the render thread. Skipping this is what left
    ///     the audio running behind the closed overlay.
    /// </summary>
    private void Release()
    {
        if (_player == null)
            return;
        try
        {
            _player.Stop();
            if (_playerView != null)
                _playerView.Player = null;
            _player.Release();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Release: " + ex.GetType().Name);
        }
        try
        {
            if (_playerView?.Parent is ViewGroup parent)
                parent.RemoveView(_playerView);
        }
        catch (Exception)
        {
        }
        _player = null;
        _playerView = null;
        _loadingUrl = null;
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        // Called when the page is torn down, so the player cannot outlive it holding a decoder.
        Release();
        base.DisconnectHandler(platformView);
    }
}