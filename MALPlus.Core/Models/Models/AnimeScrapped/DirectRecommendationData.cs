using System.ComponentModel;
using System.Runtime.CompilerServices;
using MALPlus.Models.Enums;
using MALPlus.Models.Interfaces;

namespace MALPlus.Models.Models.AnimeScrapped
{
    /// <summary>
    ///     Direct as in recommendation from details page
    /// </summary>
    public class DirectRecommendationData : IDetailsPageArgs, INotifyPropertyChanged
    {
        private string _descriptionBottom;
        private string _descriptionTop;
        private bool _hasDescriptionBottom;
        private bool _hasDescriptionTop;

        private string _mediaType;
        private int _allEpisodes;
        private int _myEpisodesForBadge;
        private string _myStatusForBadge;
        private bool _isOnMyList;

        public event PropertyChangedEventHandler PropertyChanged;

        private string _description;
        public string Description
        {
            get => _description;
            set
            {
                if (_description == value)
                    return;
                _description = value;
                DescriptionSplitSource = null;
                OnPropertyChanged();
            }
        }

        public string ImageUrl { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public RelatedItemType Type { get; set; }
        public string AirDayTillBind
        {
            get => _airDayTillBind;
            set
            {
                if (_airDayTillBind == value)
                    return;
                _airDayTillBind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeExtra));
            }
        }

        private string _airDayTillBind;

        public bool IsOnMyList
        {
            get => _isOnMyList;
            set
            {
                if (_isOnMyList == value)
                    return;
                _isOnMyList = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LibraryBadgeAccent));
                OnPropertyChanged(nameof(LibraryBadgeMain));
            }
        }

        public int MyEpisodesForBadge
        {
            get => _myEpisodesForBadge;
            set
            {
                if (_myEpisodesForBadge == value)
                    return;
                _myEpisodesForBadge = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LibraryBadgeMain));
            }
        }

        public string MyStatusForBadge
        {
            get => _myStatusForBadge;
            set
            {
                if (_myStatusForBadge == value)
                    return;
                _myStatusForBadge = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LibraryBadgeAccent));
                OnPropertyChanged(nameof(LibraryBadgeMain));
            }
        }

        /// <remarks>
        ///     Notifying, because the metadata arrives after the card is already on screen: the
        ///     recommendation tab shows the scraper's rows straight away and fills the format and the
        ///     count in behind them. A plain auto property left the type chip and the counter blank on
        ///     every row the enrichment reached late.
        /// </remarks>
        public string MediaType
        {
            get => _mediaType;
            set
            {
                if (_mediaType == value)
                    return;
                _mediaType = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Total episodes or chapters of the recommended entry, 0 when unknown.
        /// </summary>
        /// <remarks>
        ///     Read from the same payload that supplies <see cref="MediaType" />, so the episode half
        ///     of the card badge costs nothing extra. Recommendations are not library entries, so there
        ///     is no watched count for them: the badge rule in <see cref="Utils.AnimeCardBadge" />
        ///     decides what a poster says about an entry the user does not follow.
        /// </remarks>
        public int AllEpisodes
        {
            get => _allEpisodes;
            set
            {
                if (_allEpisodes == value)
                    return;
                _allEpisodes = value;
                // The badge is what the cell binds, not this property, so the computed name has to be
                // raised by hand or the card keeps drawing the count it had when the row was built.
                OnPropertyChanged();
                OnPropertyChanged(nameof(LibraryBadgeMain));
            }
        }

        /// <summary>
        ///     The badge's episode half, by the same rule the poster cell uses.
        /// </summary>
        public string LibraryBadgeAccent
            => MALPlus.XShared.Utils.AnimeCardBadge.Status(IsOnMyList, () => MyStatusForBadge);

        /// <summary>
        ///     The badge's episode half, by the same rule the poster cell uses.
        /// </summary>
public string LibraryBadgeMain
            => IsOnMyList
                ? MALPlus.XShared.Utils.AnimeCardBadge.Episodes(true,
                    MyEpisodesForBadge, AllEpisodes)
                : string.Empty;
        /// <summary>Never blank on a recommendation row: the status there is the entry's own.</summary>
        public string BadgeExtra => AirDayTillBind;

        public string DescriptionTop
        {
            get => _descriptionTop;
            private set => SetFlag(ref _descriptionTop, value, ref _hasDescriptionTop, nameof(DescriptionTop), nameof(HasDescriptionTop));
        }

        public bool HasDescriptionTop
        {
            get => _hasDescriptionTop;
            private set
            {
                if (_hasDescriptionTop == value)
                    return;
                _hasDescriptionTop = value;
                OnPropertyChanged();
            }
        }

        public string DescriptionBottom
        {
            get => _descriptionBottom;
            private set => SetFlag(ref _descriptionBottom, value, ref _hasDescriptionBottom, nameof(DescriptionBottom), nameof(HasDescriptionBottom));
        }

        public bool HasDescriptionBottom
        {
            get => _hasDescriptionBottom;
            private set
            {
                if (_hasDescriptionBottom == value)
                    return;
                _hasDescriptionBottom = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Memo of the inputs used to produce the current split, so the measuring handler can skip
        ///     recomputing on every layout pass and can notice a late description overwrite.
        /// </summary>
        public string DescriptionSplitSource { get; set; }

        public double SplitWidth { get; set; }
        public double SplitTitleHeight { get; set; }

        public void SetDescriptionSplit(string top, string bottom)
        {
            DescriptionTop = top;
            DescriptionBottom = bottom;
            DescriptionSplitSource = Description;
        }

        private void SetFlag(ref string field, string value, ref bool flag, params string[] names)
        {
            if (field == value)
                return;
            field = value;
            flag = !string.IsNullOrWhiteSpace(value);
            foreach (var name in names)
                OnPropertyChanged(name);
        }

        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
