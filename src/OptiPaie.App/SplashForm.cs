using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using OptiPaie.App.Common;

namespace OptiPaie.App
{
    /// <summary>
    /// Lightweight startup splash (logo text, product name, version, marquee
    /// progress) shown while the services and database initialise.
    /// </summary>
    public sealed class SplashForm : XtraForm
    {
        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(440, 240);
            ShowInTaskbar = false;
            BackColor = UiTheme.Surface;

            var topAccent = new PanelControl { Dock = DockStyle.Top, Height = 6, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            topAccent.Appearance.BackColor = UiTheme.Primary;
            topAccent.Appearance.Options.UseBackColor = true;
            Controls.Add(topAccent);

            var title = new LabelControl
            {
                Location = new Point(0, 64),
                Width = 440,
                AutoSizeMode = LabelAutoSizeMode.None
            };
            title.Appearance.Font = new Font(UiTheme.FontName, 22F, FontStyle.Bold);
            title.Appearance.ForeColor = UiTheme.Primary;
            title.Appearance.Options.UseForeColor = true;
            title.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            title.Text = "OptiPaie DZ";
            Controls.Add(title);

            var subtitle = new LabelControl
            {
                Location = new Point(0, 112),
                Width = 440,
                AutoSizeMode = LabelAutoSizeMode.None
            };
            subtitle.Appearance.ForeColor = UiTheme.TextMuted;
            subtitle.Appearance.Options.UseForeColor = true;
            subtitle.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            subtitle.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Controls.Add(subtitle);

            var progress = new MarqueeProgressBarControl
            {
                Location = new Point(70, 160),
                Width = 300
            };
            Controls.Add(progress);
        }
    }
}
