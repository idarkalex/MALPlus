using System;
using System.Globalization;

namespace MALPlus.Models.Models.MalSpecific
{
    public class UserFeedEntryModel
    {
        public MalUser User { get; set; }
        public string Title { get; set; }
        public string Header { get; set; }
        public string Link { get; set; }
        public int Id { get; set; }
        public bool IsManga { get; set; }
        public string MediaType { get; set; }
        public string EntryImgUrl { get; set; }
        public double Score { get; set; }
        public string Description { get; set; }
        public DateTime Date { get; set; }

        public string UserName => User?.Name ?? "";
        public string MediaBadge => IsManga ? "MANGA" : "ANIME";
        public bool HasEntryImg => !string.IsNullOrEmpty(EntryImgUrl);
        public string DateLabel => Date.Year == DateTime.Now.Year
            ? Date.ToString("d MMM", CultureInfo.CurrentUICulture)
            : Date.ToString("d MMM yy", CultureInfo.CurrentUICulture);

        public string MetaLine
        {
            get
            {
                var scorePart = Score > 0
                    ? "★ " + Score.ToString("0.0", CultureInfo.InvariantCulture)
                    : "";
                if (!string.IsNullOrEmpty(MediaType) && !string.IsNullOrEmpty(scorePart))
                    return $"{MediaType} · {scorePart}";
                return !string.IsNullOrEmpty(MediaType) ? MediaType : scorePart;
            }
        }
    }
}
