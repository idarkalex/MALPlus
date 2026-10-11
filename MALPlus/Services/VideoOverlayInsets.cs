namespace MALPlus.Services;

/// <summary>
///     Thin shim over <see cref="WindowInsetsHelper.ApplyTop" />.
/// </summary>
/// <remarks>
///     Kept because the three video overlays call it by name. The logic moved to
///     <see cref="WindowInsetsHelper" /> when the app was actually made edge-edge: before that the
///     window was laid out inside the bars, every inset read back as zero, and this helper only ever
///     added its own 10dp. It looked like it was fixing the notch and was not.
/// </remarks>
public static class VideoOverlayInsets
{
    public static void Apply(Microsoft.Maui.Controls.View leftButton, Microsoft.Maui.Controls.View rightButton)
    {
        WindowInsetsHelper.ApplyTop(leftButton, rightButton);
    }
}