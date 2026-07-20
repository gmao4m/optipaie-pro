using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using OptiPaie.App.Common;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Modules.Settings
{
    /// <summary>Application settings: language, theme, CNAS rate (read-only) and backup/restore.</summary>
    public sealed class SettingsControl : BaseModuleControl
    {
        private LabelControl _pageTitle;
        private LabelControl _pageSubtitle;
        private LabelControl _prefsHeader;
        private LabelControl _dataHeader;
        private LabelControl _languageLabel;
        private ImageComboBoxEdit _language;
        private LabelControl _themeLabel;
        private ImageComboBoxEdit _theme;
        private LabelControl _cnasLabel;
        private LabelControl _cnasValue;
        private SimpleButton _backupButton;
        private SimpleButton _restoreButton;

        private PanelControl _prefsCard;
        private PanelControl _dataCard;

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            // -- Preferences card (language + theme) -------------------------------
            _prefsCard = new PanelControl { Location = new Point(28, 92), Size = new Size(560, 150) };
            UiTheme.Card(_prefsCard);
            Controls.Add(_prefsCard);

            _prefsHeader = UiTheme.SectionHeader(new LabelControl { Location = new Point(20, 14), AutoSizeMode = LabelAutoSizeMode.None, Width = 480, Height = 24 });
            _prefsCard.Controls.Add(_prefsHeader);

            _languageLabel = MakeLabel(_prefsCard, 54);
            _language = new ImageComboBoxEdit { Location = new Point(250, 52), Width = 280 };
            _language.Properties.Items.Add(new ImageComboBoxItem("Français", "fr"));
            _language.Properties.Items.Add(new ImageComboBoxItem("العربية", "ar"));
            UiTheme.StyleInput(_language);
            _language.EditValueChanged += (s, e) => OnLanguagePicked();
            _prefsCard.Controls.Add(_language);

            _themeLabel = MakeLabel(_prefsCard, 98);
            _theme = new ImageComboBoxEdit { Location = new Point(250, 96), Width = 280 };
            _theme.Properties.Items.Add(new ImageComboBoxItem(T("Theme_Light"), ThemeManager.Light));
            // OptiPaie DZ ships a single, professionally-tuned light theme (always
            // readable). Dark/Office skins are intentionally not offered.
            _theme.Properties.ReadOnly = true;
            UiTheme.StyleInput(_theme);
            _theme.EditValueChanged += (s, e) => OnThemePicked();
            _prefsCard.Controls.Add(_theme);

            // -- Data & security card (legal rate + backup/restore) ----------------
            _dataCard = new PanelControl { Location = new Point(28, 258), Size = new Size(560, 200) };
            UiTheme.Card(_dataCard);
            Controls.Add(_dataCard);

            _dataHeader = UiTheme.SectionHeader(new LabelControl { Location = new Point(20, 14), AutoSizeMode = LabelAutoSizeMode.None, Width = 480, Height = 24 });
            _dataCard.Controls.Add(_dataHeader);

            _cnasLabel = MakeLabel(_dataCard, 54);
            _cnasValue = new LabelControl { Location = new Point(250, 54), AutoSizeMode = LabelAutoSizeMode.None, Width = 280, Height = 22 };
            _cnasValue.Appearance.Font = UiTheme.BodyStrong();
            _cnasValue.Appearance.ForeColor = UiTheme.TextPrimary;
            _cnasValue.Appearance.Options.UseFont = true;
            _cnasValue.Appearance.Options.UseForeColor = true;
            _dataCard.Controls.Add(_cnasValue);

            _backupButton = new SimpleButton { Location = new Point(20, 120), Width = 220, Height = 36 };
            UiTheme.PrimaryButton(_backupButton);
            _backupButton.Click += (s, e) => DoBackup();
            _dataCard.Controls.Add(_backupButton);

            _restoreButton = new SimpleButton { Location = new Point(252, 120), Width = 220, Height = 36 };
            UiTheme.SecondaryButton(_restoreButton);
            _restoreButton.Click += (s, e) => DoRestore();
            _dataCard.Controls.Add(_restoreButton);

            AddPageHeader(out _pageTitle, out _pageSubtitle);

            Resize += (s, e) => LayoutSettings();
            LayoutSettings();
        }

        /// <summary>Positions the cards and their fields from the leading edge (left in fr, right in ar).</summary>
        private void LayoutSettings()
        {
            if (_prefsCard == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int w = ClientSize.Width;

            _prefsCard.Location = new Point(UiTheme.LeadX(w, 28, _prefsCard.Width, rtl), 92);
            _dataCard.Location = new Point(UiTheme.LeadX(w, 28, _dataCard.Width, rtl), 258);

            int pc = _prefsCard.ClientSize.Width;
            _prefsHeader.Width = pc - 40;
            _prefsHeader.Location = new Point(20, 14);
            UiTheme.PlaceLead(_languageLabel, pc, 20, 58, rtl);
            UiTheme.PlaceLead(_language, pc, 250, 52, rtl);
            UiTheme.PlaceLead(_themeLabel, pc, 20, 102, rtl);
            UiTheme.PlaceLead(_theme, pc, 250, 96, rtl);

            int dc = _dataCard.ClientSize.Width;
            _dataHeader.Width = dc - 40;
            _dataHeader.Location = new Point(20, 14);
            UiTheme.PlaceLead(_cnasLabel, dc, 20, 58, rtl);
            UiTheme.PlaceLead(_cnasValue, dc, 250, 54, rtl);
            UiTheme.PlaceLead(_backupButton, dc, 20, 120, rtl);
            UiTheme.PlaceLead(_restoreButton, dc, 252, 120, rtl);
        }

        private static LabelControl MakeLabel(PanelControl card, int y)
        {
            var label = new LabelControl { Location = new Point(20, y + 4), AutoSizeMode = LabelAutoSizeMode.None, Width = 220 };
            UiTheme.FieldCaption(label);
            card.Controls.Add(label);
            return label;
        }

        public override void Localize()
        {
            _pageTitle.Text = T("Module_Settings");
            _pageSubtitle.Text = T("Settings_Subtitle");
            _prefsHeader.Text = T("Settings_GroupPreferences");
            _dataHeader.Text = T("Settings_GroupData");
            _languageLabel.Text = T("Settings_Language");
            _themeLabel.Text = T("Settings_Theme");
            _cnasLabel.Text = T("Diagnostic_CnasRate");
            _backupButton.Text = T("Settings_Backup");
            _restoreButton.Text = T("Settings_Restore");

            UiTheme.FitButton(_backupButton, 200);
            UiTheme.FitButton(_restoreButton, 180);

            LayoutSettings();
        }

        public override void OnActivated()
        {
            _language.EditValue = Services.Localization.CurrentLanguage;

            // Single light theme: normalise any previously saved value to it.
            _theme.EditValue = ThemeManager.Light;
            if (!string.Equals(Services.Settings.GetTheme(), ThemeManager.Light, System.StringComparison.OrdinalIgnoreCase))
            {
                Services.Settings.SetTheme(ThemeManager.Light);
            }

            decimal rate = Services.ConfigurationService.GetCnasEmployeeRate();
            _cnasValue.Text = (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %";
        }

        private void OnLanguagePicked()
        {
            if (_language.EditValue is string code && code != Services.Localization.CurrentLanguage)
            {
                Services.Settings.SetLanguage(code);
                Services.Localization.SetLanguage(code);
            }
        }

        private void OnThemePicked()
        {
            if (_theme.EditValue is string key && !string.IsNullOrWhiteSpace(key))
            {
                ThemeManager.Apply(key);
                Services.Settings.SetTheme(key);
            }
        }

        private void DoBackup()
        {
            Result<Core.Entities.BackupRecord> result = Services.Backup.Backup(BackupType.Manual);
            if (result.IsSuccess)
            {
                UiHelper.Info(result.Value.FilePath, T("Common_Success"));
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        private void DoRestore()
        {
            using (var dialog = new OpenFileDialog { Filter = "OptiPaie DB (*.db)|*.db" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (!UiHelper.Confirm(T("Msg_RestoreConfirm"), T("Common_Confirm")))
                {
                    return;
                }

                Result result = Services.Backup.Restore(dialog.FileName);
                if (result.IsSuccess)
                {
                    UiHelper.Info(T("Msg_RestartAfterRestore"), T("Common_Success"));
                }
                else
                {
                    UiHelper.Error(result.Error, T("Common_Error"));
                }
            }
        }
    }
}
