using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.ViewModels.Main
{
    /// <summary>
    ///     A row in the Genres or Studios picker.
    /// </summary>
    /// <remarks>
    ///     These used to be raw enum values bound straight to the list, which is fine for a label
    ///     but leaves nowhere to hang a studio logo. MAL does not return those; Tenrai does, at
    ///     /v1/producers/{mal_id}, and the AnimeStudios enum already carries every studio's MAL id
    ///     as its value, so the icon is one lookup away and needs somewhere to live.
    ///
    ///     The logo is fetched on demand when the row is realised and cached to disk, rather than
    ///     preloading all 618 studios: that would be 25 pages of list requests before the tab
    ///     showed anything.
    /// </remarks>
    public class SearchChoiceItem : INotifyPropertyChanged
    {
        private string _iconUrl;

        public SearchChoiceItem(Enum value, string name)
        {
            Value = value;
            Name = name;
        }

        /// <summary>The underlying genre or studio, which is what the catalogue query needs.</summary>
        public Enum Value { get; }

        public string Name { get; }

        /// <summary>
        ///     MAL producer id for a studio, or 0 for a genre. Genres have no logo anywhere.
        /// </summary>
        public int MalId { get; init; }

        public string IconUrl
        {
            get
            {
                // Reading this is what starts the fetch. The list virtualises, so only the studios
                // actually realised on screen are ever asked for, which is what keeps this from
                // becoming 618 requests the moment the tab opens.
                if (string.IsNullOrEmpty(_iconUrl) && MalId > 0)
                {
                    var known = StudioIconCache.GetIconUrl(MalId);
                    if (!string.IsNullOrEmpty(known))
                        _iconUrl = known;
                }
                return _iconUrl;
            }
            set
            {
                if (_iconUrl == value)
                    return;
                _iconUrl = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}