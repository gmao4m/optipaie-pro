using System;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The login screen shown at startup when the login gate is enabled. Verifies the
    /// username/password and records the signed-in user in the session. The password is read
    /// from the PasswordBox through <see cref="PasswordAccessor"/> (never bound/stored).
    /// </summary>
    public sealed class LoginViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private string _username = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;

        public LoginViewModel(AppServices services)
        {
            _services = services;
            LoginCommand = new RelayCommand(Login);
        }

        /// <summary>Raised to close the window; true = signed in.</summary>
        public Action<bool> CloseRequested { get; set; }

        /// <summary>Supplied by the window to read the PasswordBox securely.</summary>
        public Func<string> PasswordAccessor { get; set; }

        public string ProductName => "OptiPaie PRO";
        public string Subtitle => "Connexion";

        public string Username
        {
            get => _username;
            set => Set(ref _username, value);
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }

        public ICommand LoginCommand { get; }

        private void Login()
        {
            string password = PasswordAccessor != null ? PasswordAccessor() : string.Empty;

            Result<User> result = _services.Users.Authenticate(_username, password);
            if (result.IsFailure)
            {
                IsError = true;
                StatusMessage = Localization.ResultText.Localize(_services.Localization, result.Error, result.ErrorCode);
                return;
            }

            _services.Session.Current = result.Value;
            CloseRequested?.Invoke(true);
        }
    }
}
