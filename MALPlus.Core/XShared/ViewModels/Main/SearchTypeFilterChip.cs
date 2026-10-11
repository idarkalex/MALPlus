using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MALPlus.XShared.ViewModels.Main
{
    /// <summary>
    ///     One chip in the search filter bar.
    /// </summary>
    /// <remarks>
    ///     It notifies on Selected because the visual state is the only thing it carries, and the
    ///     XAML drives that with a DataTrigger rather than a converter: the marked and unmarked
    ///     looks are two real colours, and a converter would have to return a whole brush to
    ///     express that.
    ///
    ///     The collection is rebuilt whenever the selection changes rather than mutated in place,
    ///     which is why the notification matters less than it looks: it keeps the state honest if
    ///     the row is ever bound to something long lived.
    /// </remarks>
    public class SearchTypeFilterChip : INotifyPropertyChanged
    {
        private bool _selected;

        public SearchTypeFilterChip(string label) => Label = label;

        /// <summary>Display label, which is also MAL's own string for that format.</summary>
        public string Label { get; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}