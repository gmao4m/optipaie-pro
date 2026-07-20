using System.Windows;
using System.Windows.Controls;

namespace OptiPaie.Desktop.Views
{
    public partial class ActivationWindow : Window
    {
        public ActivationWindow()
        {
            InitializeComponent();
        }

        // Keep the caret at the end while the key auto-formats (dashes are inserted
        // by the view model as the user types or pastes).
        private void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            KeyBox.CaretIndex = KeyBox.Text.Length;
        }
    }
}
