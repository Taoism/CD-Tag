using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CDTag.Common;
using CDTag.Common.Model;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.ID3v2.Frames;

namespace CDTag.Model.Tag
{
    public class Picture : ModelBase
    {
        public Picture(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            PictureType = PictureType.CoverFront;

            byte[] pictureData = File.ReadAllBytes(path);
            PictureBytes = pictureData;
            try
            {
                MemoryStream memoryStream = new MemoryStream(pictureData);
                memoryStream.Seek(0, SeekOrigin.Begin);
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();

                ImageSource = bitmapImage;
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>Creates a picture from raw image bytes (e.g. a FLAC PICTURE block).</summary>
        public Picture(byte[] pictureData, PictureType pictureType, string description)
        {
            ArgumentNullException.ThrowIfNull(pictureData);

            PictureType = pictureType;
            Description = description;
            PictureBytes = pictureData;
            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = new MemoryStream(pictureData);
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                ImageSource = bitmapImage;
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>
        /// Parses a FLAC PICTURE metadata block (all integers big-endian):
        /// type(4) mimeLength(4) mime descriptionLength(4) description(UTF-8) width(4) height(4) depth(4) colors(4) dataLength(4) data.
        /// Returns null if the block is malformed.
        /// </summary>
        public static Picture FromFlacPictureBlock(byte[] block)
        {
            if (block == null)
                return null;
            try
            {
                int offset = 0;
                int type = ReadInt32BigEndian(block, ref offset);
                int mimeLength = ReadInt32BigEndian(block, ref offset);
                offset += mimeLength;
                int descriptionLength = ReadInt32BigEndian(block, ref offset);
                string description = System.Text.Encoding.UTF8.GetString(block, offset, descriptionLength);
                offset += descriptionLength;
                offset += 16; // width, height, colour depth, indexed colours
                int dataLength = ReadInt32BigEndian(block, ref offset);
                if (dataLength <= 0 || offset + dataLength > block.Length)
                    return null;

                byte[] data = new byte[dataLength];
                Buffer.BlockCopy(block, offset, data, 0, dataLength);

                PictureType pictureType = Enum.IsDefined(typeof(PictureType), (byte)type) ? (PictureType)(byte)type : PictureType.Other;
                return new Picture(data, pictureType, description);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static int ReadInt32BigEndian(byte[] data, ref int offset)
        {
            int value = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
            return value;
        }

        public Picture(IAttachedPicture attachedPicture)
        {
            ArgumentNullException.ThrowIfNull(attachedPicture);

            AttachedPicture = attachedPicture;

            Description = attachedPicture.Description;
            PictureType = attachedPicture.PictureType;

            byte[] pictureData = attachedPicture.PictureData;
            PictureBytes = pictureData;
            if (pictureData != null)
            {
                try
                {
                    MemoryStream memoryStream = new MemoryStream(pictureData);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();

                    ImageSource = bitmapImage;
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        public ImageSource ImageSource
        {
            get { return Get<ImageSource>(nameof(ImageSource)); }
            set { Set(nameof(ImageSource), value); }
        }

        public string Description
        {
            get { return Get<string>(nameof(Description)); }
            set { Set(nameof(Description), value); }
        }

        public PictureType PictureType
        {
            get { return Get<PictureType>(nameof(PictureType)); }
            set { Set(nameof(PictureType), value); }
        }

        public byte[] PictureBytes
        {
            get { return Get<byte[]>(nameof(PictureBytes)); }
            private set { Set(nameof(PictureBytes), value); }
        }

        public IAttachedPicture AttachedPicture
        {
            get { return Get<IAttachedPicture>(nameof(AttachedPicture)); }
            private set { Set(nameof(AttachedPicture), value); }
        }
    }
}
