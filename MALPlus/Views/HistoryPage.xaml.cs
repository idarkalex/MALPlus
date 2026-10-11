using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;
using MALPlus.XShared.Utils;
using AnimeItemViewModel = MALPlus.XShared.ViewModels.AnimeItemViewModel;

namespace MALPlus.Views;

[QueryProperty(nameof(SourceUser), "user")]
public partial class HistoryPage : ContentPage
{
    private bool _initialized;
    public string SourceUser { get; set; }

    private HistoryViewModel Vm => (HistoryViewModel)BindingContext;

    public ObservableCollection<object> FlatHistory { get; } = new();

    /// <summary>
    ///     Which kind of entries the list is showing, matching the tabs MAL uses.
    /// </summary>
    private enum HistoryTab { All, Anime, Manga }

    private HistoryTab _tab = HistoryTab.All;

    public HistoryPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.History;
        HistoryList.ItemsSource = FlatHistory;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        try
        {
            Vm.Init(new HistoryNavigationArgs { Source = SourceUser ?? Credentials.UserName });
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                // The list is rebuilt as soon as the entries are in hand, on whichever poll that is.
                // Waiting for a separate "loaded" signal is what left the page blank when the first
                // poll happened before the entries had arrived.
                if (!Vm.LoadingVisibility && Vm.History != null) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS HistoryPage Init failed: " + ex.Message);
        }

