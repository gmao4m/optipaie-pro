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
    /// <summary>
    /// Manages the payroll elements permanently assigned to an employee. These are
    /// what the Payroll Playground loads automatically each month.
    /// </summary>
    public sealed class EmployeeElementsForm : XtraForm
    {
        private readonly AppServices _services;
        private readonly Employee _employee;

        private PanelControl _toolbar;
        private SimpleButton _addButton;
        private SimpleButton _editButton;
        private SimpleButton _removeButton;
        private SimpleButton _closeButton;
        private GridControl _grid;
        private GridView _view;

        public EmployeeElementsForm(AppServices services, Employee employee)
        {
            _services = services;
            _employee = employee;
            BuildUi();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("Employee_ElementsTitle");
            LoadData();
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 520);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 56 };
            UiTheme.Toolbar(_toolbar);
            _grid = new GridControl { Dock = DockStyle.Fill };
            Controls.Add(_grid);
            Controls.Add(_toolbar);

            _addButton = UiTheme.PrimaryButton(Button(16, () => AddNew()));
            _editButton = UiTheme.SecondaryButton(Button(156, () => EditSelected()));
            _removeButton = UiTheme.DangerButton(Button(296, () => RemoveSelected()));
            _closeButton = UiTheme.SecondaryButton(Button(436, () => Close()));

            _addButton.Text = T("Common_Add");
            _editButton.Text = T("Common_Edit");
            _removeButton.Text = T("Common_Delete");
            _closeButton.Text = T("Common_Close");

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_view);
            _view.DoubleClick += (s, e) => EditSelected();
        }

        private SimpleButton Button(int x, System.Action onClick)
        {
            var button = new SimpleButton { Location = new Point(x, 12), Width = 130, Height = UiTheme.ButtonHeight };
            button.Click += (s, e) => onClick();
            _toolbar.Controls.Add(button);
            return button;
        }

        private void LoadData()
        {
            var rows = new List<AssignedRow>();
            foreach (EmployeeElement assignment in _services.Employees.GetElements(_employee.Id))
            {
                PayrollElement element = _services.PayrollElements.Get(assignment.ElementId);
                rows.Add(new AssignedRow
                {
                    Id = assignment.Id,
                    ElementId = assignment.ElementId,
                    Name = element != null ? element.NameFr : T("Common_None"),
                    TypeText = element != null ? EnumLocalizer.Localize(_services.Localization, element.ElementType) : string.Empty,
                    Amount = assignment.Amount,
                    Rate = assignment.Rate,
                    Quantity = assignment.Quantity,
                    UnitPrice = assignment.UnitPrice
                });
            }

            _grid.DataSource = rows;
            _view.PopulateColumns();
            _view.Columns["Id"].Visible = false;
            _view.Columns["ElementId"].Visible = false;
            _view.Columns["Name"].Caption = T("Payroll_Element");
            _view.Columns["TypeText"].Caption = T("Payroll_Type");
            _view.Columns["Amount"].Caption = T("Payroll_Amount");
            _view.Columns["Rate"].Caption = T("Payroll_Rate");
            _view.Columns["Quantity"].Caption = T("Payroll_Quantity");
            _view.Columns["UnitPrice"].Caption = T("Payroll_UnitPrice");
        }

        private AssignedRow Selected()
        {
            return _view.GetFocusedRow() as AssignedRow;
        }

        private void AddNew()
        {
            var assignment = new EmployeeElement { EmployeeId = _employee.Id, IsActive = true };
            using (var form = new EmployeeElementEditForm(_services, assignment, true))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Result<long> result = _services.Employees.AssignElement(form.Assignment);
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

        private void EditSelected()
        {
            AssignedRow row = Selected();
            if (row == null)
            {
                return;
            }

            var assignment = new EmployeeElement
            {
                Id = row.Id,
                EmployeeId = _employee.Id,
                ElementId = row.ElementId,
                Amount = row.Amount,
                Rate = row.Rate,
                Quantity = row.Quantity,
                UnitPrice = row.UnitPrice,
                IsActive = true
            };

            using (var form = new EmployeeElementEditForm(_services, assignment, false))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Result result = _services.Employees.UpdateElement(form.Assignment);
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

        private void RemoveSelected()
        {
            AssignedRow row = Selected();
            if (row == null)
            {
                return;
            }

            if (UiHelper.Confirm(T("Msg_DeleteConfirm"), T("Common_Confirm")))
            {
                Result result = _services.Employees.RemoveElement(row.Id);
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

        private sealed class AssignedRow
        {
            public long Id { get; set; }
            public long ElementId { get; set; }
            public string Name { get; set; }
            public string TypeText { get; set; }
            public decimal? Amount { get; set; }
            public decimal? Rate { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? UnitPrice { get; set; }
        }
    }
}
