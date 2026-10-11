using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.ApiResponses;
using MALPlus.Models.Models.Forums;
using MALPlus.Models.Models.MalSpecific;
using Newtonsoft.Json;

namespace MALPlus.Models.Models.Notifications
{
    public class MalNotification
    {
        public MalNotificationsTypes Type { get; protected set; } = MalNotificationsTypes.Generic;
        public string Id { get; set; }
        public string Content { get; set; }
        public string Date { get; set; }
        public string Header { get; set; }
        public string LaunchArgs { get; set; }
        public bool IsSupported { get; set; }
        public bool IsRead { get; set; }
        public string ImgUrl { get; set; }

        /// <summary>
        ///     What to show when there is no picture, which is almost all of them.
        /// </summary>
        /// <remarks>
        ///     MAL only ever sends an image for a friend request and for a profile comment. Every other
        ///     type arrives without one, and the row reserved a fixed 40dp slot for it regardless, so
        ///     the left of nearly every notification was an empty gutter.
        ///     <para>
        ///     Glyphs rather than pictures, out of the set the rest of the app draws with, so this looks
        ///     like a notification with a type rather than a notification with something missing.
        ///     </para>
        /// </remarks>
        /// <summary>
        ///     Whether the row has a picture of its own to show.
        /// </summary>
        /// <remarks>
        ///     The row falls back to <see cref="TypeIcon" /> when this is false. It exists because the
        ///     fallback used to be a trigger on ImgUrl being null, and MAL hands back an empty string just
        ///     as often as it hands back nothing, so those rows kept an image slot that resolved to
        ///     nothing at all instead of showing the glyph.
        /// </remarks>
        public bool HasImage => !string.IsNullOrWhiteSpace(ImgUrl);

        public string TypeIcon => Type switch
        {
            MalNotificationsTypes.FriendRequest => "icon_user",
            MalNotificationsTypes.FriendRequestAcceptDeny => "icon_user",
            MalNotificationsTypes.ProfileComment => "icon_user",
            MalNotificationsTypes.Messages => "icon_envelope",
            MalNotificationsTypes.WatchedTopic => "icon_forum",
            MalNotificationsTypes.WatchedTopics => "icon_forum",
            MalNotificationsTypes.ForumQuoute => "icon_forum",
            MalNotificationsTypes.UserMentions => "icon_forum",
            MalNotificationsTypes.ClubMessages => "icon_club",
            MalNotificationsTypes.BlogComment => "icon_go",
            MalNotificationsTypes.NowAiring => "icon_calendar",
            MalNotificationsTypes.Payment => "icon_account",
            MalNotificationsTypes.NewRelatedAnime => "icon_anime",
            _ => "icon_bell"
        };

        public string SanitizedLaunchArgs => LaunchArgs.Contains("~") ? LaunchArgs.Split('~').Last() : LaunchArgs;

        public MalNotification(string id)
        {
            Id = id;
        }

        public MalNotification(MalMessageModel malMessageModel)
        {
            IsRead = false;
            Type = MalNotificationsTypes.Messages;
            Header = $"{malMessageModel.Sender} sent you a message!";
            Content = malMessageModel.Content;
            Id = malMessageModel.Id;
            IsSupported = true;
            LaunchArgs = $"https://myanimelist.net/mymessages.php?go=read&id={malMessageModel.Id}|{JsonConvert.SerializeObject(malMessageModel)}";
            Date = malMessageModel.Date;
        }

        public MalNotification(WatchedTopicModel watchedTopicModel)
        {
            Type = MalNotificationsTypes.WatchedTopic;
            Header = "Watched topic replies!";
            Content = $"New activity in {watchedTopicModel.Title} topic.";
            LaunchArgs = $"https://myanimelist.net/forum/?topicid={watchedTopicModel.Id}&goto=lastpost";
            IsSupported = true;
        }

