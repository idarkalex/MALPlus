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

    /// <summary>Width over height, from the artwork: 2064 / 753.</summary>
    public const double CapsuleAspect = 2064d / 753d;

    /// <summary>Raised on tap. No payload: the page knows which handler it wired.</summary>
    public event EventHandler Tapped;

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
}
