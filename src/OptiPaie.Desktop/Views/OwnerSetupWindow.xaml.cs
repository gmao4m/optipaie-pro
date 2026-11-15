using System.Windows;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// First-launch owner setup. Password entry stays in the view (PasswordBox is never bound), and
    /// copy/print of the recovery code are pure view operations handled here.
    /// </summary>
    public partial class OwnerSetupWindow
    {
        public OwnerSetupWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is OwnerSetupViewModel vm)
                {
                    vm.PasswordAccessor = () => PasswordBox.Password;
                    vm.ConfirmPasswordAccessor = () => ConfirmBox.Password;
                }
                EmailBox.Focus();
            };
        }

        private OwnerSetupViewModel ViewModel => DataContext as OwnerSetupViewModel;

        private void OnCopyCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Copy(ViewModel?.RecoveryCode);

        private void OnPrintCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Print(ViewModel?.RecoveryCode);
    }
}
