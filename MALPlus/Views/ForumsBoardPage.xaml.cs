using System;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;
using MALPlus.XShared.ViewModels.Forums.Items;

namespace MALPlus.Views;

/// <summary>
///     One board, or one entry's own board.
/// </summary>
[QueryProperty(nameof(BoardId), "board")]
[QueryProperty(nameof(AnimeId), "anime")]
[QueryProperty(nameof(AnimeTitle), "title")]
[QueryProperty(nameof(IsManga), "manga")]
[QueryProperty(nameof(Page), "page")]
public partial class ForumsBoardPage : ContentPage, IQueryAttributable, System.ComponentModel.INotifyPropertyChanged
{
    private bool _canStartTopicHere;
    private string _lastLoadedKey;
    private bool _navigating;

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    public string BoardId { get; set; }
    public string AnimeId { get; set; }
    public string AnimeTitle { get; set; }
    public string IsManga { get; set; }
    public string Page { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        BoardId = query.TryGetValue("board", out var b) ? b?.ToString() : null;
        AnimeId = query.TryGetValue("anime", out var a) ? a?.ToString() : null;
        AnimeTitle = query.TryGetValue("title", out var t) ? t?.ToString() : null;
        IsManga = query.TryGetValue("manga", out var m) ? m?.ToString() : null;
        Page = query.TryGetValue("page", out var p) ? p?.ToString() : null;
        _ = LoadBoardIfReadyAsync();
    }

    private ForumBoardViewModel Vm => (ForumBoardViewModel)BindingContext;

    public ForumsBoardPage()
    {
        InitializeComponent();
        var vm = ViewModelLocator.ForumsBoard;
        if (vm != null)
        {
            vm.LoadingTopics = true;
            vm.LoadErrorVisibility = false;
            vm.EmptyNoticeVisibility = false;
            vm.Topics = new System.Collections.Generic.List<ForumTopicEntryViewModel>();
        }
        BindingContext = vm;
    }

    private async Task PopBackAsync()
    {
        await Shell.Current.GoToAsync("..");
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
        if (int.TryParse(BoardId, out var board) && board > 0)
            return new ForumsBoardNavigationArgs((ForumBoards)board, pageNumber);
        return null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _navigating = false;
#if ANDROID
        global::MALPlus.MainActivity.ArmMenuBackCallback(() => _ = PopBackAsync());
#endif
        _ = LoadBoardIfReadyAsync();
    }

    private async Task LoadBoardIfReadyAsync()
    {
        var args = BuildArgs();
        if (args == null)
            return;

        var currentKey = $"{BoardId}_{AnimeId}_{Page}";
        if (_lastLoadedKey == currentKey) return;
        _lastLoadedKey = currentKey;
        try
        {
            var hasAnime = int.TryParse(AnimeId, out var id) && id > 0;
            _canStartTopicHere = hasAnime;
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(CanStartTopicHere)));

            if (Vm != null)
            {
                Vm.LoadingTopics = true;
                Vm.LoadErrorVisibility = false;
                Vm.Topics = new System.Collections.Generic.List<ForumTopicEntryViewModel>();
            }

            Vm?.Init(args, true);
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine("MALPLUS ForumsBoardPage Init failed: " + ex.Message);
        }
    }

    protected override void OnDisappearing()
    {
        _navigating = false;
#if ANDROID
        global::MALPlus.MainActivity.DisarmMenuBackCallback();
#endif
        var vm = Vm;
        if (vm != null)
        {
            vm.LoadErrorVisibility = false;
        }
        base.OnDisappearing();
    }

    private void OnRetryTapped(object sender, TappedEventArgs e)
    {
        var vm = Vm;
        if (vm != null && vm.LoadingTopics)
            return;
        var args = BuildArgs();
        if (args != null)
            vm?.Init(args, true);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = PopBackAsync();
        return true;
    }

    private void OnBackTapped(object sender, TappedEventArgs e)
    {
        _ = PopBackAsync();
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm != null && vm.LoadingTopics)
                return;
            var args = BuildArgs();
            if (args != null)
                vm?.Init(args, true);
        }
        catch { }
    }

    private void OnPreviousPageTapped(object sender, TappedEventArgs e)
    {
        var vm = Vm;
        if (vm != null && vm.CanPreviousPage)
            vm.LoadPage(vm.CurrentPage - 1, false, false);
    }

    private void OnNextPageTapped(object sender, TappedEventArgs e)
    {
        var vm = Vm;
        if (vm != null && vm.CanNextPage)
            vm.LoadPage(vm.CurrentPage + 1, false, false);
    }

    private async void OnPageJumpTapped(object sender, TappedEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        var total = vm.DisplayTotalPages;
        var result = await DisplayPromptAsync("Go to page",
            $"Enter a page between 1 and {total}",
            keyboard: Keyboard.Numeric, accept: "Go", cancel: "Cancel");
        if (!string.IsNullOrWhiteSpace(result) && int.TryParse(result, out var page) && page >= 1 && page <= total)
        {
            vm.LoadPage(page - 1, false, false);
        }
    }

    private void OnTopicRowTapped(object sender, TappedEventArgs e)
    {
        if (_navigating)
            return;
        try
        {
            var item = (e.Parameter as ForumTopicEntryViewModel) ??
                       ((sender as BindableObject)?.BindingContext as ForumTopicEntryViewModel);
            if (item != null)
            {
                _navigating = true;
                Vm.LoadTopic(item);
            }
        }
        catch
        {
        }
        finally
        {
            _ = Task.Delay(600).ContinueWith(_ => _navigating = false);
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
