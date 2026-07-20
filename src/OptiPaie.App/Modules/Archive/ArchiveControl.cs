using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;

namespace OptiPaie.App.Modules.Archive
{
    /// <summary>Immutable archive of generated payslips: search, reprint and export to PDF.</summary>
    public sealed class ArchiveControl : BaseModuleControl
    {
        private PanelControl _toolbar;
        private LabelControl _companyLabel;
        private LabelControl _yearLabel;
        private LabelControl _monthLabel;
        private LookUpEdit _company;
        private SpinEdit _year;
        private SpinEdit _month;
        private SimpleButton _searchButton;
        private SimpleButton _previewButton;
        private SimpleButton _exportButton;
        private GridControl _grid;
        private GridView _view;
        private EmptyStatePanel _emptyState;
        private LabelControl _pageTitle;
        private LabelControl _pageSubtitle;

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 76 };
            UiTheme.Toolbar(_toolbar);
            _toolbar.Resize += (s, e) => LayoutToolbar();
            Controls.Add(_grid = new GridControl { Dock = DockStyle.Fill });
            Controls.Add(_toolbar);

            _companyLabel = Caption(16, 12, 220);
            _company = new LookUpEdit { Location = new Point(16, 34), Width = 240 };
            UiTheme.ConfigureLookup(_company, "NameFr", "Id");
            _toolbar.Controls.Add(_company);

            _yearLabel = Caption(272, 12, 90);
            _year = new SpinEdit { Location = new Point(272, 34), Width = 90 };
            _year.Properties.MinValue = 0;
            _year.Properties.MaxValue = 2100;
            _year.Value = System.DateTime.Now.Year;
            UiTheme.StyleInput(_year);
            _toolbar.Controls.Add(_year);

            _monthLabel = Caption(374, 12, 80);
            _month = new SpinEdit { Location = new Point(374, 34), Width = 80 };
            _month.Properties.MinValue = 0;
            _month.Properties.MaxValue = 12;
            _month.Value = 0;
            UiTheme.StyleInput(_month);
            _toolbar.Controls.Add(_month);

