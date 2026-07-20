using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine.Legal;

namespace OptiPaie.App.Modules.Dashboard
{
    /// <summary>
    /// Home screen: a page header, four headline KPI cards, quick-action tiles and the
    /// most recent activity. The layout is fully responsive — everything is sized and
    /// positioned in <see cref="LayoutDashboard"/> on resize, so the screen fills the
    /// window with no dead space at any width or DPI. Read-only; refreshed on show.
    /// </summary>
    public sealed class DashboardControl : BaseModuleControl
    {
        private readonly Action<string> _navigate;

        private LabelControl _pageTitle;
        private LabelControl _pageSubtitle;
        private LabelControl _statusLine;

        private readonly PanelControl[] _cards = new PanelControl[4];
        private readonly PanelControl[] _cardBars = new PanelControl[4];
        private readonly PanelControl[] _cardIcons = new PanelControl[4];
        private readonly LabelControl[] _cardTitles = new LabelControl[4];
        private readonly LabelControl[] _cardValues = new LabelControl[4];

        private LabelControl _actionsTitle;
        private SimpleButton _quickCompany;
        private SimpleButton _quickEmployee;
        private SimpleButton _quickPayroll;

        private LabelControl _recentTitle;
        private PanelControl _recentCard;
        private GridControl _recentGrid;
        private GridView _recentView;

        private const int IconSize = 44;
        private static readonly string[] CardGlyphs = { "▤", "○", "◷", "▦" };

        public DashboardControl(Action<string> navigate)
        {
            _navigate = navigate;
        }

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            _pageTitle = UiTheme.PageTitle(new LabelControl());
            Controls.Add(_pageTitle);

            _pageSubtitle = UiTheme.PageSubtitle(new LabelControl());
            Controls.Add(_pageSubtitle);

            Color[] accents = { UiTheme.Salary, UiTheme.Employer, UiTheme.Warning, UiTheme.Primary };
            for (int i = 0; i < 4; i++)
            {
                _cardValues[i] = CreateCard(i, accents[i], out _cards[i], out _cardTitles[i]);
            }

            _actionsTitle = SectionTitle();
            _quickCompany = CreateTile(() => _navigate?.Invoke("companies"), primary: false);
            _quickEmployee = CreateTile(() => _navigate?.Invoke("employees"), primary: false);
            _quickPayroll = CreateTile(() => _navigate?.Invoke("payroll"), primary: true);

            _recentTitle = SectionTitle();

            // The recent-activity grid lives inside a white card so it reads as a framed
            // panel rather than a bare table floating on the canvas.
            _recentCard = new PanelControl();
            UiTheme.Card(_recentCard);
            Controls.Add(_recentCard);

            _recentGrid = new GridControl { Dock = DockStyle.Fill };
            _recentView = new GridView(_recentGrid);
            _recentGrid.MainView = _recentView;
            _recentView.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_recentView);
            _recentView.Appearance.HeaderPanel.Options.UseTextOptions = true;
            _recentView.Appearance.HeaderPanel.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Near;
            _recentCard.Controls.Add(_recentGrid);

