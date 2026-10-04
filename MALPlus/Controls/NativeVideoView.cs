namespace MALPlus.Controls;

/// <summary>
///     A video surface backed by ExoPlayer.
/// </summary>
/// <remarks>
///     MAUI 11 RC1 removed the built in <c>MediaElement</c>: it is absent from
///     Microsoft.Maui.Controls on every target framework in the package, and there is no separate
///     media package to reference. ExoPlayer is still the engine that control used, so the only way
///     to get it is to host a PlayerView ourselves, which is what NativeVideoViewHandler does.
///
///     This exists for direct media only, meaning the AnimeThemes WebM files. YouTube never comes
///     here: a watch page or an embed is HTML with an iframe and a DRM script, which ExoPlayer
///     cannot load at all, so that stays on a WebView.
///
///     Properties are pushed by the handler, not two way bound, so there is nothing to keep in sync
///     by hand. <see cref="PlayFailed"/> exists so a codec the device cannot handle can hand the
///     file to another player instead of leaving a black rectangle.
/// </remarks>
public class NativeVideoView : View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(string),
        typeof(NativeVideoView),
        propertyChanged: (b, o, n) =>
        {
            var handler = ((NativeVideoView)b).Handler as NativeVideoViewHandler;
            if (handler != null)
                handler.UpdateSource(n as string);
        });

    /// <summary>URL of the media to play. Setting it loads immediately; setting null stops.</summary>
    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly BindableProperty IsPlayingProperty = BindableProperty.Create(
        nameof(IsPlaying),
        typeof(bool),
        typeof(NativeVideoView),
        false,
        propertyChanged: (b, o, n) =>
        {
            var handler = ((NativeVideoView)b).Handler as NativeVideoViewHandler;
            if (handler != null)
                handler.UpdateIsPlaying((bool)n);
        });

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted),
        typeof(bool),
        typeof(NativeVideoView),
        false,
        propertyChanged: (b, o, n) =>
        {
            var handler = ((NativeVideoView)b).Handler as NativeVideoViewHandler;
            if (handler != null)
                handler.UpdateIsMuted((bool)n);
        });

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>Raised when ExoPlayer cannot play the media, carrying the reason.</summary>
    public event EventHandler<string> PlayFailed;

    /// <summary>Raised once the media is ready enough to play.</summary>
    public event EventHandler MediaReady;

    public void RaisePlayFailed(string reason) => PlayFailed?.Invoke(this, reason);

    public void RaiseMediaReady() => MediaReady?.Invoke(this, EventArgs.Empty);
}