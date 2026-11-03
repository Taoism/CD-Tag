using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CDTag.Views.Tag.TagAlbum
{
    /// <summary>Find / Replace with for the song names in Tag Album.</summary>
    public class SearchReplaceWindow : Window
    {
        private static string _lastFind;
        private static string _lastReplace;
        private static bool _lastMatchCase;

        private readonly TextBox _find = new TextBox { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(2) };
        private readonly TextBox _replace = new TextBox { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(2) };
        private readonly CheckBox _matchCase = new CheckBox { Content = "_Match case", Margin = new Thickness(0, 6, 0, 0) };

        public SearchReplaceWindow()
        {
            Title = "Search & Replace";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Verdana");
            FontSize = 11.0d;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "Theme.Foreground");

            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Add(grid, Caption("_Find", _find), 0, 0);
            Add(grid, _find, 0, 1);
            Add(grid, Caption("_Replace with", _replace), 1, 0);
            Add(grid, _replace, 1, 1);
            Add(grid, _matchCase, 2, 1);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Replace _All", IsDefault = true, MinWidth = 86, Padding = new Thickness(10, 3, 10, 3) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 86, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(_find.Text))
                {
                    _find.Focus();
                    return;
                }
                _lastFind = _find.Text;
                _lastReplace = _replace.Text;
                _lastMatchCase = _matchCase.IsChecked == true;
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Add(grid, buttons, 3, 0);
            Grid.SetColumnSpan(buttons, 2);

            Content = grid;
            _find.Text = _lastFind ?? "";
            _replace.Text = _lastReplace ?? "";
            _matchCase.IsChecked = _lastMatchCase;
            Loaded += (s, e) =>
            {
                _find.Focus();
                _find.SelectAll();
            };
        }

        public string Find
        {
            get { return _find.Text; }
        }

        public string ReplaceWith
        {
            get { return _replace.Text; }
        }

        public bool MatchCase
        {
            get { return _matchCase.IsChecked == true; }
        }

        private static Label Caption(string text, UIElement target)
        {
            var label = new Label { Content = text, Target = target, Padding = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(ForegroundProperty, "Theme.Foreground");
            return label;
        }

        private static void Add(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }
}
