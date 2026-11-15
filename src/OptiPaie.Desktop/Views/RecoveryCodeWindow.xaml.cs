using System.Windows;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>Display-only recovery-code window (used for the sign-in backfill).</summary>
    public partial class RecoveryCodeWindow
    {
        public RecoveryCodeWindow()
        {
            InitializeComponent();
        }

        private RecoveryCodeViewModel ViewModel => DataContext as RecoveryCodeViewModel;

        private void OnCopyCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Copy(ViewModel?.RecoveryCode);

        private void OnPrintCode(object sender, RoutedEventArgs e) => RecoveryCodeOutput.Print(ViewModel?.RecoveryCode);
    }
}
