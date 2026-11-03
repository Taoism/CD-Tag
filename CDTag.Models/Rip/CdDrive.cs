using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CDTag.Model.Rip
{
    /// <summary>One entry of a CD's table of contents. Addresses are LBA sectors (75 per second).</summary>
    public class CdTrack
    {
        public int Number { get; set; }
        public int StartSector { get; set; }
        /// <summary>First sector after the track (the next track's start, or the lead-out).</summary>
        public int EndSector { get; set; }
        public bool IsAudio { get; set; }
        /// <summary>Pre-emphasis flag from the TOC control bits.</summary>
        public bool PreEmphasis { get; set; }
        public int Sectors { get { return EndSector - StartSector; } }
    }

    /// <summary>
    /// A CD/DVD drive opened for reading audio: table of contents, drive name and raw CD-DA sectors
    /// (2,352 bytes each) through the Windows CD-ROM class driver (IOCTL_CDROM_*). No admin rights needed.
    /// </summary>
    public sealed class CdDrive : IDisposable
    {
        public const int SectorBytes = 2352;
        public const int SamplesPerSector = 588;
        /// <summary>Sectors per read request (2352 * 26 = 61,152 bytes, below the usual 64 KB transfer limit).</summary>
        public const int MaxSectorsPerRead = 26;

        private const uint IOCTL_CDROM_READ_TOC = 0x00024000;
        private const uint IOCTL_CDROM_RAW_READ = 0x0002403E;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;
        private const uint IOCTL_STORAGE_CHECK_VERIFY = 0x002D4800;

        private readonly SafeFileHandle _handle;

        public char Letter { get; private set; }

        private CdDrive(char letter, SafeFileHandle handle)
        {
            Letter = letter;
            _handle = handle;
        }

        /// <summary>Opens drive <paramref name="letter"/> ("S", "S:" or "S:\").</summary>
        public static CdDrive Open(string letter)
        {
            if (string.IsNullOrWhiteSpace(letter))
                throw new ArgumentException("No drive.");
            char c = char.ToUpperInvariant(letter.Trim()[0]);
            SafeFileHandle handle = CreateFileW(@"\\.\" + c + ":", 0x80000000 /*GENERIC_READ*/, 3 /*share read|write*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new IOException(string.Format("Cannot open drive {0}: {1}", c, new Win32Exception(Marshal.GetLastWin32Error()).Message));
            return new CdDrive(c, handle);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }

        /// <summary>True when a disc is in the drive and ready.</summary>
        public bool IsReady
        {
            get
            {
                int returned;
                return DeviceIoControl(_handle, IOCTL_STORAGE_CHECK_VERIFY, null, 0, null, 0, out returned, IntPtr.Zero);
            }
        }

        /// <summary>"HL-DT-ST BDDVDRW UH12NS40 1.01": vendor, product and firmware from the storage descriptor.</summary>
        public string Name
        {
            get
            {
                string[] parts = DescriptorStrings();
                return parts == null ? null : string.Join(" ", Array.FindAll(parts, p => p.Length > 0));
            }
        }

        public string Vendor { get { string[] p = DescriptorStrings(); return p != null ? p[0] : null; } }
        public string Product { get { string[] p = DescriptorStrings(); return p != null ? p[1] : null; } }

        /// <summary>Vendor, product and revision ("" when missing); null when the drive can't be queried.</summary>
        private string[] DescriptorStrings()
        {
            {
                // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0) }
                byte[] query = new byte[12];
                byte[] buffer = new byte[1024];
                int returned;
                if (!DeviceIoControl(_handle, IOCTL_STORAGE_QUERY_PROPERTY, query, query.Length, buffer, buffer.Length, out returned, IntPtr.Zero))
                    return null;
                // STORAGE_DEVICE_DESCRIPTOR: offsets of the vendor, product and revision strings
                var parts = new string[3];
                int[] at = { 12, 16, 20 };
                for (int k = 0; k < 3; k++)
                {
                    parts[k] = "";
                    int offset = BitConverter.ToInt32(buffer, at[k]);
                    if (offset <= 0 || offset >= returned)
                        continue;
                    int end = offset;
                    while (end < returned && buffer[end] != 0)
                        end++;
                    parts[k] = Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
                }
                return parts;
            }
        }

        /// <summary>The table of contents (session 1 and later; data tracks included and marked).</summary>
        public List<CdTrack> ReadToc()
        {
            // CDROM_TOC: Length (2), FirstTrack, LastTrack, then 100 TRACK_DATA of 8 bytes
            byte[] toc = new byte[4 + 100 * 8];
            int returned;
            if (!DeviceIoControl(_handle, IOCTL_CDROM_READ_TOC, null, 0, toc, toc.Length, out returned, IntPtr.Zero))
                throw new IOException("Cannot read the table of contents: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

            int first = toc[2], last = toc[3];
            if (first < 1 || last < first || last > 99)
                throw new IOException("The table of contents is empty (no disc, or not an audio CD).");

            var entries = new List<Tuple<int, int, int>>();   // track number (0xAA = lead-out), control, LBA
            for (int i = 0; i <= last - first + 1; i++)
            {
                int o = 4 + i * 8;
                int control = toc[o + 1] & 0x0F;   // TRACK_DATA: Control is the low nibble, Adr the high one
                int number = toc[o + 2];
                int lba = Msf(toc[o + 5], toc[o + 6], toc[o + 7]);
                entries.Add(Tuple.Create(number, control, lba));
            }

            var tracks = new List<CdTrack>();
            for (int i = 0; i < entries.Count - 1; i++)
            {
                tracks.Add(new CdTrack
                {
                    Number = entries[i].Item1,
                    StartSector = entries[i].Item3,
                    EndSector = entries[i + 1].Item3,
                    IsAudio = (entries[i].Item2 & 0x4) == 0,
                    PreEmphasis = (entries[i].Item2 & 0x1) != 0
                });
            }
            // Enhanced CD: the last audio track ends 11,400 sectors (lead-out + lead-in of session 2) before the data track.
            for (int i = 0; i + 1 < tracks.Count; i++)
            {
                if (tracks[i].IsAudio && !tracks[i + 1].IsAudio)
                    tracks[i].EndSector = Math.Max(tracks[i].StartSector + 1, tracks[i + 1].StartSector - 11400);
            }
            return tracks;
        }

        /// <summary>The lead-out sector of the audio session (the end of the last audio track).</summary>
        public static int AudioLeadOut(IList<CdTrack> tracks)
        {
            int leadOut = 0;
            foreach (CdTrack t in tracks)
            {
                if (t.IsAudio)
                    leadOut = t.EndSector;
            }
            return leadOut;
        }

        /// <summary>
        /// Reads <paramref name="count"/> CD-DA sectors (at most <see cref="MaxSectorsPerRead"/>) starting at <paramref name="lba"/>
        /// into <paramref name="buffer"/> (count * 2352 bytes). Returns false when the drive reports an error.
        /// </summary>
        public bool ReadSectors(int lba, int count, byte[] buffer, int offset = 0)
        {
            if (count <= 0 || count > MaxSectorsPerRead || buffer.Length - offset < count * SectorBytes)
                throw new ArgumentOutOfRangeException("count");
            // RAW_READ_INFO: LARGE_INTEGER DiskOffset (in 2048-byte units), ULONG SectorCount, TRACK_MODE_TYPE TrackMode (CDDA = 2)
            byte[] info = new byte[16];
            BitConverter.GetBytes((long)lba * 2048).CopyTo(info, 0);
            BitConverter.GetBytes(count).CopyTo(info, 8);
            BitConverter.GetBytes(2).CopyTo(info, 12);
            int returned;
            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr target = IntPtr.Add(pin.AddrOfPinnedObject(), offset);
                return DeviceIoControl(_handle, IOCTL_CDROM_RAW_READ, info, info.Length, target, count * SectorBytes, out returned, IntPtr.Zero)
                       && returned == count * SectorBytes;
            }
            finally
            {
                pin.Free();
            }
        }

        public void Eject()
        {
            int returned;
            DeviceIoControl(_handle, IOCTL_STORAGE_EJECT_MEDIA, null, 0, null, 0, out returned, IntPtr.Zero);
        }

        private static int Msf(int m, int s, int f)
        {
            return (m * 60 + s) * 75 + f - 150;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    }
}
