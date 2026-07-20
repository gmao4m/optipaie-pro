using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Localization;

namespace OptiPaie.App.Modules.Employees
{
    /// <summary>Create/edit dialog for an <see cref="Employee"/>.</summary>
    public sealed class EmployeeEditForm : XtraForm
    {
        private readonly AppServices _services;

        private LookUpEdit _company;
        private TextEdit _lastNameFr;
        private TextEdit _lastNameAr;
        private TextEdit _firstNameFr;
        private TextEdit _firstNameAr;
        private ImageComboBoxEdit _gender;
        private DateEdit _birthDate;
        private DateEdit _hireDate;
        private DateEdit _exitDate;
        private ImageComboBoxEdit _contractType;
        private ImageComboBoxEdit _maritalStatus;
        private TextEdit _category;
        private TextEdit _poste;
        private TextEdit _nss;
        private TextEdit _nationalId;
        private SpinEdit _baseSalary;
        private ImageComboBoxEdit _paymentMode;
        private TextEdit _rib;
        private SpinEdit _dependents;
        private CheckEdit _isActive;

        public Employee Employee { get; }

        public EmployeeEditForm(AppServices services, Employee employee)
        {
            _services = services;
            Employee = employee;
            BuildUi();
            LoadFrom(employee);
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = _services.Localization.GetString("Module_Employees");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(580, 820);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new LayoutControl { Dock = DockStyle.Fill };
            Controls.Add(layout);
            LayoutControlGroup root = layout.Root;

            // Organise the record into labelled sections (identity, contract, job, payment).
            LayoutControlGroup gIdentity = root.AddGroup(T("Employee_GroupIdentity"));
            _company = new LookUpEdit();
            _company.Properties.DataSource = new List<Company>(_services.Companies.GetAll());
            UiTheme.ConfigureLookup(_company, "NameFr", "Id");
            gIdentity.AddItem(T("Employee_Company"), _company);

            _lastNameFr = AddText(gIdentity, T("Employee_LastName") + " (FR)");
            _lastNameAr = AddText(gIdentity, T("Employee_LastName") + " (AR)");
            _firstNameFr = AddText(gIdentity, T("Employee_FirstName") + " (FR)");
            _firstNameAr = AddText(gIdentity, T("Employee_FirstName") + " (AR)");

            _gender = new ImageComboBoxEdit();
            _gender.Properties.Items.Add(new ImageComboBoxItem(T("Gender_Male"), Gender.Male));
            _gender.Properties.Items.Add(new ImageComboBoxItem(T("Gender_Female"), Gender.Female));
            UiTheme.StyleInput(_gender);
            gIdentity.AddItem(T("Employee_Gender"), _gender);

            LayoutControlGroup gContract = root.AddGroup(T("Employee_GroupContract"));
            _birthDate = AddDate(gContract, T("Employee_BirthDate"));
            _hireDate = AddDate(gContract, T("Employee_HireDate"));
            _exitDate = AddDate(gContract, T("Employee_ExitDate"));
            _contractType = AddEnumCombo(gContract, T("Employee_ContractType"), Enum.GetValues(typeof(ContractType)), _services.Localization);
            _maritalStatus = AddEnumCombo(gContract, T("Employee_MaritalStatus"), Enum.GetValues(typeof(MaritalStatus)), _services.Localization);

            LayoutControlGroup gJob = root.AddGroup(T("Employee_GroupJob"));
            _category = AddText(gJob, T("Employee_Category"));
            _poste = AddText(gJob, T("Employee_Poste"));
            _nss = AddText(gJob, T("Employee_Nss"));
            _nationalId = AddText(gJob, T("Employee_NationalId"));
            _baseSalary = AddSpin(gJob, T("Employee_BaseSalary"), 100000000m, true);
            _dependents = AddSpin(gJob, T("Employee_Dependents"), 50m, false);

            LayoutControlGroup gPayment = root.AddGroup(T("Employee_GroupPayment"));
            _paymentMode = AddEnumCombo(gPayment, T("Employee_PaymentMode"), Enum.GetValues(typeof(PaymentMode)), _services.Localization);
            _rib = AddText(gPayment, T("Employee_Rib"));
            _isActive = new CheckEdit { Text = T("Employee_Active") };
            gPayment.AddItem(string.Empty, _isActive);

            var buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 56 };
            UiTheme.Toolbar(buttons);
            Controls.Add(buttons);

            var save = new SimpleButton { Text = T("Common_Save"), Width = 140, Height = UiTheme.ButtonHeight, Location = new Point(16, 12) };
            UiTheme.PrimaryButton(save);
            UiTheme.FitButton(save, 140);
            save.Click += Save_Click;
            buttons.Controls.Add(save);

