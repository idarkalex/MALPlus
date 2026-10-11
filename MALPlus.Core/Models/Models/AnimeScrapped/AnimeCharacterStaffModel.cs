using System.Collections.Generic;
using MALPlus.Models.Models.Favourites;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class AnimeCharacterStaffModel
    {
        public AnimeCharacter AnimeCharacter { get; set; } = new AnimeCharacter();
        public AnimeStaffPerson AnimeStaffPerson { get; set; } = new AnimeStaffPerson();
        public List<AnimeStaffPerson> VoiceActors { get; set; } = new List<AnimeStaffPerson>();
    }
}