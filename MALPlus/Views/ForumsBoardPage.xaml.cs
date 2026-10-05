using MALClient.Models.Enums;
using MALClient.XShared.NavArgs;
using MALClient.XShared.ViewModels;
using MALClient.XShared.ViewModels.Forums;
using MALClient.XShared.ViewModels.Forums.Items;

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
public partial class ForumsBoardPage : ContentPage
{
    private bool _initialized;
    public string BoardId { get; set; }
    public string AnimeId { get; set; }
    public string AnimeTitle { get; set; }
    public string IsManga { get; set; }

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
        if (int.TryParse(AnimeId, out var id) && id > 0)
            return new ForumsBoardNavigationArgs(id,
                string.IsNullOrWhiteSpace(AnimeTitle) ? "Forum" : Uri.UnescapeDataString(AnimeTitle),
                !string.Equals(IsManga, "true", StringComparison.OrdinalIgnoreCase));
        int.TryParse(BoardId, out var board);
        return new ForumsBoardNavigationArgs((ForumBoards)board);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        try
        {
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

    private async void OnTopicRowTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is ForumTopicEntryViewModel item)
                await NavigateToTopicAsync(item);
        }
        catch
        {
        }
    }

    private static async Task NavigateToTopicAsync(ForumTopicEntryViewModel item)
    {
        await Shell.Current.GoToAsync($"forumtopic?id={item.Data.Id}");
    }
}
