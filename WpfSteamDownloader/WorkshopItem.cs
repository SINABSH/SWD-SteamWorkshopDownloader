using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfSteamDownloader
{
    // This is our Model. It represents a single item in our download list.
    // It implements INotifyPropertyChanged so that when we change a property (like Status),
    // the UI automatically updates itself. This is a core concept of WPF data binding.
    public class WorkshopItem : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private string _url;
        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        // --- These properties are for the details panel ---
        private string _imageUrl;
        public string ImageUrl
        {
            get => _imageUrl;
            set { _imageUrl = value; OnPropertyChanged(); }
        }

        private string _author;
        public string Author
        {
            get => _author;
            set { _author = value; OnPropertyChanged(); }
        }

        private string _fileSize;
        public string FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); }
        }

        private string _datePosted;
        public string DatePosted
        {
            get => _datePosted;
            set { _datePosted = value; OnPropertyChanged(); }
        }

        private string _visitors;
        public string Visitors
        {
            get => _visitors;
            set { _visitors = value; OnPropertyChanged(); }
        }

        public string Type { get; set; } // "SingleItem" or "Collection"

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
