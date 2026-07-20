using System.Windows;

namespace OptiPaie.Admin.Shell
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new ShellViewModel();
        }
    }
}
