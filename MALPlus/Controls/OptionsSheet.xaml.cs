using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace MALPlus.Controls;

public partial class OptionsSheet : ContentView
{
    /// <summary>Raised after a row is chosen, with that row.</summary>
    public event EventHandler<OptionsSheetItem> ItemChosen;

    /// <summary>Raised when the panel closes, whether by a row, the scrim or the back gesture.</summary>
    public event EventHandler Closed;

    /// <summary>True while the panel is on screen.</summary>
    public bool IsOpen => IsVisible;

    public OptionsSheet()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     The full sheet, as the details overflow menu: anchored above the navigation, with a handle
    ///     and a title.
    /// </summary>
    public void Open(string title, string subtitle, IEnumerable<OptionsSheetItem> items)
        => Present(title, subtitle, items, compact: false, onBack: null);

    /// <summary>
    ///     The compact card, as a poster long press wants it: floating in the middle, no handle and no
    ///     title. The subtitle is dropped too, because on a grid the entry is right there under the
    ///     panel and naming it again is the biggest thing on a five row menu.
    /// </summary>
    public void OpenCompact(IEnumerable<OptionsSheetItem> items)
        => Present(null, null, items, compact: true, onBack: null);

    /// <summary>
    ///     A level below the compact card, reached by choosing "Set status", "Set score" or "Priority".
    ///     It keeps a back row because there is no other way out of it, and a small title so the level
    ///     is legible; a chevron that goes nowhere is worse than one that comes back.
    /// </summary>
    public void OpenSubmenu(string title, IEnumerable<OptionsSheetItem> items, Action onBack)
        => Present(title, null, items, compact: true, onBack: onBack);

    /// <summary>
    ///     The compact card carrying a line of explanation above the rows.
    /// </summary>
    /// <remarks>
    ///     Wanted by the random picker, whose one useful piece of feedback is the filter it is about to
    ///     apply: "Random anime" and the summary of what is being searched. The compact card had no
    ///     subtitle of its own even though the label to draw one already exists, because a poster long
    ///     press does not need it: the entry is right there under the panel.
    /// </remarks>
public void OpenCompactWithSubtitle(string title, string subtitle, IEnumerable<OptionsSheetItem> items,
        string cancelLabel = "Cancel")
    {
        var list = items?.ToList() ?? new List<OptionsSheetItem>();
        if (!string.IsNullOrWhiteSpace(cancelLabel) &&
            !list.Any(item => string.Equals(item.Label, cancelLabel, StringComparison.Ordinal)))
            list.Add(new OptionsSheetItem(cancelLabel, "icon_close"));
        Present(title, subtitle, list, compact: true, onBack: null, withClose: true);
    }

    /// <summary>
    ///     The compact card with a line of explanation, for a list of options that needs one.
    /// </summary>
    /// <remarks>
    ///     The same card as everything else, with the rows replaced by a field and a commit row. An empty
    ///     field is a real answer and is handed over as such, which is why this takes two callbacks
    ///     rather than coming back through <see cref="Closed"/>: that one cannot tell a commit from a
    ///     cancellation.
    /// </remarks>
    public void OpenPrompt(string title, string subtitle, string initial, string hint, int maxLength,
        bool withClose, Action<string> onSave, Action onCancel)
    {
        try
        {
            ApplyCompactShape();

            var hasHeader = !string.IsNullOrWhiteSpace(title) ||
                            !string.IsNullOrWhiteSpace(subtitle) ||
                            withClose;
            Header.IsVisible = hasHeader;
            CloseButton.IsVisible = withClose;
            TitleLabel.Text = title ?? "";
            TitleLabel.FontSize = string.IsNullOrWhiteSpace(subtitle) ? 16 : 15;

            if (string.IsNullOrWhiteSpace(subtitle))
            {
                SubtitleLabel.Text = "";
                SubtitleLabel.IsVisible = false;
            }
            else
            {
                SubtitleLabel.Text = subtitle;
                SubtitleLabel.IsVisible = true;
            }

            var entry = new Border
            {
                BackgroundColor = Color.FromArgb("#0A1F2E"),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(12, 0),
                HeightRequest = 48,
                Stroke = Color.FromArgb("#1F94A3B8"),
                StrokeThickness = 1
            };
            var field = new Entry
            {
                Text = initial ?? string.Empty,
                Keyboard = Keyboard.Numeric,
                MaxLength = maxLength,
                FontFamily = "InterMedium",
                FontSize = 15,
                TextColor = Colors.White,
                Placeholder = hint ?? string.Empty,
                PlaceholderColor = Color.FromArgb("#66FFFFFF"),
                BackgroundColor = Colors.Transparent,
                ClearButtonVisibility = ClearButtonVisibility.Never
            };
            entry.Content = field;

            ItemsHost.Clear();
            ItemsHost.Add(entry);

            var capturedField = field;
            var capturedSave = onSave;
            var capturedCancel = onCancel;
            // Ordinary rows, and deliberately so: the panel closes the same way it does for a choice
            // and the answer is whatever was in the field when the row was tapped. An empty field is a
            // real answer and is handed over as such, which is why this does not come back through
            // Closed: that one cannot tell a commit from an abandonment.
            ItemsHost.Add(BuildRow(new OptionsSheetItem("Save", "icon_check",
                action: () => capturedSave(capturedField.Text))));

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet prompt failed: " + ex.Message);
        }
    }

