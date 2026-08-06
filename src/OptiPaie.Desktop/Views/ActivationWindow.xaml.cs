using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    public partial class ActivationWindow : Window
    {
        public ActivationWindow()
        {
            InitializeComponent();
        }

        // Keep the caret at the end while the key auto-formats (dashes inserted by the VM).
        private void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            KeyBox.CaretIndex = KeyBox.Text.Length;
        }

        // The password is passed directly (never bound or stored).
        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ActivationViewModel vm)
            {
                await vm.SubmitAsync(PasswordBox.Password);
            }
        }

        private async void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ActivationViewModel vm)
            {
                await vm.SubmitAsync(PasswordBox.Password);
            }
        }

        // Clear the inline password error as the user types (the box can't be data-bound).
        private void Password_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ActivationViewModel vm)
            {
                vm.OnPasswordChanged();
            }
        }
    }
}
