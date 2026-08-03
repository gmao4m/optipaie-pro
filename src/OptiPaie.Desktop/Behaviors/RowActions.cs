using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OptiPaie.Desktop.Behaviors
{
    /// <summary>
    /// Helpers for the visible in-row action controls (the "Ouvrir" chevron and the "⋯"
    /// menu button) so a non-technical user never has to discover double-click / right-click.
    /// <para>
    /// <see cref="SelectRowProperty"/> selects the button's owning ListBox/DataGrid row before
    /// its command runs — a Button placed inside a row otherwise swallows the container's
    /// selection click, so a SelectedItem-based command would act on the wrong (or no) row.
    /// <see cref="OpenMenuOnClickProperty"/> pops the button's own ContextMenu on a left click,
    /// so the secondary actions are reachable without knowing about right-click.
    /// </para>
    /// </summary>
    public static class RowActions
    {
        // ---- SelectRow --------------------------------------------------------

        public static readonly DependencyProperty SelectRowProperty =
            DependencyProperty.RegisterAttached("SelectRow", typeof(bool), typeof(RowActions),
                new PropertyMetadata(false, OnSelectRowChanged));

        public static void SetSelectRow(DependencyObject o, bool v) => o.SetValue(SelectRowProperty, v);
        public static bool GetSelectRow(DependencyObject o) => (bool)o.GetValue(SelectRowProperty);

        private static void OnSelectRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement el)) return;
            if ((bool)e.NewValue) el.PreviewMouseLeftButtonDown += SelectRowHandler;
            else el.PreviewMouseLeftButtonDown -= SelectRowHandler;
        }

        private static void SelectRowHandler(object sender, MouseButtonEventArgs e)
        {
            var start = sender as DependencyObject;
            var item = FindAncestor<ListBoxItem>(start);
            if (item != null) { item.IsSelected = true; return; }
            var row = FindAncestor<DataGridRow>(start);
            if (row != null) row.IsSelected = true;
        }

        // ---- OpenMenuOnClick --------------------------------------------------

        public static readonly DependencyProperty OpenMenuOnClickProperty =
            DependencyProperty.RegisterAttached("OpenMenuOnClick", typeof(bool), typeof(RowActions),
                new PropertyMetadata(false, OnOpenMenuChanged));

        public static void SetOpenMenuOnClick(DependencyObject o, bool v) => o.SetValue(OpenMenuOnClickProperty, v);
        public static bool GetOpenMenuOnClick(DependencyObject o) => (bool)o.GetValue(OpenMenuOnClickProperty);

        private static void OnOpenMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ButtonBase b)) return;
            if ((bool)e.NewValue) b.Click += OpenMenuHandler;
            else b.Click -= OpenMenuHandler;
        }

        private static void OpenMenuHandler(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.Placement = PlacementMode.Bottom;
                fe.ContextMenu.IsOpen = true;
            }
        }

        // ---- OpenItemCommand --------------------------------------------------

        /// <summary>
        /// Set on a DataGrid/ListBox: a left click anywhere on a row runs the command with that
        /// row's item — so a non-technical user just clicks the line to open it, no double-click.
        /// Clicks that land on an inner Button (e.g. a copy button) are left alone.
        /// </summary>
        public static readonly DependencyProperty OpenItemCommandProperty =
            DependencyProperty.RegisterAttached("OpenItemCommand", typeof(ICommand), typeof(RowActions),
                new PropertyMetadata(null, OnOpenItemCommandChanged));

        public static void SetOpenItemCommand(DependencyObject o, ICommand v) => o.SetValue(OpenItemCommandProperty, v);
        public static ICommand GetOpenItemCommand(DependencyObject o) => (ICommand)o.GetValue(OpenItemCommandProperty);

        private static void OnOpenItemCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement el)) return;
            if (e.NewValue != null) el.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OpenItemHandler), true);
            else el.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OpenItemHandler));
        }

        private static void OpenItemHandler(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (FindAncestor<ButtonBase>(src) != null) return; // a copy button etc. — don't open

            object item = null;
            var row = FindAncestor<DataGridRow>(src);
            if (row != null) item = row.Item;
            else { var li = FindAncestor<ListBoxItem>(src); if (li != null) item = li.DataContext; }
            if (item == null) return;

            var cmd = GetOpenItemCommand((DependencyObject)sender);
            if (cmd != null && cmd.CanExecute(item)) cmd.Execute(item);
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
            {
                d = VisualTreeHelper.GetParent(d);
            }
            return d as T;
        }
    }
}
