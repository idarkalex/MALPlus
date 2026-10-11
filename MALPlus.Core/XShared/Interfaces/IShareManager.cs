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
    }
}