            _searchButton = UiTheme.PrimaryButton(AddButton(478, 130, () => LoadData()));
            _previewButton = UiTheme.SecondaryButton(AddButton(618, 150, () => Preview()));
            _exportButton = UiTheme.SecondaryButton(AddButton(778, 150, () => ExportPdf()));

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_view);
            _view.DoubleClick += (s, e) => Preview();

            _emptyState = new EmptyStatePanel { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_emptyState);
            _emptyState.Configure(T("Empty_NoArchive"), null, null);

            AddPageHeader(out _pageTitle, out _pageSubtitle);
        }

        private LabelControl Caption(int x, int y, int w)
        {
            var label = new LabelControl { Location = new Point(x, y), AutoSizeMode = LabelAutoSizeMode.None, Width = w };
            UiTheme.FieldCaption(label);
            _toolbar.Controls.Add(label);
            return label;
        }

        private SimpleButton AddButton(int x, int width, System.Action onClick)
        {
            var button = new SimpleButton { Location = new Point(x, 34), Width = width, Height = UiTheme.ButtonHeight };
            button.Click += (s, e) => onClick();
            _toolbar.Controls.Add(button);
            return button;
        }

        public override void Localize()
        {
            _pageTitle.Text = T("Module_Archive");
            _pageSubtitle.Text = T("Archive_Subtitle");

            _companyLabel.Text = T("Payroll_Company");
            _yearLabel.Text = T("Payroll_Year");
            _monthLabel.Text = T("Payroll_Month");
            _searchButton.Text = T("Common_Search");
            _previewButton.Text = T("Archive_Reprint");
            _exportButton.Text = T("Common_ExportPdf");

            if (_view.Columns.Count > 0)
            {
                _view.Columns["Period"].Caption = T("Archive_Period");
                _view.Columns["CompanyName"].Caption = T("Archive_Company");
                _view.Columns["EmployeeName"].Caption = T("Archive_Employee");
                _view.Columns["Net"].Caption = T("Archive_Net");
            }

            // Size every button to its caption in the active language (no clipping),
            // then position the whole toolbar from the leading edge (fr: left, ar: right).
            foreach (SimpleButton button in new[] { _searchButton, _previewButton, _exportButton })
            {
                UiTheme.FitButton(button, 110);
            }

            LayoutToolbar();
        }

        private void LayoutToolbar()
        {
            if (_company == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int w = _toolbar.Width;
            _companyLabel.Location = new Point(UiTheme.LeadX(w, 16, _companyLabel.Width, rtl), 12);
            _company.Location = new Point(UiTheme.LeadX(w, 16, _company.Width, rtl), 34);
            _yearLabel.Location = new Point(UiTheme.LeadX(w, 272, _yearLabel.Width, rtl), 12);
            _year.Location = new Point(UiTheme.LeadX(w, 272, _year.Width, rtl), 34);
            _monthLabel.Location = new Point(UiTheme.LeadX(w, 374, _monthLabel.Width, rtl), 12);
            _month.Location = new Point(UiTheme.LeadX(w, 374, _month.Width, rtl), 34);

            int bx = 478;
            foreach (SimpleButton button in new[] { _searchButton, _previewButton, _exportButton })
            {
                button.Location = new Point(UiTheme.LeadX(w, bx, button.Width, rtl), 34);
                bx += button.Width + UiTheme.Gap;
            }
        }

        public override void OnActivated()
        {
            _company.Properties.DataSource = new List<Company>(Services.Companies.GetAll());
            LoadData();
        }

        private void LoadData()
        {
            long? companyId = _company.EditValue is long c ? c : (long?)null;
            int? year = (int)_year.Value == 0 ? (int?)null : (int)_year.Value;
            int? month = (int)_month.Value == 0 ? (int?)null : (int)_month.Value;

            var lookup = Services.Companies.GetAll().ToDictionary(x => x.Id, x => x);
            var rows = new List<ArchiveRow>();

            foreach (PayrollRun run in Services.Archive.SearchRuns(companyId, year, month))
            {
                PayrollRun full = Services.Archive.GetRun(run.Id);
                if (full == null)
                {
                    continue;
                }

                foreach (Payslip payslip in full.Payslips)
                {
                    Employee employee = Services.Employees.Get(payslip.EmployeeId);
                    rows.Add(new ArchiveRow
                    {
                        PayslipId = payslip.Id,
                        CompanyId = run.CompanyId,
                        EmployeeId = payslip.EmployeeId,
                        Year = run.PeriodYear,
                        Month = run.PeriodMonth,
                        Period = run.PeriodMonth.ToString("00") + "/" + run.PeriodYear,
                        CompanyName = lookup.ContainsKey(run.CompanyId) ? lookup[run.CompanyId].NameFr : "—",
                        EmployeeName = employee != null ? employee.LastNameFr + " " + employee.FirstNameFr : "—",
                        Net = payslip.NetSalaire
                    });
                }
            }

            _grid.DataSource = rows;

            _emptyState.Visible = rows.Count == 0;
            if (_emptyState.Visible)
            {
                _emptyState.BringToFront();
            }

            _view.PopulateColumns();
            if (_view.Columns["PayslipId"] != null)
            {
                _view.Columns["PayslipId"].Visible = false;
                _view.Columns["CompanyId"].Visible = false;
                _view.Columns["EmployeeId"].Visible = false;
                _view.Columns["Year"].Visible = false;
                _view.Columns["Month"].Visible = false;
            }

            Localize();
        }

        private ArchiveRow Selected()
        {
            return _view.GetFocusedRow() as ArchiveRow;
        }

        private PayslipPrintModel BuildModel(ArchiveRow row)
        {
            Payslip payslip = Services.Archive.GetPayslip(row.PayslipId);
            if (payslip == null)
            {
                return null;
            }

            return new PayslipPrintModel
            {
                Company = Services.Companies.Get(row.CompanyId),
                Employee = Services.Employees.Get(row.EmployeeId),
                Payslip = payslip,
                Lines = payslip.Details.ToList(),
                LanguageCode = Services.Localization.CurrentLanguage,
                PeriodYear = row.Year,
                PeriodMonth = row.Month
            };
        }

        public override void OnPrint()
        {
            Preview();
        }

        private void Preview()
        {
            ArchiveRow row = Selected();
            if (row == null)
            {
                return;
            }

            PayslipPrintModel model = BuildModel(row);
            if (model != null)
            {
                new RecentItemsService(Services.Settings).Record(
                    RecentItemsService.KindArchive, row.PayslipId, row.EmployeeName + " — " + row.Period);
                Services.Reports.Preview(model);
            }
        }

        private void ExportPdf()
        {
            ArchiveRow row = Selected();
            if (row == null)
            {
                return;
            }

            PayslipPrintModel model = BuildModel(row);
            if (model == null)
            {
                return;
            }

            using (var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "bulletin_" + row.Period.Replace("/", "_") + ".pdf" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Services.Reports.ExportPdf(model, dialog.FileName);
                    UiHelper.Info(T("Common_Success"), T("Common_Success"));
                }
            }
        }

        private sealed class ArchiveRow
        {
            public long PayslipId { get; set; }
            public long CompanyId { get; set; }
            public long EmployeeId { get; set; }
            public int Year { get; set; }
            public int Month { get; set; }
            public string Period { get; set; }
            public string CompanyName { get; set; }
            public string EmployeeName { get; set; }
            public decimal Net { get; set; }
        }
    }
}
