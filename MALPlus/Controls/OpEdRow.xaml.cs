using System;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls.Shapes;

namespace MALPlus.Controls;

/// <summary>
///     One OP or ED line.
/// </summary>
/// <remarks>
///     The tap target is a real fill rather than a transparent one: a Border carrying only a gesture
///     recogniser and nothing to hit is not hit tested on Android at all, which is a row that looks
///     right and does nothing. It is given the fill of the panel it sits on, so it is invisible and
///     tappable, and it spans both columns so the colour bar is part of the target.
/// </remarks>
public partial class OpEdRow : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(OpEdRow), string.Empty);

    public static readonly BindableProperty IsOpeningProperty = BindableProperty.Create(
        nameof(IsOpening), typeof(bool), typeof(OpEdRow), true,
        propertyChanged: OnIsOpeningChanged);

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(OpEdRow), null);

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(OpEdRow), null);

    private static readonly Color OpeningAccent = Color.FromArgb("#0066FF");
    private static readonly Color EndingAccent = Color.FromArgb("#FF3B30");

    private static void OnIsOpeningChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is OpEdRow row)
        {
            row.OnPropertyChanged(nameof(Accent));
            if (row.AccentBar != null)
                row.AccentBar.Color = row.Accent;
        }
    }

    public OpEdRow()
    {
        InitializeComponent();

        if (AccentBar != null)
            AccentBar.Color = Accent;

        var target = new Border
        {
            BackgroundColor = Color.FromArgb("#101D2A"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        target.GestureRecognizers.Add(tap);

        var host = (Grid)Content;
        host.Children.Insert(0, target);
        Grid.SetColumnSpan(target, 2);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>True for an opening, false for an ending. Decides the accent.</summary>
    public bool IsOpening
    {
        get => (bool)GetValue(IsOpeningProperty);
        set => SetValue(IsOpeningProperty, value);
    }

    public Color Accent => IsOpening ? OpeningAccent : EndingAccent;

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private void OnTapped(object sender, EventArgs e)
    {
        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }
}
