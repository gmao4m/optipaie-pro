using System.Collections.Generic;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.App.Modules.Companies
{
    /// <summary>Companies management: searchable grid with add/edit/delete.</summary>
    public sealed class CompaniesControl : BaseModuleControl
    {
        private PanelControl _toolbar;
        private SimpleButton _addButton;
        private SimpleButton _editButton;
        private SimpleButton _deleteButton;
        private SimpleButton _refreshButton;
        private SimpleButton _searchButton;
        private GridControl _grid;
        private GridView _view;
        private EmptyStatePanel _emptyState;
        private LabelControl _pageTitle;
        private LabelControl _pageSubtitle;

        protected override void BuildUi()
        {
            UiTheme.Canvasize(this);

            _toolbar = new PanelControl { Dock = DockStyle.Top, Height = 56 };
            UiTheme.Toolbar(_toolbar);
            _toolbar.Resize += (s, e) => LayoutToolbar();
            Controls.Add(_grid = new GridControl { Dock = DockStyle.Fill });
            Controls.Add(_toolbar);

            _view = new GridView(_grid);
            _grid.MainView = _view;
            _view.OptionsBehavior.Editable = false;
            _view.OptionsFind.AlwaysVisible = false;
            UiTheme.StyleGrid(_view);
            _view.DoubleClick += (s, e) => EditSelected();

            _view.Columns.AddVisible("NameFr");
            _view.Columns.AddVisible("NameAr");
            _view.Columns.AddVisible("LegalForm");
            _view.Columns.AddVisible("Phone");
            _view.Columns.AddVisible("Email");
            _view.Columns.AddVisible("Nif");

            _addButton = UiTheme.PrimaryButton(AddButton(16, 150, () => AddNew()));
            _editButton = UiTheme.SecondaryButton(AddButton(172, 130, () => EditSelected()));
            _deleteButton = UiTheme.DangerButton(AddButton(308, 130, () => DeleteSelected()));
            _refreshButton = UiTheme.SecondaryButton(AddButton(444, 130, () => LoadData()));
            _searchButton = UiTheme.SecondaryButton(AddButton(580, 130, () => _view.ShowFindPanel()));

            _emptyState = new EmptyStatePanel { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_emptyState);
            _emptyState.Configure(T("Empty_NoCompanies"), T("Empty_CreateFirst"), AddNew);

            AddPageHeader(out _pageTitle, out _pageSubtitle);
        }

        private SimpleButton AddButton(int x, int width, System.Action onClick)
        {
            var button = new SimpleButton { Location = new System.Drawing.Point(x, 12), Width = width, Height = UiTheme.ButtonHeight };
            button.Click += (s, e) => onClick();
            _toolbar.Controls.Add(button);
            return button;
        }

        public override void Localize()
        {
            _pageTitle.Text = T("Module_Companies");
            _pageSubtitle.Text = T("Companies_Subtitle");

            _addButton.Text = T("Common_Add");
            _editButton.Text = T("Common_Edit");
            _deleteButton.Text = T("Common_Delete");
            _refreshButton.Text = T("Common_Refresh");
            _searchButton.Text = T("Common_Search");

            _view.Columns["NameFr"].Caption = T("Company_NameFr");
            _view.Columns["NameAr"].Caption = T("Company_NameAr");
            _view.Columns["LegalForm"].Caption = T("Company_LegalForm");
            _view.Columns["Phone"].Caption = T("Company_Phone");
            _view.Columns["Email"].Caption = T("Company_Email");
            _view.Columns["Nif"].Caption = T("Company_Nif");

            // Size every button to its caption in the active language (no label ever
            // clipped), then position them from the leading edge (left in fr, right in ar).
            foreach (SimpleButton button in new[] { _addButton, _editButton, _deleteButton, _refreshButton, _searchButton })
            {
                UiTheme.FitButton(button, 110);
            }

            LayoutToolbar();
        }

        private void LayoutToolbar()
        {
            if (_addButton == null)
            {
                return;
            }

            bool rtl = L.IsRightToLeft;
            int bx = 16;
            foreach (SimpleButton button in new[] { _addButton, _editButton, _deleteButton, _refreshButton, _searchButton })
            {
                button.Location = new System.Drawing.Point(UiTheme.LeadX(_toolbar.Width, bx, button.Width, rtl), 12);
                bx += button.Width + UiTheme.Gap;
            }
        }

        public override void OnActivated()
        {
            LoadData();
        }

        public override void OnNew()
        {
            AddNew();
        }

        public override void OnDelete()
        {
            DeleteSelected();
        }

        public override void OnFind()
        {
            _view.ShowFindPanel();
        }

        private void LoadData()
        {
            IReadOnlyList<Company> companies = Services.Companies.GetAll();
            _grid.DataSource = new List<Company>(companies);

            _emptyState.Visible = companies.Count == 0;
            if (_emptyState.Visible)
            {
                _emptyState.BringToFront();
            }
        }

        private Company Selected()
        {
            return _view.GetFocusedRow() as Company;
        }

        private void AddNew()
        {
            using (var form = new CompanyEditForm(Services, new Company(), true))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Result<long> result = Services.Companies.Create(form.Company);
                    HandleResult(result);
                    if (result.IsSuccess)
                    {
                        new RecentItemsService(Services.Settings).Record(RecentItemsService.KindCompany, result.Value, form.Company.NameFr);
                    }
                }
            }
        }

        private void EditSelected()
        {
            Company company = Selected();
            if (company == null)
            {
                return;
            }

            new RecentItemsService(Services.Settings).Record(RecentItemsService.KindCompany, company.Id, company.NameFr);

            using (var form = new CompanyEditForm(Services, company, false))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Result result = Services.Companies.Update(form.Company);
                    HandleResult(result);
                }
            }
        }

        private void DeleteSelected()
        {
            Company company = Selected();
            if (company == null)
            {
                return;
            }

            if (!UiHelper.Confirm(T("Msg_DeleteConfirm"), T("Common_Confirm")))
            {
                return;
            }

            HandleResult(Services.Companies.Delete(company.Id));
        }

        private void HandleResult(Result result)
        {
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
}