    private void ApplyCompactShape()
    {
        Panel.HorizontalOptions = LayoutOptions.Center;
        Panel.VerticalOptions = LayoutOptions.Center;
        Panel.WidthRequest = 264;
        Panel.Margin = new Thickness(0);
        Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) };
        RowsScroll.MaximumHeightRequest = 520;
        Handle.IsVisible = false;
    }

    private void Present(string title, string subtitle, IEnumerable<OptionsSheetItem> items, bool compact,
        Action onBack, bool withClose = false)
    {
        try
        {
            if (compact)
            {
                Panel.HorizontalOptions = LayoutOptions.Center;
                Panel.VerticalOptions = LayoutOptions.Center;
                Panel.WidthRequest = 264;
                Panel.Margin = new Thickness(0);
                Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) };
                RowsScroll.MaximumHeightRequest = 520;
                Handle.IsVisible = false;
            }
            else
            {
                Panel.HorizontalOptions = LayoutOptions.Fill;
                Panel.VerticalOptions = LayoutOptions.End;
                Panel.WidthRequest = -1;
                // Flush to the bottom edge. The 58dp lift was for the list page's navigation bar, and
                // on a page with no bar it left the sheet floating.
                Panel.Margin = new Thickness(0);
                Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) };
                RowsScroll.MaximumHeightRequest = 620;
                Handle.IsVisible = true;
            }

            // The subtitle lives inside the header's own grid, so a panel carrying one has to show the header
            // even when there is no title to put in it. A confirm with only a message is exactly that,
            // and hiding the header there hid the message and left a panel of two buttons saying
            // nothing about what it was confirming. The close button is the third reason: it is the only
            // way out of a panel that carries no Cancel row, and it lives in the header too, so a panel
            // relying on it has to have one.
            var hasHeader = !string.IsNullOrWhiteSpace(title) ||
                            !string.IsNullOrWhiteSpace(subtitle) ||
                            onBack != null ||
                            withClose;
            Header.IsVisible = hasHeader;
            CloseButton.IsVisible = withClose;
            if (hasHeader)
            {
                // No chevron in the title: there is a real Back row right underneath, and a title
                // that looks like it goes somewhere it does not is worse than a plain heading.
                TitleLabel.Text = title ?? "";
                TitleLabel.FontSize = onBack != null ? 14 : 16;
            }
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                SubtitleLabel.Text = "";
                SubtitleLabel.IsVisible = false;
            }
            else
            {
                SubtitleLabel.Text = subtitle;
                SubtitleLabel.IsVisible = true;
            }

            ItemsHost.Clear();
            if (onBack != null)
            {
                var back = new Border
                {
                    BackgroundColor = Color.FromArgb("#0A1F2E"),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(12, 0),
                    HeightRequest = 48
                };
                var backContent = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };
                var backChip = new Border
                {
                    WidthRequest = 32,
                    HeightRequest = 32,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                    BackgroundColor = Color.FromArgb("#1A94A3B8"),
                    Padding = new Thickness(0)
                };
                backChip.Content = new Image
                {
                    Source = "icon_chevron_left",
                    WidthRequest = 18,
                    HeightRequest = 18,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    InputTransparent = true
                };
                backContent.Add(backChip);
                backContent.Add(new Label
                {
                    Text = "Back",
                    FontFamily = "InterMedium",
                    FontSize = 15,
                    TextColor = Color.FromArgb("#8FA8C0"),
                    VerticalOptions = LayoutOptions.Center
                });
                back.Content = backContent;
                var backTap = new TapGestureRecognizer();
                var captured = onBack;
                backTap.Tapped += (_, _) => captured();
                back.GestureRecognizers.Add(backTap);
                ItemsHost.Add(back);
            }

            foreach (var item in items ?? Enumerable.Empty<OptionsSheetItem>())
            {
                var row = BuildRow(item);
                if (row != null)
                    ItemsHost.Add(row);
            }

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet open failed: " + ex.Message);
        }
    }

    public void Dismiss()
    {
        if (!IsVisible)
            return;
        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     One row.
    ///     <para>
    ///     It is a Border rather than a Button because a Button cannot host content in this MAUI
    ///     version, and the row needs an icon chip, a label and a trailing element side by side. The
    ///     background is therefore the panel's own fill, which is what makes a Border carrying only a
    ///     tap recognizer hit tested on Android.
    ///     </para>
    /// </summary>
    private View BuildRow(OptionsSheetItem item)
    {
        var sheet = Color.FromArgb("#0A1F2E");
        var destructive = Color.FromArgb("#F87171");

        var border = new Border
        {
            BackgroundColor = sheet,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(12, 0),
            HeightRequest = 48
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("Auto") }
        };

        var rowContent = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };

        if (!string.IsNullOrWhiteSpace(item.Icon))
        {
            var chip = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                BackgroundColor = ChipTint(item),
                Padding = new Thickness(0)
            };
            chip.Content = new Image
            {
                Source = item.Icon,
                WidthRequest = 18,
                HeightRequest = 18,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                // The row is the tap target; the glyph must not take the touch.
                InputTransparent = true
            };
            rowContent.Add(chip);
        }

        rowContent.Add(new Label
        {
            Text = item.Label,
            // Semibold on the destructive row, medium on the rest, matching the mockup.
            FontFamily = item.Destructive ? "InterSemiBold" : "InterMedium",
            FontSize = 15,
            TextColor = item.Destructive ? destructive : Colors.White,
            VerticalOptions = LayoutOptions.Center
        });
        grid.Add(rowContent);

        if (!string.IsNullOrWhiteSpace(item.Value))
        {
            var value = new Label
            {
                Text = item.Value,
                FontFamily = "InterMedium",
                FontSize = 11,
                TextColor = Color.FromArgb("#8FA8C0"),
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(value, 1);
            grid.Add(value);
        }
        // A submenu normally means there is a deeper level, which is what the arrow is for. A row
        // that commits where it stands says so with HideChevron, and one of those pointing at a
        // sub panel was the Discover format tick boxes promising a list that never opened.
        else if (item.ShowChevron || (item.Submenu != null && !item.HideChevron))
        {
            var chevron = new Image
            {
                Source = "icon_chevron_right",
                WidthRequest = 18,
                HeightRequest = 18,
                Opacity = 0.5,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Grid.SetColumn(chevron, 1);
            grid.Add(chevron);
        }

        border.Content = grid;

        var captured = item;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Choose(captured);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    /// <summary>
    ///     The chip's own tint, which is how the mockup tells actions apart without leaning on the
    ///     label. Each family gets its own colour rather than one accent for everything.
    /// </summary>
    private static Color ChipTint(OptionsSheetItem item)
    {
        if (item.Destructive)
            return Color.FromArgb("#26F87171");
        var icon = item.Icon ?? "";
        if (icon.Contains("forum", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A60A5FA");
        if (icon.Contains("video", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("play", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1AC084FC");
        if (icon.Contains("tag", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A2DD4BF");
        if (icon.Contains("score", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1AFBBF24");
        if (icon.Contains("episodes", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("watched", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("read", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A818CF8");
        if (icon.Contains("priority", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A38BDF8");
        if (icon.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("list", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A34D399");
        if (icon.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("search", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A38BDF8");
        if (icon.Contains("copy", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb("#1A94A3B8");
        return Color.FromArgb("#1A0066FF");
    }

    private void Choose(OptionsSheetItem item)
    {
        // A row that opens a submenu does not close the panel: the submenu replaces its contents, and
        // closing first left a blank sheet on screen for the length of the next frame.
        if (item.Submenu != null)
        {
            try
            {
                item.Submenu(this);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS options submenu failed: " + ex.GetType().Name);
                Dismiss();
                return;
            }
        }

        // Marked before the panel closes, because Dismiss raises Closed and a host listening to both has to be
        // able to tell this close apart from one that came from the scrim or the back gesture. Without it
        // the dismissal looks like a cancellation and answers the caller before the row gets to run: every
        // prompt's commit row lost to its own cancel path, which is why typing a value and pressing Save
        // did nothing at all.
        ClosingByChoice = true;
        Dismiss();
        ClosingByChoice = false;
        try
        {
            if (item.Action != null)
            {
                item.Action();
            }
            else if (item.Command != null && item.Command.CanExecute(item.CommandParameter))
            {
                item.Command.Execute(item.CommandParameter);
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Info("MALPlus",
                $"MALPLUS sheet row FAILED '{item.Label}': {ex.GetType().Name}: {ex.Message}");
        }
        ItemChosen?.Invoke(this, item);
    }

    /// <summary>
    ///     True while the panel is closing because a row was chosen, rather than by scrim or back.
    /// </summary>
    /// <remarks>
    ///     Set only around the close that a row causes, and read from inside Closed, which fires before the
    ///     row's action does. A host treats a close with this set as answered and one without it as a
    ///     cancellation.
    /// </remarks>
    public bool ClosingByChoice { get; private set; }

    private void OnScrimTapped(object sender, TappedEventArgs e)
    {
        Dismiss();
    }
}
