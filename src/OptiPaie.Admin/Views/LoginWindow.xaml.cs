using System.Windows;
using System.Windows.Input;
using OptiPaie.Admin.Shell;
using OptiPaie.Admin.ViewModels;

namespace OptiPaie.Admin.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm = new LoginViewModel();

        public LoginWindow()
        {
            InitializeComponent();
            _vm.OnSuccess = () =>
            {
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            };
            DataContext = _vm;
            EmailBox.Focus();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            await _vm.LoginAsync(PasswordBox.Password);
        }

        private async void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await _vm.LoginAsync(PasswordBox.Password);
            }
        }
    }
}
