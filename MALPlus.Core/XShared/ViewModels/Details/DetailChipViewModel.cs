using MALClient.Models.Enums;

namespace MALClient.XShared.ViewModels;

/// <summary>
///     A tappable chip on the details page: a genre or a studio, shown by its MAL name and carrying
///     the enum member that names the list to open.
/// </summary>
public class DetailChipViewModel
{
    public DetailChipViewModel(string name, AnimeGenreSearch? genre = null, AnimeStudios? studio = null)
    {
        Name = name;
        Genre = genre;
        Studio = studio;
    }

    /// <summary>Label as MAL spells it, which is what the chip shows.</summary>
    public string Name { get; }

    /// <summary>Set when the chip is a genre the search can filter by, null for themes.</summary>
    public AnimeGenreSearch? Genre { get; }

    /// <summary>Set when the chip is a studio the search can filter by.</summary>
    public AnimeStudios? Studio { get; }

    /// <summary>
    ///     Only genres and studios lead anywhere. The same row also carries MAL themes such as
    ///     "Ensemble Cast", which are not a genre id and have no list to open, so they stay plain
    ///     text rather than opening an unrelated result set.
    /// </summary>
    public bool IsNavigable => Genre.HasValue || Studio.HasValue;
}
