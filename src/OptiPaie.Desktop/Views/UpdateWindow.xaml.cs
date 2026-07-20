using System.ComponentModel;
using System.Windows;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    public partial class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
        }

        // Block closing while a download is in progress.
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is UpdateViewModel vm && vm.IsBusy)
            {
                e.Cancel = true;
            }
        }
    }
}