            var cancel = new SimpleButton { Text = T("Common_Cancel"), Width = 130, Height = UiTheme.ButtonHeight, Location = new Point(166, 12) };
            UiTheme.SecondaryButton(cancel);
            UiTheme.FitButton(cancel, 130);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private static TextEdit AddText(LayoutControlGroup group, string caption)
        {
            var edit = new TextEdit();
            UiTheme.StyleInput(edit);
            group.AddItem(caption, edit);
            return edit;
        }

        private static DateEdit AddDate(LayoutControlGroup group, string caption)
        {
            var edit = new DateEdit();
            edit.Properties.AllowNullInput = DevExpress.Utils.DefaultBoolean.True;
            edit.EditValue = null;
            UiTheme.StyleInput(edit);
            group.AddItem(caption, edit);
            return edit;
        }

        private static SpinEdit AddSpin(LayoutControlGroup group, string caption, decimal max, bool isFloat)
        {
            var edit = new SpinEdit();
            edit.Properties.MinValue = 0m;
            edit.Properties.MaxValue = max;
            edit.Properties.IsFloatValue = isFloat;
            UiTheme.StyleInput(edit);
            group.AddItem(caption, edit);
            return edit;
        }

        private static ImageComboBoxEdit AddEnumCombo(LayoutControlGroup group, string caption, Array values, ILocalizationService localization)
        {
            var combo = new ImageComboBoxEdit();
            foreach (object value in values)
            {
                combo.Properties.Items.Add(new ImageComboBoxItem(EnumLocalizer.Localize(localization, (Enum)value), value));
            }

            UiTheme.StyleInput(combo);
            group.AddItem(caption, combo);
            return combo;
        }

        private void LoadFrom(Employee e)
        {
            if (e.CompanyId > 0)
            {
                _company.EditValue = e.CompanyId;
            }

            _lastNameFr.Text = e.LastNameFr;
            _lastNameAr.Text = e.LastNameAr;
            _firstNameFr.Text = e.FirstNameFr;
            _firstNameAr.Text = e.FirstNameAr;
            _gender.EditValue = e.Gender == 0 ? Gender.Male : e.Gender;
            _birthDate.EditValue = e.BirthDate;
            _hireDate.EditValue = e.HireDate == default(DateTime) ? (object)DateTime.Today : e.HireDate;
            _exitDate.EditValue = e.ExitDate;
            _contractType.EditValue = e.ContractType == 0 ? ContractType.Cdi : e.ContractType;
            _maritalStatus.EditValue = e.MaritalStatus == 0 ? MaritalStatus.Single : e.MaritalStatus;
            _category.Text = e.Category;
            _poste.Text = e.Poste;
            _nss.Text = e.Nss;
            _nationalId.Text = e.NationalId;
            _baseSalary.Value = e.BaseSalary;
            _paymentMode.EditValue = e.PaymentMode == 0 ? PaymentMode.BankTransfer : e.PaymentMode;
            _rib.Text = e.Rib;
            _dependents.Value = e.Dependents;
            _isActive.Checked = e.IsActive;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!(_company.EditValue is long companyId) || companyId <= 0)
            {
                UiHelper.Error(T("Msg_SelectCompany"), T("Common_Error"));
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastNameFr.Text) || string.IsNullOrWhiteSpace(_firstNameFr.Text))
            {
                UiHelper.Error(T("Employee_LastName"), T("Common_Error"));
                return;
            }

            Employee.CompanyId = companyId;
            Employee.LastNameFr = _lastNameFr.Text.Trim();
            Employee.LastNameAr = _lastNameAr.Text.Trim();
            Employee.FirstNameFr = _firstNameFr.Text.Trim();
            Employee.FirstNameAr = _firstNameAr.Text.Trim();
            Employee.Gender = (Gender)_gender.EditValue;
            Employee.BirthDate = _birthDate.EditValue as DateTime?;
            Employee.HireDate = _hireDate.EditValue is DateTime hire ? hire : DateTime.Today;
            Employee.ExitDate = _exitDate.EditValue as DateTime?;
            Employee.ContractType = (ContractType)_contractType.EditValue;
            Employee.MaritalStatus = (MaritalStatus)_maritalStatus.EditValue;
            Employee.Category = _category.Text.Trim();
            Employee.Poste = _poste.Text.Trim();
            Employee.Nss = _nss.Text.Trim();
            Employee.NationalId = _nationalId.Text.Trim();
            Employee.BaseSalary = _baseSalary.Value;
            Employee.PaymentMode = (PaymentMode)_paymentMode.EditValue;
            Employee.Rib = _rib.Text.Trim();
            Employee.Dependents = (int)_dependents.Value;
            Employee.IsActive = _isActive.Checked;

            DialogResult = DialogResult.OK;
        }
    }
}
