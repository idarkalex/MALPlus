using System.Windows.Input;

namespace MALPlus.Controls;

/// <summary>
///     One row of the options sheet.
/// </summary>
/// <remarks>
///     Carries its own command rather than an event so a host can wire it from XAML the same way it
///     wires anything else, and carries the presentation in one place so every row in every sheet
///     looks the same.
/// </remarks>
public class OptionsSheetItem
    {
        public OptionsSheetItem(string label, string icon, ICommand command = null,
            object commandParameter = null, string value = null, bool destructive = false, bool showChevron = false,
            Func<OptionsSheet, bool> submenu = null, Action action = null, bool hideChevron = false)
        {
            Label = label;
            Icon = icon;
            Command = command;
            CommandParameter = commandParameter;
            Value = value;
            Destructive = destructive;
            ShowChevron = showChevron;
            Submenu = submenu;
            Action = action;
            HideChevron = hideChevron;
        }

        public string Label { get; }

        /// <summary>Name of an SVG or drawable. Null draws no chip, for plain rows.</summary>
        public string Icon { get; }

        public ICommand Command { get; }

        public object CommandParameter { get; }

        /// <summary>
        ///     Strongly referenced work for this row, and the right way to give a row something to do.
        ///     <para>
        ///     Not a RelayCommand: MvvmLight keeps the action in a WeakAction, so a lambda handed straight
        ///     to that constructor has no strong reference left once the constructor returns, the collector
        ///     takes the delegate, and Execute becomes a silent no-op that still answers CanExecute true.
        ///     That is why the view model's own commands work, rooted in a field, and a row's inline lambda
        ///     does not.
        ///     </para>
        /// </summary>
        public Action Action { get; }

        /// <summary>
        ///     True when the row works on the level it is already on, so the arrow that promises a deeper
        ///     level is withheld.
        /// </summary>
        /// <remarks>
        ///     A submenu normally means "there is more below here", which is why the sheet draws a chevron
        ///     for it. The Discover format rows also use a submenu, but theirs commits on the spot and
        ///     redraws the same list: ticking a format has nowhere to go, and an arrow there points at a
        ///     panel that does not exist.
        /// </remarks>
        public bool HideChevron { get; }

        /// <summary>
        ///     Small dim label on the right, like the "Not watched" on Set Rewatch.
        /// </summary>
        public string Value { get; }

        /// <summary>
        ///     Paints the row red and semibold. Reserved for the one action that cannot be undone, so it
        ///     never becomes decoration.
        /// </summary>
        public bool Destructive { get; }

        public bool ShowChevron { get; }

        /// <summary>
        ///     When set, choosing the row swaps the panel's contents for a second level instead of running
        ///     a command, and the panel stays open. Returns true when it handled itself.
        /// </summary>
        public Func<OptionsSheet, bool> Submenu { get; }
    }
