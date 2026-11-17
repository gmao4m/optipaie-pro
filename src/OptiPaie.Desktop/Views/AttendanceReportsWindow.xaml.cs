using System.Windows;

namespace OptiPaie.Desktop.Views
{
    public partial class AttendanceReportsWindow : Window
    {
        public AttendanceReportsWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
