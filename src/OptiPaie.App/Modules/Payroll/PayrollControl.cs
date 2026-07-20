using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Modules.Payroll
{
    /// <summary>
    /// The Payroll worksheet — modelled on a real Algerian bulletin de paie rather
    /// than a database screen. The accountant picks a company, an employee and a
    /// period; the base salary appears automatically as the first highlighted line;
    /// payroll elements are added below; gains and deductions are colour-coded; and
    /// the statutory totals (gross, CNAS, IRG, employer charge, net) update live as
    /// values are typed. Nothing is saved automatically.
    ///
    /// The grid columns mirror a payslip — Rubrique · Base · Taux · Gain · Retenue ·
    /// Observations — and never expose internal identifiers or database fields. Each
    /// row carries its element id, type and calculation method privately so the
    /// worksheet can be translated back into an engine request with no change to the
    /// payroll logic (the base salary line is intrinsic to the engine and is excluded
    /// from the request).
    /// </summary>
    public sealed class PayrollControl : BaseModuleControl
    {
        private PanelControl _toolbar;
        private PanelControl _employeeCard;
        private PanelControl _elementsBar;
        private PanelControl _totalsPanel;

        private LabelControl _companyLabel;
        private LookUpEdit _company;
        private LabelControl _employeeLabel;
        private LookUpEdit _employee;
        private LabelControl _monthLabel;
        private SpinEdit _month;
        private LabelControl _yearLabel;
        private SpinEdit _year;
        private LabelControl _headerStatus;

        // Employee summary card.
        private PanelControl _avatar;
        private LabelControl _avatarLabel;
        private LabelControl _empName;
        private LabelControl _empContract;
        private LabelControl _empEmptyHint;
        private const int AvatarSize = 56;
        private const int CardLeftInset = 92; // name + fields start right of the avatar
        private static readonly string[] EmpFieldKeys =
            { "Employee_Nss", "Employee_Category", "Employee_Poste", "Employee_HireDate", "Employee_BaseSalary" };
        private readonly LabelControl[] _empCaptions = new LabelControl[EmpFieldKeys.Length];
        private readonly LabelControl[] _empValues = new LabelControl[EmpFieldKeys.Length];

        private SimpleButton _explainButton;
        private SimpleButton _saveButton;
        private SimpleButton _printButton;
        private SimpleButton _exportPdfButton;
        private SimpleButton _resetButton;
        private LabelControl _elementsTitle;
        private LookUpEdit _catalog;
        private SimpleButton _addElementButton;
        private SimpleButton _addFreeButton;
        private SimpleButton _addTypeButton;
        private SimpleButton _manageTypesButton;
        private SimpleButton _removeElementButton;

        private GridControl _grid;
        private GridView _view;
        private BindingList<EntryRow> _rows;
        private LabelControl _statusInfo;

        private readonly Dictionary<string, LabelControl> _totalValues = new Dictionary<string, LabelControl>();
        private readonly Dictionary<string, LabelControl> _totalTitles = new Dictionary<string, LabelControl>();
        private readonly Dictionary<string, PanelControl> _totalStripes = new Dictionary<string, PanelControl>();

        // The totals shown as colour-coded cards, laid out as two rows of four
        // (Emphasis = filled card). Order = reading order across both rows.
        private static readonly (string Key, bool Emphasis)[] TotalCards =
        {
            ("Totals_Gross", false),         // gross salary
            ("Totals_Gains", false),         // total gains
            ("Totals_Retenues", false),      // total deductions
            ("Totals_Cnas", false),          // CNAS employee
            ("Totals_Taxable", false),       // taxable base
            ("Totals_Irg", false),           // IRG
            // NOTE: the employer CNAS charge ("CNAS patronale") is intentionally NOT
            // shown to the employee. It is still computed and stored internally (for
            // declarations/DAS) but never displayed on this screen or the payslip.
            ("Totals_Net", true)             // net to pay, emphasised
        };

        private const int CardsPerRow = 4;

        private PayrollResult _lastResult;
        private PayrollGenerationRequest _lastRequest;
        private bool _recomputing;
        private bool _hasComputed;

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 100 };
            UiTheme.Toolbar(_toolbar);
            AddBottomLine(_toolbar);
            _toolbar.Resize += (s, e) => LayoutToolbar();

            _elementsBar = new PanelControl { Dock = DockStyle.Top, Height = 60 };
            UiTheme.Toolbar(_elementsBar);
            AddBottomLine(_elementsBar);
            _elementsBar.Resize += (s, e) => LayoutElementsBar();

            _totalsPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 212 };
            UiTheme.Toolbar(_totalsPanel);
            _totalsPanel.Resize += (s, e) => LayoutTotals();

            _employeeCard = new PanelControl { Dock = DockStyle.Top, Height = 104 };
            UiTheme.Card(_employeeCard);
            _employeeCard.Resize += (s, e) => LayoutEmployeeCard();

            _grid = new GridControl { Dock = DockStyle.Fill };

            // Each container holds at most one Top + one Fill, so the docking order
            // (toolbar → employee card → elements bar → grid → totals) is deterministic.
            var inner = new PanelControl { Dock = DockStyle.Fill, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            inner.Controls.Add(_grid);
            inner.Controls.Add(_elementsBar);

            var outer = new PanelControl { Dock = DockStyle.Fill, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            outer.Controls.Add(inner);
            outer.Controls.Add(_employeeCard);

            Controls.Add(outer);
            Controls.Add(_totalsPanel);
            Controls.Add(_toolbar);

            BuildToolbar();
            BuildEmployeeCard();
            BuildElementsBar();
            BuildGrid();
            BuildTotals();
        }

        // -- Top context toolbar ---------------------------------------------------

        private void BuildToolbar()
        {
            _companyLabel = Caption(16, 12, 240);
            _company = new LookUpEdit { Location = new Point(16, 34), Width = 240 };
            UiTheme.ConfigureLookup(_company, "NameFr", "Id");
            _company.EditValueChanged += (s, e) => ReloadEmployees();
            _toolbar.Controls.Add(_company);

            _employeeLabel = Caption(272, 12, 240);
            _employee = new LookUpEdit { Location = new Point(272, 34), Width = 240 };
            UiTheme.ConfigureLookup(_employee, "LastNameFr", "Id");
            _employee.EditValueChanged += (s, e) =>
            {
                if (_employee.EditValue is long)
                {
                    LoadElements();
                }
            };
            _toolbar.Controls.Add(_employee);

            _monthLabel = Caption(528, 12, 70);
            _month = Spin(528, 34, 70, 1, 12, false);
            _month.Value = DateTime.Now.Month;
            _month.EditValueChanged += (s, e) => InvalidateComputation();

            _yearLabel = Caption(606, 12, 80);
            _year = Spin(606, 34, 80, 2000, 2100, false);
            _year.Value = DateTime.Now.Year;
            _year.EditValueChanged += (s, e) => InvalidateComputation();

            // The calculation is automatic (on every edit), so there is no Calculate /
            // Load button — the right of the header carries the live payroll status.
            _headerStatus = new LabelControl
            {
                AutoSizeMode = LabelAutoSizeMode.None,
                Width = 360,
                Height = 24,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _headerStatus.Appearance.Font = UiTheme.BodyStrong();
            _headerStatus.Appearance.ForeColor = UiTheme.Primary;
            _headerStatus.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;
            _headerStatus.Appearance.Options.UseFont = true;
            _headerStatus.Appearance.Options.UseForeColor = true;
            _toolbar.Controls.Add(_headerStatus);
        }

        private void LayoutToolbar()
        {
            if (_headerStatus == null || _company == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int w = _toolbar.Width;

            // Selectors flow from the leading edge (left in fr, right in ar).
            _companyLabel.Location = new Point(UiTheme.LeadX(w, 16, _companyLabel.Width, rtl), 12);
            _company.Location = new Point(UiTheme.LeadX(w, 16, _company.Width, rtl), 34);
            _employeeLabel.Location = new Point(UiTheme.LeadX(w, 272, _employeeLabel.Width, rtl), 12);
            _employee.Location = new Point(UiTheme.LeadX(w, 272, _employee.Width, rtl), 34);
            _monthLabel.Location = new Point(UiTheme.LeadX(w, 528, _monthLabel.Width, rtl), 12);
            _month.Location = new Point(UiTheme.LeadX(w, 528, _month.Width, rtl), 34);
            _yearLabel.Location = new Point(UiTheme.LeadX(w, 606, _yearLabel.Width, rtl), 12);
            _year.Location = new Point(UiTheme.LeadX(w, 606, _year.Width, rtl), 34);

            // Live status badge on the trailing edge (opposite the selectors).
            _headerStatus.Location = new Point(rtl ? UiTheme.Pad : (w - UiTheme.Pad - _headerStatus.Width), 36);
        }

        // -- Employee summary card -------------------------------------------------

        private void BuildEmployeeCard()
        {
            // Circular initials avatar — a small premium touch that anchors the card.
            _avatar = new PanelControl { Location = new Point(22, 24), Width = AvatarSize, Height = AvatarSize, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _avatar.Appearance.BackColor = UiTheme.AccentSoft;
            _avatar.Appearance.BackColor2 = UiTheme.AccentSoft;
            _avatar.Appearance.Options.UseBackColor = true;
            UiTheme.RoundCorners(_avatar, AvatarSize / 2);
            _avatarLabel = new LabelControl { Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.None };
            _avatarLabel.Appearance.Font = new Font(UiTheme.FontName, 18F, FontStyle.Bold);
            _avatarLabel.Appearance.ForeColor = UiTheme.Primary;
            _avatarLabel.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            _avatarLabel.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;
            _avatarLabel.Appearance.Options.UseFont = true;
            _avatarLabel.Appearance.Options.UseForeColor = true;
            _avatarLabel.Appearance.Options.UseTextOptions = true;
            _avatar.Controls.Add(_avatarLabel);
            _employeeCard.Controls.Add(_avatar);

            _empName = new LabelControl { Location = new Point(CardLeftInset, 16), AutoSizeMode = LabelAutoSizeMode.None, Width = 360, Height = 24 };
            _empName.Appearance.Font = new Font(UiTheme.FontName, 13F, FontStyle.Bold);
            _empName.Appearance.ForeColor = UiTheme.TextPrimary;
            _empName.Appearance.Options.UseFont = true;
            _empName.Appearance.Options.UseForeColor = true;
            _employeeCard.Controls.Add(_empName);

            _empContract = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Width = 180, Height = 22 };
            _empContract.Appearance.Font = UiTheme.BodyStrong();
            _empContract.Appearance.ForeColor = UiTheme.Employer;
            _empContract.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;
            _empContract.Appearance.Options.UseFont = true;
            _empContract.Appearance.Options.UseForeColor = true;
            _employeeCard.Controls.Add(_empContract);

            for (int i = 0; i < EmpFieldKeys.Length; i++)
            {
                _empCaptions[i] = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Height = 16 };
                UiTheme.FieldCaption(_empCaptions[i]);
                _employeeCard.Controls.Add(_empCaptions[i]);

                _empValues[i] = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Height = 20 };
                _empValues[i].Appearance.Font = UiTheme.BodyStrong();
                _empValues[i].Appearance.ForeColor = UiTheme.TextPrimary;
                _empValues[i].Appearance.Options.UseFont = true;
                _empValues[i].Appearance.Options.UseForeColor = true;
                _employeeCard.Controls.Add(_empValues[i]);
            }

            _empEmptyHint = new LabelControl { Location = new Point(20, 40), AutoSizeMode = LabelAutoSizeMode.None, Width = 400, Height = 22 };
            UiTheme.Muted(_empEmptyHint);
            _empEmptyHint.Appearance.Font = UiTheme.Body();
            _employeeCard.Controls.Add(_empEmptyHint);

            UpdateEmployeeCard(null);
        }

        private void LayoutEmployeeCard()
        {
            if (_empName == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            const int pad = 20;
            int w = _employeeCard.Width;

            // Avatar hugs the leading edge; name + fields sit just inside it; the
            // contract badge sits on the trailing edge.
            _avatar.Location = new Point(UiTheme.LeadX(w, 22, AvatarSize, rtl), 24);
            _empName.Location = new Point(UiTheme.LeadX(w, CardLeftInset, _empName.Width, rtl), 16);
            _empEmptyHint.Location = new Point(UiTheme.LeadX(w, 20, _empEmptyHint.Width, rtl), 40);
            _empContract.Location = new Point(rtl ? pad : (w - pad - _empContract.Width), 18);

            int left = CardLeftInset;
            int colW = (w - left - pad) / EmpFieldKeys.Length;
            if (colW < 90)
            {
                colW = 90;
            }

            int cellW = colW - 10;
            for (int i = 0; i < EmpFieldKeys.Length; i++)
            {
                int x = UiTheme.LeadX(w, left + i * colW, cellW, rtl);
                _empCaptions[i].Bounds = new Rectangle(x, 52, cellW, 16);
                _empValues[i].Bounds = new Rectangle(x, 70, cellW, 20);
            }
        }

        private void UpdateEmployeeCard(Employee e)
        {
            bool has = e != null;
            _avatar.Visible = has;
            _empName.Visible = has;
            _empContract.Visible = has;
            _empEmptyHint.Visible = !has;
            foreach (LabelControl c in _empCaptions) c.Visible = has;
            foreach (LabelControl v in _empValues) v.Visible = has;

            if (!has)
            {
                _empEmptyHint.Text = T("Payroll_NoEmployee");
                return;
            }

            bool rtl = L.IsRightToLeft;
            string last = rtl && !string.IsNullOrWhiteSpace(e.LastNameAr) ? e.LastNameAr : e.LastNameFr;
            string first = rtl && !string.IsNullOrWhiteSpace(e.FirstNameAr) ? e.FirstNameAr : e.FirstNameFr;
            _empName.Text = (last + " " + first).Trim();
            _avatarLabel.Text = Initials(last, first);
            _empContract.Text = EnumLocalizer.Localize(Services.Localization, e.ContractType);

            string[] values =
            {
                string.IsNullOrWhiteSpace(e.Nss) ? "—" : e.Nss,
                string.IsNullOrWhiteSpace(e.Category) ? "—" : e.Category,
                string.IsNullOrWhiteSpace(e.Poste) ? "—" : e.Poste,
                e.HireDate == default(DateTime) ? "—" : e.HireDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                UiHelper.Money(e.BaseSalary, T("Common_Currency"))
            };

            for (int i = 0; i < _empValues.Length; i++)
            {
                _empValues[i].Text = values[i];
            }

            LayoutEmployeeCard();
        }

        /// <summary>Two-letter initials (last + first) for the avatar badge.</summary>
        private static string Initials(string last, string first)
        {
            char a = !string.IsNullOrWhiteSpace(last) ? char.ToUpperInvariant(last.Trim()[0]) : ' ';
            char b = !string.IsNullOrWhiteSpace(first) ? char.ToUpperInvariant(first.Trim()[0]) : ' ';
            return (a.ToString() + b).Trim();
        }

        // -- Elements action bar ---------------------------------------------------

        private void BuildElementsBar()
        {
            _elementsTitle = new LabelControl
            {
                Location = new Point(16, 16),
                AutoSizeMode = LabelAutoSizeMode.None,
                Width = 260,
                Height = 28 // explicit height so the heading is never vertically clipped
            };
            UiTheme.SectionHeader(_elementsTitle);
            _elementsBar.Controls.Add(_elementsTitle);

            _removeElementButton = new SimpleButton { Width = 130, Height = UiTheme.ButtonHeight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.DangerButton(_removeElementButton);
            _removeElementButton.Click += (s, e) => RemoveSelectedRow();
            _elementsBar.Controls.Add(_removeElementButton);

            _addElementButton = new SimpleButton { Width = 220, Height = UiTheme.ButtonHeight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.PrimaryButton(_addElementButton);
            _addElementButton.Click += (s, e) => AddCatalogElement();
            _elementsBar.Controls.Add(_addElementButton);

            _catalog = new LookUpEdit { Width = 220, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.ConfigureLookup(_catalog, "NameFr", "Id");
            _elementsBar.Controls.Add(_catalog);

            _addFreeButton = new SimpleButton { Width = 130, Height = UiTheme.ButtonHeight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_addFreeButton);
            _addFreeButton.Click += (s, e) => AddFreeElement();
            _elementsBar.Controls.Add(_addFreeButton);

            _addTypeButton = new SimpleButton { Width = 150, Height = UiTheme.ButtonHeight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_addTypeButton);
            _addTypeButton.Click += (s, e) => AddNewType();
            _elementsBar.Controls.Add(_addTypeButton);

            _manageTypesButton = new SimpleButton { Width = 150, Height = UiTheme.ButtonHeight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_manageTypesButton);
            _manageTypesButton.Click += (s, e) => ManageTypes();
            _elementsBar.Controls.Add(_manageTypesButton);
        }

        private void LayoutElementsBar()
        {
            if (_addElementButton == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int w = _elementsBar.Width;
            int y = 14;

            // Section title on the leading edge.
            _elementsTitle.Location = new Point(UiTheme.LeadX(w, 16, _elementsTitle.Width, rtl), 16);

            // Action controls, in reading order, packed from the TRAILING edge
            // (right in French, left in Arabic).
            Control[] items = { _removeElementButton, _addElementButton, _catalog, _addFreeButton, _addTypeButton, _manageTypesButton };
            if (!rtl)
            {
                int right = w - UiTheme.Pad;
                foreach (Control c in items)
                {
                    c.Location = new Point(right - c.Width, ReferenceEquals(c, _catalog) ? y + 3 : y);
                    right -= c.Width + UiTheme.Gap;
                }
            }
            else
            {
                int leftX = UiTheme.Pad;
                foreach (Control c in items)
                {
                    c.Location = new Point(leftX, ReferenceEquals(c, _catalog) ? y + 3 : y);
                    leftX += c.Width + UiTheme.Gap;
                }
            }
        }

        /// <summary>Adds a 1px hairline along the bottom of a bar for clean separation.</summary>
        private static void AddBottomLine(PanelControl panel)
        {
            var line = new PanelControl { Dock = DockStyle.Bottom, Height = 1, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            line.Appearance.BackColor = UiTheme.Hairline;
            line.Appearance.Options.UseBackColor = true;
            panel.Controls.Add(line);
        }

        private LabelControl Caption(int x, int y, int w)
        {
            var label = new LabelControl { Location = new Point(x, y), AutoSizeMode = LabelAutoSizeMode.None, Width = w };
            UiTheme.FieldCaption(label);
            _toolbar.Controls.Add(label);
            return label;
        }

        private SpinEdit Spin(int x, int y, int w, decimal min, decimal max, bool isFloat)
        {
            var spin = new SpinEdit { Location = new Point(x, y), Width = w };
            spin.Properties.MinValue = min;
            spin.Properties.MaxValue = max;
            spin.Properties.IsFloatValue = isFloat;
            UiTheme.StyleInput(spin);
            _toolbar.Controls.Add(spin);
            return spin;
        }

        // -- Worksheet grid --------------------------------------------------------

        private void BuildGrid()
        {
            _view = new GridView(_grid);
            _grid.MainView = _view;
            UiTheme.StyleGrid(_view);
            _view.RowHeight = 34;
            _view.OptionsBehavior.Editable = true;
            _view.OptionsNavigation.EnterMoveNextColumn = true;            // Enter advances to the next cell
            _view.OptionsBehavior.EditorShowMode = DevExpress.Utils.EditorShowMode.MouseUp; // a single click starts editing

            _rows = new BindingList<EntryRow>();

            // Define the six worksheet columns EXPLICITLY, then bind. The data source
            // must be assigned AFTER the columns exist: the grid only auto-generates a
            // column per bound property when its column collection is still empty, so
            // binding first would produce a duplicate (auto + explicit) set of columns.
            GridColumn rubrique = _view.Columns.AddVisible("Rubrique");
            GridColumn baseCol = _view.Columns.AddVisible("Base");
            GridColumn taux = _view.Columns.AddVisible("Taux");
            GridColumn gain = _view.Columns.AddVisible("Gain");
            GridColumn retenue = _view.Columns.AddVisible("Retenue");
            GridColumn observations = _view.Columns.AddVisible("Observations");

            rubrique.Width = 240; // editability gated per row (free elements only) in ShowingEditor

            // Money columns: right-aligned, two decimals.
            foreach (GridColumn col in new[] { baseCol, gain, retenue })
            {
                col.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                col.DisplayFormat.FormatString = "n2";
                col.AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;
                col.AppearanceCell.Options.UseTextOptions = true;
                col.AppearanceHeader.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Far;
                col.AppearanceHeader.Options.UseTextOptions = true;
                col.Width = 110;
            }

            // Taux is free text ("10" multiplier, "10%" percentage), centre-aligned.
            taux.AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            taux.AppearanceCell.Options.UseTextOptions = true;
            taux.Width = 110;

            // Gains read green, deductions read red — instant visual separation.
            gain.AppearanceCell.ForeColor = UiTheme.Salary;
            gain.AppearanceCell.Options.UseForeColor = true;
            retenue.AppearanceCell.ForeColor = UiTheme.Deduction;
            retenue.AppearanceCell.Options.UseForeColor = true;

            observations.Width = 200;

            _grid.DataSource = _rows;

            // Per-cell editability depends on the row's calculation method, so the
            // worksheet only opens an editor where data entry actually makes sense;
            // the locked cells are greyed (View_RowCellStyle) so they read as
            // intentionally read-only rather than broken.
            _view.ShowingEditor += View_ShowingEditor;
            _view.CellValueChanged += View_CellValueChanged;
            _view.RowStyle += View_RowStyle;
            _view.RowCellStyle += View_RowCellStyle;

            // Delete key removes the focused element line (but not while editing a cell).
            _grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !_view.IsEditing)
                {
                    RemoveSelectedRow();
                    e.Handled = true;
                }
            };
        }

        private void View_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.RowHandle < 0 || e.Column == null || !(_view.GetRow(e.RowHandle) is EntryRow row))
            {
                return;
            }

            if (row.IsBaseSalary)
            {
                return; // already highlighted by View_RowStyle
            }

            // Tint the cells that do not accept input for this row's method.
            if (e.Column.FieldName != "Rubrique" && !IsEditable(row, e.Column.FieldName))
            {
                e.Appearance.BackColor = Color.FromArgb(244, 246, 248);
                e.Appearance.Options.UseBackColor = true;
            }
        }

        private void View_ShowingEditor(object sender, CancelEventArgs e)
        {
            if (!(_view.GetFocusedRow() is EntryRow row))
            {
                e.Cancel = true;
                return;
            }

            string field = _view.FocusedColumn != null ? _view.FocusedColumn.FieldName : null;
            if (!IsEditable(row, field))
            {
                e.Cancel = true;
            }
        }

        /// <summary>Which worksheet cells accept input for a given row.</summary>
        private static bool IsEditable(EntryRow row, string field)
        {
            switch (field)
            {
                case "Observations":
                    return true; // notes always allowed
                case "Rubrique":
                    return row.IsManual; // only free elements can be renamed in-grid
                case "Base":
                    return true; // Base is always editable
            }

            if (row.IsBaseSalary)
            {
                return false; // base line: only Base + Observations
            }

            // Every element line is a simple "Base × Taux" worksheet entry.
            return field == "Taux" || field == "Gain" || field == "Retenue";
        }

        private void View_CellValueChanged(object sender, DevExpress.XtraGrid.Views.Base.CellValueChangedEventArgs e)
        {
            // Re-evaluate the edited line (Base × Taux) immediately, then recompute totals.
            if (_view.GetRow(e.RowHandle) is EntryRow row && !row.IsBaseSalary)
            {
                ComputeLine(row);
                _view.RefreshData();
            }

            ScheduleRecompute();
        }

        /// <summary>Parses a Taux cell: "10" → ×10 multiplier, "10%" → 10 percent (÷100).</summary>
        private static decimal? ParseTaux(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = text.Trim();
            bool percent = text.EndsWith("%");
            string number = (percent ? text.Substring(0, text.Length - 1) : text).Trim().Replace(',', '.');
            if (decimal.TryParse(number, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal value))
            {
                return percent ? value / 100m : value;
            }

            return null;
        }

        /// <summary>Evaluates an element line: Gain/Retenue = Base × Taux, else the typed amount.</summary>
        private static void ComputeLine(EntryRow row)
        {
            if (row.IsBaseSalary)
            {
                row.LineAmount = row.Base;
                return;
            }

            decimal baseValue = row.Base ?? 0m;
            decimal? factor = ParseTaux(row.Taux);
            decimal amount;

            if (factor.HasValue)
            {
                amount = baseValue * factor.Value;
                if (row.ElementType == ElementType.Deduction)
                {
                    row.Retenue = amount;
                    row.Gain = null;
                }
                else
                {
                    row.Gain = amount;
                    row.Retenue = null;
                }
            }
            else
            {
                amount = row.ElementType == ElementType.Deduction ? (row.Retenue ?? 0m) : (row.Gain ?? 0m);
            }

            row.LineAmount = amount;
        }

        private void View_RowStyle(object sender, RowStyleEventArgs e)
        {
            if (e.RowHandle < 0)
            {
                return;
            }

            if (_view.GetRow(e.RowHandle) is EntryRow row && row.IsBaseSalary)
            {
                e.Appearance.BackColor = Color.FromArgb(232, 244, 238);
                e.Appearance.ForeColor = UiTheme.PrimaryDark;
                e.Appearance.Font = UiTheme.BodyStrong();
                e.Appearance.Options.UseBackColor = true;
                e.Appearance.Options.UseForeColor = true;
                e.Appearance.Options.UseFont = true;
            }
        }

        // -- Totals cards ----------------------------------------------------------

        private void BuildTotals()
        {
            foreach ((string key, bool emphasis) in TotalCards)
            {
                Color accent = ColorFor(key);

                var card = new PanelControl { Width = 170, Height = 66, Location = new Point(0, 10) };
                if (emphasis)
                {
                    card.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
                    card.Appearance.BackColor = accent;
                    card.Appearance.Options.UseBackColor = true;
                }
                else
                {
                    UiTheme.Card(card);
                }
                _totalsPanel.Controls.Add(card);

                var stripe = new PanelControl { Dock = DockStyle.Left, Width = 4, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
                stripe.Appearance.BackColor = emphasis ? Color.FromArgb(255, 255, 255) : accent;
                stripe.Appearance.Options.UseBackColor = true;
                card.Controls.Add(stripe);
                _totalStripes[key] = stripe;

                var title = new LabelControl { Location = new Point(16, 11), AutoSizeMode = LabelAutoSizeMode.None, Width = 150 };
                title.Appearance.Font = UiTheme.Caption();
                title.Appearance.ForeColor = emphasis ? Color.FromArgb(225, 240, 233) : UiTheme.TextMuted;
                title.Appearance.Options.UseFont = true;
                title.Appearance.Options.UseForeColor = true;
                card.Controls.Add(title);
                _totalTitles[key] = title;

                var value = new LabelControl { Location = new Point(16, emphasis ? 28 : 31), AutoSizeMode = LabelAutoSizeMode.None, Width = 150, Height = 32 };
                value.Appearance.Font = emphasis ? UiTheme.FigureLarge() : UiTheme.Figure();
                value.Appearance.ForeColor = emphasis ? Color.White : accent;
                value.Appearance.Options.UseFont = true;
                value.Appearance.Options.UseForeColor = true;
                value.Text = "0,00";
                card.Controls.Add(value);
                _totalValues[key] = value;

                card.Tag = key;
            }

            // Inline status (no popups during data entry).
            _statusInfo = new LabelControl { Location = new Point(UiTheme.Pad, 110), AutoSizeMode = LabelAutoSizeMode.None, Width = 520, Height = 22 };
            _statusInfo.Appearance.Font = UiTheme.Body();
            _statusInfo.Appearance.ForeColor = UiTheme.TextMuted;
            _statusInfo.Appearance.Options.UseFont = true;
            _statusInfo.Appearance.Options.UseForeColor = true;
            _totalsPanel.Controls.Add(_statusInfo);

            // Result actions, on their own row beneath the two rows of totals cards.
            _resetButton = new SimpleButton { Width = 130, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_resetButton);
            _resetButton.Click += (s, e) => ResetWorksheet();
            _totalsPanel.Controls.Add(_resetButton);

            _exportPdfButton = new SimpleButton { Width = 150, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_exportPdfButton);
            _exportPdfButton.Click += (s, e) => ExportPdf();
            _totalsPanel.Controls.Add(_exportPdfButton);

            _printButton = new SimpleButton { Width = 150, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_printButton);
            _printButton.Click += (s, e) => Print();
            _totalsPanel.Controls.Add(_printButton);

            _explainButton = new SimpleButton { Width = 160, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.SecondaryButton(_explainButton);
            _explainButton.Click += (s, e) => Explain();
            _totalsPanel.Controls.Add(_explainButton);

            _saveButton = new SimpleButton { Width = 190, Height = 36, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            UiTheme.PrimaryButton(_saveButton);
            _saveButton.Click += (s, e) => Save();
            _totalsPanel.Controls.Add(_saveButton);
        }

        private static Color ColorFor(string key)
        {
            switch (key)
            {
                case "Totals_Gross":
                case "Totals_Gains":
                case "Totals_Net":
                    return UiTheme.Salary;
                case "Totals_Retenues":
                case "Totals_Cnas":
                case "Totals_Irg":
                    return UiTheme.Deduction;
                case "Totals_Taxable":
                    return UiTheme.Warning;
                default:
                    return UiTheme.Neutral;
            }
        }

        private void LayoutTotals()
        {
            if (_totalsPanel == null || _totalValues.Count == 0 || _saveButton == null)
            {
                return;
            }

            // Two rows of four colour-coded cards that stretch to fill the full width
            // (no dead space on wide screens). In Arabic the columns mirror and each
            // card's accent stripe flips to the leading (right) edge.
            bool rtl = L.IsRightToLeft;
            int w = _totalsPanel.Width;
            const int rowTop = 10;
            const int rowGap = 8;
            const int cardHeight = 66;
            int cardW = (w - 2 * UiTheme.Pad - (CardsPerRow - 1) * UiTheme.Gap) / CardsPerRow;
            if (cardW < 150)
            {
                cardW = 150;
            }

            int index = 0;
            foreach ((string key, bool __) in TotalCards)
            {
                if (_totalValues.TryGetValue(key, out LabelControl value) && value.Parent is PanelControl card)
                {
                    int row = index / CardsPerRow;
                    int col = index % CardsPerRow;
                    int x = UiTheme.LeadX(w, UiTheme.Pad + col * (cardW + UiTheme.Gap), cardW, rtl);
                    int y = rowTop + row * (cardHeight + rowGap);
                    card.Bounds = new Rectangle(x, y, cardW, cardHeight);

                    value.Width = cardW - 24;
                    if (_totalTitles.TryGetValue(key, out LabelControl title))
                    {
                        title.Width = cardW - 24;
                    }

                    if (_totalStripes.TryGetValue(key, out PanelControl stripe))
                    {
                        stripe.Dock = UiTheme.LeadingDock(rtl);
                    }
                }

                index++;
            }

            // Action row beneath the cards: buttons on the trailing edge (Save primary),
            // the status detail on the leading edge.
            int btnY = rowTop + 2 * (cardHeight + rowGap) + 6;
            SimpleButton[] buttons = { _saveButton, _explainButton, _printButton, _exportPdfButton, _resetButton };
            if (!rtl)
            {
                int right = w - UiTheme.Pad;
                foreach (SimpleButton button in buttons)
                {
                    button.Location = new Point(right - button.Width, btnY);
                    right -= button.Width + UiTheme.Gap;
                }

                if (_statusInfo != null)
                {
                    _statusInfo.Location = new Point(UiTheme.Pad, btnY + 7);
                    _statusInfo.Width = Math.Max(160, right - UiTheme.Pad);
                }
            }
            else
            {
                int leftX = UiTheme.Pad;
                foreach (SimpleButton button in buttons)
                {
                    button.Location = new Point(leftX, btnY);
                    leftX += button.Width + UiTheme.Gap;
                }

                if (_statusInfo != null)
                {
                    int sw = Math.Max(160, (w - UiTheme.Pad) - leftX);
                    _statusInfo.Width = sw;
                    _statusInfo.Location = new Point(w - UiTheme.Pad - sw, btnY + 7);
                }
            }
        }

        public override void Localize()
        {
            _companyLabel.Text = T("Payroll_Company");
            _employeeLabel.Text = T("Payroll_Employee");
            _monthLabel.Text = T("Payroll_Month");
            _yearLabel.Text = T("Payroll_Year");
            _explainButton.Text = T("Payroll_Explain");
            _saveButton.Text = T("Payroll_Save");
            _printButton.Text = T("Common_Print");
            _exportPdfButton.Text = T("Common_ExportPdf");
            _resetButton.Text = T("Payroll_Reset");
            _elementsTitle.Text = T("Payroll_Elements");
            _addElementButton.Text = T("Payroll_AddElement");
            _addFreeButton.Text = "+ " + T("Payroll_AddFree");
            _addTypeButton.Text = "+ " + T("Payroll_NewType");
            _manageTypesButton.Text = T("Payroll_ManageTypes");
            _removeElementButton.Text = T("Common_Delete");
            _catalog.Properties.NullText = T("Payroll_SelectElement");

            // Size every button to its caption in the active language so no label is
            // ever clipped (the recurring truncation defect). Layout runs afterwards
            // and positions the buttons using these computed widths.
            UiTheme.FitButton(_saveButton, 150);
            UiTheme.FitButton(_explainButton, 120);
            UiTheme.FitButton(_printButton, 110);
            UiTheme.FitButton(_exportPdfButton, 120);
            UiTheme.FitButton(_resetButton, 110);
            UiTheme.FitButton(_addElementButton, 160);
            UiTheme.FitButton(_addFreeButton, 110);
            UiTheme.FitButton(_addTypeButton, 120);
            UiTheme.FitButton(_manageTypesButton, 120);
            UiTheme.FitButton(_removeElementButton, 100);

            for (int i = 0; i < EmpFieldKeys.Length; i++)
            {
                _empCaptions[i].Text = T(EmpFieldKeys[i]);
            }
            _empEmptyHint.Text = T("Payroll_NoEmployee");

            _view.Columns["Rubrique"].Caption = T("Payroll_Element");
            _view.Columns["Base"].Caption = T("Payroll_Base");
            _view.Columns["Taux"].Caption = T("Payroll_Rate");
            _view.Columns["Gain"].Caption = T("Payroll_Gain");
            _view.Columns["Retenue"].Caption = T("Payroll_Retenue");
            _view.Columns["Observations"].Caption = T("Payroll_Observations");

            foreach (KeyValuePair<string, LabelControl> pair in _totalTitles)
            {
                pair.Value.Text = T(pair.Key);
            }

            if (!_hasComputed)
            {
                SetStatus(T("Payroll_CalcReady"), false, T("Payroll_StatusReady"));
            }

            LayoutToolbar();
            LayoutEmployeeCard();
            LayoutElementsBar();
            LayoutTotals();
        }

        public override void OnActivated()
        {
            _company.Properties.DataSource = new List<Company>(Services.Companies.GetAll());
            _catalog.Properties.DataSource = new List<PayrollElement>(Services.PayrollElements.GetAll(includeDisabled: false));
            SetStatus(_hasComputed ? T("Payroll_CalcDone") : T("Payroll_CalcReady"), false,
                _hasComputed ? null : T("Payroll_StatusReady"));
        }

        private string ElementName(PayrollElement element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            return L.IsRightToLeft && !string.IsNullOrWhiteSpace(element.NameAr) ? element.NameAr : element.NameFr;
        }

        private void AddCatalogElement()
        {
            if (!(_catalog.EditValue is long elementId))
            {
                return;
            }

            PayrollElement element = Services.PayrollElements.Get(elementId);
            if (element == null)
            {
                return;
            }

            _rows.Add(BuildRow(element,
                element.DefaultAmount, element.DefaultRate, element.DefaultQuantity, element.DefaultUnitPrice));
            _view.RefreshData();
            ScheduleRecompute();
        }

        /// <summary>Adds a blank free (non-catalog) element the accountant can name and fill in-grid.</summary>
        private void AddFreeElement()
        {
            if (!(_employee.EditValue is long))
            {
                SetStatus(T("Msg_SelectEmployee"), true);
                return;
            }

            var row = new EntryRow
            {
                IsManual = true,
                ElementId = 0,
                ElementType = ElementType.Gain,
                Rubrique = T("Payroll_NewElement")
            };
            _rows.Add(row);
            _view.RefreshData();

            // Focus the new line's Rubrique cell so the user can name it immediately.
            int handle = _view.GetRowHandle(_rows.Count - 1);
            _view.FocusedRowHandle = handle;
            if (_view.Columns["Rubrique"] != null)
            {
                _view.FocusedColumn = _view.Columns["Rubrique"];
            }
            ScheduleRecompute();
        }

        /// <summary>Opens the element editor to create a permanent catalogue element, then uses it.</summary>
        private void AddNewType()
        {
            using (var form = new PayrollElementEditForm(Services, new PayrollElement()))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                Result<long> result = Services.PayrollElements.Create(form.Element);
                if (!result.IsSuccess)
                {
                    UiHelper.Error(result.Error, T("Common_Error"));
                    return;
                }

                // The new element joins the catalogue dropdown and is dropped onto the sheet.
                _catalog.Properties.DataSource = new List<PayrollElement>(Services.PayrollElements.GetAll(includeDisabled: false));
                _catalog.EditValue = result.Value;
                AddCatalogElement();
            }
        }

        /// <summary>Opens the master catalogue of element types; the worksheet's
        /// dropdown is refreshed afterwards so any change is immediately available.</summary>
        private void ManageTypes()
        {
            using (var form = new PayrollElementsForm(Services))
            {
                form.ShowDialog(this);
            }

            _catalog.Properties.DataSource = new List<PayrollElement>(Services.PayrollElements.GetAll(includeDisabled: false));
        }

        private void RemoveSelectedRow()
        {
            if (_view.GetFocusedRow() is EntryRow row)
            {
                if (row.IsBaseSalary)
                {
                    return; // the base salary line cannot be removed
                }

                _rows.Remove(row);
                _view.RefreshData();
                ScheduleRecompute();
            }
        }

        public override void OnSave()
        {
            Save();
        }

        public override void OnPrint()
        {
            Print();
        }

        private void ReloadEmployees()
        {
            if (_company.EditValue is long companyId)
            {
                _employee.Properties.DataSource = new List<Employee>(Services.Employees.GetByCompany(companyId, includeInactive: false));
            }
            else
            {
                _employee.Properties.DataSource = null;
            }

            // Selecting a different company clears the employee context.
            _rows.Clear();
            _view.RefreshData();
            UpdateEmployeeCard(null);
            InvalidateComputation();
        }

        /// <summary>
        /// Rebuilds the worksheet for the selected employee: the base salary is always
        /// the first, highlighted line, followed by the employee's recurring elements.
        /// </summary>
        private void LoadElements()
        {
            if (!(_employee.EditValue is long employeeId))
            {
                SetStatus(T("Msg_SelectEmployee"), true);
                return;
            }

            Employee employee = Services.Employees.Get(employeeId);
            UpdateEmployeeCard(employee);

            // Bulk-populate without per-row repaints (no flicker).
            _grid.BeginUpdate();
            try
            {
                _rows.Clear();
                AddBaseSalaryRow(employee != null ? employee.BaseSalary : 0m);

                foreach (EmployeeElement assignment in Services.Employees.GetElements(employeeId))
                {
                    PayrollElement element = Services.PayrollElements.Get(assignment.ElementId);
                    if (element == null || element.IsDeleted || !element.IsEnabled)
                    {
                        continue;
                    }

                    _rows.Add(BuildRow(element,
                        assignment.Amount ?? element.DefaultAmount,
                        assignment.Rate ?? element.DefaultRate,
                        assignment.Quantity ?? element.DefaultQuantity,
                        assignment.UnitPrice ?? element.DefaultUnitPrice));
                }
            }
            finally
            {
                _grid.EndUpdate();
            }

            Calculate(showErrors: false);
        }

        /// <summary>Maps an element + its monthly values onto a worksheet row (per method).</summary>
        private EntryRow BuildRow(PayrollElement element, decimal? amount, decimal? rate, decimal? quantity, decimal? unitPrice)
        {
            var row = new EntryRow
            {
                ElementId = element.Id,
                ElementType = element.ElementType,
                Rubrique = ElementName(element)
            };

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (element.CalculationMethod)
            {
                case CalculationMethod.FixedAmount:
                    if (element.ElementType == ElementType.Deduction)
                    {
                        row.Retenue = amount;
                    }
                    else
                    {
                        row.Gain = amount;
                    }
                    break;
                case CalculationMethod.Percentage:
                case CalculationMethod.BaseRate:
                    if (rate.HasValue)
                    {
                        row.Taux = (rate.Value * 100m).ToString("0.##", inv) + "%";
                    }
                    break;
                case CalculationMethod.QuantityUnitPrice:
                    row.Base = quantity;
                    if (unitPrice.HasValue)
                    {
                        row.Taux = unitPrice.Value.ToString("0.##", inv);
                    }
                    break;
            }

            ComputeLine(row);
            return row;
        }

        /// <summary>
        /// Inserts the always-present "Salaire de base" line at the top. It is a
        /// display row sourced from the employee record; the engine adds the base
        /// salary intrinsically, so this row is never sent back as an element entry.
        /// </summary>
        private void AddBaseSalaryRow(decimal baseSalary)
        {
            _rows.Add(new EntryRow
            {
                IsBaseSalary = true,
                ElementId = 0,
                ElementType = ElementType.Gain,
                Rubrique = T("Payroll_BaseSalary"),
                Base = baseSalary,
                Gain = baseSalary
            });
        }

        private PayrollGenerationRequest BuildRequest()
        {
            // Commit any in-progress edit so the request always reflects the latest value.
            _view.PostEditor();
            _view.CloseEditor();
            _view.UpdateCurrentRow();

            decimal? baseSalaryOverride = null;
            var elements = new List<PayrollElementEntry>();
            foreach (EntryRow row in _rows)
            {
                if (row.IsBaseSalary)
                {
                    // The edited base salary is sent as an override input, not an element.
                    baseSalaryOverride = row.Base;
                    continue;
                }

                ComputeLine(row); // ensure the line amount reflects the latest Base/Taux

                elements.Add(new PayrollElementEntry
                {
                    ElementId = row.IsManual ? 0 : row.ElementId,
                    LineAmount = row.LineAmount,
                    IsManual = row.IsManual,
                    ManualLabel = row.Rubrique,
                    ManualType = row.ElementType
                });
            }

            // Full month by default (no proration surprise now that the worked-days
            // field is gone): worked == workable so the base salary is never prorated.
            int year = (int)_year.Value;
            int month = (int)_month.Value;
            decimal monthDays = DateTime.DaysInMonth(year, month);

            return new PayrollGenerationRequest
            {
                CompanyId = _company.EditValue is long c ? c : 0,
                EmployeeId = _employee.EditValue is long emp ? emp : 0,
                Year = year,
                Month = month,
                WorkedDays = monthDays,
                WorkableDays = monthDays,
                BaseSalaryOverride = baseSalaryOverride,
                Elements = elements
            };
        }

        /// <summary>Defers a live recompute until the current edit settles.</summary>
        private void ScheduleRecompute()
        {
            if (_recomputing || !(_employee.EditValue is long))
            {
                return;
            }

            try
            {
                BeginInvoke((Action)(() => Calculate(showErrors: false)));
            }
            catch
            {
                // The control may not have a handle yet during construction — ignore.
            }
        }

        private void InvalidateComputation()
        {
            _hasComputed = false;
            _lastResult = null;
            _lastRequest = null;
            if (_employee.EditValue is long)
            {
                ScheduleRecompute();
            }
        }

        private void Calculate(bool showErrors)
        {
            if (_recomputing)
            {
                return;
            }

            if (!(_employee.EditValue is long))
            {
                if (showErrors)
                {
                    SetStatus(T("Msg_SelectEmployee"), true);
                }
                return;
            }

            _recomputing = true;
            try
            {
                _lastRequest = BuildRequest();
                _lastResult = Services.Payroll.Preview(_lastRequest);

                if (_lastResult == null || !_lastResult.IsSuccess)
                {
                    PayrollMessage error = _lastResult != null ? _lastResult.Errors.FirstOrDefault() : null;
                    SetStatus(error != null ? error.Text : T("Common_Error"), true);
                    return;
                }

                UpdateTotals(_lastResult);
                ApplyComputedAmounts(_lastResult);
                _hasComputed = true;
                SetStatus(T("Payroll_CalcDone"), false);
            }
            finally
            {
                _recomputing = false;
            }
        }

        /// <summary>Writes the engine's computed amounts back into the worksheet (Base/Gain/Retenue).</summary>
        private void ApplyComputedAmounts(PayrollResult result)
        {
            // The base salary line (intrinsic, ElementId null) reflects the engine's
            // paid amount. Element lines are computed in the worksheet (ComputeLine), so
            // their Base/Taux/Gain are never overwritten here — the values the
            // accountant typed always stay exactly as entered.
            PayrollLineResult baseLine = result.Lines.FirstOrDefault(l => l.ElementId == null);
            EntryRow baseRow = _rows.FirstOrDefault(r => r.IsBaseSalary);
            if (baseLine != null && baseRow != null)
            {
                baseRow.Gain = baseLine.Amount;
            }

            _view.RefreshData();
        }

        private void UpdateTotals(PayrollResult result)
        {
            PayrollTotals totals = result.Totals;
            string currency = T("Common_Currency");

            // Total gains / total deductions are display aggregates derived from the
            // computed lines (the engine totals stay the single source of truth).
            decimal totalGains = result.Lines.Where(l => l.ElementType == ElementType.Gain).Sum(l => l.Amount);
            decimal elementDeductions = result.Lines.Where(l => l.ElementType == ElementType.Deduction).Sum(l => l.Amount);
            decimal totalRetenues = elementDeductions + totals.CnasEmployee + totals.Irg;

            _totalValues["Totals_Gross"].Text = UiHelper.Money(totals.SalaireBrut, currency);
            _totalValues["Totals_Gains"].Text = UiHelper.Money(totalGains, currency);
            _totalValues["Totals_Retenues"].Text = UiHelper.Money(totalRetenues, currency);
            _totalValues["Totals_Cnas"].Text = UiHelper.Money(totals.CnasEmployee, currency);
            _totalValues["Totals_Taxable"].Text = UiHelper.Money(totals.BaseImposable, currency);
            _totalValues["Totals_Irg"].Text = UiHelper.Money(totals.Irg, currency);
            // "CNAS patronale" (employer charge) is deliberately not displayed to the
            // employee; totals.CnasEmployer is still computed/stored for declarations.
            _totalValues["Totals_Net"].Text = UiHelper.Money(totals.NetSalaire, currency);
        }

        private void SetStatus(string message, bool warning, string badge = null)
        {
            if (_statusInfo != null)
            {
                _statusInfo.Text = message;
                _statusInfo.Appearance.ForeColor = warning ? UiTheme.Deduction : UiTheme.TextMuted;
                _statusInfo.Appearance.Options.UseForeColor = true;
            }

            // The header carries a compact badge with a status dot; long guidance stays
            // on the detail line beneath the totals.
            if (_headerStatus != null)
            {
                _headerStatus.Text = (warning ? "⚠  " : "●  ") + (badge ?? message);
                _headerStatus.Appearance.ForeColor = warning ? UiTheme.Deduction : UiTheme.Salary;
                _headerStatus.Appearance.Options.UseForeColor = true;
            }
        }

        private void Explain()
        {
            if (_lastResult == null || !_lastResult.IsSuccess)
            {
                Calculate(showErrors: true);
            }

            if (_lastResult == null || !_lastResult.IsSuccess)
            {
                return;
            }

            using (var form = new ExplainForm(Services, _lastResult))
            {
                form.ShowDialog(this);
            }
        }

        private void Save()
        {
            if (_lastRequest == null || _lastResult == null || !_lastResult.IsSuccess)
            {
                Calculate(showErrors: true);
            }

            if (_lastRequest == null || _lastResult == null || !_lastResult.IsSuccess)
            {
                return;
            }

            Result<long> result = Services.Payroll.Generate(_lastRequest);
            if (result.IsSuccess)
            {
                string label = _employee.Text + " — " + _lastRequest.Month.ToString("00") + "/" + _lastRequest.Year;
                new RecentItemsService(Services.Settings).Record(RecentItemsService.KindPayroll, result.Value, label);
                SetStatus(T("Payroll_StatusSaved"), false);
                UiHelper.Info(T("Msg_PayrollSaved"), T("Common_Success"));
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        /// <summary>Re-loads the current employee's elements, discarding unsaved edits.</summary>
        private void ResetWorksheet()
        {
            if (_employee.EditValue is long)
            {
                LoadElements();
            }
            else
            {
                _rows.Clear();
                _view.RefreshData();
                SetStatus(T("Payroll_CalcReady"), false, T("Payroll_StatusReady"));
            }
        }

        /// <summary>Generates the payslip preview for the current calculation.</summary>
        private void Print()
        {
            PayslipPrintModel model = BuildPrintModel();
            if (model == null)
            {
                return;
            }

            Services.Reports.Preview(model);
            SetStatus(T("Payroll_StatusPrinted"), false);
        }

        /// <summary>Exports the current payslip to a PDF file chosen by the user.</summary>
        private void ExportPdf()
        {
            PayslipPrintModel model = BuildPrintModel();
            if (model == null)
            {
                return;
            }

            string period = _lastRequest.Month.ToString("00") + "_" + _lastRequest.Year;
            using (var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "bulletin_" + period + ".pdf" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Services.Reports.ExportPdf(model, dialog.FileName);
                    SetStatus(T("Payroll_StatusPrinted"), false);
                    UiHelper.Info(T("Common_Success"), T("Common_Success"));
                }
            }
        }

        /// <summary>
        /// Builds an in-memory print model from the current calculation, computing it
        /// first if needed. Returns null when there is nothing valid to print.
        /// </summary>
        private PayslipPrintModel BuildPrintModel()
        {
            if (_lastResult == null || !_lastResult.IsSuccess)
            {
                Calculate(showErrors: true);
            }

            if (_lastResult == null || !_lastResult.IsSuccess || _lastRequest == null)
            {
                return null;
            }

            if (!(_company.EditValue is long companyId) || !(_employee.EditValue is long employeeId))
            {
                return null;
            }

            var payslip = new Payslip
            {
                EmployeeId = employeeId,
                SalaireBrut = _lastResult.Totals.SalaireBrut,
                BaseCotisable = _lastResult.Totals.BaseCotisable,
                CnasEmployee = _lastResult.Totals.CnasEmployee,
                CnasEmployer = _lastResult.Totals.CnasEmployer,
                BaseImposable = _lastResult.Totals.BaseImposable,
                IrgBrut = _lastResult.Totals.IrgBrut,
                Abattement = _lastResult.Totals.Abattement,
                Irg = _lastResult.Totals.Irg,
                NetSalaire = _lastResult.Totals.NetSalaire,
                WorkedDays = _lastRequest.WorkedDays,
                GeneratedAtUtc = DateTime.UtcNow
            };

            foreach (PayrollLineResult line in _lastResult.Lines)
            {
                payslip.Details.Add(new PayrollDetail
                {
                    ElementId = line.ElementId,
                    LabelFr = line.LabelFr,
                    LabelAr = line.LabelAr,
                    ElementType = line.ElementType,
                    Base = line.Base,
                    Rate = line.Rate,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Amount = line.Amount,
                    IsCnasApplicable = line.IsCnasApplicable,
                    IsIrgApplicable = line.IsIrgApplicable,
                    DisplayOrder = line.DisplayOrder
                });
            }

            return new PayslipPrintModel
            {
                Company = Services.Companies.Get(companyId),
                Employee = Services.Employees.Get(employeeId),
                Payslip = payslip,
                Lines = payslip.Details.ToList(),
                LanguageCode = Services.Localization.CurrentLanguage,
                PeriodYear = _lastRequest.Year,
                PeriodMonth = _lastRequest.Month
            };
        }

        /// <summary>
        /// A worksheet row. The visible columns mirror a payslip; the technical fields
        /// (element id, type, method, carried quantity) are private to the row and are
        /// never shown — they exist only to rebuild the engine request.
        /// </summary>
        public sealed class EntryRow
        {
            [Browsable(false)] public bool IsBaseSalary { get; set; }
            [Browsable(false)] public bool IsManual { get; set; }
            [Browsable(false)] public long ElementId { get; set; }
            [Browsable(false)] public ElementType ElementType { get; set; }
            [Browsable(false)] public decimal? LineAmount { get; set; }

            public string Rubrique { get; set; }
            public decimal? Base { get; set; }
            public string Taux { get; set; }   // "10" = ×10, "10%" = 10 percent
            public decimal? Gain { get; set; }
            public decimal? Retenue { get; set; }
            public string Observations { get; set; }
        }
    }
}
