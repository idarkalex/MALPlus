using MALPlus.Models.Enums;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;
using MALPlus.XShared.ViewModels.Forums.Items;

namespace MALPlus.Views;

/// <summary>
///     One board, or one entry's own board.
///     <para>
///     The three extra query properties are why "Forum board" on an entry lands on that entry's
///     discussion instead of the global index: the view model already knows how to read an anime or
///     manga board (ForumBoardTopicsQuery takes the id), but the page only ever rebuilt a ForumBoards
///     enum out of one int, so the id was thrown away on arrival.
///     </para>
/// </summary>
[QueryProperty(nameof(BoardId), "board")]
[QueryProperty(nameof(AnimeId), "anime")]
[QueryProperty(nameof(AnimeTitle), "title")]
[QueryProperty(nameof(IsManga), "manga")]
[QueryProperty(nameof(Page), "page")]
public partial class ForumsBoardPage : ContentPage, System.ComponentModel.INotifyPropertyChanged
{
    private bool _canStartTopicHere;

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    private bool _initialized;
    public string BoardId { get; set; }
    public string AnimeId { get; set; }
    public string AnimeTitle { get; set; }
    public string IsManga { get; set; }
    public string Page { get; set; }

    private ForumBoardViewModel Vm => (ForumBoardViewModel)BindingContext;

    public ForumsBoardPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ForumsBoard;
    }

    /// <summary>
    ///     The args for whichever kind of board this is. An entry id wins over a board number, because
    ///     that is the case the global index cannot serve.
    /// </summary>
    private ForumsBoardNavigationArgs BuildArgs()
    {
        var pageNumber = 0;
        if (!int.TryParse(Page, out pageNumber))
            pageNumber = 0;
        if (int.TryParse(AnimeId, out var id) && id > 0)
            return new ForumsBoardNavigationArgs(id,
                string.IsNullOrWhiteSpace(AnimeTitle) ? "Forum" : Uri.UnescapeDataString(AnimeTitle),
                !string.Equals(IsManga, "true", StringComparison.OrdinalIgnoreCase),
                pageNumber);
        int.TryParse(BoardId, out var board);
        return new ForumsBoardNavigationArgs((ForumBoards)board, pageNumber);
    }

    protected override async void OnAppearing()
  {
      base.OnAppearing();
#if ANDROID
      // The Shell's own back handler pops the whole section instead of the board page, which drops
      // the reader out of the forum entirely. Same takeover SeasonThemesPage uses: while this board
      // is on screen, back is one pop.
      global::MALPlus.MainActivity.ArmMenuBackCallback(() => _ = Shell.Current.GoToAsync(".."));
#endif
      if (_initialized) return;
      _initialized = true;
      try
      {
          // AnimeId arrives as a Shell query property, so it is only populated after the route is
          // applied; the button's visibility has to be recomputed here rather than in the ctor.
          _canStartTopicHere = int.TryParse(AnimeId, out var mediaId) && mediaId > 0;
          PropertyChanged?.Invoke(this,
              new System.ComponentModel.PropertyChangedEventArgs(nameof(CanStartTopicHere)));

          Vm.Init(BuildArgs());
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.LoadingTopics) break;
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Info("MALPlus", "MALPLUS ForumsBoardPage Init failed: " + ex.Message);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            Vm.Init(BuildArgs(), true);
        }
        catch { }
    }

    private void OnTopicRowTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is ForumTopicEntryViewModel item)
            {
                Vm.LoadTopic(item);
            }
        }
        catch
        {
        }
    }

  /// <summary>
  ///     Only an entry's own board accepts a new topic. The global boards map to a ForumBoards
  ///     value, and posting there needs the board number instead of a media id.
  /// </summary>
  public bool CanStartTopicHere => _canStartTopicHere;

  private async void OnNewTopicTapped(object sender, TappedEventArgs e)
  {
      if (int.TryParse(AnimeId, out var animeId) && animeId > 0)
      {
          var manga = string.Equals(IsManga, "true", StringComparison.OrdinalIgnoreCase);
          var title = string.IsNullOrWhiteSpace(AnimeTitle) ? "" : Uri.EscapeDataString(AnimeTitle);
          await Shell.Current.GoToAsync(
              $"forumnewtopic?anime={animeId}&title={title}&manga={(manga ? "true" : "false")}");
          return;
      }

      if (int.TryParse(BoardId, out var boardId) && boardId > 0)
      {
          await Shell.Current.GoToAsync($"forumnewtopic?board={boardId}");
          return;
      }

      await DisplayAlert("New topic",
          "Topics can only be started from an anime or manga discussion board.", "OK");
  }
}
