using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

namespace Illustra.Models
{
    public enum ThumbnailState
    {
        NotLoaded,    // まだロードされていない
        Loading,      // ロード中
        Loaded,       // 正常にロードされた
        Error         // ロード中にエラー発生
    }

    public class FileNodeModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string FullPath { get; set; } = string.Empty;

        private ThumbnailInfo? _thumbnailInfo = null;
        public ThumbnailInfo? ThumbnailInfo
        {
            get => _thumbnailInfo;
            set
            {
                if (_thumbnailInfo != value)
                {
                    _thumbnailInfo = value;
                    OnPropertyChanged(nameof(ThumbnailInfo));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ThumbnailInfo
    {
        public BitmapSource Thumbnail { get; set; }
        public ThumbnailState State { get; set; }

        public ThumbnailInfo(BitmapSource thumbnail, ThumbnailState state)
        {
            Thumbnail = thumbnail;
            State = state;
        }
    }
}
