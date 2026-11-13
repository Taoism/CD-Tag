using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IdSharp.Tagging.ID3v1;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.ID3v2.Frames;
using IdSharp.Tagging.VorbisComment;

namespace CDTag.Model.Tag
{
    /// <summary>The fields Mass Tag edits.</summary>
    public enum TagField
    {
        TrackNumber,
        Artist,
        Title,
        Album,
        AlbumArtist,
        Year,
        Genre,
        DiscNumber,
        Comment
    }

    /// <summary>The Mass Tag fields of one file. Missing fields are "".</summary>
    public class TagValues
    {
        public static readonly TagField[] Fields = Enum.GetValues<TagField>();

        private readonly Dictionary<TagField, string> _values = new Dictionary<TagField, string>();

        public string this[TagField field]
        {
            get
            {
                string value;
                return _values.TryGetValue(field, out value) ? value : "";
            }
            set { _values[field] = (value ?? "").Trim(); }
        }

        public TagValues Clone()
        {
            var copy = new TagValues();
            foreach (TagField field in Fields)
                copy[field] = this[field];
            return copy;
        }

        /// <summary>Fields whose value differs from <paramref name="original"/>.</summary>
        public List<TagField> ChangedFrom(TagValues original)
        {
            return Fields.Where(f => !string.Equals(this[f], original[f], StringComparison.Ordinal)).ToList();
        }
    }

    /// <summary>
    /// Reads and writes the Mass Tag fields of MP3 (ID3v2, and ID3v1 when the file has one) and FLAC (Vorbis comments).
    /// Writing changes only the given fields; everything else in the tags (pictures, other frames) is kept.
    /// </summary>
    public static class MassTagger
    {
        public static bool CanTag(string path)
        {
            string extension = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return extension == ".mp3" || extension == ".flac";
        }

        public static TagValues Read(string path)
        {
            var values = new TagValues();
            if (IsFlac(path))
            {
                NameValueList items = new VorbisComment(path).Items;
                values[TagField.TrackNumber] = items.GetValue("TRACKNUMBER");
                values[TagField.Artist] = items.GetValue("ARTIST");
                values[TagField.Title] = items.GetValue("TITLE");
                values[TagField.Album] = items.GetValue("ALBUM");
                values[TagField.AlbumArtist] = items.GetValue("ALBUMARTIST");
                values[TagField.Year] = items.GetValue("DATE") ?? items.GetValue("YEAR");
                values[TagField.Genre] = items.GetValue("GENRE");
                values[TagField.DiscNumber] = items.GetValue("DISCNUMBER");
                values[TagField.Comment] = items.GetValue("COMMENT") ?? items.GetValue("DESCRIPTION");
                return values;
            }

            if (ID3v2Tag.DoesTagExist(path))
            {
                var id3v2 = new ID3v2Tag(path);
                values[TagField.TrackNumber] = id3v2.TrackNumber;
                values[TagField.Artist] = id3v2.Artist;
                values[TagField.Title] = id3v2.Title;
                values[TagField.Album] = id3v2.Album;
                values[TagField.AlbumArtist] = id3v2.AlbumArtist;
                values[TagField.Year] = id3v2.Year;
                values[TagField.Genre] = id3v2.Genre;
                values[TagField.DiscNumber] = id3v2.DiscNumber;
                IComments comment = id3v2.CommentsList.FirstOrDefault();
                values[TagField.Comment] = comment != null ? comment.Value : null;
            }
            else if (ID3v1Tag.DoesTagExist(path))
            {
                var id3v1 = new ID3v1Tag(path);
                values[TagField.TrackNumber] = id3v1.TrackNumber > 0 ? id3v1.TrackNumber.ToString() : null;
                values[TagField.Artist] = id3v1.Artist;
                values[TagField.Title] = id3v1.Title;
                values[TagField.Album] = id3v1.Album;
                values[TagField.Year] = id3v1.Year;
                values[TagField.Genre] = id3v1.GenreIndex < GenreHelper.GenreByIndex.Length ? GenreHelper.GenreByIndex[id3v1.GenreIndex] : null;
                values[TagField.Comment] = id3v1.Comment;
            }
            return values;
        }

