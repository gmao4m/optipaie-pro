using System.Windows;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>The single unified sign-in screen (owner email or local username).</summary>
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

                // Focus the password when the identifier is pre-filled, otherwise the identifier.
                if (string.IsNullOrEmpty(IdentifierBox.Text)) IdentifierBox.Focus();
                else PasswordBox.Focus();
            };
        }
    }
}
