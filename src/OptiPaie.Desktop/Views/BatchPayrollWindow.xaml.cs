using System.Windows;

namespace OptiPaie.Desktop.Views
{
    /// <summary>Company-wide payroll run dialog. See <see cref="ViewModels.BatchPayrollViewModel"/>.</summary>
    public partial class BatchPayrollWindow : Window
    {
        public BatchPayrollWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
