using System;
using System.Globalization;
using System.Threading.Tasks;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;
using Microsoft.Maui.ApplicationModel;

namespace MALPlus.Views;

[QueryProperty(nameof(TopicId), "id")]
public partial class ForumsTopicPage : ContentPage, ForumTopicViewModel.IScrollInfoProvider,
    System.ComponentModel.INotifyPropertyChanged
{
    private bool _initialized;
    private bool _handlerAttached;
    private bool _replyPreviewVisible;
    public string TopicId { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    ///     The new-topic composer could preview its BBCode, the reply box could not. Same
    ///     conversion, so the preview matches what the board will render.
    /// </summary>
    public bool ReplyPreviewVisible
    {
        get => _replyPreviewVisible;
        private set
        {
            _replyPreviewVisible = value;
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(ReplyPreviewVisible)));
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(ReplyPreviewToggleText)));
        }
    }

    public string ReplyPreviewToggleText =>
        _replyPreviewVisible ? "Hide preview" : "Preview BBCode";

    private ForumTopicViewModel Vm => BindingContext as ForumTopicViewModel;

    public ForumsTopicPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ForumsTopic;
        if (Vm != null)
            Vm.ScrollInfoProvider = this;
    }

    private async void OnReplyPreviewClicked(object sender, EventArgs e)
    {
        var message = Vm?.ReplyMessage;
        ReplyPreviewVisible = !ReplyPreviewVisible;
        if (!ReplyPreviewVisible || string.IsNullOrWhiteSpace(message))
            return;

        string body;
        try
        {
            body = MALPlus.BBCode.BBCode.ToHtml(message);
        }
        catch (Exception)
        {
            body = System.Net.WebUtility.HtmlEncode(message);
        }

        // Same document shape as the topic body: MAL's inline colours are black, which is
        // invisible on the dark sheet, so the palette is forced back on.
        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<base href=\"https://myanimelist.net/\"><style>" +
            "html,body{margin:0;padding:0;background:transparent;color:#d4e4f7;}" +
            "body{font-family:Inter,Arial,sans-serif;font-size:13px;line-height:1.5;word-wrap:break-word;}" +
            ".message-content *{color:#d4e4f7 !important;}.message-content a{color:#4da3ff !important;}" +
            ".message-content img{max-width:100%;height:auto;}" +
            ".message-content blockquote{border-left:3px solid #0066ff;background:#10283d;margin:8px 0;padding:6px 10px;}" +
            ".message-content blockquote *{color:#b9cde0 !important;}" +
            ".message-content p{margin:0 0 6px;}.message-content p:last-child{margin-bottom:0;}" +
            ".message-content code,.message-content pre{font-family:monospace;white-space:pre-wrap;}" +
            "</style></head><body><div class=\"message-content\">" + body + "</div></body></html>";

        try
        {
            // The MAUI WebView exposes no LoadDataWithBaseURL, so this goes through the platform
            // view. The base URL is what relative links and inline images resolve against, and
            // it has to be myanimelist.net for the same reason the video overlay uses it.
            if (ReplyPreviewWebView.Handler?.PlatformView is global::Android.Webkit.WebView native)
                native.LoadDataWithBaseURL("https://myanimelist.net", html, "text/html", "UTF-8", null);
            else
                ReplyPreviewWebView.Source = new HtmlWebViewSource {Html = html};
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS reply preview failed: " + ex.GetType().Name);
        }
    }

    private void OnReplyPreviewNavigated(object sender, WebNavigatedEventArgs e)
    {
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Vm == null)
            return;
        Vm.PropertyChanged -= OnVmPropertyChanged;
        Vm.PropertyChanged += OnVmPropertyChanged;
#if ANDROID
        if (!_handlerAttached)
        {
            TopicWebView.HandlerChanged += OnTopicWebViewHandlerChanged;
            _handlerAttached = true;
        }
        ConfigureTopicWebView(TopicWebView?.Handler?.PlatformView as global::Android.Views.View);
#endif
        if (_initialized || string.IsNullOrWhiteSpace(TopicId))
            return;
        _initialized = true;
        try
        {
            await Vm.Init(new ForumsTopicNavigationArgs(TopicId, null, 1));
            await UpdateTopicWebViewHeightAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ForumsTopicPage Init failed: " + ex.Message);
        }
    }

    protected override void OnDisappearing()
    {
        if (Vm != null)
            Vm.PropertyChanged -= OnVmPropertyChanged;
#if ANDROID
        if (_handlerAttached)
        {
            TopicWebView.HandlerChanged -= OnTopicWebViewHandlerChanged;
            _handlerAttached = false;
        }
#endif
        base.OnDisappearing();
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        if (Vm == null)
            return;
        try
        {
            await Vm.ReloadAsync();
            await UpdateTopicWebViewHeightAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ForumsTopicPage refresh failed: " + ex.Message);
        }
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ForumTopicViewModel.PageHtml))
            return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (TopicWebView == null)
                return;
            TopicWebView.HeightRequest = 1;
            _ = UpdateTopicWebViewHeightAsync();
        });
    }

    private async void OnTopicWebViewNavigated(object sender, WebNavigatedEventArgs e)
    {
        await UpdateTopicWebViewHeightAsync();
    }

    private async Task UpdateTopicWebViewHeightAsync()
    {
        try
        {
            var result = await MainThread.InvokeOnMainThreadAsync(() =>
                TopicWebView.EvaluateJavaScriptAsync(
                    "Math.max(1, Math.ceil(Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0, document.body ? document.body.getBoundingClientRect().height : 0)));"));
            if (!double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                return;
            var targetHeight = Math.Max(1, Math.Ceiling(height) + 4);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TopicWebView.HeightRequest = targetHeight;
            });
            // Images inside posts finish loading after the first pass and grow the
            // document, so measure again once they have settled.
            await Task.Delay(350);
            var second = await MainThread.InvokeOnMainThreadAsync(() =>
                TopicWebView.EvaluateJavaScriptAsync(
                    "Math.max(1, Math.ceil(Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0)));"));
            if (double.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out var secondHeight) &&
                secondHeight > targetHeight - 4)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TopicWebView.HeightRequest = Math.Max(1, Math.Ceiling(secondHeight) + 4);
                });
            }
        }
        catch { }
    }

#if ANDROID
    private void OnTopicWebViewHandlerChanged(object sender, EventArgs e)
    {
        ConfigureTopicWebView(TopicWebView?.Handler?.PlatformView as global::Android.Views.View);
    }

    private static void ConfigureTopicWebView(global::Android.Views.View platformView)
    {
        if (platformView is not global::Android.Webkit.WebView webView)
            return;
        try
        {
            webView.NestedScrollingEnabled = false;
            webView.VerticalScrollBarEnabled = false;
            webView.HorizontalScrollBarEnabled = false;
            webView.OverScrollMode = global::Android.Views.OverScrollMode.Never;
            webView.Settings.JavaScriptEnabled = true;
            webView.Settings.DomStorageEnabled = true;
            webView.Settings.BuiltInZoomControls = false;
            webView.Settings.DisplayZoomControls = false;
        }
        catch { }
    }
#endif

    public int GetFirstVisibleItemIndex()
    {
        return 0;
    }
}
