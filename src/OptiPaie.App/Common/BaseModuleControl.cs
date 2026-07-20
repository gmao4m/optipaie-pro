using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using OptiPaie.Localization;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Base for the module screens hosted in the main shell. Provides access to the
    /// application services, builds its UI once, and re-applies localization and
    /// layout direction whenever the language changes.
    /// </summary>
    public class BaseModuleControl : XtraUserControl
    {
        private bool _built;

        /// <summary>The composed application services.</summary>
        protected AppServices Services { get; private set; }

        /// <summary>Shortcut to the localization service.</summary>
        protected ILocalizationService L => Services.Localization;

        /// <summary>Wires the services, builds the UI (once) and localizes it.</summary>
        public void Initialize(AppServices services)
        {
            Services = services;

            if (!_built)
            {
                BuildUi();
                _built = true;
            }

            Localize();
            UiHelper.ApplyRightToLeft(this, L.IsRightToLeft);
        }

        /// <summary>Builds the control tree. Called once.</summary>
        protected virtual void BuildUi()
        {
        }

        /// <summary>Applies localized texts. Called on build and on every language change.</summary>
        public virtual void Localize()
        {
        }

        /// <summary>Called each time the module becomes visible (refresh data here). Also bound to F5.</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>Ctrl+N — create a new item (override where applicable).</summary>
        public virtual void OnNew()
        {
        }

        /// <summary>Ctrl+S — save (override where applicable).</summary>
        public virtual void OnSave()
        {
        }

        /// <summary>Ctrl+P — print (override where applicable).</summary>
        public virtual void OnPrint()
        {
        }

        /// <summary>Ctrl+F — find (override where applicable).</summary>
        public virtual void OnFind()
        {
        }

        /// <summary>Delete — delete the selected item (override where applicable).</summary>
        public virtual void OnDelete()
        {
        }

        /// <summary>Resolves a localized string.</summary>
        protected string T(string key)
        {
            return Services.Localization.GetString(key);
        }

        /// <summary>
        /// Adds a consistent premium page-header band (large title + muted subtitle, with
        /// a hairline base) to the top of the screen. Call this LAST in BuildUi so it
        /// docks above any toolbar; set the texts in <see cref="Localize"/>.
        /// </summary>
        protected PanelControl AddPageHeader(out LabelControl title, out LabelControl subtitle)
        {
            var header = new PanelControl { Dock = DockStyle.Top, Height = 64 };
            UiTheme.Toolbar(header);

            var line = new PanelControl { Dock = DockStyle.Bottom, Height = 1, BorderStyle = BorderStyles.NoBorder };
            line.Appearance.BackColor = UiTheme.Hairline;
            line.Appearance.Options.UseBackColor = true;
            header.Controls.Add(line);

            LabelControl t = UiTheme.PageTitle(new LabelControl { Location = new Point(UiTheme.Pad, 11), Height = 26 });
            LabelControl s = UiTheme.PageSubtitle(new LabelControl { Location = new Point(UiTheme.Pad, 38), Height = 18 });
            header.Controls.Add(t);
            header.Controls.Add(s);

            // Span the full width so the text aligns to the correct edge in both
            // directions (left in French, right in Arabic — DevExpress flips Near→right
            // when RightToLeft is on). Keep it full width as the header resizes.
            void LayoutHeaderText()
            {
                int w = header.Width - 2 * UiTheme.Pad;
                if (w < 0) w = 0;
                t.Width = w;
                s.Width = w;
            }
            LayoutHeaderText();
            header.Resize += (sender, e) => LayoutHeaderText();

            title = t;
            subtitle = s;

            Controls.Add(header);
            return header;
        }
    }
}
