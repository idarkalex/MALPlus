using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MALPlus.Models.Enums;
using MALPlus.Models.Interfaces;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class RelatedAnimeData : INotifyPropertyChanged, IDetailsPageArgs
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string WholeRelation { get; set; }
        public List<string> Relations { get; set; } = new List<string>();
        public int Id { get; set; }
        public string Title { get; set; }
        public RelatedItemType Type { get; set; }
        public string ImgUrl { get; set; }

        private string _mediaType;
        public string MediaType
        {
            get => _mediaType;
            set
            {
                if (_mediaType == value) return;
                _mediaType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeTag));
            }
        }

        public string TypeTag => MediaType;

        public int AllEpisodes { get; set; }

        public string AirDayTillBind { get; set; }

        private string _bottomAirTag;
        public string BottomAirTag
        {
            get => !string.IsNullOrEmpty(_bottomAirTag) ? _bottomAirTag : AirDayTillBind;
            set
            {
                if (_bottomAirTag == value) return;
                _bottomAirTag = value;
                OnPropertyChanged();
            }
        }

        private string _scoreTag;
        public string ScoreTag
        {
            get => _scoreTag;
            set
            {
                if (_scoreTag == value) return;
                _scoreTag = value;
                OnPropertyChanged();
            }
        }

        private string _seasonTag;
        public string SeasonTag
        {
            get => MALPlus.XShared.Utils.Settings.DisplaySeasonWithType ? _seasonTag : string.Empty;
            set
            {
                if (_seasonTag == value) return;
                _seasonTag = value;
                OnPropertyChanged();
            }
        }

        private bool _isOnMyList;
        public bool IsOnMyList
        {
            get => _isOnMyList;
            set
            {
                if (_isOnMyList == value) return;
                _isOnMyList = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LibraryBadgeAccent));
                OnPropertyChanged(nameof(LibraryBadgeAccentColor));
                OnPropertyChanged(nameof(LibraryBadgeMain));
                OnPropertyChanged(nameof(HasBadge));
            }
        }

        public int MyEpisodesForBadge { get; set; }
        public string MyStatusForBadge { get; set; }

        public bool HasBadge => IsOnMyList;

        public string LibraryBadgeAccent
            => MALPlus.XShared.Utils.AnimeCardBadge.Status(IsOnMyList, () => MyStatusForBadge);

        public string LibraryBadgeAccentColor
            => MALPlus.XShared.Utils.AnimeCardBadge.StatusColor(IsOnMyList, MyStatusForBadge);

        public string LibraryBadgeMain
            => IsOnMyList
                ? MALPlus.XShared.Utils.AnimeCardBadge.Episodes(true, MyEpisodesForBadge, AllEpisodes)
                : string.Empty;
    }
}
