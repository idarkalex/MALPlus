using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Library;
using MALPlus.Models.Models.Misc;
using MALPlus.XShared.Utils;

// ReSharper disable InconsistentNaming

namespace MALPlus.XShared.ViewModels
{
    /// <summary>
    ///     This class serves as a container for actual UI AnimeItem element.
    ///     It contains all values required to sort and filter those items before loading them.
    /// </summary>
    public class AnimeItemAbstraction
    {
        private readonly bool _firstConstructor;
        private readonly SeasonalAnimeData _seasonalData;
        public readonly bool Auth;
        public readonly bool RepresentsAnime = true;

        private AnimeItemViewModel _viewModel;

        public int AirDay { get; }


        public string AirStartDate
        {
            get { return VolatileData.AirStartDate; }
            set { VolatileData.AirStartDate = value; }
        }

        public float GlobalScore
        {
            get { return VolatileData.GlobalScore; }
            set { VolatileData.GlobalScore = value; }
        }

        public AnimePriority Priority
        {
            get => EntryData?.Priority ?? AnimePriority.Low;
            set => EntryData.Priority = value;
        }

        public int Index;
        public bool LoadedAnime;
        public bool LoadedModel;
        public bool LoadedVolatile;
        public VolatileDataCache VolatileData { get; set; } = new VolatileDataCache();

        //three constructors depending on original init
        private AnimeItemAbstraction(ILibraryData entry, int? id = null)
        {
            if (entry != null)
            {
                EntryData = entry;
                MyStartDate = entry.MyStartDate;
                MyEndDate = entry.MyEndDate;
                GlobalScore = entry.GlobalScore;
            }

            if (ResourceLocator.AiringInfoProvider.TryGetAiringDay(id ?? Id, out DayOfWeek day))
                AirDay = (int) day+1;
            else
                AirDay = -1;

            // A cached entry carries the score the details page fetched, so the payload only fills the gap.
            // It matters for an entry that has never been opened, and equally for a cache written
            // before the payload carried a mean at all, which is why this runs after the load
            // instead of before it: assigning VolatileData above replaces the object outright.
            if (DataCache.TryRetrieveDataForId(id ?? Id, out VolatileDataCache data))
            {
                VolatileData = data;
                LoadedVolatile = true;
            }

            if (VolatileData.GlobalScore <= 0)
                GlobalScore = entry?.GlobalScore ?? 0;
        }

        //three constructors depending on original init
        public AnimeItemAbstraction(SeasonalAnimeData data, bool anime) : this(null, data.Id)
        {
            _seasonalData = data;
            Index = data.Index;
            RepresentsAnime = anime;
            if (!int.TryParse(data.Episodes, out var eps))
                eps = 0;
            AllEpisodes = eps;
            GlobalScore = data.Score;
        }

        public AnimeItemAbstraction(bool auth, MangaLibraryItemData data) : this(data)
        {
            RepresentsAnime = false;
            Auth = auth;
            _firstConstructor = true;
        }

        public AnimeItemAbstraction(bool auth, AnimeLibraryItemData data) : this(data)
        {
            Auth = auth;
            _firstConstructor = true;
        }

        public ILibraryData EntryData { get; set; }
        private int _seasonalAllEps;
        public string Title => EntryData?.Title ?? _seasonalData?.Title;
        public int Id => EntryData?.Id ?? _seasonalData?.Id ?? 0;
        public int MalId => EntryData?.MalId ?? _seasonalData?.Id ?? 0;
        public string ImgUrl => EntryData?.ImgUrl ?? _seasonalData?.ImgUrl;

        public bool IsRewatching
        {
            get { return EntryData?.IsRewatching ?? false; }
            set
            {
                if (EntryData != null)
                    EntryData.IsRewatching = value;
            }
        }

        public string AlternateTitle
        {
            get { return EntryData?.AlternateTitle; }
        }

        public int AllEpisodes
        {
            get { return EntryData?.AllEpisodes ?? _seasonalAllEps; }
            set { _seasonalAllEps = value; }
        }

