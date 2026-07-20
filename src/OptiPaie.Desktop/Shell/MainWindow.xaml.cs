using System.Windows;
using OptiPaie.Desktop.Composition;

namespace OptiPaie.Desktop.Shell
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            AppServices services = App.Services;
            DataContext = new ShellViewModel(services);
        }
    }
}
