using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DevExpress.XtraBars.FluentDesignSystem;
using DevExpress.XtraBars.Navigation;
using DevExpress.XtraEditors;
using OptiPaie.App.About;
using OptiPaie.App.Backup;
using OptiPaie.App.Common;
using OptiPaie.App.Diagnostic;
using OptiPaie.App.Licensing;
using OptiPaie.App.Modules.Archive;
using OptiPaie.App.Modules.Companies;
using OptiPaie.App.Modules.Dashboard;
using OptiPaie.App.Modules.Employees;
using OptiPaie.App.Modules.Payroll;
using OptiPaie.App.Modules.Settings;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;
using OptiPaie.PayrollEngine.Legal;

namespace OptiPaie.App.Shell
{
    /// <summary>
    /// The application shell, built on a DevExpress <see cref="FluentDesignForm"/>: a
    /// modern flat title bar with a hamburger toggle, an <see cref="AccordionControl"/>
    /// side navigation (sectioned, SVG icons, skin-native selection), and a content
    /// container that hosts the active module above a slim status bar. Language switches
    /// instantly; the support diagnostic window opens via the hidden Ctrl+Alt+D shortcut.
    /// </summary>
    public sealed class MainForm : FluentDesignForm
    {
        private readonly AppServices _services;
        private readonly Dictionary<string, BaseModuleControl> _modules =
            new Dictionary<string, BaseModuleControl>();

        // Navigation wiring: module key -> element (for selection), element -> action,
        // element -> localization resource key.
        private readonly Dictionary<string, AccordionControlElement> _moduleItems =
            new Dictionary<string, AccordionControlElement>();
        private readonly Dictionary<AccordionControlElement, Action> _navActions =
            new Dictionary<AccordionControlElement, Action>();
        private readonly Dictionary<AccordionControlElement, string> _navText =
            new Dictionary<AccordionControlElement, string>();

        private AccordionControl _nav;
        private AccordionControlElement _workspaceGroup;
        private AccordionControlElement _systemGroup;
        private AccordionControlElement _languageItem;
        private FluentDesignFormContainer _container;
        private PanelControl _contentPanel;
        private PanelControl _statusBar;
        private LabelControl _statusLabel;
        private string _activeKey;

        public MainForm(AppServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            BuildUi();
            Localize();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            ShowModule("dashboard");

            _services.Localization.LanguageChanged += (s, e) => OnLanguageChanged();
        }

