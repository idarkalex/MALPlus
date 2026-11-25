using System;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;

namespace MALPlus.XShared.Interfaces
{
    public interface IShareManager
    {
        event EventHandler<bool> TimerStateChanged;

        void EnqueueEvent(ShareEvent action, AnimeShareDiff diff);
        string GenerateMessage();
        void Dismiss();

        string GenerateRecommendMessage(string title, int id, bool isAnime, float score, string type, int totalEpisodes);
        string GenerateMyEntryMessage(string title, int id, bool isAnime, string status, int myScore, int watched, int totalEpisodes);
        ShareCardModel GetCurrentShareCardModel();
    }
}