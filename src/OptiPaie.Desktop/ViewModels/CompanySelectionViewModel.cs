using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The blocking startup company gate shown when the choice is not automatic: several
    /// companies (pick one) or none yet (create the first). Clicking a company opens it
    /// directly — there is no "validate" button. Nothing else in the app loads until a
    /// company is chosen here, so no data screen is ever shown without an active company.
    /// </summary>
    public sealed class CompanySelectionViewModel : ObservableObject
    {
        private readonly AppServices _services;

        public CompanySelectionViewModel(AppServices services)
        {
            _services = services;
            SelectCommand = new RelayCommand(p => Select(p as Company));
            CreateCommand = new RelayCommand(Create);
            Reload();
        }

        /// <summary>The companies to choose from (each is clickable and opens directly).</summary>
        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();

        /// <summary>True when there is at least one company to list.</summary>
        public bool HasCompanies => Companies.Count > 0;

        /// <summary>True when no company exists yet — the empty state that invites creation.</summary>
        public bool IsEmpty => Companies.Count == 0;

        /// <summary>Pick a company (single click) — no separate confirm step.</summary>
        public ICommand SelectCommand { get; }

        /// <summary>Create the first (or an additional) company from the gate itself.</summary>
        public ICommand CreateCommand { get; }

        /// <summary>Raised once a company has been chosen, so the window closes and the app opens it.</summary>
        public Action CloseRequested { get; set; }

        private void Select(Company company)
        {
            if (company == null)
            {
                return;
            }

            // This is the ONE place a company becomes active at startup; the app then builds
            // its shell around it. No data is loaded before this point.
            _services.CompanyContext.Active = company;
            CloseRequested?.Invoke();
        }

        private void Create()
        {
            var company = new Company { Currency = "DZD" };
            if (!Dialogs.ShowCompanyEditor(new CompanyEditViewModel(_services, company, true)))
            {
                return;
            }

            Reload();

            // If that was the very first company, enter it directly — no extra click.
            if (Companies.Count == 1)
            {
                Select(Companies[0]);
            }
        }

        private void Reload()
        {
            Companies.Clear();
            foreach (Company c in _services.Companies.GetAll())
            {
                Companies.Add(c);
            }

            Raise(nameof(HasCompanies));
            Raise(nameof(IsEmpty));
        }
    }
}