        // Outside the try, on purpose: a failure part way through must still leave whatever did arrive
        // on screen.
        RebuildHistory();
    }

    /// <summary>
    ///     Rebuilds the flat list from the loaded history, filtered to the selected tab.
    /// </summary>
    /// <remarks>
    ///     Entries whose title could not be resolved are dropped: MAL groups the days as
    ///     "Two Weeks Ago (11)" and the count only made the label wrap onto a second line, so the day
    ///     label is trimmed here too.
    /// </remarks>
    private void RebuildHistory()
    {
        try
        {
            // A full rebuild, used for the first load, for a pull to refresh and for a tab switch. All
            // three throw away what is on screen on purpose.
            AppendHistory(replace: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS HistoryPage rebuild failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Rebuilds the flat list from the loaded history, filtered to the selected tab.
    /// </summary>
    /// <remarks>
    ///     When <paramref name="replace" /> is false only the rows that are not on screen yet are added.
    ///     That is the whole reason the page did not jump back to the top on every extra page: clearing
    ///     the bound collection is what a CollectionView reads as "the list is new", and it throws away
    ///     the scroll position along with the rows. Keyed on the day and the id together, because the
    ///     same entry legitimately appears under two different days when an episode was watched on each,
    ///     and dropping those would lose real history.
    /// </remarks>
    private void AppendHistory(bool replace)
    {
        if (replace)
        {
            FlatHistory.Clear();
        }
        else
        {
            var seen = new HashSet<(string Day, int Id)>();
            // FlatHistory is a collection of object because the row type is a KeyValuePair, so the cast
            // is what turns a row back into something with a Key and a Value on it.
            foreach (var raw in FlatHistory)
            {
                if (raw is not KeyValuePair<string, Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>> row)
                    continue;
                if (row.Value?.Item1 != null)
                    seen.Add((row.Key, row.Value.Item1.Id));
            }

            foreach (var row in BuildRows())
            {
                if (row.Value?.Item1 == null)
                    continue;
                if (seen.Add((row.Key, row.Value.Item1.Id)))
                    FlatHistory.Add(row);
            }

            return;
        }

        foreach (var row in BuildRows())
            FlatHistory.Add(row);
    }

    /// <summary>
    ///     The rows for the current tab, in the order the days came back.
    /// </summary>
    /// <remarks>
    ///     Entries whose title could not be resolved are dropped: MAL groups the days as
    ///     "Two Weeks Ago (11)" and the count only made the label wrap onto a second line, so the day
    ///     label is trimmed here too.
    /// </remarks>
    private List<KeyValuePair<string, Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>> BuildRows()
    {
        var rows =
            new List<KeyValuePair<string, Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>>();

        if (Vm.History == null)
            return rows;

        foreach (var kv in Vm.History)
        {
            foreach (var tuple in kv.Value)
            {
                if (tuple?.Item1 == null)
                    continue;
                if (_tab == HistoryTab.Anime && !(tuple.Item2?.Any(e => e.IsAnime) ?? false))
                    continue;
                if (_tab == HistoryTab.Manga && !(tuple.Item2?.Any(e => !e.IsAnime) ?? false))
                    continue;

                var day = kv.Key;
                var countIdx = day.IndexOf(" (", StringComparison.Ordinal);
                if (countIdx > 0)
                    day = day.Substring(0, countIdx);

                rows.Add(new KeyValuePair<string,
                    Tuple<AnimeItemViewModel, List<MalProfileHistoryEntry>>>(day, tuple));
            }
        }

        return rows;
    }

    /// <summary>
    ///     Asks for another page while the reader is still a screen or so from the end.
    /// </summary>
    /// <remarks>
    ///     The threshold on the list does the same thing, but on a collection that is still growing it
    ///     fires once and does not re-arm, so the first extra page was the only one that ever arrived.
    ///     Listening to the scroll keeps the trigger where the scroll is. The view model refuses a second
    ///     read while one is in flight, so however many scroll events arrive there is never more than one
    ///     outstanding request.
    /// </remarks>
    private void OnHistoryScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        try
        {
            if (e.LastVisibleItemIndex < 0)
                return;
            if (FlatHistory.Count - e.LastVisibleItemIndex - 1 > 8)
                return;
            Vm.LoadMoreCommand?.Execute(null);
        }
        catch
        {
            // A scroll glitch must never break the list.
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            Vm.Init(new HistoryNavigationArgs { Source = SourceUser ?? Credentials.UserName }, true);
        }
        catch { }
    }

    /// <summary>
    ///     Switches the tab and re-filters what is on screen.
    /// </summary>
    private void OnTypeTabTapped(object sender, EventArgs e)
    {
        try
        {
            if (sender is not Button { CommandParameter: string s })
                return;

            _tab = s switch
            {
                "Anime" => HistoryTab.Anime,
                "Manga" => HistoryTab.Manga,
                _ => HistoryTab.All
            };
            UpdateTabColors();
            RebuildHistory();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS HistoryPage tab switch failed: " + ex.Message);
        }
    }

    private void UpdateTabColors()
    {
        var active = Color.FromArgb("#0066FF");
        var idle = Color.FromArgb("#141d2b");
        var activeText = Colors.White;
        var idleText = Color.FromArgb("#9FB3C8");

        TabAll.BackgroundColor = _tab == HistoryTab.All ? active : idle;
        TabAll.TextColor = _tab == HistoryTab.All ? activeText : idleText;
        TabAnime.BackgroundColor = _tab == HistoryTab.Anime ? active : idle;
        TabAnime.TextColor = _tab == HistoryTab.Anime ? activeText : idleText;
        TabManga.BackgroundColor = _tab == HistoryTab.Manga ? active : idle;
        TabManga.TextColor = _tab == HistoryTab.Manga ? activeText : idleText;
    }

    /// <summary>
    ///     Opens the entry that was tapped, as the thing actually belonging to this page rather than
    ///     as belonging to a list.
    /// </summary>
    /// <remarks>
    ///     There was no handler here at all before: the row had no recognizer and the collection could
    ///     not be selected, so a tap did literally nothing. The details page already had a branch
    ///     waiting for History as the entry point, which is what makes returning from it behave
    ///     correctly, and it had never fired because nothing navigated from here.
    /// </remarks>
    private async void OnHistoryEntryTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is not AnimeItemViewModel item || item.Id == 0)
                return;

            // Read back from the entry rather than from the card, because one day can hold both an
            // anime and a manga and the card does not say which of the two it is showing.
            var entries = AllEntries()
                .Where(x => x.Id == item.Id && (_tab != HistoryTab.Manga || !x.IsAnime));
            var isAnime = entries.Any(x => x.IsAnime) || _tab != HistoryTab.Manga;

            await OpenEntry(item, isAnime);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS HistoryPage open failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     The row is selected rather than tapped on some flows, and both end up here.
    /// </summary>
    private async void OnHistoryEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is not object selected)
                return;
            HistoryList.SelectedItem = null;
            if (Vm.History == null)
                return;
            if (selected is KeyValuePair<string,
                    System.Tuple<AnimeItemViewModel,
                                 System.Collections.Generic.List<MalProfileHistoryEntry>>> pair)
                await OpenEntry(pair.Value.Item1,
                    pair.Value.Item2?.Any(x => x.IsAnime) ?? true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS HistoryPage select open failed: " + ex.Message);
        }
    }

    private async Task OpenEntry(AnimeItemViewModel item, bool isAnime)
    {
        var args = new AnimeDetailsPageNavigationArgs(item.Id, item.Title, null, item)
        {
            Source = MALPlus.Models.Enums.PageIndex.PageHistory,
            AnimeMode = isAnime
        };
        await Shell.Current.GoToAsync(
            $"animedetails?id={item.Id}&title={Uri.EscapeDataString(item.Title ?? "")}" +
            (isAnime ? "" : "&manga=true"));
    }

    private System.Collections.Generic.IEnumerable<MalProfileHistoryEntry> AllEntries()
    {
        return Vm.History?.Values
                   .SelectMany(day => day)
                   .Where(t => t?.Item2 != null)
                   .SelectMany(t => t.Item2)
            ?? System.Linq.Enumerable.Empty<MalProfileHistoryEntry>();
    }
}
