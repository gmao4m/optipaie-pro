using System.Windows;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// Confirmation before switching the IRG/CNAS calculation OFF: it affects new payslips only and
    /// saved payslips do not change. Cancel is the default (Enter/Escape both keep the deductions ON).
    /// </summary>
    public partial class LegalDeductionsConfirmWindow : Window
    {
        public LegalDeductionsConfirmWindow()
        {
            InitializeComponent();
            App.ApplyFlowDirection(this);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
