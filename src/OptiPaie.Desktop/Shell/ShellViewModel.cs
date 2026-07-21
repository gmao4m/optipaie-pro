using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.ViewModels;

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

        private HomeViewModel _home;
        private EmployeesViewModel _employees;
        private CompaniesViewModel _companies;
        private PayrollViewModel _payroll;
        private ArchiveViewModel _archive;
        private SettingsViewModel _settings;
        private AttendanceViewModel _attendance;

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

            Navigate("home");
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
            AddCore("home", "Accueil", "IconHome");
            AddCore("employees", "Employés", "IconUsers");
            AddCore("payroll", "Paie", "IconCash");
            AddCore("companies", "Entreprises", "IconBuilding");
            AddCore("archive", "Archive", "IconArchive");

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
                    return _attendance ?? (_attendance = new AttendanceViewModel(_services));
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
