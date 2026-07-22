using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.ViewModels.Attendance;

namespace OptiPaie.Desktop.Shell
{
    /// <summary>
    /// Drives the whole application: hosts the current module view model and the
    /// navigation. The navigation is GENERATED from the module registry (core screens
    /// plus every premium module), and each premium module is gated by the license:
    /// locked modules stay visible with a 🔒 and open a premium (upsell) page; when a
    /// module becomes enabled the shell swaps to its real screen automatically.
    /// </summary>
    public sealed class ShellViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly IModuleRegistry _registry;
        private readonly ILicenseGate _gate;

        private DashboardViewModel _dashboard;
        private ReportsViewModel _reports;
        private HomeViewModel _home;
        private EmployeesViewModel _employees;
        private CompaniesViewModel _companies;
        private PayrollViewModel _payroll;
        private ArchiveViewModel _archive;
        private SettingsViewModel _settings;
        private AttendanceMatrixViewModel _attendance;
        private LeaveViewModel _leave;
        private LoanViewModel _loans;
        private ContractViewModel _contracts;
        private PerformanceViewModel _performance;
        private AssetViewModel _assets;
        private TrainingViewModel _training;
        private AtsViewModel _ats;
        private CertificateViewModel _certificates;

        private readonly Dictionary<string, PremiumModuleViewModel> _premium =
            new Dictionary<string, PremiumModuleViewModel>();
        private readonly Dictionary<string, ModuleReadyViewModel> _ready =
            new Dictionary<string, ModuleReadyViewModel>();

        private readonly List<NavItemViewModel> _allNav = new List<NavItemViewModel>();

        private object _current;
        private string _activeKey;

        public ShellViewModel(AppServices services)
        {
            _services = services;
            _registry = services.Modules;
            _gate = services.LicenseGate;

            BuildNavigation();

            // Refresh lock states and the current page whenever the license changes
            // (e.g. after an online synchronization enables a newly purchased module).
            _services.Licensing.Changed += OnLicenseChanged;

            OpenNotificationCommand = new RelayCommand(p => OpenNotification(p as OptiPaie.Core.Dtos.Notification));
            SignOutCommand = new RelayCommand(() => (Application.Current as OptiPaie.Desktop.App)?.SignOut());
            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            // Load the company list once and react to header company switches: re-activate
            // the visible module so it reloads the new company's data (no stale data left).
            _services.CompanyContext.Reload();
            _services.CompanyContext.ActiveChanged += OnActiveCompanyChanged;

            Navigate("dashboard"); // also refreshes the notification bell
        }

        /// <summary>The single active-company selection bound to the header selector.</summary>
        public CompanyContext CompanyContext => _services.CompanyContext;

        private void OnActiveCompanyChanged(object sender, EventArgs e)
        {
            // Reload whatever screen is showing so it reflects the newly selected company,
            // then refresh the cross-module alert bell for that company's data.
            if (_current is IActivable activable)
            {
                activable.OnActivated();
            }

            RefreshNotifications();
        }

        /// <summary>Cross-module alerts for the header bell.</summary>
        public ObservableCollection<OptiPaie.Core.Dtos.Notification> Notifications { get; } =
            new ObservableCollection<OptiPaie.Core.Dtos.Notification>();

        private int _notificationCount;
        public int NotificationCount { get => _notificationCount; private set => Set(ref _notificationCount, value); }

        public bool HasNotifications => _notificationCount > 0;

        /// <summary>Badge text (capped for space).</summary>
        public string NotificationBadge => _notificationCount > 9 ? "9+" : _notificationCount.ToString();

        public ICommand OpenNotificationCommand { get; }

        /// <summary>Ends the session and returns to the activation / trial-start screen.</summary>
        public ICommand SignOutCommand { get; }

        /// <summary>Toggles the light/dark colour theme (persisted).</summary>
        public ICommand ToggleThemeCommand { get; }

        public string ThemeToggleText => Composition.ThemeManager.IsDark ? "Mode clair" : "Mode sombre";

