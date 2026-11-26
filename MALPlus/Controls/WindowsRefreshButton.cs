using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;

namespace MALPlus.Controls;

/// <summary>
/// Compact circular refresh button shown only on Windows Desktop where pull-to-refresh swipe is unavailable.
/// </summary>
public class WindowsRefreshButton : ContentView
{
    public event EventHandler Clicked;

    public WindowsRefreshButton()
    {
        var isWindows = DeviceInfo.Platform == DevicePlatform.WinUI;
        IsVisible = isWindows;
        VerticalOptions = LayoutOptions.Center;
        HorizontalOptions = LayoutOptions.End;

        if (!isWindows)
        {
            WidthRequest = 0;
            HeightRequest = 0;
            return;
        }

        var badge = new Border
        {
            WidthRequest = 32,
            HeightRequest = 32,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            BackgroundColor = Color.FromArgb("#14FFFFFF"),
            StrokeThickness = 0,
            Padding = new Thickness(0),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "\uf021",
                FontFamily = "FontAwesome",
                FontSize = 14,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (s, e) =>
        {
            try
            {
                badge.Opacity = 0.55;
                Clicked?.Invoke(this, EventArgs.Empty);
                await badge.FadeTo(1.0, 220);
            }
            catch
            {
                badge.Opacity = 1.0;
            }
        };
        badge.GestureRecognizers.Add(tap);
        ToolTipProperties.SetText(badge, "Refresh");
        Content = badge;
    }
}
