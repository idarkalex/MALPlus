using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace MALPlus.Controls;

public partial class OptionsSheet : ContentView
{
    // ── Cached colours / shapes ──────────────────────────────────────────────
    // These are instantiated once and reused across every BuildRow() call.
    // Before this, Color.FromArgb() was called for EVERY row on EVERY menu open,
    // allocating dozens of short-lived objects per interaction and pressuring the GC.
    private static Color _colorSheet =>
        Application.Current?.Resources.TryGetValue("EmSheet", out var val) == true && val is Color c ? c : Color.FromArgb("#0A1F2E");
    private static readonly Color _colorDestructive  = Color.FromArgb("#F87171");
    private static Color _colorMuted =>
        Application.Current?.Resources.TryGetValue("TabChipInactiveText", out var val) == true && val is Color c ? c : Color.FromArgb("#8FA8C0");
    private static readonly Color _colorBorder       = Color.FromArgb("#29FFFFFF");
    private static readonly Color _colorBackChip     = Color.FromArgb("#1A94A3B8");
    private static readonly Color _colorNoteHint     = Color.FromArgb("#66FFFFFF");
    private static readonly Color _colorConfirmOk    = Color.FromArgb("#0066FF");
    private static Color _colorConfirmBg => _colorSheet;
    private static Color _colorUpdateNotes => _colorSheet;
    private static Color _colorUpdateStroke =>
        Application.Current?.Resources.TryGetValue("EmBorder", out var val) == true && val is Color c ? c : Color.FromArgb("#1E3A52");
    private static readonly Color _colorUpdateText   = Color.FromArgb("#D4E4F7");
    private static readonly Color _colorUpdateOk     = Color.FromArgb("#0066FF");
    private static Color _colorUpdateCancel =>
        Application.Current?.Resources.TryGetValue("TabChipInactiveBg", out var val) == true && val is Color c ? c : Color.FromArgb("#141D2B");
    private static readonly Color _colorUpdateCancelText = Color.FromArgb("#8FA8C0");
    private static readonly Color _colorPromptBorder = Color.FromArgb("#1F94A3B8");
    private static readonly Color _colorError        = Color.FromArgb("#FF5555");