        public MalNotification(MalScrappedNotification notification)
        {
            IsRead = notification.isRead;

            if (IsRead)
                return;

            switch (notification.typeIdentifier)
            {
                case "friend_request":
                    Type = MalNotificationsTypes.FriendRequest;
                    Header = "New friend request";
                    Content = $"{notification.friendName} sent you a friend request!";
                    LaunchArgs = notification.url;
                    IsSupported = false;
                    ImgUrl = notification.friendImageUrl;
                    break;
                case "friend_request_accept":
                    Type = MalNotificationsTypes.FriendRequestAcceptDeny;
                    Header = "Friend request accepted";
                    Content = $"{notification.friendName} accepted your friend request!";
                    IsSupported = false;
                    break;
                case "friend_request_deny":
                    Type = MalNotificationsTypes.FriendRequestAcceptDeny;
                    Header = "Friend request denied";
                    Content = $"{notification.friendName} rejected your friend request.";
                    IsSupported = false;
                    break;
                case "profile_comment":
                    Type = MalNotificationsTypes.ProfileComment;
                    Header = "New profile comment";
                    Content = $"{notification.commentUserName} posted a comment on your profile\n{notification.text}.";
                    LaunchArgs = notification.url+$"|{notification.commentUserName}";
                    ImgUrl = notification.commentUserImageUrl;
                    IsSupported = true;
                    break;
                case "forum_quote":
                    Type = MalNotificationsTypes.ForumQuoute;
                    Header = "New forum quote!";
                    Content = $"{notification.quoteUserName} has quouted your post in the \"{notification.topicTitle}\" thread.";
                    LaunchArgs = notification.url;
                    IsSupported = true;
                    break;
                case "blog_comment":
                    Type = MalNotificationsTypes.BlogComment;
                    Header = "New blog comment.";
                    IsSupported = false;
                    break;
                case "watched_topic_message":
                    Type = MalNotificationsTypes.WatchedTopics;
                    Header = "New reply on your watched topic!";
                    Content = $"{notification.postedUserName} has posted on your watched topic: \"{notification.topicTitle}\"";
                    LaunchArgs = notification.topicUrl + "&goto=lastpost";
                    IsSupported = true;
                    break;
                case "club_mass_message_in_forum":
                    Type = MalNotificationsTypes.ClubMessages;
                    Header = "New club message.";
                    IsSupported = false;
                    break;
                case "user_mention_in_club_comment":
                    Type = MalNotificationsTypes.UserMentions;
                    Header = notification.categoryName;
                    Content = $"{notification.senderName} has mentioned you in club {notification.pageTitle}";
                    LaunchArgs = notification.pageUrl;
                    IsSupported = false;
                    break;
                case "on_air":
                    Type = MalNotificationsTypes.NowAiring;
                    Header = notification.categoryName;
                    Content = $"The anime you plan to watch began airing on {notification.date} {notification.animes.First().title}";
                    LaunchArgs = notification.animes.First().url;
                    IsSupported = true;
                    break;
                case "payment_stripe":
                    Type = MalNotificationsTypes.Payment;
                    Header = "Payment notification.";
                    Content = "(I don't know what does it mean, feel free to let me know about this on github)";
                    IsSupported = false;
                    break;
                case "related_anime_add":
                    Type = MalNotificationsTypes.NewRelatedAnime;
                    Header = notification.categoryName;
                    Content = $"{notification.anime.title} ({notification.anime.mediaType}) has just been added to MAL database!";
                    LaunchArgs = notification.url;
                    IsSupported = true;
                    break;
                case "user_mention_in_forum_message":
                    Type = MalNotificationsTypes.UserMentions;
                    Header = notification.categoryName;
                    Content = $"{notification.senderName} has mentioned you in forum message {notification.pageTitle}";
                    LaunchArgs = notification.url;
                    IsSupported = true;
                    break;
                default:
                    Type = MalNotificationsTypes.Generic;
                    break;
            }
            LaunchArgs = $"{notification.id}~{LaunchArgs}";
            Id = notification.id;
            Date = notification.createdAtForDisplay;
        }


    }
}
