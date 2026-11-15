using System.Windows;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// Owner password recovery screen (reset with the one-time code, show the fresh code, or the
    /// lost-both fingerprint fallback). Password entry and copy/print stay in the view.
    /// </summary>
    public partial class RecoveryWindow
    {
        public RecoveryWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is RecoveryViewModel vm)
                {
                    vm.PasswordAccessor = () => PasswordBox.Password;
                    vm.ConfirmPasswordAccessor = () => ConfirmBox.Password;
                }
                CodeBox.Focus();
            };
        }

        private RecoveryViewModel ViewModel => DataContext as RecoveryViewModel;

        private void OnCopyCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Copy(ViewModel?.NewRecoveryCode);

        private void OnPrintCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Print(ViewModel?.NewRecoveryCode);

        private void OnCopyFingerprint(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Copy(ViewModel?.Fingerprint);
    }
}
