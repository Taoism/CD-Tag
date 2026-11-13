using System;
using System.Runtime.InteropServices;

namespace CDTag.Common.Win32API
{
    public static partial class Win32
    {
        /// <summary>
        /// Destroys an icon.
        /// </summary>
        /// <param name="hIcon">The icon handle.</param>
        /// <returns><c>true</c> if the icon was destroyed.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
