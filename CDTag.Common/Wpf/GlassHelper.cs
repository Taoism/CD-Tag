using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CDTag.Common.Wpf
{
    /// <summary>
    /// GlassHelper
    /// </summary>
    public static class GlassHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern bool DwmIsCompositionEnabled();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // DWMWINDOWATTRIBUTE values (dwmapi.h)
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_MAINWINDOW = 2; // Mica

        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;

            public MARGINS(Thickness thickness)
            {
                Left = (int)thickness.Left;
                Right = (int)thickness.Right;
                Top = (int)thickness.Top;
                Bottom = (int)thickness.Bottom;
            }
        }

        /// <summary>Extends the glass frame.</summary>
        /// <param name="window">The window.</param>
        /// <param name="margin">The margin.</param>
        /// <returns><c>true</c> if successful; otherwise, <c>false</c>.</returns>
        /// <summary>
        /// Asks DWM to draw the window's title bar and frame dark (Windows 10 1809+ / Windows 11).
        /// Safe to call repeatedly; does nothing when the window has no handle yet or the OS lacks support.
        /// </summary>
        public static void SetDarkTitleBar(Window window, bool dark)
        {
            ArgumentNullException.ThrowIfNull(window);

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int value = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                    _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        /// <summary>
        /// Requests the Windows 11 Mica backdrop for the window. Mica shows through wherever the
        /// glass frame is extended and WPF draws nothing (see <see cref="ExtendGlassFrame"/>).
        /// Returns false on systems without support (they keep the plain extended frame).
        /// </summary>
        public static bool TrySetMicaBackdrop(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            int value = DWMSBT_MAINWINDOW;
            try
            {
                return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        public static bool ExtendGlassFrame(Window window, Thickness margin)
        {
            ArgumentNullException.ThrowIfNull(window);

            Brush oldBackground = window.Background;
            try
            {
                if (!DwmIsCompositionEnabled())
                    return false;

                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    throw new InvalidOperationException("The window must be shown before extending glass.");

                // Set the background to transparent from both the WPF and Win32 perspectives
                window.Background = Brushes.Transparent;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

                MARGINS margins = new MARGINS(margin);
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return true;
            }
            catch (DllNotFoundException)
            {
                window.Background = oldBackground;
                return false;
            }
        }
    }
}
