using System;
using System.Drawing;
using DevExpress.Utils;
using DevExpress.XtraEditors;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// A reusable, centered empty-state panel (icon glyph + message + primary action)
    /// shown over a grid when it has no rows, instead of a bare "no data" grid.
    /// </summary>
    public sealed class EmptyStatePanel : PanelControl
    {
        private readonly LabelControl _icon;
        private readonly LabelControl _message;
        private readonly SimpleButton _action;
        private Action _onAction;

        public EmptyStatePanel()
        {
            BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            Appearance.BackColor = UiTheme.Surface;
            Appearance.Options.UseBackColor = true;

            _icon = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Width = 160, Height = 90 };
            _icon.Appearance.Font = new Font(UiTheme.FontName, 40F);
            _icon.Appearance.ForeColor = Color.FromArgb(193, 199, 206);
            _icon.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            _icon.Text = "📁";
            Controls.Add(_icon);

            _message = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Width = 440, Height = 26 };
            _message.Appearance.Font = new Font(UiTheme.FontName, 11.5F);
            _message.Appearance.ForeColor = UiTheme.TextMuted;
            _message.Appearance.Options.UseForeColor = true;
            _message.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            Controls.Add(_message);

            _action = new SimpleButton { Width = 220, Height = 38, Visible = false };
            UiTheme.PrimaryButton(_action);
            _action.Click += (s, e) => _onAction?.Invoke();
            Controls.Add(_action);

            Resize += (s, e) => Recenter();
        }

        public void Configure(string message, string actionText, Action onAction)
        {
            _message.Text = message;
            _onAction = onAction;

            if (string.IsNullOrEmpty(actionText))
            {
                _action.Visible = false;
            }
            else
            {
                _action.Text = actionText;
                _action.Visible = true;
            }

            Recenter();
        }

        private void Recenter()
        {
            int cx = Width / 2;
            int cy = Height / 2;
            _icon.Location = new Point(cx - _icon.Width / 2, cy - 110);
            _message.Location = new Point(cx - _message.Width / 2, cy - 10);
            _action.Location = new Point(cx - _action.Width / 2, cy + 30);
        }
    }
}
