using System.Windows;

namespace OptiPaie.Desktop.Views
{
    public partial class ModuleActivationWindow : Window
    {
        public ModuleActivationWindow()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
