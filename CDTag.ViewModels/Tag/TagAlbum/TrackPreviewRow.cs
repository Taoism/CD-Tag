using System;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    /// <summary>One line of Tag Album's Track Preview.</summary>
    public class TrackPreviewRow : IEquatable<TrackPreviewRow>
    {
        public int Disc { get; set; }
        public int Track { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Time { get; set; }

        public bool Equals(TrackPreviewRow other)
        {
            return other != null && Disc == other.Disc && Track == other.Track && Artist == other.Artist && Title == other.Title && Time == other.Time;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TrackPreviewRow);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Disc, Track, Artist, Title, Time);
        }
    }
}
