using System.Drawing;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Grid;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Central design system for the application: a single, consistent commercial
    /// palette, a small type scale and reusable styling helpers for buttons, panels,
    /// cards and grids. Every screen styles its controls through this class so the
    /// whole product looks and feels like one premium piece of software.
    ///
    /// The helpers only set <c>Appearance</c> values and turn off the default
    /// look-and-feel where a custom colour is required — they never change behaviour,
    /// data binding or layout logic, so polishing a screen can never break it.
    /// </summary>
    public static class UiTheme
    {
        // -- Brand palette (deep professional green — Algerian, calm, commercial) --
        public static readonly Color Primary = Color.FromArgb(15, 110, 79);
        public static readonly Color PrimaryHover = Color.FromArgb(22, 138, 98);
        public static readonly Color PrimaryDark = Color.FromArgb(10, 84, 60);

        // -- Sidebar (dark rail, the classic premium-ERP navigation) --------------
        public static readonly Color Sidebar = Color.FromArgb(28, 39, 51);
        public static readonly Color SidebarHover = Color.FromArgb(40, 54, 69);
        public static readonly Color SidebarText = Color.FromArgb(208, 215, 222);

        // -- Surfaces & text -------------------------------------------------------
        public static readonly Color Canvas = Color.FromArgb(242, 244, 247);
        public static readonly Color Surface = Color.White;
        public static readonly Color Header = Color.White;
        public static readonly Color Border = Color.FromArgb(224, 228, 233);
        public static readonly Color Hairline = Color.FromArgb(230, 234, 239); // softer 1px card edge
        public static readonly Color TextPrimary = Color.FromArgb(28, 35, 45);
        public static readonly Color TextMuted = Color.FromArgb(96, 104, 116);

        // -- Accent tints (selection / hover surfaces, brand-tinted) ---------------
        public static readonly Color AccentSoft = Color.FromArgb(233, 244, 239);      // light green wash
        public static readonly Color AccentSoftHover = Color.FromArgb(224, 238, 231);

        // -- Neutral chip (secondary buttons) -------------------------------------
        public static readonly Color Chip = Color.FromArgb(241, 243, 246);
        public static readonly Color ChipHover = Color.FromArgb(231, 235, 240);
        public static readonly Color ChipPressed = Color.FromArgb(221, 226, 232);
        public static readonly Color ChipBorder = Color.FromArgb(210, 216, 223);

        // -- Semantic colours for payroll figures ---------------------------------
        public static readonly Color Salary = Color.FromArgb(19, 123, 80);   // gains / net
        public static readonly Color Deduction = Color.FromArgb(191, 64, 54); // employee deductions
        public static readonly Color Employer = Color.FromArgb(43, 87, 161);  // employer charges
        public static readonly Color Warning = Color.FromArgb(201, 122, 16);  // intermediate / attention
        public static readonly Color Neutral = Color.FromArgb(74, 85, 99);    // intermediate bases

        // -- Type scale ------------------------------------------------------------
        public const string FontName = "Segoe UI";

        public static Font Title() { return new Font(FontName, 14.5F, FontStyle.Bold); }
        public static Font Subtitle() { return new Font(FontName, 9.75F); }
        public static Font Heading() { return new Font(FontName, 11.5F, FontStyle.Bold); }
        public static Font Body() { return new Font(FontName, 9.75F); }
        public static Font BodyStrong() { return new Font(FontName, 9.75F, FontStyle.Bold); }
        public static Font Caption() { return new Font(FontName, 8.75F); }
        public static Font Figure() { return new Font(FontName, 15F, FontStyle.Bold); }
        public static Font FigureLarge() { return new Font(FontName, 21F, FontStyle.Bold); }

        // -- Standard spacing ------------------------------------------------------
        public const int ButtonHeight = 32;
        public const int InputHeight = 28;
        public const int Gap = 10;
        public const int Pad = 16;

        // -- Buttons ---------------------------------------------------------------

        /// <summary>Filled brand button for the primary action on a screen. Rendered by
        /// the active skin (real button chrome) and tinted with the brand accent.</summary>
        public static SimpleButton PrimaryButton(SimpleButton button)
        {
            ApplyFill(button, Primary, Color.White);
            ApplyState(button.AppearanceHovered, PrimaryHover, Color.White);
            ApplyState(button.AppearancePressed, PrimaryDark, Color.White);
            button.Appearance.Font = BodyStrong();
            button.Appearance.Options.UseFont = true;
            if (button.Height < ButtonHeight) button.Height = ButtonHeight;
            return button;
        }

        /// <summary>Neutral secondary action — a soft grey "chip" with a clear hover and
        /// dark readable text, so it always reads as a real, clickable button (never the
        /// near-invisible white-on-white it used to be on light toolbars).</summary>
        public static SimpleButton SecondaryButton(SimpleButton button)
        {
            ApplyFill(button, Chip, TextPrimary);
            ApplyState(button.AppearanceHovered, ChipHover, TextPrimary);
            ApplyState(button.AppearancePressed, ChipPressed, TextPrimary);
            button.Appearance.Font = Body();
            button.Appearance.Options.UseFont = true;
            if (button.Height < ButtonHeight) button.Height = ButtonHeight;
            return button;
        }

        /// <summary>Destructive action (delete / remove): same calm grey chip as a
        /// secondary button but with a red label, so it is consistent yet unmistakable.</summary>
        public static SimpleButton DangerButton(SimpleButton button)
        {
            ApplyFill(button, Chip, Deduction);
            ApplyState(button.AppearanceHovered, Color.FromArgb(250, 235, 233), Deduction);
            ApplyState(button.AppearancePressed, Color.FromArgb(245, 224, 221), Deduction);
            button.Appearance.Font = BodyStrong();
            button.Appearance.Options.UseFont = true;
            if (button.Height < ButtonHeight) button.Height = ButtonHeight;
            return button;
        }

        /// <summary>
        /// Widens a button so its caption is never clipped. Measures the rendered text
        /// with the button's own font and adds horizontal room for the button chrome,
        /// while never shrinking below <paramref name="minWidth"/>. Call this AFTER the
        /// button's <c>Text</c> (and any language switch) is set so every screen sizes
        /// to the active language — the recurring source of truncated labels.
        /// </summary>
        public static SimpleButton FitButton(SimpleButton button, int minWidth = 0, int horizontalPadding = 30)
        {
            Font font = button.Appearance.Font ?? Body();
            Size text = TextRenderer.MeasureText(button.Text ?? string.Empty, font);
            int width = text.Width + horizontalPadding;
            button.Width = width > minWidth ? width : minWidth;
            if (button.Height < ButtonHeight) button.Height = ButtonHeight;
            return button;
        }

        // -- Panels ----------------------------------------------------------------

        /// <summary>A flat toolbar/header strip (white, no heavy border).</summary>
        public static PanelControl Toolbar(PanelControl panel)
        {
            panel.BorderStyle = BorderStyles.NoBorder;
            panel.Appearance.BackColor = Surface;
            panel.Appearance.Options.UseBackColor = true;
            return panel;
        }

        /// <summary>A clean white content card with a hairline border — the building
        /// block for the whole UI (KPIs, totals, panels).</summary>
        public static PanelControl Card(PanelControl panel)
        {
            panel.BorderStyle = BorderStyles.Simple;
            panel.Appearance.BackColor = Surface;
            panel.Appearance.BorderColor = Hairline;
            panel.Appearance.Options.UseBackColor = true;
            panel.Appearance.Options.UseBorderColor = true;
            return panel;
        }

        /// <summary>Paints a control area with the application canvas colour.</summary>
        public static void Canvasize(Control control)
        {
            control.BackColor = Canvas;
        }

        // -- Labels ----------------------------------------------------------------

        public static LabelControl Muted(LabelControl label)
        {
            label.Appearance.ForeColor = TextMuted;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Font = Caption();
            label.Appearance.Options.UseFont = true;
            return label;
        }

        public static LabelControl FieldCaption(LabelControl label)
        {
            label.Appearance.ForeColor = TextMuted;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Font = Caption();
            label.Appearance.Options.UseFont = true;
            return label;
        }

        /// <summary>The page title at the top of a screen (large, dark, confident).</summary>
        public static LabelControl PageTitle(LabelControl label)
        {
            label.AutoSizeMode = LabelAutoSizeMode.None;
            label.Appearance.ForeColor = TextPrimary;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Font = Title();
            label.Appearance.Options.UseFont = true;
            return label;
        }

        /// <summary>The muted one-line description beneath a page title.</summary>
        public static LabelControl PageSubtitle(LabelControl label)
        {
            label.AutoSizeMode = LabelAutoSizeMode.None;
            label.Appearance.ForeColor = TextMuted;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Font = Subtitle();
            label.Appearance.Options.UseFont = true;
            return label;
        }

        /// <summary>A section header above a group of cards or a grid (brand-dark, bold).</summary>
        public static LabelControl SectionHeader(LabelControl label)
        {
            label.AutoSizeMode = LabelAutoSizeMode.None;
            label.Appearance.ForeColor = PrimaryDark;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Font = Heading();
            label.Appearance.Options.UseFont = true;
            return label;
        }

        /// <summary>
        /// A soft, rounded "icon chip": a tinted square with a glyph, used to give KPI
        /// cards and tiles a premium product feel. Returns the chip panel; the caller
        /// positions it. Rounded via a region so it reads as a modern badge.
        /// </summary>
        public static PanelControl IconChip(string glyph, Color accent, int size = 40)
        {
            var chip = new PanelControl
            {
                Width = size,
                Height = size,
                BorderStyle = BorderStyles.NoBorder
            };
            Color tint = Blend(accent, Surface, 0.86); // soft tint: mostly white
            chip.Appearance.BackColor = tint;
            chip.Appearance.BackColor2 = tint; // flat fill, no gradient
            chip.Appearance.Options.UseBackColor = true;
            RoundCorners(chip, 11);

            var label = new LabelControl
            {
                Dock = DockStyle.Fill,
                AutoSizeMode = LabelAutoSizeMode.None
            };
            label.Appearance.Font = new Font(FontName, size * 0.42F);
            label.Appearance.ForeColor = accent;
            label.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            label.Appearance.TextOptions.VAlignment = VertAlignment.Center;
            label.Appearance.Options.UseFont = true;
            label.Appearance.Options.UseForeColor = true;
            label.Appearance.Options.UseTextOptions = true;
            label.Text = glyph;
            chip.Controls.Add(label);
            return chip;
        }

        /// <summary>Softly rounds a panel's corners with an anti-aliased region.</summary>
        public static void RoundCorners(Control control, int radius)
        {
            void Apply()
            {
                if (control.Width <= 0 || control.Height <= 0)
                {
                    return;
                }

                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int d = radius * 2;
                    var r = new Rectangle(0, 0, control.Width, control.Height);
                    path.AddArc(r.X, r.Y, d, d, 180, 90);
                    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    control.Region = new Region(path);
                }
            }

            Apply();
            control.Resize += (s, e) => Apply();
        }

        /// <summary>Gives an editor the house input height and font, so every field on
        /// every screen lines up with the captions and buttons beside it.</summary>
        public static T StyleInput<T>(T edit) where T : BaseEdit
        {
            // Editors auto-size to their font by default and ignore a manual Height —
            // turn that off so every field gets the same house height.
            if (edit.Properties is DevExpress.XtraEditors.Repository.RepositoryItemTextEdit textProps)
            {
                textProps.AutoHeight = false;
            }

            edit.Height = InputHeight;
            edit.Properties.Appearance.Font = Body();
            edit.Properties.Appearance.Options.UseFont = true;
            return edit;
        }

        // -- Lookups ---------------------------------------------------------------

        /// <summary>
        /// Configures a <see cref="LookUpEdit"/> to show a single, clean popup column
        /// (the display member) instead of DevExpress's default behaviour of auto-
        /// generating one column per public property of the bound entity — which is
        /// what produced the "dozens of empty columns" popup. Also pins the font so
        /// the editor and its drop-down render with the same crisp typography.
        /// </summary>
        public static void ConfigureLookup(LookUpEdit edit, string displayMember, string valueMember)
        {
            var p = edit.Properties;
            p.DisplayMember = displayMember;
            p.ValueMember = valueMember;
            p.NullText = string.Empty;

            // Exactly one visible column, no header, no internal fields, no scroll.
            p.Columns.Clear();
            p.Columns.Add(new LookUpColumnInfo(displayMember, string.Empty));
            p.ShowHeader = false;
            p.ShowFooter = false;
            p.DropDownRows = 12;
            p.PopupSizeable = true;
            p.BestFitMode = BestFitMode.BestFitResizePopup;

            p.Appearance.Font = Body();
            p.Appearance.Options.UseFont = true;
            p.AppearanceDropDown.Font = Body();
            p.AppearanceDropDown.Options.UseFont = true;

            p.AutoHeight = false;
            edit.Height = InputHeight; // consistent with every other field and the buttons
        }

        // -- Grids -----------------------------------------------------------------

        /// <summary>Applies the clean, readable house grid style to a view.</summary>
        public static void StyleGrid(GridView view)
        {
            view.OptionsView.ShowGroupPanel = false;
            view.OptionsView.ColumnAutoWidth = true;
            view.OptionsView.ShowIndicator = false;
            view.OptionsView.EnableAppearanceEvenRow = true;
            view.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            view.OptionsView.ShowHorizontalLines = DevExpress.Utils.DefaultBoolean.True;
            view.OptionsSelection.EnableAppearanceFocusedCell = false;
            view.OptionsSelection.EnableAppearanceHideSelection = false;
            view.OptionsBehavior.AutoExpandAllGroups = true;
            view.RowHeight = 36;
            view.GridControl.LookAndFeel.UseDefaultLookAndFeel = true;

            // A calm tinted header bar with a brand-dark label — the premium "report" look.
            view.Appearance.HeaderPanel.Font = new Font(FontName, 9.5F, FontStyle.Bold);
            view.Appearance.HeaderPanel.Options.UseFont = true;
            view.Appearance.HeaderPanel.BackColor = Color.FromArgb(244, 247, 250);
            view.Appearance.HeaderPanel.ForeColor = PrimaryDark;
            view.Appearance.HeaderPanel.Options.UseBackColor = true;
            view.Appearance.HeaderPanel.Options.UseForeColor = true;
            view.Appearance.HeaderPanel.TextOptions.HAlignment = HorzAlignment.Center;
            view.Appearance.HeaderPanel.Options.UseTextOptions = true;
            view.ColumnPanelRowHeight = 38;

            view.Appearance.Row.Font = Body();
            view.Appearance.Row.BackColor = Surface;
            view.Appearance.Row.ForeColor = TextPrimary;
            view.Appearance.Row.Options.UseFont = true;
            view.Appearance.Row.Options.UseBackColor = true;
            view.Appearance.Row.Options.UseForeColor = true;

            // Pin a dark-on-light appearance for EVERY row state so no skin (incl. the
            // dark theme) can ever leave white text on a light row background.
            view.OptionsView.EnableAppearanceOddRow = true;
            view.Appearance.OddRow.BackColor = Color.FromArgb(248, 250, 252);
            view.Appearance.OddRow.ForeColor = TextPrimary;
            view.Appearance.OddRow.Options.UseBackColor = true;
            view.Appearance.OddRow.Options.UseForeColor = true;

            view.Appearance.EvenRow.BackColor = Surface;
            view.Appearance.EvenRow.ForeColor = TextPrimary;
            view.Appearance.EvenRow.Options.UseBackColor = true;
            view.Appearance.EvenRow.Options.UseForeColor = true;

            view.Appearance.FocusedRow.BackColor = AccentSoft;
            view.Appearance.FocusedRow.ForeColor = PrimaryDark;
            view.Appearance.FocusedRow.Options.UseBackColor = true;
            view.Appearance.FocusedRow.Options.UseForeColor = true;

            view.Appearance.SelectedRow.BackColor = AccentSoft;
            view.Appearance.SelectedRow.ForeColor = PrimaryDark;
            view.Appearance.SelectedRow.Options.UseBackColor = true;
            view.Appearance.SelectedRow.Options.UseForeColor = true;

            // Hairline row separators in a barely-there grey (premium, not busy).
            view.Appearance.HorzLine.BackColor = Hairline;
            view.Appearance.HorzLine.Options.UseBackColor = true;
        }

        // -- internals -------------------------------------------------------------

        private static void ApplyFill(SimpleButton button, Color back, Color fore)
        {
            button.Appearance.BackColor = back;
            button.Appearance.BackColor2 = back;
            button.Appearance.ForeColor = fore;
            button.Appearance.Options.UseBackColor = true;
            button.Appearance.Options.UseForeColor = true;
            button.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
        }

        private static void ApplyState(AppearanceObject appearance, Color back, Color fore)
        {
            appearance.BackColor = back;
            appearance.BackColor2 = back;
            appearance.ForeColor = fore;
            appearance.Options.UseBackColor = true;
            appearance.Options.UseForeColor = true;
        }

        // -- Right-to-left layout --------------------------------------------------

        /// <summary>
        /// Leading-edge-aware horizontal position. DevExpress right-aligns text when a
        /// control's <c>RightToLeft</c> is on, but it never mirrors the X coordinates of
        /// hand-positioned controls — so every custom layout measures its offset from the
        /// LEADING edge (left in LTR, right in Arabic) through this single helper. Because
        /// each LayoutXxx recomputes positions from scratch, computing the mirrored X here
        /// (rather than flipping afterwards) stays correct on every resize.
        /// </summary>
        public static int LeadX(int containerWidth, int x, int controlWidth, bool rtl)
        {
            return rtl ? containerWidth - x - controlWidth : x;
        }

        /// <summary>Positions a control at the logical offset (x,y) from the leading edge.</summary>
        public static void PlaceLead(Control c, int containerWidth, int x, int y, bool rtl)
        {
            c.Location = new System.Drawing.Point(rtl ? containerWidth - x - c.Width : x, y);
        }

        /// <summary>The leading dock edge — Left in LTR, Right in Arabic (for accent bars).</summary>
        public static DockStyle LeadingDock(bool rtl)
        {
            return rtl ? DockStyle.Right : DockStyle.Left;
        }

        /// <summary>Mixes <paramref name="a"/> toward <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
        public static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int Mix(int x, int y) => (int)System.Math.Round(x + (y - x) * t);
            return Color.FromArgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
        }
    }
}
