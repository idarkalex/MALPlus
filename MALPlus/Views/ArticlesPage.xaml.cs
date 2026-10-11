using MALPlus.Models.Enums;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

[QueryProperty(nameof(InitialWorkMode), "mode")]
public partial class ArticlesPage : ContentPage
{
    private bool _initialized;
    private ArticlePageWorkMode _currentMode = ArticlePageWorkMode.Articles;
    public string InitialWorkMode { get; set; }

    private MalArticlesViewModel Vm => (MalArticlesViewModel)BindingContext;

    // EM theme CSS (Electric Midnight) — matches v2's "CssManager.GetArticleBody()".
    private const string EmCss = @"
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;
background:#051522;color:#d4e4f7;line-height:1.6;padding:16px;margin:0;font-size:14px;}
h1,h2,h3{color:#FFFFFF;font-weight:600;line-height:1.3;margin:18px 0 10px;}
h1{font-size:20px;} h2{font-size:17px;} h3{font-size:15px;}
p{margin:0 0 12px;}
a{color:#0066FF;text-decoration:none;} a:hover{text-decoration:underline;}
img{max-width:100%;height:auto;border-radius:6px;margin:8px 0;background:#000;}
blockquote{border-left:3px solid #0066FF;margin:12px 0;padding:8px 14px;color:#a8c5e0;background:rgba(0,102,255,0.05);}
cite{font-style:italic;color:#a8c5e0;}
ul,ol{margin:0 0 12px 22px;padding:0;}
li{margin:4px 0;}
table{border-collapse:collapse;width:auto;min-width:100%;max-width:none;table-layout:auto;margin:12px 0;font-size:12px;}
td,th{border:1px solid #1E3A52;padding:6px 8px;text-align:left;overflow-wrap:break-word;word-break:break-word;vertical-align:top;}
/* A wide table must scroll inside its own box. Squeezing it into the viewport with
   width:100% turned every extra column into one unreadable word per line, and the
   page itself cannot scroll sideways because of overflow-x:hidden below. */
.tw{overflow-x:auto;max-width:100%;-webkit-overflow-scrolling:touch;margin:12px 0;}
.tw table{margin:0;}
.unsupported{margin:12px 0;padding:12px 14px;border:1px solid #1E3A52;border-radius:8px;
            background:rgba(0,102,255,0.05);color:#a8c5e0;font-size:13px;}
th{background:#0d1d2c;color:#FFFFFF;}
/* Nothing may push the page sideways: wide media and long code lines have to
   fit the screen instead of forcing a horizontal scroll through the article.
   Words are only broken when a single one cannot fit, never mid-word. */
img,video,iframe,embed,object{max-width:100%!important;height:auto;}
html,body{max-width:100%;overflow-x:hidden;}
.intro,.meat{color:#d4e4f7;}
hr{border:none;border-top:1px solid #1E3A52;margin:18px 0;}
pre,code{background:#0a1d2e;padding:6px 10px;border-radius:4px;font-size:12px;color:#a8c5e0;white-space:pre-wrap;overflow-wrap:anywhere;}
";

    public ArticlesPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.MalArticles;
        Vm.OpenWebView += OnOpenWebView;
        NewsList.Scrolled += OnListScrolled;
    }

    // Static because the page is rebuilt every time it is opened: an instance field dies with the
    // page that set it, which is exactly why the position was never restored. The tab is kept
    // with it, because coming back to the flyout entry reopens whatever feed that entry points
    // at, and landing on a different feed than the one that was left is just as disorienting as
    // losing the place.
    private static int _savedScrollIndex = -1;
    private static ArticlePageWorkMode? _savedScrollMode;
    private static ArticlePageWorkMode? _lastVisitedMode;

    private int _firstVisibleIndex;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
        {
            // Reused instance: the list is still loaded, so only the position is missing.
            RestoreListScroll();
            return;
        }
        _initialized = true;
        try
        {
            var requested = ResolveWorkMode(InitialWorkMode);
            _currentMode = _lastVisitedMode ?? requested;
            _lastVisitedMode = _currentMode;
            if (_currentMode != requested)
                HighlightTab(_currentMode);
            Vm.Init(MakeArgs(_currentMode));
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.LoadingVisibility) break;
            }
            HighlightTab(_currentMode);
            RestoreListScroll();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ArticlesPage Init failed: " + ex.Message);
        }
    }

// Nothing to do here: navigating away through the flyout does not raise this event on the
    // page, which is why both the remembered feed and the place are kept as they change
    // instead. Kept because a shell back navigation does raise it, and then the mode is
    // remembered even if the reader never scrolled a single row.
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _lastVisitedMode = _currentMode;
        _savedScrollIndex = _firstVisibleIndex;
        _savedScrollMode = _currentMode;
    }

private void OnListScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (e.FirstVisibleItemIndex < 0)
            return;

        _firstVisibleIndex = e.FirstVisibleItemIndex;
        // Saved as it changes rather than on the way out: OnDisappearing is not raised when the
        // flyout navigates away from this page, so a save there never ran and the place was
        // always lost.
        _savedScrollIndex = e.FirstVisibleItemIndex;
        _savedScrollMode = _currentMode;
        _lastVisitedMode = _currentMode;
    }

private async void RestoreListScroll()
    {
        var target = _savedScrollIndex;
        if (target <= 0 || NewsList == null || _savedScrollMode != _currentMode)
            return;

        _savedScrollIndex = -1;
        try
        {
            // The collection has to be populated before an index means anything, and a scroll to
            // an item that is not there yet is a no-op, so it is retried for a few seconds.
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(250);
                var count = Vm?.Articles?.Count ?? 0;
                if (count == 0 || target >= count)
                    continue;
                NewsList.ScrollTo(target, position: ScrollToPosition.Start, animate: false);
                _firstVisibleIndex = target;
                return;
            }
        }
        catch (Exception)
        {
        }
    }

    private static ArticlePageWorkMode ResolveWorkMode(string s)
    {
        if (string.Equals(s, "News", StringComparison.OrdinalIgnoreCase)) return ArticlePageWorkMode.News;
        if (string.Equals(s, "AnnNews", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "ANN", StringComparison.OrdinalIgnoreCase)) return ArticlePageWorkMode.AnnNews;
        return ArticlePageWorkMode.Articles;
    }

    private static MalArticlesPageNavigationArgs MakeArgs(ArticlePageWorkMode mode) => new()
    {
        WorkMode = mode,
        Source = PageIndex.PageMore
    };

    private void OnWorkModeClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button b && b.CommandParameter is string s)
            {
                _currentMode = ResolveWorkMode(s);
                // A different feed starts at the top; keeping the old offset would land the
                // reader in the middle of an unrelated list.
                _firstVisibleIndex = 0;
                _savedScrollIndex = -1;
                _savedScrollMode = null;
                _lastVisitedMode = _currentMode;
                Vm.Init(MakeArgs(_currentMode), true);
                HighlightTab(_currentMode);
            }
        }
        catch { }
    }

    private void HighlightTab(ArticlePageWorkMode mode)
    {
        ArticlesTab.BackgroundColor = mode == ArticlePageWorkMode.Articles
            ? Color.FromArgb("#0066FF") : Colors.Transparent;
        ArticlesTab.TextColor = mode == ArticlePageWorkMode.Articles
            ? Colors.White : Color.FromArgb("#A0FFFFFF");
        NewsTab.BackgroundColor = mode == ArticlePageWorkMode.News
            ? Color.FromArgb("#0066FF") : Colors.Transparent;
        NewsTab.TextColor = mode == ArticlePageWorkMode.News
            ? Colors.White : Color.FromArgb("#A0FFFFFF");
        AnnTab.BackgroundColor = mode == ArticlePageWorkMode.AnnNews
            ? Color.FromArgb("#0066FF") : Colors.Transparent;
        AnnTab.TextColor = mode == ArticlePageWorkMode.AnnNews
            ? Colors.White : Color.FromArgb("#A0FFFFFF");
    }

    private void OnArticleTapped(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is MalNewsUnitModel item)
            {
                ((CollectionView)sender).SelectedItem = null;
                Vm.LoadArticleCommand.Execute(item);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS article nav failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Gives a normal table its own horizontal scroll box. Done here rather than in each query
    ///     so MAL and ANN articles get it at once, and after the body is cached, so it costs
    ///     nothing on repeat visits.
    ///     A table that only works with the site's own javascript is replaced by a link instead:
    ///     wrapped in a scroll box it renders as a tall empty rectangle, which reads as a bug.
    /// </summary>
    private static string WrapTables(string html, string articleUrl)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) < 0)
            return html;
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(
                html,
                "<table\\b[^>]*>[\\s\\S]*?</table>",
                match => NeedsItsOwnPage(match.Value)
                    ? "<div class=\"unsupported\">This chart needs ANN's own page to draw. " +
                      (string.IsNullOrEmpty(articleUrl)
                          ? string.Empty
                          : "<a href=\"" + articleUrl + "\">Open it on ANN</a>") +
                      "</div>"
                    : "<div class=\"tw\">" + match.Value + "</div>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch (Exception)
        {
            return html;
        }
    }

