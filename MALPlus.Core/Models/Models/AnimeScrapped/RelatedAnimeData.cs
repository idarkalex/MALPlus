using System.Collections.Generic;
using MALPlus.Models.Enums;
using MALPlus.Models.Interfaces;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class RelatedAnimeData : IDetailsPageArgs
    {
        public string WholeRelation { get; set; }
        public List<string> Relations { get; set; } = new List<string>();
        public int Id { get; set; }
        public string Title { get; set; }
        public RelatedItemType Type { get; set; }
        public string ImgUrl { get; set; }
        public string MediaType { get; set; }
        public string AirDayTillBind { get; set; }
    }
}