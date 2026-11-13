using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CDTag.Model.Tag;

namespace CDTag.Model.Checksum
{
    /// <summary>Checksum file formats: SFV (CRC-32), MD5 and SHA-1 lists.</summary>
    public enum ChecksumKind
    {
        Sfv,
        Md5,
        Sha1
    }

    /// <summary>One line of a checksum file: a file name (relative to the checksum file) and its expected hash.</summary>
    public class ChecksumEntry
    {
        public string FileName { get; set; }
        public string Hash { get; set; }
    }

    /// <summary>A file and its calculated hash, for <see cref="ChecksumList.Build"/>.</summary>
    public class ChecksumItem
    {
        public string Path { get; set; }
        public string Hash { get; set; }
    }

    /// <summary>Reads, writes and calculates SFV/MD5/SHA-1 checksum lists.</summary>
    public static partial class ChecksumList
    {
        /// <summary>Extensions of checksum files, never listed in a checksum file themselves.</summary>
        public static readonly string[] Extensions = { ".sfv", ".md5", ".sha1" };

        public static string GetExtension(ChecksumKind kind)
        {
            switch (kind)
            {
                case ChecksumKind.Md5:
                    return ".md5";
                case ChecksumKind.Sha1:
                    return ".sha1";
                default:
                    return ".sfv";
            }
        }

        /// <summary>The kind of checksum file from its extension; null when it is not one.</summary>
        public static ChecksumKind? FromPath(string path)
        {
            switch ((Path.GetExtension(path) ?? "").ToLowerInvariant())
            {
                case ".sfv":
                    return ChecksumKind.Sfv;
                case ".md5":
                    return ChecksumKind.Md5;
                case ".sha1":
                    return ChecksumKind.Sha1;
                default:
                    return null;
            }
        }

        public static bool IsChecksumFile(string path)
        {
            return FromPath(path) != null;
        }

        /// <summary>Reads a checksum file. Comment lines (";" in SFV, "#" or ";" in MD5/SHA-1) are skipped.</summary>
        public static List<ChecksumEntry> Parse(string path)
        {
            ChecksumKind kind = FromPath(path) ?? ChecksumKind.Sfv;
            return Parse(ReadText(File.ReadAllBytes(path)), kind);
        }

        public static List<ChecksumEntry> Parse(string text, ChecksumKind kind)
        {
            var entries = new List<ChecksumEntry>();
            int hashLength = kind == ChecksumKind.Md5 ? 32 : 40;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith(';') || (kind != ChecksumKind.Sfv && line.StartsWith('#')))
                    continue;

                Match match;
                if (kind == ChecksumKind.Sfv)
                {
                    match = SfvLine().Match(line);
                }
                else
                {
                    match = HashLine().Match(line);
                    if (!match.Success || match.Groups["hash"].Length != hashLength)
                        match = BsdLine().Match(line);
                    if (match.Success && match.Groups["hash"].Length != hashLength)
                        continue;
                }
                if (!match.Success)
                    continue;

                entries.Add(new ChecksumEntry
                {
                    FileName = match.Groups["name"].Value.Trim().Replace('/', '\\'),
                    Hash = Normalize(match.Groups["hash"].Value, kind)
                });
            }
            return entries;
        }

        /// <summary>The hash of a file, in the case used in checksum files (SFV upper case, MD5/SHA-1 lower case).</summary>
        public static string Compute(string path, ChecksumKind kind, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var stream = new CancellableStream(File.OpenRead(path), cancellationToken))
            {
                if (kind == ChecksumKind.Sfv)
                    return Crc32.Compute(stream).ToString("X8", CultureInfo.InvariantCulture);

                using (HashAlgorithm algorithm = kind == ChecksumKind.Md5 ? (HashAlgorithm)MD5.Create() : SHA1.Create())
                    return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
            }
        }

        public static bool HashEquals(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The contents of a checksum file for <paramref name="items"/> (paths relative to <paramref name="folder"/>), in the
        /// CD-Tag 2.x layout: a "Generated by" comment, (SFV only) a size/date comment per file, then one line per file.
        /// Windows-1252 when every name fits, otherwise UTF-8.
        /// </summary>
        public static byte[] Build(ChecksumKind kind, IList<ChecksumItem> items, string folder, bool hideGeneratedBy = false, bool hideFileInfo = false)
        {
            var sb = new StringBuilder();
            if (!hideGeneratedBy)
                sb.AppendFormat(CultureInfo.InvariantCulture, "; Generated by CD-Tag on {0:yyyy-MM-dd} at {0:HH:mm.ss}\r\n", DateTime.Now);

            if (kind == ChecksumKind.Sfv)
            {
                if (!hideFileInfo)
                {
                    foreach (ChecksumItem item in items)
                    {
                        var info = new FileInfo(item.Path);
                        sb.AppendFormat(CultureInfo.InvariantCulture, ";{0,13}  {1:HH:mm.ss} {1:yyyy-MM-dd} {2}\r\n", info.Length, info.LastWriteTime, RelativeTo(folder, item.Path));
                    }
                }
                foreach (ChecksumItem item in items)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}\r\n", RelativeTo(folder, item.Path), item.Hash);
            }
            else
            {
                foreach (ChecksumItem item in items)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0} *{1}\r\n", item.Hash, RelativeTo(folder, item.Path));
            }

            return Encode(sb.ToString());
        }

        /// <summary>Windows-1252 when the text survives a round trip, otherwise UTF-8 with a BOM.</summary>
        public static byte[] Encode(string text)
        {
            Encoding ansi = Ansi;
            byte[] bytes = ansi.GetBytes(text);
            if (ansi.GetString(bytes) == text)
                return bytes;
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        }

        /// <summary>UTF-8 (with or without a BOM) when the bytes are valid UTF-8, otherwise Windows-1252.</summary>
        public static string ReadText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Ansi.GetString(bytes);
            }
        }

        public static string RelativeTo(string folder, string path)
        {
            string root = folder.TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : Path.GetFileName(path);
        }

        private static string Normalize(string hash, ChecksumKind kind)
        {
            return kind == ChecksumKind.Sfv ? hash.ToUpperInvariant() : hash.ToLowerInvariant();
        }

        private static Encoding Ansi
        {
            get
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1252);
            }
        }

        /// <summary>A read-only stream that throws when the token is cancelled, so long hashes can be stopped.</summary>
        private sealed class CancellableStream : Stream
        {
            private readonly Stream _inner;
            private readonly CancellationToken _token;

            public CancellableStream(Stream inner, CancellationToken token)
            {
                _inner = inner;
                _token = token;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return _inner.Length; } }
            public override long Position { get { return _inner.Position; } set { throw new NotSupportedException(); } }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _token.ThrowIfCancellationRequested();
                return _inner.Read(buffer, offset, count);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        [GeneratedRegex(@"^(?<name>.+?)\s+(?<hash>[0-9A-Fa-f]{8})\s*$", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex SfvLine();

        [GeneratedRegex(@"^(?:MD5|SHA1)\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9A-Fa-f]{32,40})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex BsdLine();

        [GeneratedRegex(@"^(?<hash>[0-9A-Fa-f]{32,40})\s+[*\s]?(?<name>.+?)\s*$", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex HashLine();
    }
}
