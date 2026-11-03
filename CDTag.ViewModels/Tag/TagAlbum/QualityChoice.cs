namespace CDTag.ViewModels.Tag.TagAlbum
{
    /// <summary>A Bit/kHz choice for WEB FLAC release names ("24BIT-96KHZ"); Value "" = from the files.</summary>
    public class QualityChoice
    {
        public string Value { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
