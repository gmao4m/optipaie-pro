using System;
using System.Threading.Tasks;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class LoginViewModel : ObservableObject
    {
        private string _email = string.Empty;
        private string _error = string.Empty;
        private bool _busy;

        public Action OnSuccess { get; set; }

        public string Email { get => _email; set => Set(ref _email, value); }
        public string Error { get => _error; private set => Set(ref _error, value); }

        public bool Busy
        {
            get => _busy;
            private set { if (Set(ref _busy, value)) Raise(nameof(NotBusy)); }
        }

        public bool NotBusy => !_busy;

        public async Task LoginAsync(string password)
        {
            Error = string.Empty;
            if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrEmpty(password))
            {
                Error = "Saisissez votre email et mot de passe.";
                return;
            }

            Busy = true;
            try
            {
                await App.Api.SignInAsync(_email.Trim(), password);
                OnSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                Busy = false;
            }
        }
    }
}
