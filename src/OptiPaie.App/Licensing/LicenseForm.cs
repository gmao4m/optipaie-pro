using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using OptiPaie.App.Common;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Licensing
{
    /// <summary>License Manager window: status, machine id, and offline activation.</summary>
    public sealed class LicenseForm : XtraForm
    {
        private readonly AppServices _services;

        private LabelControl _statusValue;
        private LabelControl _machineValue;
        private LabelControl _customerValue;
        private LabelControl _serialValue;
        private TextEdit _serialInput;
        private TextEdit _customerInput;

        public LicenseForm(AppServices services)
        {
            _services = services;
            BuildUi();
            RefreshStatus();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("License_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            int y = 20;
            _statusValue = AddRow(T("License_Status"), ref y);
            _machineValue = AddRow(T("License_MachineId"), ref y);
            _customerValue = AddRow(T("License_Customer"), ref y);
            _serialValue = AddRow(T("License_Serial"), ref y);

            y += 16;
            AddCaption(T("License_Serial"), y);
            _serialInput = new TextEdit { Location = new Point(190, y - 2), Width = 250 };
            Controls.Add(_serialInput);
            y += 34;

            AddCaption(T("License_Customer"), y);
            _customerInput = new TextEdit { Location = new Point(190, y - 2), Width = 250 };
            Controls.Add(_customerInput);
            y += 40;

            var activate = new SimpleButton { Text = T("License_Activate"), Location = new Point(190, y), Width = 150, Height = UiTheme.ButtonHeight };
            UiTheme.PrimaryButton(activate);
            activate.Click += (s, e) => Activate();
            Controls.Add(activate);

            var close = new SimpleButton { Text = T("Common_Close"), Dock = DockStyle.Bottom, Height = 40 };
            UiTheme.SecondaryButton(close);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private LabelControl AddRow(string label, ref int y)
        {
            AddCaption(label, y);
            var value = new LabelControl { Location = new Point(190, y), AutoSizeMode = LabelAutoSizeMode.None, Width = 260 };
            Controls.Add(value);
            y += 30;
            return value;
        }

        private void AddCaption(string label, int y)
        {
            var caption = new LabelControl { Location = new Point(24, y), AutoSizeMode = LabelAutoSizeMode.None, Width = 160 };
            caption.Appearance.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            caption.Text = label;
            Controls.Add(caption);
        }

        private void RefreshStatus()
        {
            LicenseInfo info = _services.License.GetStatus();
            _statusValue.Text = StatusText(info.Status);
            _machineValue.Text = info.MachineId;
            _customerValue.Text = string.IsNullOrEmpty(info.CustomerName) ? T("Common_None") : info.CustomerName;
            _serialValue.Text = string.IsNullOrEmpty(info.SerialNumber) ? T("Common_None") : info.SerialNumber;
        }

        private string StatusText(LicenseStatus status)
        {
            switch (status)
            {
                case LicenseStatus.Active:
                    return T("License_Active");
                case LicenseStatus.Expired:
                    return T("License_Expired");
                case LicenseStatus.Trial:
                    return T("License_Trial");
                default:
                    return T("License_Inactive");
            }
        }

        private void Activate()
        {
            Result result = _services.License.Activate(_serialInput.Text, _customerInput.Text);
            if (result.IsSuccess)
            {
                UiHelper.Info(T("License_Activated"), T("Common_Success"));
                RefreshStatus();
            }
            else
            {
                UiHelper.Error(T("License_InvalidSerial"), T("Common_Error"));
            }
        }
    }
}