        /// <summary>Writes <paramref name="fields"/> of <paramref name="values"/>; an empty value removes the field.</summary>
        public static void Write(string path, TagValues values, ICollection<TagField> fields)
        {
            if (fields.Count == 0)
                return;

            if (IsFlac(path))
            {
                var vorbis = new VorbisComment(path);
                NameValueList items = vorbis.Items;
                foreach (TagField field in fields)
                {
                    switch (field)
                    {
                        case TagField.TrackNumber: Set(items, "TRACKNUMBER", values[field]); break;
                        case TagField.Artist: Set(items, "ARTIST", values[field]); break;
                        case TagField.Title: Set(items, "TITLE", values[field]); break;
                        case TagField.Album: Set(items, "ALBUM", values[field]); break;
                        case TagField.AlbumArtist: Set(items, "ALBUMARTIST", values[field]); break;
                        case TagField.Year:
                            Set(items, "DATE", values[field]);
                            Remove(items, "YEAR");
                            break;
                        case TagField.Genre: Set(items, "GENRE", values[field]); break;
                        case TagField.DiscNumber: Set(items, "DISCNUMBER", values[field]); break;
                        case TagField.Comment:
                            Set(items, "COMMENT", values[field]);
                            Remove(items, "DESCRIPTION");
                            break;
                    }
                }
                vorbis.Save(path);
                return;
            }

            var id3v2 = new ID3v2Tag(path);
            foreach (TagField field in fields)
            {
                string value = values[field].Length == 0 ? null : values[field];
                switch (field)
                {
                    case TagField.TrackNumber: id3v2.TrackNumber = value; break;
                    case TagField.Artist: id3v2.Artist = value; break;
                    case TagField.Title: id3v2.Title = value; break;
                    case TagField.Album: id3v2.Album = value; break;
                    case TagField.AlbumArtist: id3v2.AlbumArtist = value; break;
                    case TagField.Year: id3v2.Year = value; break;
                    case TagField.Genre: id3v2.Genre = value; break;
                    case TagField.DiscNumber: id3v2.DiscNumber = value; break;
                    case TagField.Comment:
                        IComments comment = id3v2.CommentsList.FirstOrDefault();
                        if (value == null)
                        {
                            if (comment != null)
                                id3v2.CommentsList.Remove(comment);
                        }
                        else
                        {
                            if (comment == null)
                            {
                                comment = id3v2.CommentsList.AddNew();
                                comment.Description = "";
                            }
                            comment.Value = value;
                        }
                        break;
                }
            }
            id3v2.Save(path);

            // Keep an existing ID3v1 tag in step (30 characters per field).
            if (ID3v1Tag.DoesTagExist(path))
            {
                var id3v1 = new ID3v1Tag(path);
                foreach (TagField field in fields)
                {
                    string value = values[field];
                    switch (field)
                    {
                        case TagField.TrackNumber:
                            int number;
                            id3v1.TrackNumber = int.TryParse(value.Split('/')[0], out number) && number > 0 && number < 256 ? number : 0;
                            break;
                        case TagField.Artist: id3v1.Artist = Truncate(value, 30); break;
                        case TagField.Title: id3v1.Title = Truncate(value, 30); break;
                        case TagField.Album: id3v1.Album = Truncate(value, 30); break;
                        case TagField.Year: id3v1.Year = Truncate(value, 4); break;
                        case TagField.Genre: id3v1.GenreIndex = value.Length == 0 ? 255 : GenreHelper.GetGenreIndex(value); break;
                        case TagField.Comment: id3v1.Comment = Truncate(value, 28); break;
                    }
                }
                id3v1.Save(path);
            }
        }

        private static bool IsFlac(string path)
        {
            return string.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase);
        }

        private static void Set(NameValueList items, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Remove(items, key);
            else
                items.SetValue(key, value.Trim());
        }

        private static void Remove(NameValueList items, string key)
        {
            foreach (NameValueItem item in items.Where(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase)).ToList())
                items.Remove(item);
        }

        private static string Truncate(string text, int length)
        {
            text = (text ?? "").Trim();
            return text.Length > length ? text.Substring(0, length) : text;
        }
    }
}
