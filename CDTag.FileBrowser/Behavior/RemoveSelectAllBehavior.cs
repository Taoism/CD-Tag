using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using System.Windows.Media;

namespace CDTag.FileBrowser.Behavior
{
    /// <summary>
    /// Removes the Select All (Ctrl+A) command from a DataGrid.
    /// </summary>
    public class RemoveSelectAllBehavior : Behavior<DataGrid>
    {
        /// <summary>Called after the behavior is attached to an AssociatedObject.</summary>
        protected override void OnAttached()
        {
            AssociatedObject.Loaded += AssociatedObject_Loaded;
        }

        /// <summary>Called when the behavior is detached.</summary>
        protected override void OnDetaching()
        {
            AssociatedObject.Loaded -= AssociatedObject_Loaded;
            base.OnDetaching();
        }

        private void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
        {
            // Loaded fires again whenever the grid is re-shown (e.g. switching main-window tabs);
            // the work only needs doing once.
            AssociatedObject.Loaded -= AssociatedObject_Loaded;

            DependencyObject dep = AssociatedObject;
            while (dep != null && !(dep is Button))
            {
                dep = VisualTreeHelper.GetChildrenCount(dep) > 0 ? VisualTreeHelper.GetChild(dep, 0) : null;
            }

            Button button = dep as Button;
            if (button == null)
                return;

            // Note: This clears ApplicationCommands.SelectAll, be careful reusing this code
            RoutedUICommand routedUICommand = button.Command as RoutedUICommand;
            if (routedUICommand != null)
                routedUICommand.InputGestures.Clear();
            button.Command = null;
        }
    }
}
