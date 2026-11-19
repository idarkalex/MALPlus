using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MALPlus.Controls;

/// <summary>
///     Unified Design System helper for MAL+ Option A Tab Chips (Modern Squircle / RoundRectangle 8).
///     Enforces consistent dimensions, corner radius, colors and borders across all tabbed views.
/// </summary>
public static class TabChipHelper
{
    public static readonly Color ActiveBackground = Color.FromArgb("#0066FF");
    public static readonly Color ActiveText = Colors.White;
    public static Color InactiveBackground =>
        Application.Current?.Resources.TryGetValue("TabChipInactiveBg", out var val) == true && val is Color c ? c : Color.FromArgb("#141D2B");
    public static Color InactiveText =>
        Application.Current?.Resources.TryGetValue("TabChipInactiveText", out var val) == true && val is Color c ? c : Color.FromArgb("#8FA8C0");
    public static Color InactiveBorder =>
        Application.Current?.Resources.TryGetValue("EmBorder", out var val) == true && val is Color c ? c : Color.FromArgb("#1E3A52");

    public const double ChipHeight = 34;
    public const int ChipCornerRadius = 8;
    public const double ChipFontSize = 13;

    public static Button CreateChip(string text, object commandParameter = null, EventHandler onClicked = null, bool isActive = false)
    {
        var button = new Button
        {
            Text = text,
            CommandParameter = commandParameter
        };
        if (onClicked != null)
        {
            button.Clicked += onClicked;
        }
        ApplyStyle(button, isActive);
        return button;
    }

    public static void ApplyStyle(Button button, bool isActive)
    {
        if (button == null) return;
        button.FontFamily = "InterSemiBold";
        button.FontSize = ChipFontSize;
        button.HeightRequest = ChipHeight;
        button.CornerRadius = ChipCornerRadius;
        button.Padding = new Thickness(14, 0);
        button.BackgroundColor = isActive ? ActiveBackground : InactiveBackground;
        button.TextColor = isActive ? ActiveText : InactiveText;
        button.BorderWidth = isActive ? 0 : 1;
        button.BorderColor = isActive ? Colors.Transparent : InactiveBorder;
    }
}
