using System.Collections;
using System.Windows.Input;

namespace MALPlus.Controls;

public partial class AnimePosterCard : ContentView
{
    public static readonly BindableProperty PosterUrlProperty =
        BindableProperty.Create(nameof(PosterUrl), typeof(string), typeof(AnimePosterCard), default(string));

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(AnimePosterCard), default(string));

    public static readonly BindableProperty TypeTagProperty =
        BindableProperty.Create(nameof(TypeTag), typeof(string), typeof(AnimePosterCard), default(string));

    /// <summary>
    /// Mean score, drawn as a second blue tag under the type tag. Tops and search results used
    /// to spell it out in the counter pill as "Score 8.72", which put a word where a number
    /// belongs and left the type tag alone at the top.
    /// </summary>
    public static readonly BindableProperty ScoreTagProperty =
        BindableProperty.Create(nameof(ScoreTag), typeof(string), typeof(AnimePosterCard), default(string));

    public static readonly BindableProperty SeasonTagProperty =
        BindableProperty.Create(nameof(SeasonTag), typeof(string), typeof(AnimePosterCard), default(string));

    public static readonly BindableProperty BadgeVisibleProperty =
        BindableProperty.Create(nameof(BadgeVisible), typeof(bool), typeof(AnimePosterCard), false);

    public static readonly BindableProperty BadgeAccentProperty =
        BindableProperty.Create(nameof(BadgeAccent), typeof(string), typeof(AnimePosterCard), default(string),
            propertyChanged: OnBadgeChanged);

    public static readonly BindableProperty BadgeAccentColorProperty =
        BindableProperty.Create(nameof(BadgeAccentColor), typeof(Color), typeof(AnimePosterCard), Color.FromArgb("#4ADE80"));

    public static readonly BindableProperty BadgeMainProperty =
        BindableProperty.Create(nameof(BadgeMain), typeof(string), typeof(AnimePosterCard), default(string),
            propertyChanged: OnBadgeChanged);

    public static readonly BindableProperty BadgeExtraProperty =
        BindableProperty.Create(nameof(BadgeExtra), typeof(string), typeof(AnimePosterCard), default(string),
            propertyChanged: OnBadgeChanged);

    /// <summary>
    /// True when at least one badge carries text. The accent/main pill had no visibility of
    /// its own, so a card with no badges still drew an empty dark blob in the corner.
    /// </summary>
    public static readonly BindableProperty HasBadgeProperty =
        BindableProperty.Create(nameof(HasBadge), typeof(bool), typeof(AnimePosterCard), false);

    public bool HasBadge
    {
        get => (bool)GetValue(HasBadgeProperty);
        private set => SetValue(HasBadgeProperty, value);
    }

    /// <summary>
    ///     True when the countdown is worth a separator inside the single merged counter pill.
    /// </summary>
    public static readonly BindableProperty HasBadgeExtraProperty =
        BindableProperty.Create(nameof(HasBadgeExtra), typeof(bool), typeof(AnimePosterCard), false);

    public static readonly BindableProperty IsAiringExtraProperty =
        BindableProperty.Create(nameof(IsAiringExtra), typeof(bool), typeof(AnimePosterCard), false);

    public bool IsAiringExtra
    {
        get => (bool)GetValue(IsAiringExtraProperty);
        private set => SetValue(IsAiringExtraProperty, value);
    }

    public static readonly BindableProperty HasBadgeExtraTopProperty =
        BindableProperty.Create(nameof(HasBadgeExtraTop), typeof(bool), typeof(AnimePosterCard), false);

    public bool HasBadgeExtraTop
    {
        get => (bool)GetValue(HasBadgeExtraTopProperty);
        private set => SetValue(HasBadgeExtraTopProperty, value);
    }

    public bool HasBadgeExtra
    {
        get => (bool)GetValue(HasBadgeExtraProperty);
        private set => SetValue(HasBadgeExtraProperty, value);
    }

    private static void OnBadgeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (AnimePosterCard)bindable;
        card.HasBadge = !string.IsNullOrWhiteSpace(card.BadgeAccent)
                        || !string.IsNullOrWhiteSpace(card.BadgeMain)
                        || !string.IsNullOrWhiteSpace(card.BadgeExtra);
        card.IsAiringExtra = string.Equals(card.BadgeExtra?.Trim(), "Airing now", StringComparison.OrdinalIgnoreCase);
        // The dot only earns its place when there is something on both sides of it. The countdown
        // itself shows either way, because on the calendar it is all a non-listed entry has: it
        // used to be gated behind a neighbour, which left those cards with an empty bubble.
        card.HasBadgeExtra = !string.IsNullOrWhiteSpace(card.BadgeExtra)
                              && (!string.IsNullOrWhiteSpace(card.BadgeAccent)
                                  || !string.IsNullOrWhiteSpace(card.BadgeMain))
                              && !card.IsAiringExtra;
        card.HasBadgeExtraTop = !string.IsNullOrWhiteSpace(card.BadgeExtra) && !card.IsAiringExtra;
    }

    /// <summary>
    ///     Whether this panel is one where the tag and note indicators belong at all.
    /// </summary>
    /// <remarks>
    ///     Off by default and the grid has to ask for them. These are per library entry facts, so
    ///     they mean nothing on a search result or a recommendation, and only the anime library list
    ///     wants them.
    /// </remarks>
    public static readonly BindableProperty IndicatorsVisibleProperty =
        BindableProperty.Create(nameof(IndicatorsVisible), typeof(bool), typeof(AnimePosterCard), false,
            propertyChanged: OnIndicatorChanged);

    public bool IndicatorsVisible
    {
        get => (bool)GetValue(IndicatorsVisibleProperty);
        set => SetValue(IndicatorsVisibleProperty, value);
    }

    /// <summary>True when the entry carries at least one tag.</summary>
    public static readonly BindableProperty TagsPresentProperty =
        BindableProperty.Create(nameof(TagsPresent), typeof(bool), typeof(AnimePosterCard), false,
            propertyChanged: OnIndicatorChanged);

    public bool TagsPresent
    {
        get => (bool)GetValue(TagsPresentProperty);
        set => SetValue(TagsPresentProperty, value);
    }

    /// <summary>True when the entry carries a note.</summary>
    public static readonly BindableProperty NotePresentProperty =
        BindableProperty.Create(nameof(NotePresent), typeof(bool), typeof(AnimePosterCard), false,
            propertyChanged: OnIndicatorChanged);

    public bool NotePresent
    {
        get => (bool)GetValue(NotePresentProperty);
        set => SetValue(NotePresentProperty, value);
    }

    public static readonly BindableProperty HasTagIndicatorProperty =
        BindableProperty.Create(nameof(HasTagIndicator), typeof(bool), typeof(AnimePosterCard), false);

    public bool HasTagIndicator
    {
        get => (bool)GetValue(HasTagIndicatorProperty);
        private set => SetValue(HasTagIndicatorProperty, value);
    }

    public static readonly BindableProperty HasNoteIndicatorProperty =
        BindableProperty.Create(nameof(HasNoteIndicator), typeof(bool), typeof(AnimePosterCard), false);

    public bool HasNoteIndicator
    {
        get => (bool)GetValue(HasNoteIndicatorProperty);
        private set => SetValue(HasNoteIndicatorProperty, value);
    }

    public static readonly BindableProperty HasAnyIndicatorProperty =
        BindableProperty.Create(nameof(HasAnyIndicator), typeof(bool), typeof(AnimePosterCard), false);

    /// <summary>True when at least one of the two is showing, so the row is only there for them.</summary>
    public bool HasAnyIndicator
    {
        get => (bool)GetValue(HasAnyIndicatorProperty);
        private set => SetValue(HasAnyIndicatorProperty, value);
    }

    /// <summary>
    ///     Recomputes the two indicators from the three inputs, the same way the counter pill is
    ///     derived from its three strings rather than each one carrying its own visibility.
    /// </summary>
    private static void OnIndicatorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (AnimePosterCard)bindable;
        card.HasTagIndicator = card.IndicatorsVisible && card.TagsPresent;
        card.HasNoteIndicator = card.IndicatorsVisible && card.NotePresent;
        card.HasAnyIndicator = card.HasTagIndicator || card.HasNoteIndicator;
    }

    /// <summary>
    ///     Position in a ranked list, as "#20". Empty on any panel that has no ranking.
    /// </summary>
    /// <remarks>
    ///     Its own property rather than one of the generic chips because the grid has to be able to
    ///     leave it blank without a page opting out: a template that always passes it means a panel
    ///     that does not care simply binds an empty string, and the cell's own visibility rule decides
    ///     whether it draws.
    /// </remarks>
    public static readonly BindableProperty RankTagProperty =
        BindableProperty.Create(nameof(RankTag), typeof(string), typeof(AnimePosterCard), default(string),
            propertyChanged: OnRankChanged);

    public string RankTag
    {
        get => (string)GetValue(RankTagProperty);
        set => SetValue(RankTagProperty, value);
    }

    /// <summary>
    ///     True when there is a rank to draw. Derived, because a Border with no text would still paint
    ///     its background and put a blue rectangle in the corner of every poster in the library.
    /// </summary>
    public static readonly BindableProperty HasRankProperty =
        BindableProperty.Create(nameof(HasRank), typeof(bool), typeof(AnimePosterCard), false);

    public bool HasRank
    {
        get => (bool)GetValue(HasRankProperty);
        private set => SetValue(HasRankProperty, value);
    }

    private static void OnRankChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (AnimePosterCard)bindable;
        card.HasRank = !string.IsNullOrWhiteSpace(card.RankTag);
    }

    public static readonly BindableProperty TitleOverlayProperty =
        BindableProperty.Create(nameof(TitleOverlay), typeof(bool), typeof(AnimePosterCard), true);

    public static readonly BindableProperty ChipSourceProperty =
        BindableProperty.Create(nameof(ChipSource), typeof(IEnumerable), typeof(AnimePosterCard), default(IEnumerable));

    public static readonly BindableProperty ChipTemplateProperty =
        BindableProperty.Create(nameof(ChipTemplate), typeof(DataTemplate), typeof(AnimePosterCard),
            default(DataTemplate));

    public AnimePosterCard()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Raised when a poster is held down. The grid re-raises it as
    ///     <see cref="AnimePosterGrid.ItemLongPressed" />, which is what hosts actually subscribe to.
    /// </summary>
    public event EventHandler<object> LongPressed;

    private void OnLongPressed(object sender, EventArgs e)
    {
        try
        {
            // The BindingContext is the view model, not the card: the card is recycled by the grid and
            // by this binding, while the model behind it is what an action has to act on.
            var context = BindingContext;
            if (context == null)
                return;

            LongPressed?.Invoke(this, context);

            // Also handed to the enclosing grid directly. A card lives inside a DataTemplate, so the
            // grid cannot subscribe to it from XAML, and five grids each passing their own cell
            // template is exactly the duplication this is meant to remove. Walking up to the grid is
            // what makes one recognizer cover the default template and every custom one.
            var parent = Parent;
            while (parent != null)
            {
                if (parent is AnimePosterGrid grid)
                {
                    grid.RaiseItemLongPressed(context);
                    return;
                }
                parent = parent.Parent;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS poster long press failed: " + ex.GetType().Name);
        }
    }

    public string PosterUrl
    {
        get => (string)GetValue(PosterUrlProperty);
        set => SetValue(PosterUrlProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string TypeTag
    {
        get => (string)GetValue(TypeTagProperty);
        set => SetValue(TypeTagProperty, value);
    }

    public string ScoreTag
    {
        get => (string)GetValue(ScoreTagProperty);
        set => SetValue(ScoreTagProperty, value);
    }

    public string SeasonTag
    {
        get => (string)GetValue(SeasonTagProperty);
        set => SetValue(SeasonTagProperty, value);
    }

    public bool BadgeVisible
    {
        get => (bool)GetValue(BadgeVisibleProperty);
        set => SetValue(BadgeVisibleProperty, value);
    }

    public string BadgeAccent
    {
        get => (string)GetValue(BadgeAccentProperty);
        set => SetValue(BadgeAccentProperty, value);
    }

    public Color BadgeAccentColor
    {
        get => (Color)GetValue(BadgeAccentColorProperty);
        set => SetValue(BadgeAccentColorProperty, value);
    }

    public string BadgeMain
    {
        get => (string)GetValue(BadgeMainProperty);
        set => SetValue(BadgeMainProperty, value);
    }

    public string BadgeExtra
    {
        get => (string)GetValue(BadgeExtraProperty);
        set => SetValue(BadgeExtraProperty, value);
    }

    public bool TitleOverlay
    {
        get => (bool)GetValue(TitleOverlayProperty);
        set => SetValue(TitleOverlayProperty, value);
    }

    public IEnumerable ChipSource
    {
        get => (IEnumerable)GetValue(ChipSourceProperty);
        set => SetValue(ChipSourceProperty, value);
    }

    public DataTemplate ChipTemplate
    {
        get => (DataTemplate)GetValue(ChipTemplateProperty);
        set => SetValue(ChipTemplateProperty, value);
    }
}