    private static bool NeedsItsOwnPage(string table)
    {
        // The ranking charts are drawn into a canvas and the marks are absolutely positioned divs
        // whose coordinates only mean something with ANN's own stylesheet.
        return table.IndexOf("<canvas", StringComparison.OrdinalIgnoreCase) >= 0
               || System.Text.RegularExpressions.Regex.IsMatch(table, "position\\s*:\\s*absolute",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private void OnOpenWebView(string html, MalNewsUnitModel model)
    {
        try
        {
            // Relative images need the right base URL: MAL content lives on
            // myanimelist.net and ANN content on animenewsnetwork.com.
            var baseUrl = model?.Source == "ANN"
                ? "https://www.animenewsnetwork.com"
                : "https://myanimelist.net";

            var wrapped = "<html><head><meta charset='utf-8'/>" +
                          "<meta name='viewport' content='width=device-width,initial-scale=1'/>" +
                          "<style>" + EmCss + "</style></head><body>" + WrapTables(html, model?.Url) + "</body></html>";
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ArticleWebView.IsVisible = true;
                ArticleWebView.Source = new HtmlWebViewSource { Html = wrapped, BaseUrl = baseUrl };
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS article html load failed: " + ex.Message);
        }
    }


    private void OnBackToList(object sender, EventArgs e)
    {
        ReturnToList();
    }

    /// <summary>
    ///     Back from an open article has to go back to the list, not pop the page
    ///     and leave the web view half torn down (that left the app stuck). The
    ///     system back button is intercepted for the same reason.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (Vm != null && Vm.PendingArticle != null)
        {
            ReturnToList();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void ReturnToList()
    {
        try
        {
            Vm.PendingArticle = null;
            Vm.CurrentNews = -1;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    ArticleWebView.IsVisible = false;
                    ArticleWebView.Source = new HtmlWebViewSource { Html = "<html><body></body></html>" };
                }
                catch { }
            });
        }
        catch { }
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button b && b.CommandParameter is MalNewsUnitModel item)
            {
                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = item.Title + "\n" + item.Url,
                    Title = "Share article"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS share failed: " + ex.Message);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            Vm.Init(MakeArgs(_currentMode), true);
        }
        catch { }
    }
}
