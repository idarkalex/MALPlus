using System;
using System.Globalization;
using System.Threading.Tasks;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

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
            Console.WriteLine("MALPLUS reply preview moved to ForumReplyPage: " + html.Length);
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
#if ANDROID
        // The Shell's own back handler pops the whole section instead of this page, so the press is
        // taken over while the page is on screen: close the reply overlay if open, otherwise pop one
        // page (to the board, or to the forum index when the topic was opened from there).
        ArmTopicBack();
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
            Vm.RegisterSelfBackNav();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ForumsTopicPage Init failed: " + ex.Message);
        }
    }

    protected override void OnDisappearing()
    {
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
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ForumsTopicPage refresh failed: " + ex.Message);
        }
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void OnPreviousPageTapped(object sender, TappedEventArgs e)
    {
        if (Vm?.CanPreviousPage == true)
            Vm.PreviousPageCommand?.Execute(null);
    }

    private void OnNextPageTapped(object sender, TappedEventArgs e)
    {
        if (Vm?.CanNextPage == true)
            Vm.NextPageCommand?.Execute(null);
    }

    private async void OnPageJumpTapped(object sender, TappedEventArgs e)
    {
        await PromptPageJumpAsync();
    }

    private async void OnPageChipClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button button && button.BindingContext is Tuple<int, bool> chip)
            {
                if (chip.Item1 > 0)
                {
                    Vm?.LoadPageCommand?.Execute(chip.Item1);
                    return;
                }

                await PromptPageJumpAsync();
                return;
            }

            await PromptPageJumpAsync();
        }
        catch
        {
        }
    }

    private async void OnPageJumpClicked(object sender, EventArgs e)
    {
        await PromptPageJumpAsync();
    }

    private async Task PromptPageJumpAsync()
    {
        var vm = Vm;
        if (vm?.CurrentTopicData == null)
            return;
        var result = await DisplayPromptAsync("Go to page",
            $"Enter a page between 1 and {vm.CurrentTopicData.AllPages}",
            keyboard: Keyboard.Numeric, accept: "Go", cancel: "Cancel");
        if (!string.IsNullOrWhiteSpace(result) && int.TryParse(result, out var page))
            vm.LoadPageCommand.Execute(page);
    }

    private async void OnOpenReplyPageTapped(object sender, EventArgs e)
    {
        try
        {
            if (Vm == null)
            {
                await DisplayAlert("Reply", "Vm not available", "OK");
                return;
            }
            Vm.ReplyMessage = Vm.ReplyMessage ?? string.Empty;
            ReplyOverlay.IsVisible = true;
            ReplyOverlayEditor.Text = Vm.ReplyMessage;
            await Task.Delay(1);
            ReplyOverlayEditor.Focus();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ForumsTopicPage: reply open failed: " + ex.GetType().Name);
        }
    }

    private void OnHideReplyOverlayTapped(object sender, EventArgs e)
    {
        ReplyOverlay.IsVisible = false;
    }

#if ANDROID
    private void ArmTopicBack()
    {
        global::MALPlus.MainActivity.ArmMenuBackCallback(() =>
        {
            if (ReplyOverlay.IsVisible)
            {
                ReplyOverlay.IsVisible = false;
                // The menu callback disables itself after this press; re-arm once it has finished
                // (posted, so the re-arm is not clobbered by that internal cleanup).
                MainThread.BeginInvokeOnMainThread(ArmTopicBack);
                return;
            }

            _ = Shell.Current.GoToAsync("..");
        });
    }
