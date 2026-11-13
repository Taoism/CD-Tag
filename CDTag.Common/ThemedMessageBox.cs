using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CDTag.Common
{
    /// <summary>
    /// Message boxes that follow the app theme: the Windows message box in light mode, and a dark dialog with the same
    /// layout (icon and text above, buttons on a footer strip) in dark mode, since Windows draws message boxes light only.
    /// </summary>
    public static class ThemedMessageBox
    {
        /// <summary>True while the dark theme is on (set by the theme manager).</summary>
        public static Func<bool> IsDark { get; set; }

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return Show(null, text, caption, button, icon);
        }

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            if (owner == null && Application.Current != null)
                owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
            if (owner != null && !owner.IsLoaded)
                owner = null;

            bool dark = IsDark != null && IsDark();
            if (!dark)
            {
                return owner != null
                    ? MessageBox.Show(owner, text ?? "", caption ?? "", button, icon)
                    : MessageBox.Show(text ?? "", caption ?? "", button, icon);
            }
            return ShowDark(owner, text ?? "", caption ?? "", button, icon);
        }

        private static readonly Brush Body = Frozen(0x20, 0x20, 0x20);
        private static readonly Brush Footer = Frozen(0x2B, 0x2B, 0x2B);
        private static readonly Brush Text = Frozen(0xE4, 0xE4, 0xE4);

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static MessageBoxResult ShowDark(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var result = DefaultResult(button);
            var window = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Background = Body,
                Foreground = Text,
                FontFamily = SystemFonts.MessageFontFamily,
                FontSize = SystemFonts.MessageFontSize,
                UseLayoutRounding = true,
                MinWidth = 180,
            };

            var root = new DockPanel { LastChildFill = true };

            // Footer with the buttons, right-aligned like the Windows message box.
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 11, 10, 11) };
            var footer = new Border { Background = Footer, Child = buttons };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var body = new DockPanel { Margin = new Thickness(24, 22, 28, 22) };
            ImageSource image = IconImage(icon);
            if (image != null)
            {
                var picture = new Image { Source = image, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
                DockPanel.SetDock(picture, Dock.Left);
                body.Children.Add(picture);
            }
            body.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                VerticalAlignment = image != null && !text.Contains('\n') ? VerticalAlignment.Center : VerticalAlignment.Top,
                Foreground = Text,
            });
            root.Children.Add(body);

            Button first = null;
            foreach (KeyValuePair<string, MessageBoxResult> choice in Choices(button))
            {
                MessageBoxResult value = choice.Value;
                var b = new Button
                {
                    Content = new AccessText { Text = choice.Key },
                    MinWidth = 88,
                    MinHeight = 24,
                    Padding = new Thickness(10, 2, 10, 2),
                    Margin = new Thickness(buttons.Children.Count == 0 ? 0 : 10, 0, 0, 0),
                    IsDefault = first == null,
                    IsCancel = value == MessageBoxResult.Cancel || (button == MessageBoxButton.OK && value == MessageBoxResult.OK),
                };
                AutomationName(b, choice.Key.Replace("_", ""));
                b.Click += (s, e) => { result = value; window.DialogResult = true; };
                buttons.Children.Add(b);
                if (first == null)
                    first = b;
            }

            // Ctrl+C copies the message, as in the Windows message box.
            window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    try { Clipboard.SetText(caption + Environment.NewLine + Environment.NewLine + text); } catch (Exception) { }
                }
            };
            // Yes/No has no Cancel: closing the window means No, like the Windows box (which disables the close button).
            window.Closing += (s, e) => { if (window.DialogResult != true) result = DefaultResult(button); };
            window.Loaded += (s, e) => { if (first != null) first.Focus(); };
            window.SourceInitialized += (s, e) => HideIcon(window);
            window.Content = root;

            PlaySound(icon);
            window.ShowDialog();
            return result;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>No icon in the title bar, like the Windows message box (dialog frame).</summary>
        private static void HideIcon(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            const int GWL_EXSTYLE = -20, WS_EX_DLGMODALFRAME = 0x0001;
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_DLGMODALFRAME);
            const int WM_SETICON = 0x0080;
            SendMessage(hwnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);       // ICON_SMALL
            SendMessage(hwnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);     // ICON_BIG
            // SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020);
        }

        private static void AutomationName(UIElement element, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        }

        private static MessageBoxResult DefaultResult(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK: return MessageBoxResult.OK;
                case MessageBoxButton.YesNo: return MessageBoxResult.No;
                default: return MessageBoxResult.Cancel;
            }
        }

        private static KeyValuePair<string, MessageBoxResult>[] Choices(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OKCancel:
                    return new[] { Pair("OK", MessageBoxResult.OK), Pair("Cancel", MessageBoxResult.Cancel) };
                case MessageBoxButton.YesNo:
                    return new[] { Pair("_Yes", MessageBoxResult.Yes), Pair("_No", MessageBoxResult.No) };
                case MessageBoxButton.YesNoCancel:
                    return new[] { Pair("_Yes", MessageBoxResult.Yes), Pair("_No", MessageBoxResult.No), Pair("Cancel", MessageBoxResult.Cancel) };
                default:
                    return new[] { Pair("OK", MessageBoxResult.OK) };
            }
        }

        private static KeyValuePair<string, MessageBoxResult> Pair(string text, MessageBoxResult result)
        {
            return new KeyValuePair<string, MessageBoxResult>(text, result);
        }

        private static ImageSource IconImage(MessageBoxImage icon)
        {
            System.Drawing.Icon system;
            switch (icon)
            {
                case MessageBoxImage.Question: system = System.Drawing.SystemIcons.Question; break;
                case MessageBoxImage.Warning: system = System.Drawing.SystemIcons.Warning; break;
                case MessageBoxImage.Error: system = System.Drawing.SystemIcons.Error; break;
                case MessageBoxImage.Information: system = System.Drawing.SystemIcons.Information; break;
                default: return null;
            }
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(system.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }

        private static void PlaySound(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Warning: SystemSounds.Exclamation.Play(); break;
                case MessageBoxImage.Error: SystemSounds.Hand.Play(); break;
                case MessageBoxImage.Information: SystemSounds.Asterisk.Play(); break;
            }
        }
    }
}
