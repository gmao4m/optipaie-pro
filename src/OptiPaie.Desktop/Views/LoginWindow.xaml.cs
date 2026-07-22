using System.Windows;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>Startup login screen (shown only when the login gate is enabled).</summary>
    public partial class LoginWindow
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is LoginViewModel vm)
                {
                    vm.PasswordAccessor = () => PasswordBox.Password;
                }
                UsernameBox.Focus();
            };
        }
    }
}
