using System.ComponentModel;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

public partial class RecommendationsPage : ContentPage
{
    private bool _initialized;
    private RecommendationsViewModel Vm => (RecommendationsViewModel)BindingContext;

    public RecommendationsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.Recommendations;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        try
        {
            UpdateTabStyles(RecommendationsPageWorkMode.TopAnime);
            Vm.CurrentWorkMode = RecommendationsPageWorkMode.TopAnime;
            await WaitForLoad();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus RecommendationsPage Init failed: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task WaitForLoad()
    {
        for (int i = 0; i < 60; i++)
        {
            await System.Threading.Tasks.Task.Delay(200);
            if (!Vm.Loading) break;
        }
    }

    /// <summary>
    ///     Opens whichever entry the tap landed on.
    /// </summary>
    /// <remarks>
    ///     Same route the poster grids use everywhere else: the arguments go through the handoff first
    ///     and the query string only carries what the shell needs to find the page, so the manga tab
    ///     cannot open an anime and the other way round.
    /// </remarks>
    private async void OpenEntry(AnimeItemViewModel model)
    {
        if (model == null || model.Id <= 0)
            return;
        try
        {
            var isAnime = Vm.CurrentWorkMode == RecommendationsPageWorkMode.TopAnime ||
                          Vm.CurrentWorkMode == RecommendationsPageWorkMode.Anime;
            var titleQs = Uri.EscapeDataString(model.Title ?? string.Empty);

            MALPlus.Services.MauiDetailsNavigationHandoff.Set(
                new AnimeDetailsPageNavigationArgs(model.Id, model.Title, null, null, null)
                {
                    AnimeMode = isAnime,
                    Source = PageIndex.PageRecomendations
                });

            var query = isAnime ? string.Empty : "&manga=true";
            await Shell.Current.GoToAsync($"animedetails?id={model.Id}&title={titleQs}{query}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus recommendation tap failed: " + ex.Message);
        }
    }

    /// <summary>The "If you liked..." cover: opens the entry the recommendation hangs off.</summary>
    private void OnDependentTapped(object sender, TappedEventArgs e) => OpenEntry(Vm.DependentItem);

    /// <summary>The "then you might like" cover: opens the entry being recommended.</summary>
    private void OnRecommendedTapped(object sender, TappedEventArgs e) => OpenEntry(Vm.RecommendedItem);

    private void OnPreviousClicked(object sender, EventArgs e) => Vm.GoToPreviousItemCommand.Execute(null);

    private void OnNextClicked(object sender, EventArgs e) => Vm.GoToNextItemCommand.Execute(null);

    private void OnModeClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button b && b.CommandParameter is string s)
            {
                var mode = s switch
                {
                    "TopAnime" => RecommendationsPageWorkMode.TopAnime,
                    "Anime" => RecommendationsPageWorkMode.Anime,
                    "TopManga" => RecommendationsPageWorkMode.TopManga,
                    "Manga" => RecommendationsPageWorkMode.Manga,
                    _ => RecommendationsPageWorkMode.TopAnime
                };

                Vm.CurrentWorkMode = mode;
                UpdateTabStyles(mode);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPlus rec mode switch failed: " + ex.Message);
        }
    }

    private void UpdateTabStyles(RecommendationsPageWorkMode activeMode)
    {
        SetTabStyle(TopAnimeTab, activeMode == RecommendationsPageWorkMode.TopAnime);
        SetTabStyle(RecentAnimeTab, activeMode == RecommendationsPageWorkMode.Anime);
        SetTabStyle(TopMangaTab, activeMode == RecommendationsPageWorkMode.TopManga);
        SetTabStyle(RecentMangaTab, activeMode == RecommendationsPageWorkMode.Manga);
    }

    private void SetTabStyle(Button btn, bool isActive)
    {
        if (btn == null) return;
        btn.BackgroundColor = isActive ? Color.FromArgb("#0066FF") : Colors.Transparent;
        btn.TextColor = isActive ? Colors.White : Color.FromArgb("#80FFFFFF");
    }
}