using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;
using Microsoft.Maui.ApplicationModel;
using MALPlus.Services;

namespace MALPlus.Views;

public partial class VideosPage : ContentPage
{
    private bool _initialized;
    private bool _overlayVisible;
    private PopularVideosViewModel Vm => (PopularVideosViewModel)BindingContext;

    public bool VideoOverlayVisibility
    {
        get => _overlayVisible;
        set { _overlayVisible = value; OnPropertyChanged(); }
    }

    public VideosPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.PopularVideos;
#if ANDROID
        VideoWebView.HandlerChanged += OnVideoWebViewHandlerChanged;
#endif
    }

#if ANDROID
    private void OnVideoWebViewHandlerChanged(object sender, EventArgs e)
    {
        try
        {
            var pv = VideoWebView?.Handler?.PlatformView as global::Android.Views.View;
            if (pv != null)
            {
                MALPlus.Services.VideoWebViewHelper.ConfigurePlatformView(pv);
                MALPlus.Services.VideoWebViewHelper.Resume(pv);
            }
        }
        catch { }
    }

    private void ResumeVideoWebView()
    {
        try
        {
            var pv = VideoWebView?.Handler?.PlatformView as global::Android.Views.View;
            if (pv != null) MALPlus.Services.VideoWebViewHelper.Resume(pv);
        }
        catch { }
    }

    private static void SetSystemBars(bool video)
    {
        try
        {
            var window = Platform.CurrentActivity?.Window;
            if (window == null) return;
            window.SetStatusBarColor(video
                ? global::Android.Graphics.Color.Black
                : global::Android.Graphics.Color.ParseColor("#051522"));
            window.SetNavigationBarColor(global::Android.Graphics.Color.Black);
        }
        catch { }
    }
#else
    private void ResumeVideoWebView() { }
    private static void SetSystemBars(bool video) { }
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        try
        {
            Vm.Init();
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                // Watches the page-in-flight flag rather than Loading. Polling Loading raced the
                // RefreshView writing it back to true, and the loop then waited on a spinner nobody
                // was ever going to clear.
                if (!Vm.Loading && Vm.HasLoadedOnce)
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS VideosPage Init failed: " + ex.Message);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        // Refreshing is set here rather than trusting the binding: RefreshView has already written true
        // into it to start its own spinner, and clearing it from the page is what keeps the two from
        // disagreeing. The gesture itself drives the reload; this only ends the gesture.
        try { Vm.Refreshing = true; Vm.Init(); } catch { }
    }

    /// <summary>
    ///     Asks for the next page when the reader nears the end of the list.
    /// </summary>
    private async void OnLoadMore(object sender, EventArgs e)
    {
        try
        {
            await Vm.LoadMore();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "load more failed: " + ex.GetType().Name);
        }
    }

    private void OnVideoTapped(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is AnimeVideoData video && !string.IsNullOrEmpty(video.YtLink))
            {
                ((CollectionView)sender).SelectedItem = null;
                ResumeVideoWebView();
                VideoWebView.Source = MALPlus.Services.VideoWebViewHelper.BuildSource(video.YtLink);
                // MAUI re-applies MediaPlaybackRequiresUserGesture on every source update and puts
                // it back to true, so the media element would never start without this.
                MALPlus.Services.VideoWebViewHelper.ApplyMediaSettings(VideoWebView);
MALPlus.Services.VideoWebViewHelper.BackHandler = OnVideoBackPressed;
                  // Registered here rather than once at startup: OnBackPressedDispatcher runs the
                  // most recently added callback first and the Shell adds its own after
                  // MainActivity.OnCreate, so a callback added back then never sees the press.
                  MainActivity.ArmVideoBackCallback();
                  SetSystemBars(true);
                VideoOverlayVisibility = true;
                VideoOverlay.IsVisible = true;
                // Opening in portrait too: the bars are visible and reserve space of their own.
                VideoOverlayInsets.Apply(FullscreenButton, VideoCloseButton);
                VideoListRoot.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS video play failed: " + ex.Message);
        }
    }

private void OnCloseVideo(object sender, EventArgs e)
    {
        try
        {
            // Leaving full screen belongs to closing: an overlay that went away while the activity
            // was still landscape left the whole app sideways.
            FullscreenVideo.Exit();
MALPlus.Services.VideoWebViewHelper.BackHandler = null;
              MainActivity.DisarmVideoBackCallback();
              VideoWebView.Source = null;
            VideoOverlay.IsVisible = false;
            VideoOverlayVisibility = false;
            VideoListRoot.IsVisible = true;
            SetSystemBars(false);
        }
        catch { }
    }

    /// <summary>
    ///     Toggles full screen and swaps the glyph so the button says what it will do next.
    /// </summary>
    private void OnToggleFullscreen(object sender, TappedEventArgs e)
    {
        try
        {
            if (FullscreenVideo.IsActive)
                FullscreenVideo.Exit();
            else
                FullscreenVideo.Enter();
            if (FullscreenIcon != null)
                FullscreenIcon.Source = FullscreenVideo.IsActive ? "icon_exit_fullscreen" : "icon_fullscreen";
                // The cutout moves onto the long edge when the video rotates to landscape, so
                // the buttons are re-inset after the change rather than before it.
                Device.BeginInvokeOnMainThread(() =>
                    VideoOverlayInsets.Apply(FullscreenButton, VideoCloseButton));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Fullscreen toggle failed: " + ex.GetType().Name);
        }
    }


    /// <summary>
    ///     Back gesture while the player is up drops it and stays on the promo list instead of
    ///     popping the page. Routed through MainActivity.OnBackPressed: MAUI 7 has no
    ///     Page.BackButtonPressed and the WebView child swallows a swipe recognizer.
    /// </summary>
    private bool OnVideoBackPressed()
    {
        if (VideoOverlay == null || !VideoOverlay.IsVisible)
            return false;
        OnCloseVideo(this, EventArgs.Empty);
        return true;
    }
}
