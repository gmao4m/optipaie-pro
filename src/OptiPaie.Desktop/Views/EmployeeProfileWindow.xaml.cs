using System.Windows;

namespace OptiPaie.Desktop.Views
{
    /// <summary>360° employee profile. See <see cref="ViewModels.EmployeeProfileViewModel"/>.</summary>
    public partial class EmployeeProfileWindow : Window
    {
        public EmployeeProfileWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
