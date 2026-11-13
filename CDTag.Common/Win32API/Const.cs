using System;

namespace CDTag.Common.Win32API
{
    public static partial class Win32
    {
        /// <summary>FILE_ATTRIBUTE_NORMAL</summary>
        public const UInt32 FILE_ATTRIBUTE_NORMAL = 0x80;
        /// <summary>FILE_ATTRIBUTE_DIRECTORY</summary>
        public const UInt32 FILE_ATTRIBUTE_DIRECTORY = 0x10;

        /// <summary>SHGFI_ICON; Get icon.</summary>
        public const UInt32 SHGFI_ICON = 0x100;
        /// <summary>SHGFI_SMALLICON; Get small icon.</summary>
        public const UInt32 SHGFI_SMALLICON = 0x01;
        /// <summary>SHGFI_DISPLAYNAME; Get display name.</summary>
        public const UInt32 SHGFI_DISPLAYNAME = 0x200;
        /// <summary>SHGFI_USEFILEATTRIBUTES; Use passed dwFileAttribute.</summary>
        public const UInt32 SHGFI_USEFILEATTRIBUTES = 0x10;
        /// <summary>SHGFI_TYPENAME; Get type name.</summary>
        public const UInt32 SHGFI_TYPENAME = 0x400;
    }
}
