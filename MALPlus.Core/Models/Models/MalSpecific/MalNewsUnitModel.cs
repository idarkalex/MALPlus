using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MALClient.Models.Models.MalSpecific
{
    public enum MalNewsType
    {
        Article,
        News,
    }

    public class MalNewsUnitModel : INotifyPropertyChanged
    {
        private string _imgUrl;

        /// <summary>
        ///     Notifies because the ANN list fills the missing pictures in the background, once
        ///     the story pages have been fetched. Without this the cards would keep the blank
        ///     they were bound with until the whole list was rebuilt.
        /// </summary>
        public string ImgUrl
        {
            get => _imgUrl;
            set
            {
                if (_imgUrl == value)
                    return;
                _imgUrl = value;
                OnPropertyChanged();
            }
        }

        public string Url { get; set; }
        public string Title { get; set; }
        public string Highlight { get; set; }
        public string Author { get; set; }

        /// <summary>
        ///     Shown next to the source instead of the author. MAL writes the age of the news in
        ///     the same line as the editor ("2 hours ago by someone") and ANN has a real date, so
        ///     one string covers both and the editor name, which was often blank or meaningless,
        ///     stops taking up the space.
        /// </summary>
        public string PublishedLabel { get; set; }
        public string Views { get; set; }
        public string Tags { get; set; }
        public MalNewsType Type { get; set; }
        public string Source { get; set; } = "MAL";
        public DateTime? PublishedAt { get; set; }
        public string Id { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}