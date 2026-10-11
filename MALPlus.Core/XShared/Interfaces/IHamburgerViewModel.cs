using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.XShared.Comm.Anime;

namespace MALPlus.XShared.Interfaces
{
    public interface IHamburgerViewModel
    {
        Task UpdateProfileImg(bool dl = true);
        //Desktop
        void SetActiveButton(HamburgerButtons val);
        void UpdateApiDependentButtons();
        void UpdateAnimeFiltersSelectedIndex();
        void UpdateLogInLabel();
        bool MangaSectionVisbility { get; set; }
        void SetActiveButton(TopAnimeType topType);
        void UpdatePinnedProfiles();
        void UpdateBottomMargin();
    }
}
