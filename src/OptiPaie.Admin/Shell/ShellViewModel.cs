using System.Windows;
using System.Windows.Input;
using OptiPaie.Admin.Mvvm;
using OptiPaie.Admin.ViewModels;
using OptiPaie.Admin.Views;

namespace OptiPaie.Admin.Shell
{
    public sealed class ShellViewModel : ObservableObject
    {
        private SectionViewModel _current;
        private string _activeKey;

        private DashboardViewModel _dashboard;
        private LicensesViewModel _licenses;
        private DevicesViewModel _devices;
        private UpdatesViewModel _updates;
        private ReportsViewModel _reports;
        private AuditViewModel _audit;

        public ShellViewModel()
        {
            NavigateCommand = new RelayCommand(p => Navigate(p as string));
            SignOutCommand = new RelayCommand(_ => SignOut());
            Navigate("dashboard");
        }

        public SectionViewModel Current { get => _current; private set => Set(ref _current, value); }
        public string ActiveKey { get => _activeKey; private set => Set(ref _activeKey, value); }
        public string UserEmail => App.Api.UserEmail;
        public ICommand NavigateCommand { get; }

        /// <summary>Ends the owner session and returns to the login screen.</summary>
        public ICommand SignOutCommand { get; }

        private void SignOut()
        {
            App.Api.SignOut();

            // Open the login window first, then close the console — so the app never runs
            // out of windows (which would shut it down) during the swap.
            var login = new LoginWindow();
            Window old = Application.Current.MainWindow;
            Application.Current.MainWindow = login;
            login.Show();
            old?.Close();
        }

        private void Navigate(string key)
        {
            switch (key)
            {
                case "licenses": Current = _licenses ?? (_licenses = new LicensesViewModel()); break;
                case "devices": Current = _devices ?? (_devices = new DevicesViewModel()); break;
                case "updates": Current = _updates ?? (_updates = new UpdatesViewModel()); break;
                case "reports": Current = _reports ?? (_reports = new ReportsViewModel()); break;
                case "audit": Current = _audit ?? (_audit = new AuditViewModel()); break;
                default: key = "dashboard"; Current = _dashboard ?? (_dashboard = new DashboardViewModel()); break;
            }

            ActiveKey = key;
            Current.Load();
        }
    }
}