        /// <summary>
        ///     Episode count for a panel that is showing a list's own number.
        /// </summary>
        /// <remarks>
        ///     The plain setter is not enough on a card that came from the user's library, because
        ///     <see cref="AllEpisodes" /> prefers the library entry over anything written here. That
        ///     is correct for the library and wrong for a Top or Seasonal panel, where the number that
        ///     belongs on the card is the one the incoming list carried, not the user's own. Setting
        ///     the field directly is what makes the card show the list it is actually on.
        /// </remarks>
        public void SetEpisodesForPanel(int episodes) => _seasonalAllEps = episodes;

        public int AllVolumes => (EntryData as MangaLibraryItemData)?.AllVolumes ?? 0;
        public int Type => EntryData?.Type ?? _seasonalData?.Type ?? 0;

        public string Notes
        {
            get { return EntryData?.Notes ?? ""; }
            set
            {
                if (EntryData != null)
                {
                    EntryData.Notes = value;
                    _tags = null;
                }
            }
        }

        private List<string> _tags;

        public List<string> Tags => _tags ?? (_tags = string.IsNullOrEmpty(Notes)
            ? new List<string>()
            : Notes.Contains(",")
                ? Notes.Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s).ToList()
                : new List<string> {Notes});

        public DateTime LastWatched
        {
            get { return EntryData?.LastWatched ?? DateTime.MinValue; }
            set
            {
                if (EntryData != null)
                    EntryData.LastWatched = value;
            }
        }

        public int MyEpisodes
        {
            get { return EntryData?.MyEpisodes ?? 0; }
            set { EntryData.MyEpisodes = value; }
        }

        public float MyScore
        {
            get { return EntryData?.MyScore ?? 0; }
            set { EntryData.MyScore = value; }
        }

        public AnimeStatus MyStatus
        {
            get { return EntryData?.MyStatus ?? AnimeStatus.AllOrAiring; }
            set { EntryData.MyStatus = (AnimeStatus) value; }
        }

        public int MyVolumes
        {
            get { return (EntryData as MangaLibraryItemData)?.MyVolumes ?? 0; }
            set
            {
                if (EntryData is MangaLibraryItemData)
                    ((MangaLibraryItemData) EntryData).MyVolumes = value;
            }
        }

        public string MyStartDate
        {
            get { return EntryData?.MyStartDate ?? AnimeItemViewModel.InvalidStartEndDate; }
            set
            {
                if (EntryData != null) EntryData.MyStartDate = value;
            }
        }

        public string MyEndDate
        {
            get { return EntryData?.MyEndDate ?? AnimeItemViewModel.InvalidStartEndDate; }
            set
            {
                if (EntryData != null) EntryData.MyEndDate = value;
            }
        }

        public ExactAiringTimeData ExactAiringTime
        {
            get { return VolatileData.ExactAiringTime; }
            set
            {
                VolatileData.ExactAiringTime = value;
                VolatileData.LastFailedAiringTimeFetchAttempt = null;
            }
        }


        public AnimeItemViewModel ViewModel
        {
            get
            {
                if (LoadedAnime)
                    return _viewModel;
                ViewModel = LoadElementModel();
                return _viewModel;
            }
            private set { _viewModel = value; }
        }



        public bool TryRetrieveVolatileData(bool force = false)
        {
            if (LoadedVolatile && !force)
                return true;
            VolatileDataCache data;
            if (!DataCache.TryRetrieveDataForId(Id, out data)) return false;
            VolatileData = data;
            LoadedVolatile = true;
            return true;
        }

        private AnimeItemViewModel LoadElementModel()
        {
            if (LoadedModel)
                return _viewModel;
            LoadedModel = true;
            if (RepresentsAnime)
                return _firstConstructor
                    ? new AnimeItemViewModel(Auth, Title, ImgUrl, Id, AllEpisodes, this, false)
                    : new AnimeItemViewModel(_seasonalData, this);
            return
                _firstConstructor
                    ? new AnimeItemViewModel(Auth, Title, ImgUrl, Id, AllEpisodes, this, false, AllVolumes)
                    : new AnimeItemViewModel(_seasonalData, this);
        }
    }
}