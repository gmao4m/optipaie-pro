using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using OptiPaie.App.Common;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;
using OptiPaie.PayrollEngine.Legal;

namespace OptiPaie.App.About
{
    /// <summary>Professional About window: product, version, build, legal profile, copyright, support.</summary>
    public sealed class AboutForm : XtraForm
    {
        private readonly AppServices _services;

        public AboutForm(AppServices services)
        {
            _services = services;
            BuildUi();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("About_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(460, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var accent = new PanelControl { Dock = DockStyle.Top, Height = 4, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            accent.Appearance.BackColor = UiTheme.Primary;
            accent.Appearance.Options.UseBackColor = true;
            Controls.Add(accent);

            var title = new LabelControl { Location = new Point(24, 24), AutoSizeMode = LabelAutoSizeMode.None, Width = 400 };
            title.Appearance.Font = new Font(UiTheme.FontName, 16F, FontStyle.Bold);
            title.Appearance.ForeColor = UiTheme.Primary;
            title.Appearance.Options.UseForeColor = true;
            title.Text = T("App_Title");
            Controls.Add(title);

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            LegalProfile profile = new BuiltInLegalProfileProvider().GetProfile(new PayrollPeriod(DateTime.Now.Year, DateTime.Now.Month));

            int y = 70;
            AddLine(T("About_Version"), version.ToString(3), ref y);
            AddLine(T("About_Build"), EngineVersion.Version + " / " + EngineVersion.CalculationVersion, ref y);
            AddLine(T("About_LegalProfile"), profile.LegalVersion, ref y);
            AddLine(T("About_Support"), "support@optipaie.dz", ref y);
            AddLine(T("About_Website"), "www.optipaie.dz", ref y);

            var copyright = new LabelControl { Location = new Point(24, y + 16), AutoSizeMode = LabelAutoSizeMode.None, Width = 400 };
            copyright.Text = T("About_Copyright");
            Controls.Add(copyright);

            var close = new SimpleButton { Text = T("Common_Close"), Dock = DockStyle.Bottom, Height = 40 };
            UiTheme.SecondaryButton(close);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private void AddLine(string label, string value, ref int y)
        {
            var caption = new LabelControl { Location = new Point(24, y), AutoSizeMode = LabelAutoSizeMode.None, Width = 160 };
            caption.Appearance.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            caption.Text = label;
            Controls.Add(caption);

            var content = new LabelControl { Location = new Point(190, y), AutoSizeMode = LabelAutoSizeMode.None, Width = 230 };
            content.Text = value;
            Controls.Add(content);

            y += 30;
        }
    }
}