#endif

    protected override bool OnBackButtonPressed()
    {
        if (ReplyOverlay.IsVisible)
        {
            ReplyOverlay.IsVisible = false;
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnFormatBBCodeTapped(object sender, EventArgs e)
    {
        var tag = (sender as Button)?.CommandParameter as string;
        if (string.IsNullOrWhiteSpace(tag) || ReplyOverlayEditor == null)
            return;

        var text = ReplyOverlayEditor.Text ?? string.Empty;
        var start = Math.Max(0, Math.Min(ReplyOverlayEditor.CursorPosition, text.Length));
        var length = Math.Max(0, Math.Min(ReplyOverlayEditor.SelectionLength, text.Length - start));
        var openTag = $"[{tag}]";
        var closeTag = $"[/{tag}]";
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            text = text.Remove(start, length).Insert(start, openTag + selected + closeTag);
            ReplyOverlayEditor.Text = text;
            ReplyOverlayEditor.CursorPosition = start + openTag.Length + selected.Length + closeTag.Length;
        }
        else
        {
            text = text.Insert(start, openTag + closeTag);
            ReplyOverlayEditor.Text = text;
            ReplyOverlayEditor.CursorPosition = start + openTag.Length;
        }
    }

    private async void OnInsertUrlTapped(object sender, EventArgs e)
    {
        var url = await DisplayPromptAsync("URL", "Enter the URL", keyboard: Keyboard.Url);
        if (string.IsNullOrWhiteSpace(url))
            return;
        var editor = ReplyOverlayEditor;
        var start = Math.Max(0, Math.Min(editor.CursorPosition, editor.Text?.Length ?? 0));
        var length = Math.Max(0, Math.Min(editor.SelectionLength, (editor.Text?.Length ?? 0) - start));
        var text = editor.Text ?? string.Empty;
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            var replacement = $"[url={url}]{selected}[/url]";
            text = text.Remove(start, length).Insert(start, replacement);
            editor.Text = text;
            editor.CursorPosition = start + replacement.Length;
        }
        else
        {
            var replacement = $"[url={url}]Text[/url]";
            text = text.Insert(start, replacement);
            editor.Text = text;
            editor.CursorPosition = start + replacement.Length;
        }
    }

    private async void OnInsertImageTapped(object sender, EventArgs e)
    {
        var url = await DisplayPromptAsync("Image", "Enter image URL", keyboard: Keyboard.Url);
        if (string.IsNullOrWhiteSpace(url))
            return;
        InsertAtCursor($"[img]{url}[/img]");
    }

    private async void OnInsertQuoteTapped(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Quote", "Quote author (optional)", accept: "OK", cancel: "Cancel");
        var editor = ReplyOverlayEditor;
        var start = Math.Max(0, Math.Min(editor.CursorPosition, editor.Text?.Length ?? 0));
        var length = Math.Max(0, Math.Min(editor.SelectionLength, (editor.Text?.Length ?? 0) - start));
        var text = editor.Text ?? string.Empty;
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            var replacement = $"[quote={name} message=0]{selected}[/quote]";
            text = text.Remove(start, length).Insert(start, replacement);
            editor.Text = text;
            editor.CursorPosition = start + replacement.Length;
        }
        else
        {
            var replacement = $"[quote={name} message=0]Text[/quote]";
            text = text.Insert(start, replacement);
            editor.Text = text;
            editor.CursorPosition = start + replacement.Length;
        }
    }

    private void OnInsertListTapped(object sender, EventArgs e)
    {
        InsertAtCursor("[list]\n[*]Item\n[/list]");
    }

    private async void OnInsertColorTapped(object sender, EventArgs e)
    {
        var color = await DisplayPromptAsync("Color", "Enter color (red/blue/#00FF00)", accept: "OK", cancel: "Cancel");
        if (string.IsNullOrWhiteSpace(color))
            return;
        var start = Math.Max(0, Math.Min(ReplyOverlayEditor.CursorPosition, ReplyOverlayEditor.Text?.Length ?? 0));
        var length = Math.Max(0, Math.Min(ReplyOverlayEditor.SelectionLength, (ReplyOverlayEditor.Text?.Length ?? 0) - start));
        var text = ReplyOverlayEditor.Text ?? string.Empty;
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            text = text.Remove(start, length).Insert(start, $"[color={color}]{selected}[/color]");
            ReplyOverlayEditor.Text = text;
            ReplyOverlayEditor.CursorPosition = start + selected.Length + $"[color={color}]".Length + 8;
        }
        else
        {
            var replacement = $"[color={color}][/color]";
            text = text.Insert(start, replacement);
            ReplyOverlayEditor.Text = text;
            ReplyOverlayEditor.CursorPosition = start + replacement.Length - 9;
        }
    }

    private void InsertAtCursor(string content)
    {
        if (ReplyOverlayEditor == null)
            return;
        var text = ReplyOverlayEditor.Text ?? string.Empty;
        var start = Math.Max(0, Math.Min(ReplyOverlayEditor.CursorPosition, text.Length));
        text = text.Insert(start, content);
        ReplyOverlayEditor.Text = text;
        ReplyOverlayEditor.CursorPosition = start + content.Length;
    }

    private void OnReplyOverlayPreviewTapped(object sender, EventArgs e)
    {
        if (ReplyOverlayEditor == null || Vm == null)
            return;
        var html = MALPlus.BBCode.BBCode.ToHtml(ReplyOverlayEditor.Text ?? string.Empty);
        var body =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<base href=\"https://myanimelist.net/\"><style>" +
            "html,body{margin:0;padding:0;background:transparent;color:#d4e4f7;}" +
            "body{font-family:Inter,Arial,sans-serif;font-size:13px;line-height:1.5;word-wrap:break-word;}" +
            ".message-content *{color:#d4e4f7 !important;}.message-content a{color:#4da3ff !important;}" +
            ".message-content img{max-width:100%;height:auto;}" +
            ".message-content blockquote{border-left:3px solid #0066ff;background:#10283d;margin:8px 0;padding:6px 10px;}" +
            ".message-content p{margin:0 0 6px;}.message-content p:last-child{margin-bottom:0;}" +
            "</style></head><body><div class=\"message-content\">" + html + "</div></body></html>";
        try
        {
            if (ReplyOverlayPreview.Handler?.PlatformView is global::Android.Webkit.WebView native)
                native.LoadDataWithBaseURL("https://myanimelist.net", html, "text/html", "UTF-8", null);
            else
                ReplyOverlayPreview.Source = new HtmlWebViewSource { Html = body };
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS reply preview failed: " + ex.GetType().Name);
        }
        ReplyOverlayPreview.IsVisible = true;
    }

    private async void OnSendReplyOverlayTapped(object sender, EventArgs e)
    {
        if (Vm == null || ReplyOverlayEditor == null)
            return;
        if (Vm.CurrentTopicData?.Id == null && !string.IsNullOrWhiteSpace(TopicId))
        {
            await Vm.Init(new ForumsTopicNavigationArgs(TopicId, null, 1));
            Vm.RegisterSelfBackNav();
        }
        Vm.ReplyMessage = ReplyOverlayEditor.Text;
        if (await Vm.SendReplyAsync())
        {
            ReplyOverlay.IsVisible = false;
            if (ReplyOverlayEditor != null)
                ReplyOverlayEditor.Text = Vm.ReplyMessage;
        }
    }

    private void OnTopicWebViewNavigating(object sender, WebNavigatingEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(e.Url))
                return;

            if (e.Url.StartsWith("malplusquote:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var prefix = "malplusquote:";
                var rest = Uri.UnescapeDataString(e.Url.Substring(prefix.Length));
                var sep = rest.IndexOf('?');
                var id = sep >= 0 ? rest.Substring(0, sep) : rest;
                var user = "Unknown";
                if (sep >= 0)
                {
                    foreach (var pair in rest.Substring(sep + 1).Split('&'))
                    {
                        var kv = pair.Split('=', 2);
                        if (kv.Length == 2 && string.Equals(kv[0], "user", StringComparison.OrdinalIgnoreCase))
                            user = Uri.UnescapeDataString(kv[1]);
                    }
                }
                _ = Task.Run(async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        Vm?.QuouteMessage(id, user);
                        ReplyOverlay.IsVisible = true;
                        ReplyOverlayEditor.Text = Vm?.ReplyMessage;
                        ReplyOverlayEditor.Focus();
                    });
                });
                return;
            }

            if (e.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = Launcher.OpenAsync(new Uri(e.Url));
            }
        }
        catch
        {
        }
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
            webView.NestedScrollingEnabled = true;
            webView.VerticalScrollBarEnabled = false;
            webView.HorizontalScrollBarEnabled = false;
            webView.OverScrollMode = global::Android.Views.OverScrollMode.IfContentScrolls;
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
