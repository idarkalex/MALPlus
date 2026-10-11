using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.NavArgs
{
    public class AnimeDetailsPageNavigationArgs
    {
        public readonly AnimeGeneralDetailsData AnimeElement;
        public readonly IAnimeData AnimeItem;
        public int Id { get; private set; }
        public object PrevPageSetup;
        public readonly string Title;
        public bool AnimeMode = true;
        public bool RegisterBackNav = true;
        public PageIndex Source;
        public int SourceTabIndex;
        private bool _isHumId;

        public AnimeDetailsPageNavigationArgs(int id, string title, AnimeGeneralDetailsData element,
            IAnimeData animeReference,
            object args = null,bool humId = false)
        {
            Id = id;
            Title = title;
            AnimeElement = element;
            PrevPageSetup = args;
            AnimeItem = animeReference;
            _isHumId = humId;

        }

        /// <summary>
        /// Prepares Id -> converts from hummingbird Id to mal one
        /// </summary>
        /// <returns></returns>
        public async Task Prepare()
        {

        }
    }
}
