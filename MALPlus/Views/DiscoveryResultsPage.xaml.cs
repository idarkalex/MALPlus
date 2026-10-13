using System.Collections.ObjectModel;
using System.ComponentModel;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.Services;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Views;

[QueryProperty(nameof(ItemsJson), "items")]
[QueryProperty(nameof(HeaderText), "header")]
public partial class DiscoveryResultsPage : ContentPage, INotifyPropertyChanged
{
    private string _itemsJson;
    private string _headerText;
    private WebView _videoWebView;
    private DiscoveryItem _heroItem;

    public ObservableCollection<DiscoveryItem> Results { get; } = new();

    /// <summary>
    ///     Set for single-item results (the weekly gem) to switch the page from a list to one hero
    ///     card. Two-way visibility: the ScrollView and the CollectionView both claim Grid.Row 1,
    ///     so whichever is not in use has to be explicitly hidden or they overlap.
    /// </summary>
    public bool IsHeroVisible => _heroItem != null;

    public bool IsListVisible => _heroItem == null;

    public string HeroTitle => _heroItem?.Title ?? "";

    public string HeroSubtitle => _heroItem?.Subtitle ?? "";

    public string HeroImgUrl => _heroItem?.ImgUrl ?? "";

    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetHero(DiscoveryItem item)
    {
        _heroItem = item;
        Raise(nameof(IsHeroVisible));
        Raise(nameof(IsListVisible));
        Raise(nameof(HeroTitle));
        Raise(nameof(HeroSubtitle));
        Raise(nameof(HeroImgUrl));
    }

    public DiscoveryResultsPage()
    {
        InitializeComponent();
        // The grid of cards rather than the raw feed items: the poster chips, the library badge and
        // the type are all read off the item view model the rest of the app lists with, so a row here
        // says the same thing a row on the anime list says. Binding DiscoveryItem directly gave a
        // poster with no TV chip and no "on your list" state, because that item carries neither.
        ResultsList.ItemsSource = Cards;
        BindingContext = this;
    }

    /// <summary>
    ///     The rows as the poster grid wants them, built from the feed items.
    /// </summary>
    public ObservableCollection<AnimeItemViewModel> Cards { get; } = new();

    private async void OnHeroTapped(object sender, TappedEventArgs e)
    {
        if (_heroItem != null)
            await OpenAsync(_heroItem);
    }

    public string ItemsJson
    {
        get => _itemsJson;
        set
        {
            _itemsJson = value;
            Load();
        }
    }

    public string HeaderText
    {
        get => _headerText;
        set
        {
            _headerText = value;
            if (!string.IsNullOrEmpty(value))
                Title = value;
            HeadingLabel.Text = value;
        }
    }

    private void Load()
    {
        Results.Clear();
        Cards.Clear();
        SetHero(null);
        if (string.IsNullOrWhiteSpace(_itemsJson))
            return;
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<DiscoveryItem>>(_itemsJson);
            if (items == null)
                return;
            foreach (var item in items)
            {
                Results.Add(item);
                Cards.Add(ToCard(item));
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Discover", "results decode: " + ex.GetType().Name);
        }

        // A single result reads as one recommendation, not a one-row list, so it gets the hero
        // card. Only for entries that carry artwork: the OP/ED rows are a list even at one item.
        if (Results.Count == 1 && !string.IsNullOrEmpty(Results[0].ImgUrl) &&
            string.IsNullOrEmpty(Results[0].ThemeUrl))
            SetHero(Results[0]);

        EmptyLabel.IsVisible = Results.Count == 0;
    }

