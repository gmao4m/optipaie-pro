using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;

namespace OptiPaie.App.Modules.Employees
{
    /// <summary>Assigns a payroll element to an employee (with optional override values).</summary>
    public sealed class EmployeeElementEditForm : XtraForm
    {
        private readonly AppServices _services;
        private readonly bool _isNew;

        private LookUpEdit _element;
        private TextEdit _amount;
        private TextEdit _rate;
        private TextEdit _quantity;
        private TextEdit _unitPrice;

        public EmployeeElement Assignment { get; }

        public EmployeeElementEditForm(AppServices services, EmployeeElement assignment, bool isNew)
        {
            _services = services;
            _isNew = isNew;
            Assignment = assignment;
            BuildUi();
            LoadFrom(assignment);
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = _services.Localization.GetString("Employee_Elements");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(440, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new LayoutControl { Dock = DockStyle.Fill };
            Controls.Add(layout);

            _element = new LookUpEdit();
            _element.Properties.DataSource = new List<PayrollElement>(_services.PayrollElements.GetAll(includeDisabled: false));
            UiTheme.ConfigureLookup(_element, "NameFr", "Id");
            _element.Enabled = _isNew;
            layout.AddItem(T("Payroll_Element"), _element);

            _amount = AddText(layout, T("Payroll_Amount"));
            _rate = AddText(layout, T("Payroll_Rate"));
            _quantity = AddText(layout, T("Payroll_Quantity"));
            _unitPrice = AddText(layout, T("Payroll_UnitPrice"));

            var buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 56 };
            UiTheme.Toolbar(buttons);
            Controls.Add(buttons);

            var save = new SimpleButton { Text = T("Common_Save"), Width = 130, Height = UiTheme.ButtonHeight, Location = new Point(16, 12) };
            UiTheme.PrimaryButton(save);
            save.Click += Save_Click;
            buttons.Controls.Add(save);

            var cancel = new SimpleButton { Text = T("Common_Cancel"), Width = 120, Height = UiTheme.ButtonHeight, Location = new Point(156, 12) };
            UiTheme.SecondaryButton(cancel);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private static TextEdit AddText(LayoutControl layout, string caption)
        {
            var edit = new TextEdit();
            layout.AddItem(caption, edit);
            return edit;
        }

        private void LoadFrom(EmployeeElement a)
        {
            if (a.ElementId > 0)
            {
                _element.EditValue = a.ElementId;
            }

            _amount.Text = Format(a.Amount);
            _rate.Text = Format(a.Rate);
            _quantity.Text = Format(a.Quantity);
            _unitPrice.Text = Format(a.UnitPrice);
        }

        private static string Format(decimal? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static decimal? ParseNullable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return decimal.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : (decimal?)null;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (_isNew && !(_element.EditValue is long))
            {
                UiHelper.Error(T("Msg_NoSelection"), T("Common_Error"));
                return;
            }

            if (_isNew)
            {
                Assignment.ElementId = (long)_element.EditValue;
            }

            Assignment.Amount = ParseNullable(_amount.Text);
            Assignment.Rate = ParseNullable(_rate.Text);
            Assignment.Quantity = ParseNullable(_quantity.Text);
            Assignment.UnitPrice = ParseNullable(_unitPrice.Text);
            Assignment.IsActive = true;

            DialogResult = DialogResult.OK;
        }
    }
}
