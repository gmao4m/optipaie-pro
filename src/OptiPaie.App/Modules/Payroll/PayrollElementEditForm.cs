using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.App.Modules.Payroll
{
    /// <summary>
    /// Create/edit dialog for a catalogue payroll element type. A type defines only the
    /// element's <em>legal behaviour</em>: its name, whether it is a gain or a deduction,
    /// and its CNAS/IRG treatment (Oui / Non / Partiel %). The numeric Base and Taux are
    /// NOT part of the definition — they belong to the payroll worksheet when the element
    /// is actually used, so this dialog never asks for them.
    ///
    /// The app is bilingual globally, so a single <c>Nom</c> is captured and stored on
    /// both name fields rather than asking the accountant for two labels per element.
    /// </summary>
    public sealed class PayrollElementEditForm : XtraForm
    {
        private readonly AppServices _services;

        private TextEdit _name;
        private ImageComboBoxEdit _type;
        private ImageComboBoxEdit _cotisableMode;
        private SpinEdit _cotisablePercent;
        private ImageComboBoxEdit _imposableMode;
        private SpinEdit _imposablePercent;
        private TextEdit _observations;

        /// <summary>The created/edited element (the same instance passed in).</summary>
        public PayrollElement Element { get; }

        public PayrollElementEditForm(AppServices services, PayrollElement element)
        {
            _services = services;
            Element = element;
            BuildUi();
            LoadFrom(element);
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("Element_Title");
        }

        private string T(string key) => _services.Localization.GetString(key);

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new LayoutControl { Dock = DockStyle.Fill };
            Controls.Add(layout);

            // Identity — a single name (the software is bilingual globally).
            _name = AddText(layout, T("Element_Name"));

            _type = new ImageComboBoxEdit();
            _type.Properties.Items.Add(new ImageComboBoxItem(T("ElementType_Gain"), (int)ElementType.Gain));
            _type.Properties.Items.Add(new ImageComboBoxItem(T("ElementType_Deduction"), (int)ElementType.Deduction));
            layout.AddItem(T("Payroll_Type"), _type);

            // Legal treatment. The percentage editor is enabled ONLY for "Partiel (%)";
            // Oui and Non keep it disabled (100 % / 0 % respectively).
            _cotisableMode = AddModeCombo(layout, T("Element_Cotisable"));
            _cotisablePercent = AddSpin(layout, T("Element_Percent"));
            _cotisableMode.EditValueChanged += (s, e) => UpdatePercentEnabled();

            _imposableMode = AddModeCombo(layout, T("Element_Imposable"));
            _imposablePercent = AddSpin(layout, T("Element_Percent"));
            _imposableMode.EditValueChanged += (s, e) => UpdatePercentEnabled();

            _observations = AddText(layout, T("Payroll_Observations"));

            var buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 56 };
            UiTheme.Toolbar(buttons);
            Controls.Add(buttons);

            var save = new SimpleButton { Text = T("Common_Save"), Width = 140, Height = UiTheme.ButtonHeight, Location = new Point(16, 12) };
            UiTheme.PrimaryButton(save);
            UiTheme.FitButton(save, 140);
            save.Click += Save_Click;
            buttons.Controls.Add(save);

            var cancel = new SimpleButton { Text = T("Common_Cancel"), Width = 120, Height = UiTheme.ButtonHeight, Location = new Point(166, 12) };
            UiTheme.SecondaryButton(cancel);
            UiTheme.FitButton(cancel, 120);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private static TextEdit AddText(LayoutControl layout, string caption)
        {
            var edit = new TextEdit();
            UiTheme.StyleInput(edit);
            layout.AddItem(caption, edit);
            return edit;
        }

        private static SpinEdit AddSpin(LayoutControl layout, string caption)
        {
            var edit = new SpinEdit();
            edit.Properties.MinValue = 0m;
            edit.Properties.MaxValue = 100m;
            edit.Properties.IsFloatValue = true;
            UiTheme.StyleInput(edit);
            layout.AddItem(caption, edit);
            return edit;
        }

        // Mode combo: 0 = Non, 1 = Oui (100 %), 2 = Partiel (use the percent field).
        private ImageComboBoxEdit AddModeCombo(LayoutControl layout, string caption)
        {
            var combo = new ImageComboBoxEdit();
            combo.Properties.Items.Add(new ImageComboBoxItem(T("Common_Yes"), 1));
            combo.Properties.Items.Add(new ImageComboBoxItem(T("Common_No"), 0));
            combo.Properties.Items.Add(new ImageComboBoxItem(T("Element_Partial"), 2));
            UiTheme.StyleInput(combo);
            layout.AddItem(caption, combo);
            return combo;
        }

        /// <summary>Enables a percentage editor only when its mode is "Partiel (%)".</summary>
        private void UpdatePercentEnabled()
        {
            _cotisablePercent.Enabled = (_cotisableMode.EditValue is int c) && c == 2;
            _imposablePercent.Enabled = (_imposableMode.EditValue is int i) && i == 2;
        }

        private void LoadFrom(PayrollElement e)
        {
            _name.Text = !string.IsNullOrWhiteSpace(e.NameFr) ? e.NameFr : e.NameAr;
            _type.EditValue = (int)(e.ElementType == 0 ? ElementType.Gain : e.ElementType);
            _observations.Text = e.Description;

            LoadMode(_cotisableMode, _cotisablePercent, e.CnasPercent, e.IsCnasApplicable);
            LoadMode(_imposableMode, _imposablePercent, e.IrgPercent, e.IsIrgApplicable);
            UpdatePercentEnabled();
        }

        private static void LoadMode(ImageComboBoxEdit mode, SpinEdit percent, decimal? storedPercent, bool flag)
        {
            if (storedPercent.HasValue && storedPercent.Value > 0m && storedPercent.Value < 100m)
            {
                mode.EditValue = 2; // Partiel
                percent.Value = storedPercent.Value;
            }
            else
            {
                bool yes = storedPercent.HasValue ? storedPercent.Value >= 100m : flag;
                mode.EditValue = yes ? 1 : 0;
                percent.Value = yes ? 100m : 0m;
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                UiHelper.Error(T("Element_Name"), T("Common_Error"));
                return;
            }

            // "Partiel" makes the percentage mandatory (1..99); 0 or 100 are the Non/Oui modes.
            if (!ValidatePartial(_cotisableMode, _cotisablePercent) ||
                !ValidatePartial(_imposableMode, _imposablePercent))
            {
                UiHelper.Error(T("Element_PercentRequired"), T("Common_Error"));
                return;
            }

            string name = _name.Text.Trim();
            // Bilingual app, single label: store the one name on both fields so it shows
            // correctly whichever language is active and the engine/validator are happy.
            Element.NameFr = name;
            Element.NameAr = name;
            Element.Description = _observations.Text.Trim();
            Element.ElementType = _type.EditValue is int t ? (ElementType)t : ElementType.Gain;
            Element.CalculationMethod = CalculationMethod.QuantityUnitPrice; // worksheet Base × Taux

            // The numeric Base/Taux live on the worksheet, not the type definition.
            Element.DefaultQuantity = null;
            Element.DefaultUnitPrice = null;
            Element.DefaultAmount = null;
            Element.DefaultRate = null;

            ApplyMode(_cotisableMode, _cotisablePercent, out bool cnas, out decimal? cnasPct);
            Element.IsCnasApplicable = cnas;
            Element.CnasPercent = cnasPct;

            ApplyMode(_imposableMode, _imposablePercent, out bool irg, out decimal? irgPct);
            Element.IsIrgApplicable = irg;
            Element.IrgPercent = irgPct;

            Element.IsIncludedInGross = Element.ElementType == ElementType.Gain;
            // Preserve the active flag on edit; default a brand-new type to active.
            if (Element.Id == 0)
            {
                Element.IsEnabled = true;
            }
            Element.IsEditable = true;
            Element.IsSystem = false;
            Element.IsPrintable = true;
            Element.Periodicity = ElementPeriodicity.Monthly;
            Element.IsDeleted = false;

            DialogResult = DialogResult.OK;
        }

        /// <summary>True unless a "Partiel" mode is selected without a valid 1..99 percentage.</summary>
        private static bool ValidatePartial(ImageComboBoxEdit mode, SpinEdit percent)
        {
            if (!(mode.EditValue is int m) || m != 2)
            {
                return true;
            }

            return percent.Value > 0m && percent.Value < 100m;
        }

        private static void ApplyMode(ImageComboBoxEdit mode, SpinEdit percent, out bool applicable, out decimal? storedPercent)
        {
            int m = mode.EditValue is int v ? v : 1;
            switch (m)
            {
                case 0: // Non
                    applicable = false;
                    storedPercent = 0m;
                    break;
                case 2: // Partiel
                    applicable = percent.Value > 0m;
                    storedPercent = percent.Value;
                    break;
                default: // Oui
                    applicable = true;
                    storedPercent = 100m;
                    break;
            }
        }
    }
}
