using System;
using System.Runtime.InteropServices;

namespace CDTag.Common.Win32API
{
    public static partial class Win32
    {
        /// <summary>
        /// SHGetFileInfo (the Unicode SHGetFileInfoW, matching <see cref="SHFILEINFO"/>).
        /// </summary>
        /// <param name="pszPath">The path of the file or directory.</param>
        /// <param name="dwFileAttributes">The file attributes.</param>
        /// <param name="psfi">The SHFILEINFO.</param>
        /// <param name="cbSizeFileInfo">The size file info.</param>
        /// <param name="uFlags">The flags.</param>
        /// <returns>Non-zero on success (the meaning depends on <paramref name="uFlags"/>).</returns>
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr SHGetFileInfo(string pszPath,
                                             int dwFileAttributes,
                                             out SHFILEINFO psfi,
                                             uint cbSizeFileInfo,
                                             UInt32 uFlags);
    }
}
