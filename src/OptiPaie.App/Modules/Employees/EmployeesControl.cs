using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Modules.Employees
{
    /// <summary>Employee management, filtered by company.</summary>
    public sealed class EmployeesControl : BaseModuleControl
    {
        private PanelControl _toolbar;
        private LabelControl _companyLabel;
        private LookUpEdit _companyFilter;
        private SimpleButton _addButton;
        private SimpleButton _editButton;
        private SimpleButton _deleteButton;
        private SimpleButton _elementsButton;
        private SimpleButton _refreshButton;
        private GridControl _grid;
        private GridView _view;
        private EmptyStatePanel _emptyState;
        private LabelControl _pageTitle;
        private LabelControl _pageSubtitle;

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 56 };
            UiTheme.Toolbar(_toolbar);
            _toolbar.Resize += (s, e) => LayoutToolbar();
            Controls.Add(_grid = new GridControl { Dock = DockStyle.Fill });
            Controls.Add(_toolbar);

            _companyLabel = new LabelControl { Location = new Point(16, 20), AutoSizeMode = LabelAutoSizeMode.None, Width = 84 };
            UiTheme.FieldCaption(_companyLabel);
            _toolbar.Controls.Add(_companyLabel);

            _companyFilter = new LookUpEdit { Location = new Point(104, 15), Width = 240 };
            UiTheme.ConfigureLookup(_companyFilter, "NameFr", "Id");
            _companyFilter.EditValueChanged += (s, e) => LoadData();
            _toolbar.Controls.Add(_companyFilter);

            _addButton = UiTheme.PrimaryButton(AddButton(360, 130, () => AddNew()));
            _editButton = UiTheme.SecondaryButton(AddButton(496, 130, () => EditSelected()));
            _deleteButton = UiTheme.DangerButton(AddButton(632, 130, () => DeleteSelected()));
            _elementsButton = UiTheme.SecondaryButton(AddButton(768, 160, () => ManageElements()));
            _refreshButton = UiTheme.SecondaryButton(AddButton(934, 130, () => LoadData()));

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_view);
            _view.DoubleClick += (s, e) => EditSelected();

            _view.Columns.AddVisible("LastNameFr");
            _view.Columns.AddVisible("FirstNameFr");
            _view.Columns.AddVisible("Poste");
            _view.Columns.AddVisible("BaseSalary");
            _view.Columns.AddVisible("IsActive");

            _emptyState = new EmptyStatePanel { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_emptyState);
            _emptyState.Configure(T("Empty_NoEmployees"), T("Common_Add"), () => AddNew());

            AddPageHeader(out _pageTitle, out _pageSubtitle);
        }

        private SimpleButton AddButton(int x, int width, System.Action onClick)
        {
            var button = new SimpleButton { Location = new Point(x, 12), Width = width, Height = UiTheme.ButtonHeight };
            button.Click += (s, e) => onClick();
            _toolbar.Controls.Add(button);
            return button;
        }

        public override void Localize()
        {
            _pageTitle.Text = T("Module_Employees");
            _pageSubtitle.Text = T("Employees_Subtitle");

            _companyLabel.Text = T("Employee_Company");
            _addButton.Text = T("Common_Add");
            _editButton.Text = T("Common_Edit");
            _deleteButton.Text = T("Common_Delete");
            _elementsButton.Text = T("Employee_Elements");
            _refreshButton.Text = T("Common_Refresh");

            _view.Columns["LastNameFr"].Caption = T("Employee_LastName");
            _view.Columns["FirstNameFr"].Caption = T("Employee_FirstName");
            _view.Columns["Poste"].Caption = T("Employee_Poste");
            _view.Columns["BaseSalary"].Caption = T("Employee_BaseSalary");
            _view.Columns["IsActive"].Caption = T("Employee_Active");

            // Size every button to its caption in the active language (no clipping),
            // then position the whole toolbar from the leading edge (fr: left, ar: right).
            foreach (SimpleButton button in new[] { _addButton, _editButton, _deleteButton, _elementsButton, _refreshButton })
            {
                UiTheme.FitButton(button, 110);
            }

            LayoutToolbar();
        }

        private void LayoutToolbar()
        {
            if (_companyFilter == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int w = _toolbar.Width;
            _companyLabel.Location = new Point(UiTheme.LeadX(w, 16, _companyLabel.Width, rtl), 20);
            _companyFilter.Location = new Point(UiTheme.LeadX(w, 104, _companyFilter.Width, rtl), 15);

            int bx = 360;
            foreach (SimpleButton button in new[] { _addButton, _editButton, _deleteButton, _elementsButton, _refreshButton })
            {
                button.Location = new Point(UiTheme.LeadX(w, bx, button.Width, rtl), 12);
                bx += button.Width + UiTheme.Gap;
            }
        }

        public override void OnActivated()
        {
            _companyFilter.Properties.DataSource = new List<Company>(Services.Companies.GetAll());
            LoadData();
        }

        public override void OnNew()
        {
            AddNew();
        }

        public override void OnDelete()
        {
            DeleteSelected();
        }

        private long? SelectedCompanyId()
        {
            if (_companyFilter.EditValue is long id)
            {
                return id;
            }

            return null;
        }

        private void LoadData()
        {
            long? companyId = SelectedCompanyId();
            List<Employee> list = companyId.HasValue
                ? new List<Employee>(Services.Employees.GetByCompany(companyId.Value))
                : new List<Employee>();

            _grid.DataSource = list;

            _emptyState.Visible = list.Count == 0;
            if (_emptyState.Visible)
            {
                _emptyState.BringToFront();
            }
        }

        private Employee Selected()
        {
            return _view.GetFocusedRow() as Employee;
        }

        private void AddNew()
        {
            long? companyId = SelectedCompanyId();
            if (!companyId.HasValue)
            {
                UiHelper.Error(T("Msg_SelectCompany"), T("Common_Error"));
                return;
            }

            var employee = new Employee { CompanyId = companyId.Value, IsActive = true, HireDate = System.DateTime.Today };
            using (var form = new EmployeeEditForm(Services, employee))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    HandleResult(Services.Employees.Create(form.Employee));
                }
            }
        }

        private void EditSelected()
        {
            Employee employee = Selected();
            if (employee == null)
            {
                return;
            }

            using (var form = new EmployeeEditForm(Services, employee))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    HandleResult(Services.Employees.Update(form.Employee));
                }
            }
        }

        private void DeleteSelected()
        {
            Employee employee = Selected();
            if (employee == null)
            {
                return;
            }

            if (UiHelper.Confirm(T("Msg_DeleteConfirm"), T("Common_Confirm")))
            {
                HandleResult(Services.Employees.Delete(employee.Id));
            }
        }

        private void ManageElements()
        {
            Employee employee = Selected();
            if (employee == null)
            {
                UiHelper.Error(T("Msg_SelectEmployee"), T("Common_Error"));
                return;
            }

            using (var form = new EmployeeElementsForm(Services, employee))
            {
                form.ShowDialog(this);
            }
        }

        private void HandleResult(Result result)
        {
            if (result.IsSuccess)
            {
                LoadData();
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }
    }
}
