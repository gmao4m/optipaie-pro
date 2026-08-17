using System.Windows;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.Common
{
    /// <summary>Small helper for modal dialogs and message boxes (kept out of view models).</summary>
    public static class Dialogs
    {
        public static void Error(string message)
        {
            MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static void Info(string message, string caption = "OptiPaie DZ")
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static bool Confirm(string message)
        {
            return MessageBox.Show(message, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Shows a small themed text prompt. Returns the entered text, or <c>null</c> when the user
        /// cancels. When <paramref name="required"/> is true, an empty value keeps the dialog open.
        /// </summary>
        public static string Prompt(string title, string label, string initial = null, bool required = true)
        {
            var window = new TextPromptWindow(title, label, initial, required)
            {
                Owner = Application.Current.MainWindow
            };
            App.ApplyFlowDirection(window);
            return window.ShowDialog() == true ? window.Value : null;
        }

        public static bool ShowEmployeeEditor(EmployeeEditViewModel vm)
        {
            var window = new EmployeeEditWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        public static bool ShowCompanyEditor(CompanyEditViewModel vm)
        {
            var window = new CompanyEditWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        public static void ShowPayrollItemsManager(PayrollItemsViewModel vm)
        {
            var window = new PayrollItemsWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        public static void ShowEmployeeProfile(EmployeeProfileViewModel vm)
        {
            var window = new EmployeeProfileWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        public static void ShowBatchPayroll(BatchPayrollViewModel vm)
        {
            var window = new BatchPayrollWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        public static bool ShowPayrollItemEditor(PayrollItemEditViewModel vm)
        {
            var window = new PayrollItemEditWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }
    }
}