        private void BuildUi()
        {
            Text = "OptiPaie DZ";
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(1100, 700);
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;

            // Fluent title bar, side navigation and content container.
            var titleBar = new FluentDesignFormControl();
            _nav = new AccordionControl { Dock = DockStyle.Left };
            _container = new FluentDesignFormContainer { Dock = DockStyle.Fill };

            Controls.Add(_container);
            Controls.Add(_nav);
            Controls.Add(titleBar);

            FluentDesignFormControl = titleBar;
            NavigationControl = _nav;
            ControlContainer = _container;

            // Content host + status bar live inside the Fluent container.
            _contentPanel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            UiTheme.Canvasize(_contentPanel);
            _container.Controls.Add(_contentPanel);

            _statusBar = new PanelControl { Dock = DockStyle.Bottom, Height = 28, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _statusBar.Appearance.BackColor = UiTheme.Sidebar;
            _statusBar.Appearance.Options.UseBackColor = true;
            _statusLabel = new LabelControl
            {
                Location = new Point(16, 6),
                AutoSizeMode = LabelAutoSizeMode.None,
                Width = 1600
            };
            _statusLabel.Appearance.ForeColor = UiTheme.SidebarText;
            _statusLabel.Appearance.Font = UiTheme.Caption();
            _statusLabel.Appearance.Options.UseForeColor = true;
            _statusLabel.Appearance.Options.UseFont = true;
            _statusBar.Controls.Add(_statusLabel);
            _container.Controls.Add(_statusBar);

            BuildNavigation();
        }

        // -- Navigation ------------------------------------------------------------

        private void BuildNavigation()
        {
            _workspaceGroup = new AccordionControlElement { Style = ElementStyle.Group, Expanded = true };
            _nav.Elements.Add(_workspaceGroup);

            AddModuleItem(_workspaceGroup, "dashboard", "Module_Dashboard", "svgimages/icon builder/actions_home.svg");
            AddModuleItem(_workspaceGroup, "companies", "Module_Companies", "svgimages/icon builder/actions_addressbook.svg");
            AddModuleItem(_workspaceGroup, "employees", "Module_Employees", "svgimages/icon builder/actions_user.svg");
            AddModuleItem(_workspaceGroup, "payroll", "Module_Payroll", "svgimages/icon builder/actions_currency.svg");
            AddModuleItem(_workspaceGroup, "archive", "Module_Archive", "svgimages/icon builder/actions_database.svg");

            _systemGroup = new AccordionControlElement { Style = ElementStyle.Group, Expanded = true };
            _nav.Elements.Add(_systemGroup);

            AddModuleItem(_systemGroup, "settings", "Module_Settings", "svgimages/icon builder/actions_settings.svg");
            AddActionItem(_systemGroup, "Menu_Backup", "svgimages/icon builder/actions_download.svg",
                () => OpenDialog(new BackupManagerForm(_services)));
            AddActionItem(_systemGroup, "Menu_License", "svgimages/icon builder/actions_key.svg",
                () => OpenDialog(new LicenseForm(_services)));
            AddActionItem(_systemGroup, "Menu_About", "svgimages/icon builder/actions_info.svg",
                () => OpenDialog(new AboutForm(_services)));
            _languageItem = AddActionItem(_systemGroup, null, "svgimages/icon builder/actions_globe.svg", ToggleLanguage);
        }

        private void AddModuleItem(AccordionControlElement group, string key, string resourceKey, string icon)
        {
            AccordionControlElement item = NewItem(group, resourceKey, icon);
            _navActions[item] = () => ShowModule(key);
            _moduleItems[key] = item;
        }

        private AccordionControlElement AddActionItem(AccordionControlElement group, string resourceKey, string icon, Action action)
        {
            AccordionControlElement item = NewItem(group, resourceKey, icon);
            _navActions[item] = action;
            return item;
        }

        private AccordionControlElement NewItem(AccordionControlElement group, string resourceKey, string icon)
        {
            var item = new AccordionControlElement { Style = ElementStyle.Item };
            item.ImageOptions.ImageUri.Uri = icon;
            item.ImageOptions.SvgImageSize = new Size(22, 22);
            item.Click += NavItemClick;
            group.Elements.Add(item);

            if (resourceKey != null)
            {
                _navText[item] = resourceKey;
            }

            return item;
        }

        private void NavItemClick(object sender, EventArgs e)
        {
            if (sender is AccordionControlElement element && _navActions.TryGetValue(element, out Action action))
            {
                action();
            }
        }

        // -- Module hosting --------------------------------------------------------

        private void ShowModule(string key)
        {
            BaseModuleControl module = GetOrCreateModule(key);
            if (module == null)
            {
                return;
            }

            _contentPanel.SuspendLayout();
            try
            {
                _contentPanel.Controls.Clear();
                module.Initialize(_services);
                module.Dock = DockStyle.Fill;
                _contentPanel.Controls.Add(module);
                module.OnActivated();
            }
            finally
            {
                _contentPanel.ResumeLayout();
            }

            _activeKey = key;

            if (_moduleItems.TryGetValue(key, out AccordionControlElement element))
            {
                _nav.SelectedElement = element;
            }
        }

        private BaseModuleControl GetOrCreateModule(string key)
        {
            if (_modules.TryGetValue(key, out BaseModuleControl existing))
            {
                return existing;
            }

            BaseModuleControl created;
            switch (key)
            {
                case "dashboard":
                    created = new DashboardControl(NavigateTo);
                    break;
                case "companies":
                    created = new CompaniesControl();
                    break;
                case "employees":
                    created = new EmployeesControl();
                    break;
                case "payroll":
                    created = new PayrollControl();
                    break;
                case "archive":
                    created = new ArchiveControl();
                    break;
                case "settings":
                    created = new SettingsControl();
                    break;
                default:
                    return null;
            }

            _modules[key] = created;
            return created;
        }

        /// <summary>Allows modules (e.g. the dashboard quick buttons) to navigate.</summary>
        private void NavigateTo(string key)
        {
            ShowModule(key);
        }

        private void OpenDialog(XtraForm form)
        {
            using (form)
            {
                form.ShowDialog(this);
            }
        }

        private BaseModuleControl ActiveModule()
        {
            return _activeKey != null && _modules.ContainsKey(_activeKey) ? _modules[_activeKey] : null;
        }

        // -- Localization & status -------------------------------------------------

        private void ToggleLanguage()
        {
            string next = _services.Localization.CurrentLanguage == "ar" ? "fr" : "ar";
            _services.Settings.SetLanguage(next);
            _services.Localization.SetLanguage(next);
        }

        private void OnLanguageChanged()
        {
            Localize();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);

            foreach (BaseModuleControl module in _modules.Values)
            {
                module.Localize();
            }
        }

        private void Localize()
        {
            _workspaceGroup.Text = _services.Localization.GetString("Nav_Workspace");
            _systemGroup.Text = _services.Localization.GetString("Nav_System");

            foreach (KeyValuePair<AccordionControlElement, string> pair in _navText)
            {
                pair.Key.Text = _services.Localization.GetString(pair.Value);
            }

            // The language item shows the language it switches TO (proper noun).
            _languageItem.Text = _services.Localization.CurrentLanguage == "ar" ? "Français" : "العربية";

            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            var loc = _services.Localization;
            string db = System.IO.Path.GetFileName(_services.Configuration.DatabaseFilePath);
            LegalProfile profile = new BuiltInLegalProfileProvider()
                .GetProfile(new PayrollPeriod(DateTime.Now.Year, DateTime.Now.Month));
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            _statusLabel.Text = string.Format(
                "{0}: {1}      •      {2}: {3}      •      {4}: {5}      •      {6}: {7}",
                loc.GetString("Status_Database"), db,
                loc.GetString("Status_Language"), loc.CurrentLanguage.ToUpperInvariant(),
                loc.GetString("Status_Legal"), profile.LegalVersion,
                loc.GetString("Status_Version"), version);
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Alt && e.KeyCode == Keys.D)
            {
                OpenDialog(new DiagnosticForm(_services));
                e.Handled = true;
                return;
            }

            BaseModuleControl active = ActiveModule();
            if (active == null)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                active.OnActivated();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.N)
            {
                active.OnNew();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                active.OnSave();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                active.OnPrint();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                active.OnFind();
                e.Handled = true;
            }
        }
    }
}
