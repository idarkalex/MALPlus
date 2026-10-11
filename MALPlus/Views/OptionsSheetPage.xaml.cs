using System;
using MALPlus.Controls;

namespace MALPlus.Views;

/// <summary>
///     A dialog, hosted in a page of its own so any page can raise it.
/// </summary>
/// <remarks>
///     Deliberately dumb, and deliberately so. Everything that decides what the panel holds, what a row
///     does and when the page goes away lives in <see cref="OptionsSheetHost"/>, which owns the one
///     thing a dialog needs in order to survive: something waiting on the answer. A page that held its
///     own result would have to be found before it could be read, and by then the caller has moved on.
/// </remarks>
public partial class OptionsSheetPage : ContentPage
{
    public OptionsSheetPage()
    {
        InitializeComponent();
    }

    public OptionsSheet Sheet => SheetPanel;

    /// <summary>A row was tapped. The panel has not closed yet.</summary>
    public event EventHandler<OptionsSheetItem> RowChosen;

    /// <summary>
    ///     The dialog is going away without anything having been chosen: the scrim, the close button,
    ///     the back gesture, or anything else that pops the page from underneath.
    /// </summary>
    public event EventHandler Dismissed;

    /// <summary>
    ///     Binds the panel for this presentation.
    /// </summary>
    /// <remarks>
    ///     Unsubscribed first, because a new page is made for every dialog and the sheet inside it is
    ///     not shared: a subscription left from a previous dialog would fire against a host that is no
    ///     longer waiting on anything.
    /// </remarks>
    public void Bind()
    {
        SheetPanel.ItemChosen -= OnItemChosen;
        SheetPanel.ItemChosen += OnItemChosen;
        SheetPanel.Closed -= OnClosed;
        SheetPanel.Closed += OnClosed;
    }

    private void OnItemChosen(object sender, OptionsSheetItem item)
    {
        RowChosen?.Invoke(this, item);
    }

    private void OnClosed(object sender, EventArgs e)
    {
        // A close caused by a row is answered by the row, which is on its way right behind this, and
        // reporting it as a dismissal too would settle the question with the wrong answer before it was
        // asked. Everything else that closes the panel is a dismissal.
        if (SheetPanel.ClosingByChoice)
            return;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     The page is going away, whichever way it was sent.
    /// </summary>
    /// <remarks>
    ///     The back gesture pops a modal page from the platform side, and nothing inside the panel knows:
    ///     no row was tapped and no scrim was touched, so the panel never dismisses itself and never
    ///     raises Closed. The host would then wait on an answer that cannot arrive, and wait forever,
    ///     with its gate held. That silence is the whole reason dialogs stopped opening after one was
    ///     dismissed by the back gesture.
    ///     <para>
    ///     Dismissing the panel from here first is what keeps the two paths consistent: whether the
    ///     dialog left because of a gesture or because a row was tapped, the panel is gone by the time
    ///     the page is, and the host hears exactly one of the two outcomes.
    ///     </para>
    /// </remarks>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SheetPanel.Dismiss();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
