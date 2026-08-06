using System.Configuration;
using System.Windows;
using OptiPaie.Admin.Api;

namespace OptiPaie.Admin.Shell
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new ShellViewModel();

            // Best-effort update check once the console is shown (never blocks).
            Loaded += async (s, e) =>
                await AdminUpdater.CheckAsync(ConfigurationManager.AppSettings["Admin.UpdateUrl"]);
        }
    }
}
