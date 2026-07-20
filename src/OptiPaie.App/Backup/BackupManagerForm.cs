using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Backup
{
    /// <summary>Backup Manager: manual backup, restore and backup history.</summary>
    public sealed class BackupManagerForm : XtraForm
    {
        private readonly AppServices _services;
        private GridControl _grid;
        private GridView _view;

        public BackupManagerForm(AppServices services)
        {
            _services = services;
            BuildUi();
            LoadHistory();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("Backup_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 480);

            var toolbar = new PanelControl { Dock = DockStyle.Top, Height = 56 };
            UiTheme.Toolbar(toolbar);
            _grid = new GridControl { Dock = DockStyle.Fill };
            Controls.Add(_grid);
            Controls.Add(toolbar);

            var backup = new SimpleButton { Text = T("Backup_Manual"), Location = new Point(16, 12), Width = 170, Height = UiTheme.ButtonHeight };
            UiTheme.PrimaryButton(backup);
            backup.Click += (s, e) => DoBackup();
            toolbar.Controls.Add(backup);

            var restore = new SimpleButton { Text = T("Settings_Restore"), Location = new Point(196, 12), Width = 160, Height = UiTheme.ButtonHeight };
            UiTheme.SecondaryButton(restore);
            restore.Click += (s, e) => DoRestore();
            toolbar.Controls.Add(restore);

            var close = new SimpleButton { Text = T("Common_Close"), Dock = DockStyle.Bottom, Height = 40 };
            UiTheme.SecondaryButton(close);
            close.Click += (s, e) => Close();
            Controls.Add(close);

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_view);
        }

        private void LoadHistory()
        {
            var rows = new List<BackupRow>();
            foreach (BackupRecord record in _services.Backup.GetRecent(50))
            {
                rows.Add(new BackupRow
                {
                    Date = record.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    TypeText = EnumLocalizer.Localize(_services.Localization, record.BackupType),
                    SizeText = (record.SizeBytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
                    FilePath = record.FilePath
                });
            }

            _grid.DataSource = rows;
            _view.PopulateColumns();
            if (_view.Columns["FilePath"] != null)
            {
                _view.Columns["FilePath"].Visible = false;
            }

            _view.Columns["Date"].Caption = T("Backup_Date");
            _view.Columns["TypeText"].Caption = T("Backup_Type");
            _view.Columns["SizeText"].Caption = T("Backup_Size");
        }

        private void DoBackup()
        {
            Result<BackupRecord> result = null;
            UiHelper.RunBusy(() => result = _services.Backup.Backup(BackupType.Manual));

            if (result != null && result.IsSuccess)
            {
                LoadHistory();
                UiHelper.Info(result.Value.FilePath, T("Common_Success"));
            }
            else if (result != null)
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        private void DoRestore()
        {
            var row = _view.GetFocusedRow() as BackupRow;
            string path = row != null ? row.FilePath : null;

            if (path == null)
            {
                using (var dialog = new OpenFileDialog { Filter = "OptiPaie DB (*.db)|*.db" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    path = dialog.FileName;
                }
            }

            if (!UiHelper.Confirm(T("Msg_RestoreConfirm"), T("Common_Confirm")))
            {
                return;
            }

            Result result = null;
            UiHelper.RunBusy(() => result = _services.Backup.Restore(path));

            if (result != null && result.IsSuccess)
            {
                UiHelper.Info(T("Msg_RestartAfterRestore"), T("Common_Success"));
            }
            else if (result != null)
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        private sealed class BackupRow
        {
            public string Date { get; set; }
            public string TypeText { get; set; }
            public string SizeText { get; set; }
            public string FilePath { get; set; }
        }
    }
}
