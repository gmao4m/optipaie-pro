using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OptiPaie.Desktop.Views
{
    public partial class EmployeesView : UserControl
    {
        public EmployeesView()
        {
            InitializeComponent();
        }

        // A right-click selects the row under the cursor BEFORE its context menu opens, so
        // the menu's actions (open / edit / delete) always act on the row you clicked — a
        // ListBox does not select on right-click on its own.
        private void List_RightClickSelect(object sender, MouseButtonEventArgs e)
        {
            var container = ItemsControl.ContainerFromElement(
                (ListBox)sender, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (container != null)
            {
                container.IsSelected = true;
            }
        }
    }
}
