using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MALPlus.Models.Models.Forums
{
    public class ForumTopicPollOption
    {
        public long Id { get; set; }
        public string Text { get; set; }
        public int Votes { get; set; }
    }

    public class ForumTopicPollData
    {
        public long Id { get; set; }
        public string Question { get; set; }
        public bool Closed { get; set; }
        public List<ForumTopicPollOption> Options { get; set; } = new List<ForumTopicPollOption>();
        public int TotalVotes => Options?.Sum(o => o.Votes) ?? 0;
    }

    public class ForumTopicData
    {
        public List<ForumMessageEntry> Messages { get; set; } = new List<ForumMessageEntry>();
        public int AllPages { get; set; }
        public int CurrentPage { get; set; }
        public string Title { get; set; }
        public List<ForumBreadcrumb> Breadcrumbs { get; set; } = new List<ForumBreadcrumb>();
        public bool IsLocked { get; set; }
        public string TargetMessageId { get; set; }
        public string Id { get; set; }
        public ForumTopicPollData Poll { get; set; }
        public bool ShowPollResults { get; set; }
    }
}