    // Chip tint cache: computed once per icon string rather than on every row build.
    private static readonly Dictionary<string, Color> _chipTintCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Color _chipTintForum     = Color.FromArgb("#1A60A5FA");
    private static readonly Color _chipTintVideo     = Color.FromArgb("#1AC084FC");
    private static readonly Color _chipTintTag       = Color.FromArgb("#1A2DD4BF");
    private static readonly Color _chipTintScore     = Color.FromArgb("#1AFBBF24");
    private static readonly Color _chipTintEpisodes  = Color.FromArgb("#1A818CF8");
    private static readonly Color _chipTintPriority  = Color.FromArgb("#1A38BDF8");
    private static readonly Color _chipTintStatus    = Color.FromArgb("#1A34D399");
    private static readonly Color _chipTintBrowser   = Color.FromArgb("#1A38BDF8");
    private static readonly Color _chipTintCopy      = Color.FromArgb("#1A94A3B8");
    private static readonly Color _chipTintDestructive = Color.FromArgb("#26F87171");
    private static readonly Color _chipTintDefault   = Color.FromArgb("#1A0066FF");
    // ── End cached colours / shapes ─────────────────────────────────────────
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
                SubtitleLabel.MaxLines = 1;
                SubtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
            }
            else
            {
                SubtitleLabel.Text = subtitle;
                SubtitleLabel.IsVisible = true;
                SubtitleLabel.MaxLines = 6;
                SubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
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
                    PlaceholderColor = _colorNoteHint,
                    BackgroundColor = Colors.Transparent,
                    HeightRequest = 150,
                    VerticalOptions = LayoutOptions.Fill
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
                    PlaceholderColor = _colorNoteHint,
                    BackgroundColor = Colors.Transparent,
                    ClearButtonVisibility = ClearButtonVisibility.Never,
                    VerticalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.Center
                };
            }

            var entry = new Border
            {
                BackgroundColor = _colorSheet,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(14, multiline ? 10 : 0),
                HeightRequest = multiline ? 150 : 48,
                Stroke = _colorPromptBorder,
                StrokeThickness = 1,
                Margin = new Thickness(0, 4, 0, 8),
                VerticalOptions = LayoutOptions.Center
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
                var saveButton = CreateActionButton("Save", () =>
                {
                    ClosingByChoice = true;
                    Dismiss();
                    ClosingByChoice = false;
                    capturedSave(readField());
                }, ActionButtonKind.Primary);
                saveButton.Margin = new Thickness(0, 2, 0, 4);
                ItemsHost.Add(saveButton);
            }
            else
            {
                if (capturedCancel != null)
                {
                    var cancelBtn = CreateActionButton("Cancel", () =>
                    {
                        capturedCancel();
                    }, ActionButtonKind.Secondary);
                    var saveBtn = CreateActionButton("Save", () =>
                    {
                        capturedSave?.Invoke(readField());
                    }, ActionButtonKind.Primary);
                    ItemsHost.Add(CreateActionPair(cancelBtn, saveBtn));
                }
                else
                {
                    var saveBtn = CreateActionButton("Save", () =>
                    {
                        capturedSave?.Invoke(readField());
                    }, ActionButtonKind.Primary);
                    saveBtn.Margin = new Thickness(0, 2, 0, 4);
                    ItemsHost.Add(saveBtn);
                }
            }

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet prompt failed: " + ex.Message);
        }
    }

    public void OpenSharePreview(Models.Models.Anime.ShareCardModel model, Action<string, bool, string> onShare, Action onCancel)
    {
        try
        {
            ApplyCompactShape();

            Header.IsVisible = true;
            CloseButton.IsVisible = true;
            BackButton.IsVisible = false;
            TitleLabel.Text = "Share";
            TitleLabel.FontSize = 16;
            SubtitleLabel.Text = "";
            SubtitleLabel.IsVisible = false;

            // 1. Preview Card Container
            var spinner = new ActivityIndicator
            {
                IsRunning = true,
                IsVisible = true,
                Color = Color.FromArgb("#0066FF"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 32,
                HeightRequest = 32
            };

            var previewImage = new Image
            {
                Aspect = Aspect.AspectFit,
                IsVisible = false,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            var cardGrid = new Grid();
            cardGrid.Children.Add(spinner);
            cardGrid.Children.Add(previewImage);

            var previewCard = new Border
            {
                BackgroundColor = Color.FromArgb("#051522"),
                Stroke = Color.FromArgb("#1E3A52"),
                StrokeThickness = 1.5,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                WidthRequest = 140,
                HeightRequest = 208,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 4, 0, 8),
                Padding = 0,
                Content = cardGrid
            };

            string cardPath = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    cardPath = await Services.ShareCardGenerator.GenerateCardAsync(model);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        spinner.IsRunning = false;
                        spinner.IsVisible = false;
                        if (!string.IsNullOrEmpty(cardPath) && File.Exists(cardPath))
                        {
                            previewImage.Source = ImageSource.FromFile(cardPath);
                            previewImage.IsVisible = true;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS card generation preview failed: " + ex.Message);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        spinner.IsRunning = false;
                        spinner.IsVisible = false;
                    });
                }
            });

            // 2. Message Editor
            var editor = new Editor
            {
                Text = model?.ShareText ?? string.Empty,
                Keyboard = Keyboard.Text,
                FontFamily = "InterMedium",
                FontSize = 13,
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                HeightRequest = 68,
                VerticalOptions = LayoutOptions.Fill
            };

            var editorBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#14FFFFFF"),
                Stroke = Color.FromArgb("#25FFFFFF"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Content = editor
            };

            // 3. Checkbox Row: "Attach visual card" (default true)
            var checkBox = new CheckBox
            {
                IsChecked = true,
                Color = Color.FromArgb("#0066FF"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            };

            var checkLabelStack = new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center
            };
            checkLabelStack.Children.Add(new Label
            {
                Text = "Attach visual card",
                FontFamily = "InterSemiBold",
                FontSize = 13,
                TextColor = Colors.White
            });
            checkLabelStack.Children.Add(new Label
            {
                Text = "Poster, progress & score image",
                FontFamily = "InterMedium",
                FontSize = 11,
                TextColor = Color.FromArgb("#8E9DAE")
            });

            var checkLeftStack = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center
            };
            checkLeftStack.Children.Add(new Image
            {
                Source = "icon_share",
                WidthRequest = 18,
                HeightRequest = 18,
                VerticalOptions = LayoutOptions.Center
            });
            checkLeftStack.Children.Add(checkLabelStack);

            var checkGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Padding = new Thickness(4, 2, 4, 8),
                VerticalOptions = LayoutOptions.Center
            };
            checkGrid.Children.Add(checkLeftStack);
            Grid.SetColumn(checkLeftStack, 0);
            checkGrid.Children.Add(checkBox);
            Grid.SetColumn(checkBox, 1);

            // Tapping anywhere on the row toggles the checkbox
            var checkTap = new TapGestureRecognizer();
            checkTap.Tapped += (s, e) => checkBox.IsChecked = !checkBox.IsChecked;
            checkGrid.GestureRecognizers.Add(checkTap);

            // 4. Buttons
            var cancelBtn = CreateActionButton("Cancel", () =>
            {
                Dismiss();
                onCancel?.Invoke();
            }, ActionButtonKind.Secondary);

            var shareBtn = CreateActionButton("Share", () =>
            {
                var text = editor.Text;
                var attach = checkBox.IsChecked;
                ClosingByChoice = true;
                Dismiss();
                ClosingByChoice = false;
                onShare?.Invoke(text, attach, cardPath);
            }, ActionButtonKind.Primary);

            var actionPair = CreateActionPair(cancelBtn, shareBtn);

            ItemsHost.Clear();
            ItemsHost.Add(previewCard);
            ItemsHost.Add(editorBorder);
            ItemsHost.Add(checkGrid);
            ItemsHost.Add(actionPair);

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OpenSharePreview failed: " + ex.Message);
        }
    }

    private Action _capturedBack;

    private void OnBackTapped(object sender, TappedEventArgs e)
    {
        _capturedBack?.Invoke();
    }

    public enum ActionButtonKind
    {
        Primary,
        Secondary,
        Destructive
    }

    public static Border CreateActionButton(string label, Action onTap, ActionButtonKind kind = ActionButtonKind.Primary)
    {
        var (bgColor, textColor, fontFamily) = kind switch
        {
            ActionButtonKind.Primary => (_colorUpdateOk, Colors.White, "InterSemiBold"),
            ActionButtonKind.Destructive => (Color.FromArgb("#24141E"), _colorDestructive, "InterSemiBold"),
            _ => (_colorUpdateCancel, _colorUpdateCancelText, "InterMedium")
        };

        var border = new Border
        {
            BackgroundColor = bgColor,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(10, 0),
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill
        };

        border.Content = new Label
        {
            Text = label,
            FontFamily = fontFamily,
            FontSize = 14,
            TextColor = textColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            InputTransparent = true
        };

        if (onTap != null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTap();
            border.GestureRecognizers.Add(tap);
        }

        return border;
    }

    public static Grid CreateActionPair(View left, View right)
    {
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = new ColumnDefinitionCollection { new("*"), new("*") },
            Margin = new Thickness(0, 6, 0, 0)
        };
        Microsoft.Maui.Controls.Grid.SetColumn(left, 0);
        Microsoft.Maui.Controls.Grid.SetColumn(right, 1);
        row.Add(left);
        row.Add(right);
        return row;
    }

    /// <summary>
    ///     A question with two answers, arranged either side-by-side or stacked vertically when labels are long.
    /// </summary>
    public void OpenConfirm(string title, string subtitle, string okLabel, string cancelLabel,
        bool destructive = false, Action onConfirm = null, Action onCancel = null)
    {
        try
        {
            ApplyCompactShape();
            Panel.WidthRequest = 330;

            var hasCancel = !string.IsNullOrWhiteSpace(cancelLabel);

            Header.IsVisible = true;
            CloseButton.IsVisible = !hasCancel;
            BackButton.IsVisible = false;
            TitleLabel.Text = title ?? "";
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
                SubtitleLabel.MaxLines = 8;
                SubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
            }

            var capturedConfirm = onConfirm;
            var capturedCancel = onCancel;

            ItemsHost.Clear();

            var okText = okLabel ?? "OK";
            if (hasCancel)
            {
                var isLong = okText.Length > 11 || cancelLabel.Length > 11;

                var cancelButton = CreateActionButton(cancelLabel, () =>
                {
                    ClosingByChoice = true;
                    Dismiss();
                    ClosingByChoice = false;
                    capturedCancel?.Invoke();
                }, ActionButtonKind.Secondary);
                var okButton = CreateActionButton(okText, () =>
                {
                    ClosingByChoice = true;
                    Dismiss();
                    ClosingByChoice = false;
                    capturedConfirm?.Invoke();
                }, destructive ? ActionButtonKind.Destructive : ActionButtonKind.Primary);

                if (isLong)
                {
                    okButton.Margin = new Thickness(0, 10, 0, 0);
                    cancelButton.Margin = new Thickness(0, 8, 0, 0);
                    ItemsHost.Add(okButton);
                    ItemsHost.Add(cancelButton);
                }
                else
                {
                    ItemsHost.Add(CreateActionPair(cancelButton, okButton));
                }
            }
            else
            {
                var okButton = CreateActionButton(okText, () =>
                {
                    ClosingByChoice = true;
                    Dismiss();
                    ClosingByChoice = false;
                    capturedConfirm?.Invoke();
                }, destructive ? ActionButtonKind.Destructive : ActionButtonKind.Primary);
                okButton.Margin = new Thickness(0, 6, 0, 0);
                ItemsHost.Add(okButton);
            }

            IsVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options sheet confirm failed: " + ex.Message);
        }
    }

    public void OpenUpdate(string title, string subtitle, string notes, string okLabel, string cancelLabel,
        Func<Action<double, string>, Task<bool>> onPerformUpdate = null,
        Action onConfirm = null,
        Action onCancel = null)
    {
        try
        {
            ApplyCompactShape();
            Panel.WidthRequest = 340;

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
                var cleanSub = subtitle;
                if (cleanSub.Contains(" - "))
                {
                    var subParts = cleanSub.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    if (subParts.Length == 2 && string.Equals(subParts[0].Trim(), subParts[1].Trim(), StringComparison.OrdinalIgnoreCase))
                        cleanSub = subParts[0].Trim();
                }

                SubtitleLabel.Text = cleanSub;
                SubtitleLabel.IsVisible = true;
                SubtitleLabel.MaxLines = 4;
                SubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
            }

            ItemsHost.Clear();

            if (!string.IsNullOrWhiteSpace(notes))
            {
                var notesBorder = new Border
                {
                    BackgroundColor = _colorUpdateNotes,
                    Stroke = _colorUpdateStroke,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Padding = new Thickness(14, 10),
                    Margin = new Thickness(0, 4, 0, 8),
                    MaximumHeightRequest = 360
                };

                var notesScroll = new ScrollView
                {
                    Content = BuildFormattedNotesView(notes)
                };
                notesBorder.Content = notesScroll;
                ItemsHost.Add(notesBorder);
            }

            Action performUpdateAction = () =>
            {
                if (onPerformUpdate != null)
                {
                    try
                    {
                        ItemsHost.Clear();
                        Header.IsVisible = true;
                        CloseButton.IsVisible = false;
                        BackButton.IsVisible = false;
                        TitleLabel.Text = title ?? "Updating MAL+";
                        SubtitleLabel.Text = "Downloading update package...";
                        SubtitleLabel.IsVisible = true;

                        var progressStack = new VerticalStackLayout
                        {
                            Spacing = 10,
                            Padding = new Thickness(4, 12, 4, 12),
                            HorizontalOptions = LayoutOptions.Fill
                        };

                        var spinner = new ActivityIndicator
                        {
                            IsRunning = true,
                            Color = _colorUpdateOk,
                            HeightRequest = 32,
                            WidthRequest = 32,
                            HorizontalOptions = LayoutOptions.Center
                        };

                        var progressBar = new ProgressBar
                        {
                            Progress = 0,
                            ProgressColor = _colorUpdateOk,
                            BackgroundColor = _colorUpdateCancel,
                            HeightRequest = 8,
                            HorizontalOptions = LayoutOptions.Fill
                        };

                        var statusLabel = new Label
                        {
                            Text = "Connecting to server...",
                            FontFamily = "InterMedium",
                            FontSize = 12,
                            TextColor = _colorUpdateCancelText,
                            HorizontalTextAlignment = TextAlignment.Center
                        };

                        progressStack.Add(spinner);
                        progressStack.Add(progressBar);
                        progressStack.Add(statusLabel);
                        ItemsHost.Add(progressStack);

                        Action<double, string> reportProgress = (fraction, status) =>
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                if (fraction >= 0)
                                    progressBar.Progress = Math.Clamp(fraction, 0.0, 1.0);
                                if (!string.IsNullOrWhiteSpace(status))
                                    statusLabel.Text = status;
                            });
                        };

                        Task.Run(async () =>
                        {
                            try
                            {
                                var success = await onPerformUpdate(reportProgress);
                                if (success)
                                {
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        ClosingByChoice = true;
                                        Dismiss();
                                        ClosingByChoice = false;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    spinner.IsRunning = false;
                                    spinner.IsVisible = false;
                                    progressBar.IsVisible = false;
                                    statusLabel.Text = $"Download failed: {ex.Message}";
                                    statusLabel.TextColor = _colorError;
                                    CloseButton.IsVisible = true;

                                    var closeBtn = CreateActionButton("Close", () => Dismiss(), ActionButtonKind.Secondary);
                                    closeBtn.Margin = new Thickness(0, 6, 0, 0);
                                    ItemsHost.Add(closeBtn);
                                });
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("MALPLUS OpenUpdate download transition failed: " + ex.Message);
                    }
                }
                else
                {
                    ClosingByChoice = true;
                    Dismiss();
                    ClosingByChoice = false;
                    onConfirm?.Invoke();
                }
            };

            if (string.IsNullOrWhiteSpace(cancelLabel))
            {
                var singleBtn = CreateActionButton(okLabel ?? "OK", performUpdateAction, ActionButtonKind.Primary);
                singleBtn.Margin = new Thickness(0, 6, 0, 0);
                ItemsHost.Add(singleBtn);
            }
            else
            {
                var cancelButton = CreateActionButton(cancelLabel, () =>
                {
                    Dismiss();
                    onCancel?.Invoke();
                }, ActionButtonKind.Secondary);

                var okButton = CreateActionButton(okLabel ?? "OK", performUpdateAction, ActionButtonKind.Primary);
                if ((okLabel?.Length ?? 0) > 11 || cancelLabel.Length > 11)
                {
                    okButton.Margin = new Thickness(0, 10, 0, 0);
                    cancelButton.Margin = new Thickness(0, 8, 0, 0);
                    ItemsHost.Add(okButton);
                    ItemsHost.Add(cancelButton);
                }
                else
                {
                    ItemsHost.Add(CreateActionPair(cancelButton, okButton));
                }
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
        Panel.WidthRequest = 330;
        Panel.Margin = new Thickness(16, 0);
        Panel.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) };
        // A centred card carries its own breathing room: the sheet shape gets it from being flush to
        // the bottom of the screen, and the rows host below only ever padded its sides.
        //
        // These four numbers are the card's css, in one place, for every panel that uses this shape:
        // the confirms, the note and tag prompts, and everything raised through OptionsSheetHost.
        Panel.Padding = new Thickness(0, 14, 0, 12);
        Header.Margin = new Thickness(20, 0, 12, 8);
        Handle.IsVisible = false;

        // The header and everything in it hang from the top of the card rather than sitting wherever
        // the 32dp close button happens to land. Measured on a device with the header's own layout
        // untouched, the panel's border sat 45dp above the title, and cutting 10dp of padding only
        // took it to 35dp: the block was being centred in a header taller than the text in it, so
        // the empty band was above the title rather than beside it.
        Header.VerticalOptions = LayoutOptions.Start;
        TitleLabel.VerticalOptions = LayoutOptions.Start;
        SubtitleLabel.VerticalOptions = LayoutOptions.Start;
        CloseButton.VerticalOptions = LayoutOptions.Start;
        BackButton.VerticalOptions = LayoutOptions.Start;

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
        Panel.Stroke = _colorBorder;
        Panel.StrokeThickness = 1;

        RowsScroll.MaximumHeightRequest = 520;
    }

    private static string CleanMarkdownLinks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([^\]]+)\]\([^\)]+\)",
            "$1");
    }

    private static View BuildFormattedNotesView(string rawNotes)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(0, 2)
        };

        if (string.IsNullOrWhiteSpace(rawNotes))
            return stack;

        var lines = rawNotes.Replace("\r\n", "\n").Split('\n');
        bool isFirstSection = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip top-level release title (e.g. # MAL+ v0.7.6) as it's already in the header
            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
                continue;

            // Section header: ## Section or ### Section
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("### "))
            {
                var headerText = CleanMarkdownLinks(trimmed.TrimStart('#').Trim());
                var headerLabel = new Label
                {
                    Text = headerText,
                    FontFamily = "InterBold",
                    FontSize = 13,
                    TextColor = _colorUpdateOk,
                    Margin = new Thickness(0, isFirstSection ? 2 : 12, 0, 4)
                };
                stack.Add(headerLabel);
                isFirstSection = false;
                continue;
            }

            // Bullet points: - item or * item
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") ||
                trimmed.StartsWith("• ") || trimmed.StartsWith("  - ") || trimmed.StartsWith("    - "))
            {
                bool isSubBullet = line.StartsWith("  ") || line.StartsWith("\t");
                var bulletContent = trimmed;
                if (bulletContent.StartsWith("- ") || bulletContent.StartsWith("* ") || bulletContent.StartsWith("• "))
                    bulletContent = bulletContent.Substring(2).Trim();

                var label = new Label
                {
                    LineBreakMode = LineBreakMode.WordWrap,
                    Margin = new Thickness(isSubBullet ? 14 : 0, 2, 0, 3)
                };

                var fs = new FormattedString();
                fs.Spans.Add(new Span
                {
                    Text = isSubBullet ? "◦ " : "• ",
                    FontFamily = "InterBold",
                    FontSize = isSubBullet ? 11 : 12,
                    TextColor = isSubBullet ? _colorUpdateCancelText : _colorUpdateOk
                });

                // Parse **bold** portions
                var parts = bulletContent.Split(new[] { "**" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    var isBold = (i % 2 == 1);
                    var partText = CleanMarkdownLinks(parts[i]);
                    if (!string.IsNullOrEmpty(partText))
                    {
                        fs.Spans.Add(new Span
                        {
                            Text = partText,
                            FontFamily = isBold ? "InterSemiBold" : "Inter",
                            FontSize = 12,
                            TextColor = isBold ? Colors.White : _colorUpdateText
                        });
                    }
                }

                label.FormattedText = fs;
                stack.Add(label);
                continue;
            }

            // Plain paragraph
            var plainLabel = new Label
            {
                Text = CleanMarkdownLinks(trimmed.Replace("**", "")),
                FontFamily = "Inter",
                FontSize = 12,
                TextColor = _colorUpdateText,
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(0, 2, 0, 3)
            };
            stack.Add(plainLabel);
        }

        return stack;
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
            BackButton.IsVisible = onBack != null;
            _capturedBack = onBack;
            if (hasHeader)
            {
                TitleLabel.Text = title ?? "";
                TitleLabel.FontSize = 16;
            }
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
                SubtitleLabel.MaxLines = 6;
                SubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
            }

            ItemsHost.Clear();

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
    /// </summary>
    private View BuildRow(OptionsSheetItem item)
    {
        if (item.SecondaryItem != null)
        {
            var leftKind = item.Destructive ? ActionButtonKind.Destructive : ActionButtonKind.Secondary;
            var rightKind = item.SecondaryItem.Destructive ? ActionButtonKind.Destructive : ActionButtonKind.Primary;

            var left = CreateActionButton(item.Label, () => Choose(item), leftKind);
            var right = CreateActionButton(item.SecondaryItem.Label, () => Choose(item.SecondaryItem), rightKind);
            return CreateActionPair(left, right);
        }

        return BuildSingleRow(item);
    }

    private View BuildSingleRow(OptionsSheetItem item)
    {
        var label = item.Label ?? string.Empty;
        var description = item.Description;
        if (string.IsNullOrEmpty(description) && label.Contains(" — "))
        {
            var parts = label.Split(new[] { " — " }, 2, StringSplitOptions.None);
            if (parts.Length == 2 && parts[1].Length > 5)
            {
                label = parts[0].Trim();
                description = parts[1].Trim();
            }
        }
        var hasDesc = !string.IsNullOrWhiteSpace(description);
        var hasPreview = !string.IsNullOrWhiteSpace(item.PreviewUrl);
        var hasLeading = hasPreview || !string.IsNullOrWhiteSpace(item.Icon);

        var border = new Border
        {
            BackgroundColor = _colorSheet,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(12, hasPreview ? 6 : (hasDesc ? 8 : 0)),
            MinimumHeightRequest = hasPreview ? 56 : 48
        };
        if (!hasDesc && !hasPreview)
        {
            border.HeightRequest = 48;
        }

        var grid = new Grid
        {
            ColumnSpacing = 12
        };

        if (hasPreview)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var thumbBorder = new Border
            {
                WidthRequest = 76,
                HeightRequest = 46,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
                Stroke = new SolidColorBrush(Color.FromArgb("#33FFFFFF")),
                StrokeThickness = 1,
                BackgroundColor = Color.FromArgb("#141D2B"),
                Padding = new Thickness(0),
                VerticalOptions = LayoutOptions.Center
            };
            var thumbGrid = new Grid { IsClippedToBounds = true };
            thumbGrid.Add(new Image
            {
                Source = item.PreviewUrl,
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true
            });
            thumbGrid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#99000000"),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                WidthRequest = 22,
                HeightRequest = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
                Content = new Label
                {
                    Text = "▶",
                    TextColor = Colors.White,
                    FontSize = 9,
                    FontFamily = "InterBold",
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            });
            thumbBorder.Content = thumbGrid;
            Grid.SetColumn(thumbBorder, 0);
            grid.Add(thumbBorder);
        }
        else if (hasLeading)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var chip = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                BackgroundColor = ChipTint(item),
                Padding = new Thickness(0),
                VerticalOptions = LayoutOptions.Center
            };
            chip.Content = new Image
            {
                Source = item.Icon,
                WidthRequest = 18,
                HeightRequest = 18,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Grid.SetColumn(chip, 0);
            grid.Add(chip);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        var textCol = hasLeading ? 1 : 0;
        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };

        var titleLabel = new Label
        {
            Text = label,
            FontFamily = item.Destructive ? "InterSemiBold" : "InterMedium",
            FontSize = 14,
            TextColor = item.Destructive ? _colorDestructive : Colors.White,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };
        textStack.Add(titleLabel);

        if (hasDesc)
        {
            var descLabel = new Label
            {
                Text = description,
                FontFamily = "Inter",
                FontSize = 11,
                TextColor = _colorMuted,
                LineBreakMode = LineBreakMode.WordWrap
            };
            textStack.Add(descLabel);
        }

        Grid.SetColumn(textStack, textCol);
        grid.Add(textStack);

        var endCol = hasLeading ? 2 : 1;
        if (!string.IsNullOrWhiteSpace(item.Value))
        {
            var isCheck = item.Value == "✓";
            var value = new Label
            {
                Text = item.Value,
                FontFamily = isCheck ? "InterBold" : "InterMedium",
                FontSize = isCheck ? 15 : 11,
                TextColor = isCheck ? Color.FromArgb("#0066FF") : _colorMuted,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(value, endCol);
            grid.Add(value);
        }
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
            Grid.SetColumn(chevron, endCol);
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
            return _chipTintDestructive;
        var icon = item.Icon ?? "";
        if (icon.Length == 0)
            return _chipTintDefault;
        if (_chipTintCache.TryGetValue(icon, out var cached))
            return cached;
        Color result;
        if (icon.Contains("forum", StringComparison.OrdinalIgnoreCase))
            result = _chipTintForum;
        else if (icon.Contains("video", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("play", StringComparison.OrdinalIgnoreCase))
            result = _chipTintVideo;
        else if (icon.Contains("tag", StringComparison.OrdinalIgnoreCase))
            result = _chipTintTag;
        else if (icon.Contains("score", StringComparison.OrdinalIgnoreCase))
            result = _chipTintScore;
        else if (icon.Contains("episodes", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("watched", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("read", StringComparison.OrdinalIgnoreCase))
            result = _chipTintEpisodes;
        else if (icon.Contains("priority", StringComparison.OrdinalIgnoreCase))
            result = _chipTintPriority;
        else if (icon.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("list", StringComparison.OrdinalIgnoreCase))
            result = _chipTintStatus;
        else if (icon.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            icon.Contains("search", StringComparison.OrdinalIgnoreCase))
            result = _chipTintBrowser;
        else if (icon.Contains("copy", StringComparison.OrdinalIgnoreCase))
            result = _chipTintCopy;
        else
            result = _chipTintDefault;
        _chipTintCache[icon] = result;
        return result;
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
