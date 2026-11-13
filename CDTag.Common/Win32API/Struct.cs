using System;
using System.Runtime.InteropServices;

namespace CDTag.Common.Win32API
{
    public static partial class Win32
    {
        /// <summary>Contains information about a file object. Used by <see cref="SHGetFileInfo"/>.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            /// <summary>Handle to the icon that represents the file.</summary>
            public IntPtr hIcon;
            /// <summary>Index of the icon image within the system image list.</summary>
            public int iIcon;
            /// <summary>Array of values that indicates the attributes of the file object.</summary>
            public UInt32 dwAttributes;
            /// <summary>String that contains the name of the file as it appears in the Microsoft Windows Shell, or the path and file name of the
            /// file that contains the icon representing the file.</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public String szDisplayName;
            /// <summary>String that describes the type of file.</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public String szTypeName;
        }
    }
}
