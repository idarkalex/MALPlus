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
        // No Cancel row. This panel presents with a close button in its header, which is already the
        // way out, so the row underneath was a second control for the same thing on every card opened
        // this way. It read worst on the status picker, where it put a full row between the last
        // choice and the bottom edge of the screen.
        // cancelLabel is kept so the callers that still pass one keep compiling; it no longer draws
        // anything.
        Present(title, subtitle, items?.ToList() ?? new List<OptionsSheetItem>(),
            compact: true, onBack: null, withClose: true);
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
        bool withClose, Action<string> onSave, Action onCancel, bool multiline = false,
        Keyboard? keyboard = null, bool closeOnSave = true)
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

            View field;
            if (multiline)
            {
                // A note is paragraphs, not a score. The single line Entry this shares with the
                // other prompts is 48dp tall with a numeric keyboard, which is unusable for one and
                // hides all but the first line of what was written.
                field = new Editor
                {
                    Text = initial ?? string.Empty,
                    Keyboard = Keyboard.Text,
                    MaxLength = maxLength,
                    FontFamily = "InterMedium",
                    FontSize = 15,
                    TextColor = Colors.White,
                    Placeholder = hint ?? string.Empty,
                    PlaceholderColor = Color.FromArgb("#66FFFFFF"),
                    BackgroundColor = Colors.Transparent,
                    HeightRequest = 150
                };
            }
            else
            {
                field = new Entry
                {
                    Text = initial ?? string.Empty,
                    // Numeric is the default because every caller that used to reach here wanted it.
                    // A text field that opens a numeric keyboard silently swallows every letter
                    // typed into it, which is how the tag prompt looked broken rather than wrong.
                    Keyboard = keyboard ?? Keyboard.Numeric,
                    MaxLength = maxLength,
                    FontFamily = "InterMedium",
                    FontSize = 15,
                    TextColor = Colors.White,
                    Placeholder = hint ?? string.Empty,
                    PlaceholderColor = Color.FromArgb("#66FFFFFF"),
                    BackgroundColor = Colors.Transparent,
                    ClearButtonVisibility = ClearButtonVisibility.Never
                };
            }

            var entry = new Border
            {
                BackgroundColor = Color.FromArgb("#0A1F2E"),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(12, multiline ? 8 : 0),
                HeightRequest = multiline ? 150 : 48,
                Stroke = Color.FromArgb("#1F94A3B8"),
                StrokeThickness = 1
            };
            entry.Content = field;

            ItemsHost.Clear();
            ItemsHost.Add(entry);

            var capturedSave = onSave;
            // The field is a View because it is either an Entry or an Editor, and View has no Text.
            // Read through whichever one it turned out to be at the moment the row is tapped, which
            // is also when the answer is decided.
            Func<string> readField = () => field switch
            {
                Editor editor => editor.Text,
                Entry entryControl => entryControl.Text,
                _ => string.Empty
            };
            var capturedCancel = onCancel;
            if (closeOnSave)
            {
                // Ordinary rows, and deliberately so: the panel closes the same way it does for a choice
                // and the answer is whatever was in the field when the row was tapped.
                ItemsHost.Add(BuildRow(new OptionsSheetItem("Save", "icon_check",
                    action: () => capturedSave(readField()))));
            }
            else
            {
                // In a multi-level sheet, we don't dismiss the sheet when saving an in-place field;
                // instead, the submenu callback runs onSave without triggering Dismiss/ItemChosen,
                // allowing the caller to redraw the parent menu in place.
                ItemsHost.Add(BuildRow(new OptionsSheetItem("Save", "icon_check",
                    submenu: _ =>
                    {
                        capturedSave?.Invoke(readField());
                        return true;
                    })));

                if (capturedCancel != null)
                {
                    ItemsHost.Add(BuildRow(new OptionsSheetItem("Cancel", "icon_close",
                        submenu: _ =>
                        {
                            capturedCancel();
                            return true;
                        })));
                }
            }

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet prompt failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     A question with two answers, and both of them in the same row.
    /// </summary>
    /// <remarks>
    ///     A confirm used to be built by handing <see cref="OpenCompactWithSubtitle" /> a list of
    ///     ordinary rows, which stacks them. That reads as a menu rather than a question, and it is
    ///     worse when the caller has a real label for each answer: the two labels then land in two
    ///     rows with a full row height between them, so "Yes" and "No" do not look like the two sides
    ///     of one decision.
    /// </remarks>
    public void OpenConfirm(string title, string subtitle, string okLabel, string cancelLabel,
        bool destructive = false, Action onConfirm = null)
    {
        try
        {
            ApplyCompactShape();

            Header.IsVisible = true;
            CloseButton.IsVisible = false;
            TitleLabel.Text = title ?? "";
            // Same size the prompts use: the smaller one is for a title that has a subtitle under it.
            TitleLabel.FontSize = string.IsNullOrWhiteSpace(subtitle) ? 16 : 15;
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                SubtitleLabel.Text = "";
                SubtitleLabel.IsVisible = false;
                SubtitleLabel.MaxLines = 1;
                SubtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
            }
            else
            {
                SubtitleLabel.Text = subtitle;
                SubtitleLabel.IsVisible = true;
                // The question under the title is a sentence, not a caption. It was declared for one
                // line with a tail, so every confirmation that said anything longer than the card's
                // width lost the end of itself behind an ellipsis: "Are you sure about deleting this
                // entry fro...". Wrapped, with a ceiling so a long one still cannot take the card over.
                SubtitleLabel.MaxLines = 4;
                SubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
            }

            var capturedConfirm = onConfirm;

            // Returns the Border itself, not a View, so the caller can place it in a column: SetColumn needs
            // an IView and the declared return type was hiding that.
            Border MakeButton(string label, bool commit, bool danger, bool cancels)
            {
                // A centred label, no icon chip. The rows in the sheet carry a 32dp chip because they
                // are full width, but two of them side by side leaves each about 128dp, and a chip plus
                // a 12dp gap in there costs 44 of it. That is why "I'm sure" was cut at "I'm sur":
                // the label had no room left. Here the label fills the button and is centred, and the
                // accent carries the answer rather than a block of colour.
                var labelColor = danger ? Color.FromArgb("#F87171") : commit ? Color.FromArgb("#0066FF") : Colors.White;

                var border = new Border
                {
                    BackgroundColor = Color.FromArgb("#0A1F2E"),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(12, 0),
                    HeightRequest = 48,
                    HorizontalOptions = LayoutOptions.Fill
                };

                border.Content = new Label
                {
                    Text = label,
                    FontFamily = danger ? "InterSemiBold" : "InterMedium",
                    FontSize = 15,
                    TextColor = labelColor,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1,
                    InputTransparent = true
                };

                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) =>
                {
                    // The commit has to close as a choice, for the same reason Choose marks it: a
                    // plain Dismiss raises Closed, and a host listening to that answers the question
                    // with "no" a moment before the commit gets to answer it with yes.
                    if (cancels)
                    {
                        Dismiss();
                    }
                    else
                    {
                        ClosingByChoice = true;
                        Dismiss();
                        ClosingByChoice = false;
                        capturedConfirm?.Invoke();
                    }
                };
                border.GestureRecognizers.Add(tap);
                return border;
            }

            // No margin above: the panel's own padding already opens the card, and a second one here
            // is the gap that made this panel look emptier than the prompts beside it.
            // Two equal columns, so the pair spans it the way the rows span the sheet's panel. Measured
            // as a stack, the two sat together in the middle with the card's width either side of them,
            // which read as a narrower card than the prompt above it came from.
            // Two equal columns, so the pair spans the card the way the rows span the sheet's panel.
            // Measured as a stack, the two sat together in the middle with the card's width either
            // side of them, which read as a narrower card than the prompt above it came from.
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("*") }
            };
            var okButton = MakeButton(okLabel, true, destructive, false);
            var cancelButton = MakeButton(cancelLabel, false, false, true);
            // Set on the buttons themselves before they go in: in MAUI an element added to a Grid
            // without a column goes to column 0, so both would otherwise stack in the left one and the
            // confirm row would come out as a single button.
            Microsoft.Maui.Controls.Grid.SetColumn(okButton, 0);
            Microsoft.Maui.Controls.Grid.SetColumn(cancelButton, 1);
            row.Add(okButton);
            row.Add(cancelButton);

            ItemsHost.Clear();
            ItemsHost.Add(row);

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet confirm failed: " + ex.Message);
        }
    }

    public void OpenUpdate(string title, string subtitle, string notes, string okLabel, string cancelLabel,
        Action onConfirm = null)
    {
        try
        {
            ApplyCompactShape();
            Panel.WidthRequest = 310;

            Header.IsVisible = true;
            CloseButton.IsVisible = true;
            TitleLabel.Text = title ?? "Update";
            TitleLabel.FontSize = 16;
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                SubtitleLabel.Text = "";
                SubtitleLabel.IsVisible = false;
            }
            else
            {
                SubtitleLabel.Text = subtitle;
                SubtitleLabel.IsVisible = true;
                SubtitleLabel.MaxLines = 2;
                SubtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
            }

            ItemsHost.Clear();

            if (!string.IsNullOrWhiteSpace(notes))
            {
                var notesBorder = new Border
                {
                    BackgroundColor = Color.FromArgb("#0A1F2E"),
                    Stroke = Color.FromArgb("#1E3A52"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Padding = new Thickness(12, 10),
                    Margin = new Thickness(0, 4, 0, 8),
                    MaximumHeightRequest = 240
                };

                var notesScroll = new ScrollView
                {
                    Content = new Label
                    {
                        Text = notes,
                        FontFamily = "Inter",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#D4E4F7"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                };
                notesBorder.Content = notesScroll;
                ItemsHost.Add(notesBorder);
            }

            Border MakeButton(string label, bool commit, bool cancels)
            {
                var labelColor = commit ? Colors.White : Color.FromArgb("#8FA8C0");
                var border = new Border
                {
                    BackgroundColor = commit ? Color.FromArgb("#0066FF") : Color.FromArgb("#14293D"),
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Padding = new Thickness(10, 0),
                    HeightRequest = 44,
                    HorizontalOptions = LayoutOptions.Fill
                };

                border.Content = new Label
                {
                    Text = label,
                    FontFamily = commit ? "InterSemiBold" : "InterMedium",
                    FontSize = 14,
                    TextColor = labelColor,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1,
                    InputTransparent = true
                };

                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) =>
                {
                    if (cancels)
                    {
                        Dismiss();
                    }
                    else
                    {
                        ClosingByChoice = true;
                        Dismiss();
                        ClosingByChoice = false;
                        onConfirm?.Invoke();
                    }
                };
                border.GestureRecognizers.Add(tap);
                return border;
            }

            if (string.IsNullOrWhiteSpace(cancelLabel))
            {
                var singleBtn = MakeButton(okLabel ?? "OK", true, false);
                ItemsHost.Add(singleBtn);
            }
            else
            {
                var row = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("*") }
                };
                var cancelButton = MakeButton(cancelLabel, false, true);
                var okButton = MakeButton(okLabel ?? "OK", true, false);
                Microsoft.Maui.Controls.Grid.SetColumn(cancelButton, 0);
                Microsoft.Maui.Controls.Grid.SetColumn(okButton, 1);
                row.Add(cancelButton);
                row.Add(okButton);
                ItemsHost.Add(row);
            }

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OpenUpdate failed: " + ex.Message);
        }
    }

    private void ApplyCompactShape()
    {
        Panel.HorizontalOptions = LayoutOptions.Center;
        Panel.VerticalOptions = LayoutOptions.Center;
        Panel.WidthRequest = 264;
        Panel.Margin = new Thickness(0);
        Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) };
        // A centred card carries its own breathing room: the sheet shape gets it from being flush to
        // the bottom of the screen, and the rows host below only ever padded its sides.
        //
        // These four numbers are the card's css, in one place, for every panel that uses this shape:
        // the confirms, the note and tag prompts, and everything raised through OptionsSheetHost.
        // They are tight on purpose. An earlier pass put 12 above and 18 below to even out a header
        // margin that has since changed, and the result was a card that read as empty at both ends.
        Panel.Padding = new Thickness(0, 14, 0, 8);
        Header.Margin = new Thickness(20, 0, 12, 8);

        // The header and everything in it hang from the top of the card rather than sitting wherever
        // the 32dp close button happens to land. Measured on a device with the header's own layout
        // untouched, the panel's border sat 45dp above the title, and cutting 10dp of padding only
        // took it to 35dp: the block was being centred in a header taller than the text in it, so
        // the empty band was above the title rather than beside it.
        Header.VerticalOptions = LayoutOptions.Start;
        TitleLabel.VerticalOptions = LayoutOptions.Start;
        SubtitleLabel.VerticalOptions = LayoutOptions.Start;
        CloseButton.VerticalOptions = LayoutOptions.Start;

        // The drag handle belongs to the sheet shape, and hiding the BoxView is not enough to take its
        // row with it. IsVisible=false, HeightRequest=0 and Margin=0 together all left the row 24dp
        // tall on a device: the uiautomator tree still reported it, occupying exactly the band that
        // was measured above the title. The row itself has to collapse, so it is the row's height
        // that goes, not the bar's.
        Handle.IsVisible = false;
        PanelGrid.RowDefinitions[0].Height = new GridLength(0);

        // A hairline so the card reads as a surface rather than as a slightly different patch of the
        // background. #29FFFFFF is the same weight the chips and the details action bar use; EmBorder
        // (#1E3A52) is the theme's own line and is too close to the fill to separate anything.
        Panel.Stroke = Color.FromArgb("#29FFFFFF");
        Panel.StrokeThickness = 1;

        RowsScroll.MaximumHeightRequest = 520;
    }

    private void Present(string title, string subtitle, IEnumerable<OptionsSheetItem> items, bool compact,
        Action onBack, bool withClose = false)
    {
        try
        {
            if (compact)
            {
                // One shape, one place. This used to be a second copy of ApplyCompactShape inline,
                // which is how the compact padding ended up surviving into the sheet: the else branch
                // below never reset it, so opening a prompt and then the overflow left the full sheet
                // wearing the card's padding.
                ApplyCompactShape();
                Handle.IsVisible = false;
            }
            else
            {
                Panel.HorizontalOptions = LayoutOptions.Fill;
                Panel.VerticalOptions = LayoutOptions.End;
                Panel.WidthRequest = -1;
                // Flush to the bottom edge. The 58dp lift was for the list page's navigation bar, and on
                // a page with no bar it left the sheet floating. Measured on device, the panel then ends
                // at the last row of pixels of the screen, so there is nothing left to take off.
                Panel.Margin = new Thickness(0);
                Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) };
                // Flush to the bottom means flush to the bottom, and none of the card's chrome comes
                // with it: the centred shape's padding, its hairline and its header margin are all
                // restored to the XAML values here. Without this, opening a prompt and then the
                // overflow left the full sheet lifted off the screen edge and wearing a border.
                Panel.Padding = new Thickness(0);
                Panel.Stroke = Colors.Transparent;
                Panel.StrokeThickness = 0;
                Header.Margin = new Thickness(20, 6, 12, 10);
                RowsScroll.MaximumHeightRequest = 620;
                Handle.IsVisible = true;
                // The card collapses the handle's row rather than the handle, so the sheet has to give
                // that row its height back. Prompts and the overflow share this control.
                PanelGrid.RowDefinitions[0].Height = GridLength.Auto;
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
