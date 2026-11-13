using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Controls;

/// <summary>
///     The one vertical-poster grid for the whole app: anime list, manga list, search results,
///     Related, Discover, ... They all used to hand-roll a CollectionView + GridItemsLayout and
///     hardcode the cell height, which is why the spacing drifted apart between pages and why
///     cards ended up with a visible gap next to the artwork.
///
///     Here the cell width comes from the width the control actually gets, and the cell height is
///     DERIVED from it through <see cref="PosterAspect" />. A poster therefore always fills its cell
///     exactly, on any screen, and rows are flush with a single <see cref="Spacing" /> between them.
/// </summary>
public partial class AnimePosterGrid : ContentView
{
    /// <summary>Poster aspect (width / height). MAL covers are 2:3.</summary>
    public const double PosterAspect = 2d / 3d;

    /// <summary>
    ///     The single spacing value used by every poster grid in the app. Zero: posters sit edge to
    ///     edge with no gutter and no frame, which is the look that was signed off.
    /// </summary>
    public const double DefaultSpacing = 0d;

    /// <summary>Cards per row. The app is designed around three.</summary>
    public const int DefaultSpan = 3;

    /// <summary>
    ///     Card height the app has always used. It is deliberately NOT derived from the cell width:
    ///     the posters are AspectFill, so the cell crops instead of letterboxing, and 195 is the
    ///     proportion that was signed off.
    /// </summary>
    public const double DefaultCellHeight = 195d;