    /// <summary>
    ///     One feed item as the poster grid's own row, so the chips and the library badge behave the
    ///     same here as on the anime list.
    /// </summary>
    /// <remarks>
    ///     Binding the feed item itself gave a bare poster: it carries no type, no score and no
    ///     "already on your list" state, because those live on the item view model the rest of the app
    ///     lists with. Building one here is what puts the TV chip and the list badge on the card.
    /// </remarks>
    private static AnimeItemViewModel ToCard(DiscoveryItem item)
    {
        MALPlus.Models.Models.Library.AnimeLibraryItemData data = item.IsManga
            ? new MALPlus.Models.Models.Library.MangaLibraryItemData()
            : new MALPlus.Models.Models.Library.AnimeLibraryItemData();

        data.Id = item.Id;
        data.Title = item.Title;
        data.ImgUrl = item.ImgUrl;

        return new AnimeItemAbstraction(false, data).ViewModel;
    }
    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not DiscoveryItem item)
            return;
        ((CollectionView)sender).SelectedItem = null;
        await OpenAsync(item);
    }

    /// <summary>
    ///     A tap on a result row, which is how the poster opens an entry here.
    /// </summary>
    /// <remarks>
    ///     The row carries its own recogniser because the poster is a control with its own long press on
    ///     it, and the tap that opens an entry is a different gesture. The parameter is what carries the
    ///     item: a recogniser without one has nothing to act on and does nothing at all.
    /// </remarks>
    private async void OnRowTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not DiscoveryItem item)
            return;
        ResultsList.SelectedItem = null;
        await OpenAsync(item);
    }

    /// <summary>
    ///     Width of a poster in a result row, taken from the poster grid's own numbers.
    /// </summary>
    /// <remarks>
    ///     Derived here rather than written into the page because the height is bound to the grid's
    ///     constant from the template, and a hand written width beside a bound height is how the two
    ///     drift apart.
    /// </remarks>
    public double PosterCellWidth => Controls.AnimePosterGrid.DefaultCellHeight * Controls.AnimePosterGrid.PosterAspect;

    private async Task OpenAsync(DiscoveryItem item)
    {
        try
        {
            if (!string.IsNullOrEmpty(item.ThemeUrl))
            {
                PlayTheme(item.ThemeUrl);
                return;
            }
            var qs = item.IsManga ? "&manga=true" : "";
            await Shell.Current.GoToAsync(
                $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}{qs}");
        }
        catch
        {
        }
    }

    private void PlayTheme(string url)
    {
        try
        {
            var webView = EnsureVideoWebView();
            VideoOverlay.IsVisible = true;
            // Opening in portrait too: the bars are visible and reserve space of their own.
            VideoOverlayInsets.Apply(null, VideoCloseButton);
            VideoWebViewHelper.Resume(webView.Handler?.PlatformView as global::Android.Views.View);
            webView.Source = VideoWebViewHelper.BuildSource(url);
            // MAUI re-applies MediaPlaybackRequiresUserGesture on every source update and puts it
            // back to true, so the media element would never start without this.
            VideoWebViewHelper.ApplyMediaSettings(webView);
VideoWebViewHelper.BackHandler = OnVideoBackPressed;
              // Registered here rather than once at startup: OnBackPressedDispatcher runs the most
              // recently added callback first and the Shell adds its own after
              // MainActivity.OnCreate, so a callback added back then never sees the press.
              MainActivity.ArmVideoBackCallback();
          }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Discover", "theme play: " + ex.GetType().Name);
        }
    }

    private WebView EnsureVideoWebView()
    {
        if (_videoWebView != null)
            return _videoWebView;
        _videoWebView = new WebView();
        // This overlay has no RowDefinitions, so Grid.SetRow is ignored and the WebView lands in
        // the single implicit row. Without an explicit Fill it also keeps whatever size it was
        // measured at while the overlay was still hidden, and the <video> renders small and off
        // centre inside a zero-sized viewport.
        _videoWebView.HorizontalOptions = LayoutOptions.Fill;
        _videoWebView.VerticalOptions = LayoutOptions.Fill;
        _videoWebView.BackgroundColor = Colors.Black;
        VideoOverlay.Add(_videoWebView);
#if ANDROID
        _videoWebView.HandlerChanged += (s, e) =>
        {
            var pv = _videoWebView?.Handler?.PlatformView as global::Android.Views.View;
            if (pv != null)
            {
                VideoWebViewHelper.ConfigurePlatformView(pv);
                VideoWebViewHelper.Resume(pv);
            }
        };
#endif
        return _videoWebView;
    }

private void OnCloseVideo(object sender, EventArgs e)
    {
        try
        {
            // Leaving full screen belongs to closing: an overlay that went away while the activity
            // was still landscape left the whole app sideways.
            FullscreenVideo.Exit();
            VideoWebViewHelper.BackHandler = null;
              MainActivity.DisarmVideoBackCallback();
              // Source = null on its own leaves the media element running, so the overlay went away
              // and the audio carried on.
              if (_videoWebView != null)
                  VideoWebViewHelper.StopMediaAndClear(_videoWebView);
            VideoOverlay.IsVisible = false;
        }
        catch
        {
        }
    }

    /// <summary>
    ///     Back gesture while the overlay is up: it closes the video and stays on the results list
    ///     instead of popping the page.
    /// </summary>
    private bool OnVideoBackPressed()
    {
        if (VideoOverlay == null || !VideoOverlay.IsVisible)
            return false;
        OnCloseVideo(this, EventArgs.Empty);
        return true;
    }

    private async void OnClose(object sender, EventArgs e)
    {
        try
        {
            await Navigation.PopModalAsync();
        }
        catch
        {
        }
    }
}
