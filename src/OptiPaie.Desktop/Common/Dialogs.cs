using System.Windows;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.Common
{
    /// <summary>Small helper for modal dialogs and message boxes (kept out of view models).</summary>
    public static class Dialogs
    {
        /// <summary>Set once at startup so <see cref="ErrorWithLog"/> can write the technical detail
        /// to the journal instead of showing it to the client.</summary>
        public static OptiPaie.Common.Logging.ILogger Logger { get; set; }

        public static void Error(string message)
        {
            MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Shows the business message AND the real technical exception (type · message · full dump
        /// with a bilingual « Copier les détails » button), and writes it to the journal. A caught
        /// failure — e.g. a payslip that will not render — must never be a dead end that tells the
        /// customer and support nothing: the exact cause is on screen and one click from the clipboard.
        /// Reuses the crash dialog built for unhandled UI exceptions.
        /// </summary>
        public static void ErrorWithLog(string businessMessage, System.Exception ex)
        {
            try { Logger?.Error(businessMessage, ex); } catch { /* logging must never mask the message */ }

            try
            {
                string details = businessMessage + System.Environment.NewLine +
                                 CrashLog.Describe(businessMessage, ex);
                CrashLog.Fatal(businessMessage, ex); // also persist to crash.log for after-the-fact support
                CrashDialog.Show(ex, CrashLog.Directory, details);
            }
            catch
            {
                // The rich dialog must never itself swallow the error — fall back to a plain box.
                MessageBox.Show(businessMessage, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            App.ApplyFlowDirection(window); // RTL in Arabic, like every other dialog
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
            App.ApplyFlowDirection(window);
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
            App.ApplyFlowDirection(window);
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
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }
    }
}