    /// <summary>
    ///     True when there is nothing to show, so the host can draw its own empty state (the
    ///     control has no CollectionView.EmptyView to lean on).
    /// </summary>
    public static readonly BindableProperty IsEmptyProperty =
        BindableProperty.Create(nameof(IsEmpty), typeof(bool), typeof(AnimePosterGrid), true,
            propertyChanged: (b, o, n) => ((AnimePosterGrid)b).ItemsView.IsVisible = !(bool)n);

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        private set => SetValue(IsEmptyProperty, value);
    }

    private bool _spanApplied;
    private double _lastWidth = -1;

    /// <summary>
    ///     How many upcoming items get their countdown resolved ahead of the scroll.
    ///     A countdown is only free when the series is in the cached airing schedule; the rest cost
    ///     one request each. Resolving just the visible screenful meant everything below it waited
    ///     in the global lookup queue, so the lower rows of a grid showed their date about ten
    ///     seconds late. A window of thirty six covers a long screenful plus what the user
    ///     reaches while the next batch is already in flight.
    /// </summary>
    private const int AirWarmWindow = 36;

    /// <summary>Index up to which countdowns have been asked for, in grid order.</summary>
    private int _airWarmed;

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(AnimePosterGrid),
            default(IEnumerable), propertyChanged: OnItemsSourceChanged);

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var grid = (AnimePosterGrid)bindable;
        if (oldValue is INotifyCollectionChanged oldNotifier)
            oldNotifier.CollectionChanged -= grid.OnItemsSourceCollectionChanged;
        if (newValue is INotifyCollectionChanged newNotifier)
            newNotifier.CollectionChanged += grid.OnItemsSourceCollectionChanged;
        grid.Inner.ItemsSource = newValue as IEnumerable;
        grid._airWarmed = 0;
        grid.ApplyLayout(force: true);
        grid.WarmAirTimes();
    }

    /// <summary>
    ///     A fixed-height grid lives inside a ScrollView, so it has to grow as items stream in.
    ///     Infinite scroll appends to the same collection, so the warm window is extended here
    ///     too: without it only the first screenful of a long grid would ever be resolved ahead
    ///     of the scroll. A reset means a different list, so the window starts over.
    /// </summary>
    private void OnItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _airWarmed = 0;
        ApplyLayout(force: true);
        WarmAirTimes();
    }

    /// <summary>
    ///     Asks for the countdown of the next window of items, in the order they appear, so the
    ///     posters below the fold already carry their date by the time they are scrolled to.
    ///     <see cref="AnimeItemViewModel.RefreshTimeTillNextAirInBackground" /> is a no-op for an
    ///     entry that is already fresh, in flight, or in failure backoff, so this is safe to call
    ///     as often as the scroll asks for it.
    /// </summary>
    private void WarmAirTimes()
    {
        if (ItemsView.ItemsSource is not IEnumerable source)
            return;

        // Only anime air. A manga card is skipped outright rather than queued behind the gate for
        // a lookup that can never return anything.
        // We iterate only the active window [ _airWarmed .. target ] directly instead of filtering
        // and allocating a new List of the entire collection on every scroll frame.
        if (source is IList list)
        {
            var count = list.Count;
            if (count == 0 || _airWarmed >= count)
                return;

            var target = Math.Min(count, _airWarmed + AirWarmWindow);
            for (var i = _airWarmed; i < target; i++)
            {
                if (list[i] is AnimeItemViewModel item && item.ParentAbstraction?.RepresentsAnime == true)
                    item.RefreshTimeTillNextAirInBackground();
            }
            _airWarmed = target;
        }
        else
        {
            int index = 0;
            int warmed = _airWarmed;
            int target = warmed + AirWarmWindow;
            foreach (var obj in source)
            {
                if (index >= target)
                    break;
                if (index >= warmed)
                {
                    if (obj is AnimeItemViewModel item && item.ParentAbstraction?.RepresentsAnime == true)
                        item.RefreshTimeTillNextAirInBackground();
                }
                index++;
            }
            _airWarmed = Math.Min(index, target);
        }
    }

    /// <summary>
    ///     Grows the warm window as the user approaches its end, so a long grid keeps resolving
    ///     ahead of the scroll instead of one row at a time behind it.
    /// </summary>
    private void OnItemsScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (e.LastVisibleItemIndex < 0 || _airWarmed == 0)
            return;
        if (e.LastVisibleItemIndex >= _airWarmed - AirWarmWindow / 2)
            WarmAirTimes();
    }

    public static readonly BindableProperty SpanProperty =
        BindableProperty.Create(nameof(Span), typeof(int), typeof(AnimePosterGrid), 0,
            propertyChanged: (b, o, n) => ((AnimePosterGrid)b).ApplyLayout(force: true));

    /// <summary>Gap between two cards, and half of it on each outer edge.</summary>
    public static readonly BindableProperty SpacingProperty =
        BindableProperty.Create(nameof(Spacing), typeof(double), typeof(AnimePosterGrid), DefaultSpacing,
            propertyChanged: (b, o, n) => ((AnimePosterGrid)b).ApplyLayout(force: true));

    /// <summary>Optional cell content. Null uses the standard library cell.</summary>
    public static readonly BindableProperty CellTemplateProperty =
        BindableProperty.Create(nameof(CellTemplate), typeof(DataTemplate), typeof(AnimePosterGrid),
            default(DataTemplate), propertyChanged: (b, o, n) => ((AnimePosterGrid)b).ApplyLayout(force: true));

    /// <summary>Width of a single card, derived from the available width.</summary>
    public static readonly BindableProperty CellWidthProperty =
        BindableProperty.Create(nameof(CellWidth), typeof(double), typeof(AnimePosterGrid), 0d);

    /// <summary>Height of a single card, derived from <see cref="CellWidth" />.</summary>
    public static readonly BindableProperty CellHeightProperty =
        BindableProperty.Create(nameof(CellHeight), typeof(double), typeof(AnimePosterGrid), 0d);

    /// <summary>Vertical pitch of one row: card height plus its margin.</summary>
    public static readonly BindableProperty RowHeightProperty =
        BindableProperty.Create(nameof(RowHeight), typeof(double), typeof(AnimePosterGrid), 0d);

    /// <summary>Half the spacing, so the row keeps the same gutter on its outer edges.</summary>
    public static readonly BindableProperty CellMarginProperty =
        BindableProperty.Create(nameof(CellMargin), typeof(Thickness), typeof(AnimePosterGrid), new Thickness(0));

    /// <summary>
    ///     Set when the grid lives inside a ScrollView and therefore cannot take its height from a
    ///     scroller: the control measures itself instead of letting the host guess.
    /// </summary>
    public static readonly BindableProperty FixedHeightProperty =
        BindableProperty.Create(nameof(FixedHeight), typeof(bool), typeof(AnimePosterGrid), false,
            propertyChanged: (b, o, n) => ((AnimePosterGrid)b).ApplyLayout(force: true));

    /// <summary>
    ///     Shows the tag and note indicators on the cards.
    /// </summary>
    /// <remarks>
    ///     Off unless a page asks. They are facts about the user's library entry, so they mean
    ///     nothing on a search result, a recommendation or a ranking, and the default cell is shared
    ///     by every grid in the app. Only the anime library list turns them on.
    /// </remarks>
    public static readonly BindableProperty ShowEntryIndicatorsProperty =
        BindableProperty.Create(nameof(ShowEntryIndicators), typeof(bool), typeof(AnimePosterGrid), false);

    public bool ShowEntryIndicators
    {
        get => (bool)GetValue(ShowEntryIndicatorsProperty);
        set => SetValue(ShowEntryIndicatorsProperty, value);
    }

    /// <summary>Raised with the bound item when a card is tapped.</summary>
    public event EventHandler<object> ItemTapped;

    /// <summary>
    ///     Raised with the bound item on a long press.
    ///     <para>
    ///     This used to be inert by design, on the grounds that a recognizer in the shared cell
    ///     template would change the gesture behaviour of every poster grid in the app. It now lives
    ///     in AnimePosterCard instead of the template, which is where it belongs: every poster in the
    ///     app is that control, so one place covers the list, search, calendar, Discover and Related,
    ///     and any grid added later gets it for free. Tap and long press coexist, so opening an entry
    ///     is unchanged.
    ///     </para>
    /// </summary>
    public event EventHandler<object> ItemLongPressed;

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>0 means "pick from the width" (3 columns on a phone, more on a tablet).</summary>
    public int Span
    {
        get => (int)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public DataTemplate CellTemplate
    {
        get => (DataTemplate)GetValue(CellTemplateProperty);
        set => SetValue(CellTemplateProperty, value);
    }

    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        private set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        private set => SetValue(CellHeightProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        private set => SetValue(RowHeightProperty, value);
    }

    public Thickness CellMargin
    {
        get => (Thickness)GetValue(CellMarginProperty);
        private set => SetValue(CellMarginProperty, value);
    }

    public bool FixedHeight
    {
        get => (bool)GetValue(FixedHeightProperty);
        set => SetValue(FixedHeightProperty, value);
    }

    public AnimePosterGrid()
    {
        InitializeComponent();
        ItemsView.Scrolled += OnItemsScrolled;
        ApplyLayout(force: true);
    }

    /// <summary>
    ///     The inner collection, for the few hosts that need list-level features the grid does not
    ///     model (infinite scroll, selection). Layout and sizing stay owned by the control.
    /// </summary>
    public CollectionView Inner => ItemsView;

    /// <summary>
    ///     Height a poster grid of <paramref name="count" /> items needs when it cannot scroll.
    /// </summary>
    public static double CalculateHeight(int count, int span, double rowHeight)
    {
        if (count <= 0 || rowHeight <= 0)
            return 0;
        var columns = span > 0 ? span : DefaultSpan;
        var rows = (int)Math.Ceiling(count / (double)columns);
        return rows * rowHeight;
    }

    public int EffectiveSpan(double availableWidth)
    {
        if (Span > 0)
            return Span;
        return DefaultSpan;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;
        // CellWidth is informational (no cell template sets a width: the layout divides the slot
        // and the card fills it), but keep it truthful for anything that asks.
        CellWidth = (width - (EffectiveSpan(width) - 1) * Spacing) / EffectiveSpan(width) - Spacing;
        if (Math.Abs(width - _lastWidth) < 0.5)
            return;
        _lastWidth = width;
        ApplyLayout(force: true);
    }

    private void ApplyLayout(bool force)
    {
        // The empty state has to be correct even before the control has a width, otherwise a grid
        // that never gets sized would stay hidden forever.
        var count = (ItemsView.ItemsSource as ICollection)?.Count
                    ?? (ItemsView.ItemsSource as IList)?.Count
                    ?? (ItemsView.ItemsSource as IEnumerable)?.Cast<object>().Count()
                    ?? 0;
        IsEmpty = count == 0;

        if (!force && _spanApplied)
            return;

        // Span and cell template do NOT depend on the width. A grid hosted inside a CarouselView
        // can be measured at zero width on its first pass and never get a second one, and bailing
        // out there left the calendar day pages rendering nothing at all.
        var span = EffectiveSpan(0);
        if (!_spanApplied || (ItemsView.ItemsLayout is GridItemsLayout grid && grid.Span != span))
        {
            // The gutter lives in the layout, not in a card margin: a margin insets the artwork on
            // all four sides and shows up as a dark frame around every poster.
            ItemsView.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = Spacing,
                VerticalItemSpacing = Spacing
            };
            _spanApplied = true;
        }

        // The card template reads these through x:Reference, so no page has to repeat the numbers.
//
// Every one of these is only written when it actually changes, and that is the whole point of
// this method: it is called on EVERY collection change, so while scrolling a long list. Assigning
// ItemTemplate again, even to the same template, makes the CollectionView drop its adapter and
// re-template every visible cell, and re-writing the sizing properties invalidates the layout of
// every card again. That is what a scroll through already-loaded posters was stuttering against:
// nothing about the grid had changed, but all of it was being rebuilt on every row that arrived.
var wantedTemplate = CellTemplate ?? (DataTemplate)Root.Resources["DefaultCellTemplate"];
if (!ReferenceEquals(_appliedTemplate, wantedTemplate))
{
    _appliedTemplate = wantedTemplate;
    ItemsView.ItemTemplate = wantedTemplate;
}

if (_appliedCellHeight != DefaultCellHeight)
{
    _appliedCellHeight = DefaultCellHeight;
    CellHeight = DefaultCellHeight;
    RowHeight = CellHeight + Spacing;
}

if (_appliedMargin != NoMargin)
{
    _appliedMargin = NoMargin;
    CellMargin = NoMargin;
}

if (FixedHeight)
    HeightRequest = CalculateHeight(count, span, RowHeight);
}

private static readonly Thickness NoMargin = new Thickness(0);
private DataTemplate _appliedTemplate;
private double _appliedCellHeight = -1;
private Thickness _appliedMargin = new Thickness(-1);

    private void OnCellTapped(object sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext != null)
            ItemTapped?.Invoke(this, view.BindingContext);
    }

    /// <summary>
    ///     Same contract as <see cref="OnCellTapped" />. Called by the card itself, which walks up to
    ///     its enclosing grid, so this covers the default cell template and every custom one built
    ///     from AnimePosterCard without any host having to wire anything.
    /// </summary>
    public void RaiseItemLongPressed(object item)
    {
        if (item != null)
            ItemLongPressed?.Invoke(this, item);
    }
}
