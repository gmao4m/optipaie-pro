using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;

namespace OptiPaie.App.Wizard
{
    /// <summary>
    /// First-run wizard shown when no company exists yet. Collects the first
    /// company's legal/banking identity and the default language, then creates it.
    /// The application is not usable until this completes.
    /// </summary>
    public sealed class FirstRunWizardForm : XtraForm
    {
        private readonly AppServices _services;

        private TextEdit _nameFr;
        private TextEdit _nameAr;
        private TextEdit _nif;
        private TextEdit _nis;
        private TextEdit _rc;
        private TextEdit _articleImposition;
        private TextEdit _address;
        private TextEdit _phone;
        private TextEdit _email;
        private TextEdit _cnas;
        private TextEdit _cacobatph;
        private TextEdit _bank;
        private TextEdit _bankAccount;
        private TextEdit _currency;
        private ImageComboBoxEdit _language;
        private PictureEdit _logo;
        private byte[] _logoBytes;

        public FirstRunWizardForm(AppServices services)
        {
            _services = services;
            BuildUi();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("Wizard_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 860);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;

            var accent = new PanelControl { Dock = DockStyle.Top, Height = 4, BorderStyle = BorderStyles.NoBorder };
            accent.Appearance.BackColor = UiTheme.Primary;
            accent.Appearance.Options.UseBackColor = true;
            Controls.Add(accent);

            var header = new PanelControl { Dock = DockStyle.Top, Height = 64 };
            UiTheme.Toolbar(header);
            Controls.Add(header);
            var welcome = new LabelControl { Location = new Point(16, 12), AutoSizeMode = LabelAutoSizeMode.None, Width = 520 };
            welcome.Appearance.Font = new Font(UiTheme.FontName, 13.5F, FontStyle.Bold);
            welcome.Appearance.ForeColor = UiTheme.Primary;
            welcome.Appearance.Options.UseForeColor = true;
            welcome.Text = T("Wizard_Welcome");
            header.Controls.Add(welcome);
            var intro = new LabelControl { Location = new Point(16, 40), AutoSizeMode = LabelAutoSizeMode.None, Width = 520 };
            UiTheme.Muted(intro);
            intro.Text = T("Wizard_Intro");
            header.Controls.Add(intro);

            var layout = new LayoutControl { Dock = DockStyle.Fill };
            Controls.Add(layout);
            Controls.SetChildIndex(layout, 0);

            _nameFr = AddText(layout, T("Company_NameFr"));
            _nameAr = AddText(layout, T("Company_NameAr"));
            _nif = AddText(layout, T("Company_Nif"));
            _nis = AddText(layout, T("Company_Nis"));
            _rc = AddText(layout, T("Company_Rc"));
            _articleImposition = AddText(layout, T("Company_ArticleImposition"));
            _address = AddText(layout, T("Company_AddressFr"));
            _phone = AddText(layout, T("Company_Phone"));
            _email = AddText(layout, T("Company_Email"));
            _cnas = AddText(layout, T("Company_CnasEmployerNumber"));
            _cacobatph = AddText(layout, T("Wizard_Cacobatph"));
            _bank = AddText(layout, T("Wizard_Bank"));
            _bankAccount = AddText(layout, T("Wizard_BankAccount"));
            _currency = AddText(layout, T("Wizard_Currency"));
            _currency.Text = "DZD";

            _language = new ImageComboBoxEdit();
            _language.Properties.Items.Add(new ImageComboBoxItem("Français", "fr"));
            _language.Properties.Items.Add(new ImageComboBoxItem("العربية", "ar"));
            _language.EditValue = _services.Localization.CurrentLanguage;
            UiTheme.StyleInput(_language);
            layout.AddItem(T("Wizard_DefaultLanguage"), _language);

            _logo = new PictureEdit { Height = 110 };
            _logo.Properties.SizeMode = PictureSizeMode.Zoom;
            _logo.Properties.ShowMenu = false;
            layout.AddItem(T("Company_Logo"), _logo);
            var upload = new SimpleButton { Text = T("Company_UploadLogo") };
            UiTheme.SecondaryButton(upload);
            upload.Click += (s, e) => UploadLogo();
            layout.AddItem(string.Empty, upload);

            var buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 56 };
            UiTheme.Toolbar(buttons);
            Controls.Add(buttons);
            var finish = new SimpleButton { Text = T("Common_Finish"), Width = 180, Height = UiTheme.ButtonHeight, Location = new Point(16, 12) };
            UiTheme.PrimaryButton(finish);
            UiTheme.FitButton(finish, 180);
            finish.Click += Finish_Click;
            buttons.Controls.Add(finish);
            AcceptButton = finish;
        }

        private static TextEdit AddText(LayoutControl layout, string caption)
        {
            var edit = new TextEdit();
            UiTheme.StyleInput(edit);
            layout.AddItem(caption, edit);
            return edit;
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

        private void Finish_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameFr.Text))
            {
                UiHelper.Error(T("Company_NameFr"), T("Common_Error"));
                return;
            }

            string language = _language.EditValue as string ?? "fr";
            _services.Settings.SetLanguage(language);
            _services.Localization.SetLanguage(language);

            var company = new Company
            {
                NameFr = _nameFr.Text.Trim(),
                NameAr = _nameAr.Text.Trim(),
                Nif = _nif.Text.Trim(),
                Nis = _nis.Text.Trim(),
                Rc = _rc.Text.Trim(),
                ArticleImposition = _articleImposition.Text.Trim(),
                AddressFr = _address.Text.Trim(),
                Phone = _phone.Text.Trim(),
                Email = _email.Text.Trim(),
                CnasEmployerNumber = _cnas.Text.Trim(),
                Cacobatph = _cacobatph.Text.Trim(),
                Bank = _bank.Text.Trim(),
                BankAccount = _bankAccount.Text.Trim(),
                Currency = string.IsNullOrWhiteSpace(_currency.Text) ? "DZD" : _currency.Text.Trim(),
                Logo = _logoBytes
            };

            var result = _services.Companies.Create(company);
            if (result.IsSuccess)
            {
                DialogResult = DialogResult.OK;
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }
    }
}
