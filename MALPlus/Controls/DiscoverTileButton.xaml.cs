namespace MALPlus.Controls;

/// <summary>
///     A Discover destination, drawn as the capsule from its artwork and nothing else.
///     <para>
///     The page wires eleven of these and nothing else, so the button's proportions live here rather
///     than in the layout: the exported files are 2064x753 out of the source artwork, and a control
///     that has to be told its own height invites the capsules to be stretched.
///     </para>
/// </summary>
public partial class DiscoverTileButton : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(DiscoverTileButton), default(string),
            propertyChanged: (b, _, v) => ((DiscoverTileButton)b).Art.Source = v as string);

    /// <summary>Name of a file in Resources/Images, without the extension.</summary>
    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Width over height, from the exported artwork: 620 / 248.</summary>
    public const double CapsuleAspect = 620d / 248d;

    /// <summary>
    ///     How much of the right hand edge opens the filters rather than the destination.
    /// </summary>
    /// <remarks>
    ///     The two random capsules carry a gear on the right and it is painted into the artwork, so
    ///     tapping the left of the picture has to open the entry and tapping the gear has to open the
    ///     filters. A quarter is a starting point, not something measured: the capsule is a gradient,
    ///     so the gear cannot be located by looking at the pixels. It is a named constant so moving the
    ///     line is one edit.
    /// </remarks>
    public const double GearZoneFraction = 0.25d;

    /// <summary>Raised on tap. No payload: the page knows which handler it wired.</summary>
    public event EventHandler Tapped;

    /// <summary>Raised when the tap landed on the gear zone instead of the picture.</summary>
    public event EventHandler GearTapped;

    public DiscoverTileButton()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || double.IsNaN(width))
            return;

        // Setting HeightRequest from inside the allocation would loop, so it only happens when the
        // height is actually wrong by more than half a pixel.
        var wanted = width / CapsuleAspect;
        if (double.IsNaN(HeightRequest) || Math.Abs(HeightRequest - wanted) > 0.5)
            HeightRequest = wanted;

        // The gear strip is a quarter of the width, laid out from the right edge rather than as a
        // ColumnDefinition, so a page that never uses it pays nothing and a page that does not have to
        // know the fraction.
        var gearWidth = width * GearZoneFraction;
        if (Math.Abs(GearZone.WidthRequest - gearWidth) > 0.5)
            GearZone.WidthRequest = gearWidth;
    }

    private void OnTapped(object sender, TappedEventArgs e)
    {
        try
        {
            Tapped?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus", "MALPLUS discover tile tap failed: " + ex.GetType().Name);
        }
    }

    private void OnGearTapped(object sender, TappedEventArgs e)
    {
        // A tile with no gear handler keeps the old behaviour, which is the whole picture opening the
        // destination. Otherwise the gear strip would be a strip that swallows taps and does nothing.
        if (GearTapped == null)
        {
            OnTapped(sender, e);
            return;
        }

        try
        {
            GearTapped(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus", "MALPLUS discover gear tap failed: " + ex.GetType().Name);
        }
    }
}
