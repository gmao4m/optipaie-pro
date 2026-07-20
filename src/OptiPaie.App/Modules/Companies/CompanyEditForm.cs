using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;

namespace OptiPaie.App.Modules.Companies
{
    /// <summary>Create/edit dialog for a <see cref="Company"/>.</summary>
    public sealed class CompanyEditForm : XtraForm
    {
        private readonly AppServices _services;

        private TextEdit _nameFr;
        private TextEdit _nameAr;
        private TextEdit _legalForm;
        private TextEdit _addressFr;
        private TextEdit _addressAr;
        private TextEdit _phone;
        private TextEdit _email;
        private TextEdit _nif;
        private TextEdit _nis;
        private TextEdit _rc;
        private TextEdit _articleImposition;
        private TextEdit _cnasEmployerNumber;
        private TextEdit _cacobatph;
        private TextEdit _bank;
        private TextEdit _bankAccount;
        private TextEdit _currency;
        private PictureEdit _logo;
        private byte[] _logoBytes;

        /// <summary>The edited company (the same instance passed in).</summary>
        public Company Company { get; }

        public CompanyEditForm(AppServices services, Company company, bool isNew)
        {
            _services = services;
            Company = company;
            BuildUi();
            LoadFrom(company);
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = _services.Localization.GetString("Module_Companies");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(580, 880);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new LayoutControl { Dock = DockStyle.Fill };
            Controls.Add(layout);
            LayoutControlGroup root = layout.Root;

            // Group the fields into labelled sections so a long form reads as an
            // organised, premium record rather than a flat wall of inputs.
            LayoutControlGroup gIdentity = root.AddGroup(T("Company_GroupIdentity"));
            _nameFr = AddField(gIdentity, T("Company_NameFr"));
            _nameAr = AddField(gIdentity, T("Company_NameAr"));
            _legalForm = AddField(gIdentity, T("Company_LegalForm"));

            LayoutControlGroup gContact = root.AddGroup(T("Company_GroupContact"));
            _addressFr = AddField(gContact, T("Company_AddressFr"));
            _addressAr = AddField(gContact, T("Company_AddressAr"));
            _phone = AddField(gContact, T("Company_Phone"));
            _email = AddField(gContact, T("Company_Email"));

            LayoutControlGroup gLegal = root.AddGroup(T("Company_GroupLegal"));
            _nif = AddField(gLegal, T("Company_Nif"));
            _nis = AddField(gLegal, T("Company_Nis"));
            _rc = AddField(gLegal, T("Company_Rc"));
            _articleImposition = AddField(gLegal, T("Company_ArticleImposition"));
            _cnasEmployerNumber = AddField(gLegal, T("Company_CnasEmployerNumber"));
            _cacobatph = AddField(gLegal, T("Wizard_Cacobatph"));

            LayoutControlGroup gBank = root.AddGroup(T("Company_GroupBank"));
            _bank = AddField(gBank, T("Wizard_Bank"));
            _bankAccount = AddField(gBank, T("Wizard_BankAccount"));
            _currency = AddField(gBank, T("Wizard_Currency"));

            _logo = new PictureEdit { Height = 120 };
            _logo.Properties.SizeMode = PictureSizeMode.Zoom;
            _logo.Properties.ShowMenu = false;
            gBank.AddItem(T("Company_Logo"), _logo);

            var uploadButton = new SimpleButton { Text = T("Company_UploadLogo") };
            UiTheme.SecondaryButton(uploadButton);
            uploadButton.Click += (s, e) => UploadLogo();
            gBank.AddItem(string.Empty, uploadButton);

            var clearLogoButton = new SimpleButton { Text = T("Company_ClearLogo") };
            UiTheme.SecondaryButton(clearLogoButton);
            clearLogoButton.Click += (s, e) => ClearLogo();
            gBank.AddItem(string.Empty, clearLogoButton);

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

        private static TextEdit AddField(LayoutControlGroup group, string caption)
        {
            var edit = new TextEdit();
            UiTheme.StyleInput(edit);
            group.AddItem(caption, edit);
            return edit;
        }

        private void LoadFrom(Company c)
        {
            _nameFr.Text = c.NameFr;
            _nameAr.Text = c.NameAr;
            _legalForm.Text = c.LegalForm;
            _addressFr.Text = c.AddressFr;
            _addressAr.Text = c.AddressAr;
            _phone.Text = c.Phone;
            _email.Text = c.Email;
            _nif.Text = c.Nif;
            _nis.Text = c.Nis;
            _rc.Text = c.Rc;
            _articleImposition.Text = c.ArticleImposition;
            _cnasEmployerNumber.Text = c.CnasEmployerNumber;
            _cacobatph.Text = c.Cacobatph;
            _bank.Text = c.Bank;
            _bankAccount.Text = c.BankAccount;
            _currency.Text = string.IsNullOrWhiteSpace(c.Currency) ? "DZD" : c.Currency;

            _logoBytes = c.Logo;
            if (c.Logo != null && c.Logo.Length > 0)
            {
                try
                {
                    _logo.Image = Image.FromStream(new MemoryStream(c.Logo));
                }
                catch
                {
                    _logo.Image = null;
                }
            }
        }

        private void UploadLogo()
        {
            using (var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    _logoBytes = File.ReadAllBytes(dialog.FileName);
                    _logo.Image = Image.FromStream(new MemoryStream(_logoBytes));
                }
                catch
                {
                    UiHelper.Error(T("Common_Error"), T("Common_Error"));
                }
            }
        }

        private void ClearLogo()
        {
            _logoBytes = null;
            _logo.Image = null;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameFr.Text))
            {
                UiHelper.Error(T("Company_NameFr"), T("Common_Error"));
                return;
            }

            Company.NameFr = _nameFr.Text.Trim();
            Company.NameAr = _nameAr.Text.Trim();
            Company.LegalForm = _legalForm.Text.Trim();
            Company.AddressFr = _addressFr.Text.Trim();
            Company.AddressAr = _addressAr.Text.Trim();
            Company.Phone = _phone.Text.Trim();
            Company.Email = _email.Text.Trim();
            Company.Nif = _nif.Text.Trim();
            Company.Nis = _nis.Text.Trim();
            Company.Rc = _rc.Text.Trim();
            Company.ArticleImposition = _articleImposition.Text.Trim();
            Company.CnasEmployerNumber = _cnasEmployerNumber.Text.Trim();
            Company.Cacobatph = _cacobatph.Text.Trim();
            Company.Bank = _bank.Text.Trim();
            Company.BankAccount = _bankAccount.Text.Trim();
            Company.Currency = string.IsNullOrWhiteSpace(_currency.Text) ? "DZD" : _currency.Text.Trim();
            Company.Logo = _logoBytes;

            DialogResult = DialogResult.OK;
        }
    }
}
