using MALPlus.Models.Enums;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;

namespace MALPlus.Views;

[QueryProperty(nameof(BoardId), "board")]
  [QueryProperty(nameof(MediaId), "anime")]
  [QueryProperty(nameof(MediaTitle), "title")]
  [QueryProperty(nameof(IsManga), "manga")]
  public partial class ForumNewTopicPage : ContentPage
  {
      private bool _initialized;
      public string BoardId { get; set; }

      /// <summary>
      ///     Set when the composer was opened from an entry's own discussion board rather than one
      ///     of the global boards. MAL posts there under the media id, and the board number means
      ///     nothing there, so the two are exclusive and the media id wins.
      /// </summary>
      public string MediaId { get; set; }

      public string MediaTitle { get; set; }
      public string IsManga { get; set; }
  
      private ForumNewTopicViewModel Vm => (ForumNewTopicViewModel)BindingContext;
  
      public ForumNewTopicPage()
      {
          InitializeComponent();
          BindingContext = ViewModelLocator.ForumsNewTopic;
          Vm.UpdatePreview += OnUpdatePreview;
      }

    private void OnUpdatePreview(object sender, string html)
    {
        MainThread.BeginInvokeOnMainThread(() => PreviewLabel.Text = html);
    }

protected override void OnAppearing()
      {
          base.OnAppearing();
          if (_initialized) return;
          _initialized = true;
          try
          {
              if (int.TryParse(MediaId, out var mediaId) && mediaId > 0)
              {
                  var manga = string.Equals(IsManga, "true", StringComparison.OrdinalIgnoreCase);
                  Vm.Init(new ForumsNewTopicNavigationArgs(
                      manga ? TopicType.Manga : TopicType.Anime,
                      string.IsNullOrWhiteSpace(MediaTitle) ? "this title" : Uri.UnescapeDataString(MediaTitle),
                      mediaId));
                  return;
              }

              int board = 0;
              int.TryParse(BoardId, out board);
              Vm.Init(new ForumsNewTopicNavigationArgs((ForumBoards)board));
          }
          catch (Exception ex)
          {
              Console.WriteLine("MALPLUS ForumNewTopicPage Init failed: " + ex.Message);
          }
      }
}
