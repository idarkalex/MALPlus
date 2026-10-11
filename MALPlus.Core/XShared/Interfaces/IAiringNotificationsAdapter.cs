using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Models.Notifications;

namespace MALPlus.XShared.Interfaces
{
    public interface IAiringNotificationsAdapter
    {
        void ScheduleToast(AiringShowNotificationEntry entry);
        void RemoveToasts(string id);
        bool AreNotificationRegistered(string id);
    }
}
