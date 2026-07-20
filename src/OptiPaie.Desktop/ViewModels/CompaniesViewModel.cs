using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Companies module: searchable list + profile, with add/edit/delete.</summary>
    public sealed class CompaniesViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private readonly List<Company> _all = new List<Company>();

        private Company _selected;
        private string _search = string.Empty;

        public CompaniesViewModel(AppServices services)
        {
            _services = services;
            Companies = new ObservableCollection<Company>();

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit);
            DeleteCommand = new RelayCommand(Delete);
        }

        public ObservableCollection<Company> Companies { get; }

        public Company SelectedCompany
        {
            get => _selected;
            set { if (Set(ref _selected, value)) Raise(nameof(HasSelection)); }
        }

        public bool HasSelection => _selected != null;

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) ApplyFilter(); }
        }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public void OnActivated() => LoadCompanies();

        private void LoadCompanies()
        {
            long? keepId = _selected?.Id;

            _all.Clear();
            _all.AddRange(_services.Companies.GetAll());
            ApplyFilter();

            if (keepId.HasValue)
            {
                SelectedCompany = Companies.FirstOrDefault(c => c.Id == keepId) ?? Companies.FirstOrDefault();
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<Company> filtered = _all;
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string q = _search.Trim().ToLowerInvariant();
                filtered = _all.Where(c =>
                    (c.NameFr ?? string.Empty).ToLowerInvariant().Contains(q)
                    || (c.Nif ?? string.Empty).ToLowerInvariant().Contains(q));
            }

            Companies.Clear();
            foreach (Company c in filtered)
            {
                Companies.Add(c);
            }

            if (SelectedCompany == null || Companies.All(c => c.Id != SelectedCompany.Id))
            {
                SelectedCompany = Companies.FirstOrDefault();
            }
        }

        private void New()
        {
            var company = new Company { Currency = "DZD" };
            if (Dialogs.ShowCompanyEditor(new CompanyEditViewModel(_services, company, true)))
            {
                LoadCompanies();
            }
        }

        private void Edit()
        {
            if (SelectedCompany == null)
            {
                return;
            }

            Company full = _services.Companies.Get(SelectedCompany.Id);
            if (full == null)
            {
                return;
            }

            if (Dialogs.ShowCompanyEditor(new CompanyEditViewModel(_services, full, false)))
            {
                LoadCompanies();
            }
        }

        private void Delete()
        {
            if (SelectedCompany == null)
            {
                return;
            }

            if (!Dialogs.Confirm("Voulez-vous vraiment supprimer cette entreprise ?"))
            {
                return;
            }

            Result result = _services.Companies.Delete(SelectedCompany.Id);
            if (result.IsSuccess)
            {
                LoadCompanies();
            }
            else
            {
                Dialogs.Error(result.Error);
            }
        }
    }
}
