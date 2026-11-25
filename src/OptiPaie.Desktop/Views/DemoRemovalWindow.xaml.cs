using System.Windows;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// Preview and confirmation before the trial demo company is removed. Cancel is the default
    /// (Enter/Escape keep everything); Delete is disabled when the demo is the only company.
    /// </summary>
    public partial class DemoRemovalWindow : Window
    {
        public DemoRemovalWindow()
        {
            InitializeComponent();
            App.ApplyFlowDirection(this);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
