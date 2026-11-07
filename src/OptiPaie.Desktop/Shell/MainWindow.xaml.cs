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

        /// <summary>
        /// Release the shell view-model's subscriptions to the process-lifetime services when the
        /// shell closes, so a retired shell (every company switch / sign-out closes the old one and
        /// builds a new one) does not stay rooted by App.Services' event lists.
        /// </summary>
        protected override void OnClosed(System.EventArgs e)
        {
            (DataContext as ShellViewModel)?.Detach();
            base.OnClosed(e);
        }
    }
}