        private void ToggleTheme()
        {
            bool dark = Composition.ThemeManager.Toggle();
            _services.Settings.Set("Ui.Theme", dark ? "dark" : "light");
            Raise(nameof(ThemeToggleText));
        }

        private void RefreshNotifications()
        {
            Notifications.Clear();
            foreach (OptiPaie.Core.Dtos.Notification n in _services.Notifications.GetNotifications())
            {
                Notifications.Add(n);
            }

            NotificationCount = Notifications.Count;
            Raise(nameof(HasNotifications));
            Raise(nameof(NotificationBadge));
        }

        private void OpenNotification(OptiPaie.Core.Dtos.Notification notification)
        {
            if (notification != null && !string.IsNullOrWhiteSpace(notification.ModuleKey))
            {
                Navigate(notification.ModuleKey);
            }
        }

        /// <summary>Fixed core screens (always accessible).</summary>
        public ObservableCollection<NavItemViewModel> CoreNav { get; } = new ObservableCollection<NavItemViewModel>();

        /// <summary>Premium modules, generated from the registry (locked or unlocked).</summary>
        public ObservableCollection<NavItemViewModel> ModuleNav { get; } = new ObservableCollection<NavItemViewModel>();

        /// <summary>Settings, pinned to the bottom of the rail.</summary>
        public ObservableCollection<NavItemViewModel> SettingsNav { get; } = new ObservableCollection<NavItemViewModel>();

        public object Current
        {
            get => _current;
            private set => Set(ref _current, value);
        }

        private void BuildNavigation()
        {
            AddCore("dashboard", "Tableau de bord", "IconTrend");
            AddCore("home", "Accueil", "IconHome");
            AddCore("employees", "Employés", "IconUsers");
            AddCore("payroll", "Paie", "IconCash");
            AddCore("companies", "Entreprises", "IconBuilding");
            AddCore("archive", "Archive", "IconArchive");
            AddCore("reports", "Rapports", "IconFile");

            foreach (ModuleDescriptor module in _registry.Upsells)
            {
                NavItemViewModel item = NewItem(module.Key, module.NameFr, IconKeyFor(module.Key), true);
                item.IsLocked = !_gate.IsEnabled(module.Key);
                ModuleNav.Add(item);
                _allNav.Add(item);
            }

            NavItemViewModel settings = NewItem("settings", "Paramètres", "IconSettings", false);
            SettingsNav.Add(settings);
            _allNav.Add(settings);
        }

        private void AddCore(string key, string title, string iconKey)
        {
            NavItemViewModel item = NewItem(key, title, iconKey, false);
            CoreNav.Add(item);
            _allNav.Add(item);
        }

        private NavItemViewModel NewItem(string key, string title, string iconKey, bool isModule)
        {
            Geometry icon = Application.Current != null
                ? Application.Current.TryFindResource(iconKey) as Geometry
                : null;
            return new NavItemViewModel(key, title, icon, isModule, new RelayCommand(() => Navigate(key)));
        }

        private static string IconKeyFor(string moduleKey)
        {
            switch (moduleKey)
            {
                case ModuleKeys.Ats: return "IconClipboard";
                case ModuleKeys.Attendance: return "IconClock";
                case ModuleKeys.Leave: return "IconPlane";
                case ModuleKeys.Loans: return "IconCard";
                case ModuleKeys.Performance: return "IconTrend";
                case ModuleKeys.Contracts: return "IconFileCheck";
                case ModuleKeys.Training: return "IconSchool";
                case ModuleKeys.Assets: return "IconLaptop";
                case ModuleKeys.WorkCertificate: return "IconCertificate";
                default: return "IconFile";
            }
        }

