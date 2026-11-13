using System;
using System.Linq;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Library;
using MALPlus.XShared.Comm.Discovery;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Utils
{
    /// <summary>
    ///     Single, unified factory for constructing AnimeItemViewModel across all pages
    ///     (AnimePosterGrid, DiscoveryResultsPage, Recommendations, Trending, Top, Search).
    ///     Ensures cards have consistent visual indicators:
    ///     - Reuses user library entry if present (with user's watching progress, badge and user score).
    ///     - For non-library entries: sets format TypeTag (TV/Movie/OVA...), mean CardScore,
    ///       hides the library corner badge, and connects countdown/airing info.
    /// </summary>
    public static class AnimeItemFactory
    {
        public static AnimeItemViewModel Create(
            int id,
            string title,
            string imgUrl,
            string type = null,
            double score = 0,
            int allEpisodes = 0,
            bool isManga = false,
            string subtitle = null)
        {
            if (id <= 0)
                return null;

            var storage = ResourceLocator.AnimeLibraryDataStorage;
            AnimeItemAbstraction existing = null;

            if (storage != null)
            {
                var list = isManga
                    ? storage.AllLoadedMangaItemAbstractions
                    : storage.AllLoadedAnimeItemAbstractions;

                if (list != null && list.Count > 0)
                {
                    existing = list.FirstOrDefault(a => a != null && (a.Id == id || a.MalId == id));
                }
            }

            AnimeItemViewModel vm;
            if (existing != null)
            {
                vm = existing.ViewModel;
                if (vm != null)
                {
                    if (!string.IsNullOrEmpty(subtitle))
                        vm.Subtitle = subtitle;
                    vm.AllowDetailsNavigation = true;
                    return vm;
                }
            }

            if (isManga)
            {
                var mType = MalTypeParser.ParseMangaType(type);
                var data = new MangaLibraryItemData
                {
                    Id = id,
                    Title = title,
                    ImgUrl = imgUrl,
                    Type = (int)mType,
                    GlobalScore = (float)score,
                    AllEpisodes = allEpisodes
                };
                var abstraction = new AnimeItemAbstraction(false, data)
                {
                    GlobalScore = (float)score
                };
                vm = abstraction.ViewModel;
            }
            else
            {
                var aType = MalTypeParser.ParseAnimeType(type);
                var data = new AnimeLibraryItemData
                {
                    Id = id,
                    Title = title,
                    ImgUrl = imgUrl,
                    Type = (int)aType,
                    GlobalScore = (float)score,
                    AllEpisodes = allEpisodes
                };
                var abstraction = new AnimeItemAbstraction(false, data)
                {
                    GlobalScore = (float)score
                };
                vm = abstraction.ViewModel;
            }

            if (vm != null)
            {
                if (!string.IsNullOrEmpty(subtitle))
                    vm.Subtitle = subtitle;
                vm.AllowDetailsNavigation = true;
            }

            return vm;
        }

        public static AnimeItemViewModel CreateFromDiscovery(DiscoveryItem item)
        {
            if (item == null)
                return null;

            return Create(
                item.Id,
                item.Title,
                item.ImgUrl,
                item.Type,
                item.Score,
                item.AllEpisodes,
                item.IsManga,
                item.Subtitle);
        }
    }
}