            _statusLine = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None };
            UiTheme.Muted(_statusLine);
            Controls.Add(_statusLine);

            Resize += (s, e) => LayoutDashboard();
            LayoutDashboard();
        }

        private LabelControl CreateCard(int index, Color accent, out PanelControl panel, out LabelControl title)
        {
            panel = new PanelControl();
            UiTheme.Card(panel);
            Controls.Add(panel);

            // A slim coloured bar down the left edge — a calmer, more modern accent than
            // a full-width top stripe.
            var bar = new PanelControl { Dock = DockStyle.Left, Width = 4, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            bar.Appearance.BackColor = accent;
            bar.Appearance.Options.UseBackColor = true;
            panel.Controls.Add(bar);
            _cardBars[index] = bar;

            // Soft icon badge, positioned top-right in LayoutDashboard.
            PanelControl chip = UiTheme.IconChip(CardGlyphs[index], accent, IconSize);
            _cardIcons[index] = chip;
            panel.Controls.Add(chip);
            chip.BringToFront();

            title = new LabelControl { Location = new Point(22, 22), AutoSizeMode = LabelAutoSizeMode.None, Width = 160 };
            title.Appearance.Font = UiTheme.Caption();
            title.Appearance.ForeColor = UiTheme.TextMuted;
            title.Appearance.Options.UseFont = true;
            title.Appearance.Options.UseForeColor = true;
            panel.Controls.Add(title);

            var value = new LabelControl { Location = new Point(22, 48), AutoSizeMode = LabelAutoSizeMode.None, Width = 200, Height = 44 };
            value.Appearance.Font = UiTheme.FigureLarge();
            value.Appearance.ForeColor = UiTheme.TextPrimary;
            value.Appearance.Options.UseFont = true;
            value.Appearance.Options.UseForeColor = true;
            panel.Controls.Add(value);

            return value;
        }

        private LabelControl SectionTitle()
        {
            LabelControl label = UiTheme.SectionHeader(new LabelControl());
            Controls.Add(label);
            return label;
        }

        private SimpleButton CreateTile(Action onClick, bool primary)
        {
            var button = new SimpleButton();
            if (primary)
            {
                UiTheme.PrimaryButton(button);
            }
            else
            {
                UiTheme.SecondaryButton(button);
            }
            button.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            button.Click += (s, e) => onClick();
            Controls.Add(button);
            return button;
        }

        /// <summary>Sizes and positions everything to fill the available width responsively.</summary>
        private void LayoutDashboard()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 320 || h < 240)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int pad = 24;
            int gap = 16;
            int content = w - 2 * pad;
            int y = pad;

            // Full-width elements keep x=pad in both directions; DevExpress right-aligns
            // their text when RightToLeft is on. Only the multi-column rows below mirror.
            _pageTitle.Bounds = new Rectangle(pad, y, content, 30);
            _pageSubtitle.Bounds = new Rectangle(pad, y + 32, content, 20);
            y += 70;

            int cardW = (content - 3 * gap) / 4;
            const int cardH = 112;
            for (int i = 0; i < 4; i++)
            {
                int cx = UiTheme.LeadX(w, pad + i * (cardW + gap), cardW, rtl);
                _cards[i].Bounds = new Rectangle(cx, y, cardW, cardH);

                // Accent bar hugs the leading edge; icon and text mirror within the card.
                _cardBars[i].Dock = UiTheme.LeadingDock(rtl);
                _cardTitles[i].Width = Math.Max(80, cardW - 90);
                _cardValues[i].Width = Math.Max(80, cardW - 40);
                UiTheme.PlaceLead(_cardTitles[i], cardW, 22, 22, rtl);
                UiTheme.PlaceLead(_cardValues[i], cardW, 22, 48, rtl);
                if (_cardIcons[i] != null)
                {
                    _cardIcons[i].Location = new Point(UiTheme.LeadX(cardW, cardW - IconSize - 20, IconSize, rtl), 20);
                }
            }
            y += cardH + 28;

            _actionsTitle.Bounds = new Rectangle(pad, y, content, 24);
            y += 32;

            int tileW = (content - 2 * gap) / 3;
            int tileWLast = content - 2 * (tileW + gap);
            const int tileH = 50;
            _quickCompany.Bounds = new Rectangle(UiTheme.LeadX(w, pad, tileW, rtl), y, tileW, tileH);
            _quickEmployee.Bounds = new Rectangle(UiTheme.LeadX(w, pad + tileW + gap, tileW, rtl), y, tileW, tileH);
            _quickPayroll.Bounds = new Rectangle(UiTheme.LeadX(w, pad + 2 * (tileW + gap), tileWLast, rtl), y, tileWLast, tileH);
            y += tileH + 26;

            _recentTitle.Bounds = new Rectangle(pad, y, content, 24);
            y += 32;

            int statusH = 22;
            int gridH = h - y - pad - statusH - 8;
            if (gridH < 120)
            {
                gridH = 120;
            }
            _recentCard.Bounds = new Rectangle(pad, y, content, gridH);
            _statusLine.Bounds = new Rectangle(pad, _recentCard.Bottom + 8, content, statusH);
        }

        public override void Localize()
        {
            _pageTitle.Text = T("Module_Dashboard");
            _pageSubtitle.Text = T("Dashboard_Subtitle");
            _actionsTitle.Text = T("Dashboard_QuickActions");
            _recentTitle.Text = T("Dashboard_RecentItems");

            _cardTitles[0].Text = T("Dashboard_Companies");
            _cardTitles[1].Text = T("Dashboard_Employees");
            _cardTitles[2].Text = T("Dashboard_CurrentMonth");
            _cardTitles[3].Text = T("Dashboard_ArchivedRuns");

            _quickCompany.Text = T("Dashboard_QuickNewCompany");
            _quickEmployee.Text = T("Dashboard_QuickNewEmployee");
            _quickPayroll.Text = T("Dashboard_QuickPayroll");

            if (_recentView.Columns.Count > 0)
            {
                _recentView.Columns["Kind"].Caption = T("Recent_Kind");
                _recentView.Columns["Label"].Caption = T("Recent_Label");
                _recentView.Columns["Date"].Caption = T("Recent_Date");
            }

            LayoutDashboard();
        }

        public override void OnActivated()
        {
            IReadOnlyList<Company> companies = Services.Companies.GetAll();
            _cardValues[0].Text = companies.Count.ToString(CultureInfo.InvariantCulture);

            int employeeCount = companies.Sum(c => Services.Employees.GetByCompany(c.Id).Count);
            _cardValues[1].Text = employeeCount.ToString(CultureInfo.InvariantCulture);

            _cardValues[2].Text = DateTime.Now.ToString("MM/yyyy", CultureInfo.InvariantCulture);

            List<PayrollRun> runs = Services.Archive.SearchRuns(null, null, null).ToList();
            _cardValues[3].Text = runs.Count.ToString(CultureInfo.InvariantCulture);

            var recent = new RecentItemsService(Services.Settings);
            var rows = recent.GetAll().Select(r => new RecentRow
            {
                Kind = T(KindKey(r.Kind)),
                Label = r.Label,
                Date = r.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            }).ToList();

            _recentGrid.DataSource = rows;
            _recentView.PopulateColumns();

            BackupRecord lastBackup = Services.Backup.GetRecent(1).FirstOrDefault();
            string backup = lastBackup == null
                ? T("Dashboard_NoBackup")
                : lastBackup.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            LegalProfile profile = new BuiltInLegalProfileProvider()
                .GetProfile(new PayrollPeriod(DateTime.Now.Year, DateTime.Now.Month));

            string dbSize = "—";
            try
            {
                var dbFile = new FileInfo(Services.Configuration.DatabaseFilePath);
                if (dbFile.Exists)
                {
                    dbSize = (dbFile.Length / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
                }
            }
            catch
            {
                // size is informational only
            }

            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            _statusLine.Text = string.Join("      •      ", new[]
            {
                T("Dashboard_Today") + " : " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                T("Dashboard_LegalProfile") + " : " + profile.LegalVersion,
                T("Dashboard_BackupStatus") + " : " + backup,
                T("Dashboard_DbSize") + " : " + dbSize,
                T("Dashboard_Version") + " : " + version
            });

            Localize();
            LayoutDashboard();
        }

        private static string KindKey(string kind)
        {
            switch (kind)
            {
                case RecentItemsService.KindPayroll:
                    return "Module_Payroll";
                case RecentItemsService.KindArchive:
                    return "Module_Archive";
                default:
                    return "Module_Companies";
            }
        }

        private sealed class RecentRow
        {
            public string Kind { get; set; }
            public string Label { get; set; }
            public string Date { get; set; }
        }
    }
}