        private void Navigate(string key)
        {
            object target;

            switch (key)
            {
                case "employees":
                    target = _employees ?? (_employees = new EmployeesViewModel(_services));
                    break;
                case "companies":
                    target = _companies ?? (_companies = new CompaniesViewModel(_services));
                    break;
                case "payroll":
                    target = _payroll ?? (_payroll = new PayrollViewModel(_services));
                    break;
                case "archive":
                    target = _archive ?? (_archive = new ArchiveViewModel(_services));
                    break;
                case "settings":
                    target = _settings ?? (_settings = new SettingsViewModel(_services));
                    break;
                case "dashboard":
                    target = _dashboard ?? (_dashboard = new DashboardViewModel(_services, Navigate));
                    break;
                case "reports":
                    target = _reports ?? (_reports = new ReportsViewModel(_services));
                    break;
                case "home":
                    target = _home ?? (_home = new HomeViewModel(_services, Navigate));
                    break;
                default:
                    if (_registry.Exists(key))
                    {
                        target = ResolveModule(key);
                    }
                    else
                    {
                        key = "home";
                        target = _home ?? (_home = new HomeViewModel(_services, Navigate));
                    }
                    break;
            }

            _activeKey = key;
            Current = target;
            UpdateSelection(key);

            if (target is IActivable activable)
            {
                activable.OnActivated();
            }

            // Alerts may have changed after acting on a screen — keep the bell current.
            RefreshNotifications();
        }

        /// <summary>
        /// For a premium module: show its real screen when the license enables it,
        /// otherwise the premium (upsell) page. Modules whose screen is not built yet
        /// show a "module activé" placeholder once enabled.
        /// </summary>
        private object ResolveModule(string key)
        {
            if (_gate.IsEnabled(key))
            {
                if (string.Equals(key, ModuleKeys.Attendance, StringComparison.Ordinal))
                {
                    return _attendance ?? (_attendance = new AttendanceMatrixViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Leave, StringComparison.Ordinal))
                {
                    return _leave ?? (_leave = new LeaveViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Loans, StringComparison.Ordinal))
                {
                    return _loans ?? (_loans = new LoanViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Contracts, StringComparison.Ordinal))
                {
                    return _contracts ?? (_contracts = new ContractViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Performance, StringComparison.Ordinal))
                {
                    return _performance ?? (_performance = new PerformanceViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Assets, StringComparison.Ordinal))
                {
                    return _assets ?? (_assets = new AssetViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Training, StringComparison.Ordinal))
                {
                    return _training ?? (_training = new TrainingViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.Ats, StringComparison.Ordinal))
                {
                    return _ats ?? (_ats = new AtsViewModel(_services));
                }

                if (string.Equals(key, ModuleKeys.WorkCertificate, StringComparison.Ordinal))
                {
                    return _certificates ?? (_certificates = new CertificateViewModel(_services));
                }

                if (!_ready.TryGetValue(key, out ModuleReadyViewModel ready))
                {
                    ModuleDescriptor descriptor = _registry.Find(key);
                    ready = new ModuleReadyViewModel(descriptor != null ? descriptor.NameFr : key);
                    _ready[key] = ready;
                }

                return ready;
            }

            if (!_premium.TryGetValue(key, out PremiumModuleViewModel premium))
            {
                premium = new PremiumModuleViewModel(PremiumModuleCatalog.For(key));
                _premium[key] = premium;
            }

            return premium;
        }

        private void UpdateSelection(string key)
        {
            foreach (NavItemViewModel item in _allNav)
            {
                item.IsSelected = string.Equals(item.Key, key, StringComparison.Ordinal);
            }
        }

        private void OnLicenseChanged(object sender, EventArgs e)
        {
            Action refresh = () =>
            {
                foreach (NavItemViewModel item in ModuleNav)
                {
                    item.IsLocked = !_gate.IsEnabled(item.Key);
                }

                // If a module page is currently shown, re-resolve it so a freshly
                // enabled module swaps from the premium page to its real screen
                // (and vice-versa on suspension) without any restart.
                if (_activeKey != null && _registry.Exists(_activeKey))
                {
                    Navigate(_activeKey);
                }
            };

            System.Windows.Threading.Dispatcher dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(refresh);
            }
            else
            {
                refresh();
            }
        }
    }
}
