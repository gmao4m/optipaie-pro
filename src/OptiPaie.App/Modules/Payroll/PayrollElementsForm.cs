using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Modules.Payroll
{
    /// <summary>
    /// The master catalogue of payroll element types. The accountant manages every
    /// type here — add, edit, delete, activate/deactivate and change its legal rules —
    /// and the Payroll worksheet simply consumes whatever is active in this catalogue.
    /// </summary>
    public sealed class PayrollElementsForm : XtraForm
    {
        private readonly AppServices _services;

        private PanelControl _toolbar;
        private SimpleButton _addButton;
        private SimpleButton _editButton;
        private SimpleButton _toggleButton;
        private SimpleButton _removeButton;
        private SimpleButton _closeButton;
        private GridControl _grid;
        private GridView _view;

        public PayrollElementsForm(AppServices services)
        {
            _services = services;
            BuildUi();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = T("Element_CatalogTitle");
            LoadData();
        }

        private string T(string key) => _services.Localization.GetString(key);

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 560);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 56 };
            UiTheme.Toolbar(_toolbar);
            _grid = new GridControl { Dock = DockStyle.Fill };
            Controls.Add(_grid);
            Controls.Add(_toolbar);

            _addButton = MakeButton(UiTheme.PrimaryButton, T("Common_Add"), () => AddNew());
            _editButton = MakeButton(UiTheme.SecondaryButton, T("Common_Edit"), () => EditSelected());
            _toggleButton = MakeButton(UiTheme.SecondaryButton, T("Element_ToggleActive"), () => ToggleSelected());
            _removeButton = MakeButton(UiTheme.DangerButton, T("Common_Delete"), () => RemoveSelected());
            _closeButton = MakeButton(UiTheme.SecondaryButton, T("Common_Close"), () => Close());

            LayoutToolbar();
            _toolbar.Resize += (s, e) => LayoutToolbar();

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(_view);
            _view.DoubleClick += (s, e) => EditSelected();
        }

        private SimpleButton MakeButton(System.Func<SimpleButton, SimpleButton> style, string text, System.Action onClick)
        {
            var button = new SimpleButton { Text = text, Height = UiTheme.ButtonHeight, Location = new Point(0, 12) };
            style(button);
            UiTheme.FitButton(button, 110); // size to caption so no label is ever clipped
            button.Click += (s, e) => onClick();
            _toolbar.Controls.Add(button);
            return button;
        }

        private void LayoutToolbar()
        {
            if (_addButton == null)
            {
                return;
            }

            // Left-aligned action group; Close pinned to the right.
            int x = UiTheme.Pad;
            foreach (SimpleButton button in new[] { _addButton, _editButton, _toggleButton, _removeButton })
            {
                button.Location = new Point(x, 12);
                x += button.Width + UiTheme.Gap;
            }

            _closeButton.Location = new Point(_toolbar.Width - UiTheme.Pad - _closeButton.Width, 12);
        }

        private void LoadData()
        {
            var rows = new List<CatalogRow>();
            foreach (PayrollElement element in _services.PayrollElements.GetAll(includeDisabled: true))
            {
                if (element.IsDeleted)
                {
                    continue;
                }

                rows.Add(new CatalogRow
                {
                    Id = element.Id,
                    Name = string.IsNullOrWhiteSpace(element.NameFr) ? element.NameAr : element.NameFr,
                    TypeText = EnumLocalizer.Localize(_services.Localization, element.ElementType),
                    Cotisable = ModeText(element.IsCnasApplicable, element.CnasPercent),
                    Imposable = ModeText(element.IsIrgApplicable, element.IrgPercent),
                    Status = element.IsEnabled ? T("Common_Active") : T("Common_Inactive")
                });
            }

            _grid.DataSource = rows;
            _view.PopulateColumns();
            _view.Columns["Id"].Visible = false;
            _view.Columns["Name"].Caption = T("Element_Name");
            _view.Columns["TypeText"].Caption = T("Payroll_Type");
            _view.Columns["Cotisable"].Caption = T("Element_Cotisable");
            _view.Columns["Imposable"].Caption = T("Element_Imposable");
            _view.Columns["Status"].Caption = T("Element_Status");
            _view.BestFitColumns();
        }

        /// <summary>Renders a CNAS/IRG treatment as Oui / Non / Partiel n%.</summary>
        private string ModeText(bool applicable, decimal? percent)
        {
            if (percent.HasValue && percent.Value > 0m && percent.Value < 100m)
            {
                return T("Element_Partial") + " " + percent.Value.ToString("0.##") + "%";
            }

            return applicable ? T("Common_Yes") : T("Common_No");
        }

        private CatalogRow Selected() => _view.GetFocusedRow() as CatalogRow;

        private void AddNew()
        {
            using (var form = new PayrollElementEditForm(_services, new PayrollElement()))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                Result<long> result = _services.PayrollElements.Create(form.Element);
                if (result.IsSuccess)
                {
                    LoadData();
                }
                else
                {
                    UiHelper.Error(result.Error, T("Common_Error"));
                }
            }
        }

        private void EditSelected()
        {
            CatalogRow row = Selected();
            if (row == null)
            {
                return;
            }

            PayrollElement element = _services.PayrollElements.Get(row.Id);
            if (element == null)
            {
                return;
            }

            using (var form = new PayrollElementEditForm(_services, element))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                Result result = _services.PayrollElements.Update(form.Element);
                if (result.IsSuccess)
                {
                    LoadData();
                }
                else
                {
                    UiHelper.Error(result.Error, T("Common_Error"));
                }
            }
        }

        private void ToggleSelected()
        {
            CatalogRow row = Selected();
            if (row == null)
            {
                return;
            }

            PayrollElement element = _services.PayrollElements.Get(row.Id);
            if (element == null)
            {
                return;
            }

            element.IsEnabled = !element.IsEnabled;
            Result result = _services.PayrollElements.Update(element);
            if (result.IsSuccess)
            {
                LoadData();
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        private void RemoveSelected()
        {
            CatalogRow row = Selected();
            if (row == null)
            {
                return;
            }

            if (!UiHelper.Confirm(T("Msg_DeleteConfirm"), T("Common_Confirm")))
            {
                return;
            }

            Result result = _services.PayrollElements.Delete(row.Id);
            if (result.IsSuccess)
            {
                LoadData();
            }
            else
            {
                UiHelper.Error(result.Error, T("Common_Error"));
            }
        }

        private sealed class CatalogRow
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public string TypeText { get; set; }
            public string Cotisable { get; set; }
            public string Imposable { get; set; }
            public string Status { get; set; }
        }
    }
}
