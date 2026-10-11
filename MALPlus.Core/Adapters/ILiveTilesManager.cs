using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Models.Library;

namespace MALPlus.Adapters
{
    public interface ILiveTilesManager
    {
        void UpdateTile(IAnimeData item);
    }
}
