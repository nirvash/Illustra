using Prism.Events;

namespace Illustra.Events
{
    public class FavoriteDisplayNameChangedEvent : PubSubEvent<FavoriteDisplayNameChangedEventArgs>
    {
    }

    public class FavoriteDisplayNameChangedEventArgs
    {
        public string FolderPath { get; set; }
        public string NewDisplayName { get; set; }
        public string SourceId { get; set; }

        public FavoriteDisplayNameChangedEventArgs(string folderPath, string newDisplayName, string sourceId)
        {
            FolderPath = folderPath;
            NewDisplayName = newDisplayName;
            SourceId = sourceId;
        }
    }
}
