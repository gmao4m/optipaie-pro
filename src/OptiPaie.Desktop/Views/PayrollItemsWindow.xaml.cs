using System.Windows;
using System.Windows.Input;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    public partial class PayrollItemsWindow : Window
    {
        public PayrollItemsWindow()
        {
            InitializeComponent();
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PayrollItemsViewModel vm && vm.EditCommand.CanExecute(null))
            {
                vm.EditCommand.Execute(null);
            }
        }
    }
}
