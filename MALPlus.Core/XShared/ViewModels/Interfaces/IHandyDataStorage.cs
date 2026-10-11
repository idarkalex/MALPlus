using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MALPlus.Models.Models;
using MALPlus.Models.Models.Forums;
using MALPlus.Models.Models.Notifications;
using MALPlus.XShared.Interfaces;

namespace MALPlus.XShared.ViewModels.Interfaces
{
    public interface IHandyDataStorage
    {
        void Init();
        Task SaveData();
        //
        IHandyDataStorageModule<MalUser> PinnedUsers { get; }
        //
        IHandyDataStorageModule<WatchedTopicModel> WatchedTopics { get; }
        //
        IHandyDataStorageModule<AiringShowNotificationEntry> RegisteredAiringNotifications { get; }
        //
        ReadOnlyDictionary<string, List<StarredForumMessage>> StarredMessages { get; }
        void StarForumMessage(StarredForumMessage forumMessage);
        void UnstarForumMessage(string messageId, MalUser poster);
        bool IsMessageStarred(string forumMessageId, MalUser poster);
        void ResetStarredMessages(MalUser user);
        void ResetStarredMessages();
        //
    }
}
