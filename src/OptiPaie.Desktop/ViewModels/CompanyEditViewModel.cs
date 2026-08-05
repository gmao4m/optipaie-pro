using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Edit dialog view model for a company, including logo upload.</summary>
    public sealed class CompanyEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly bool _isNew;

        public CompanyEditViewModel(AppServices services, Company company, bool isNew)
        {
            _services = services;
            _isNew = isNew;
            Company = company;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            PickLogoCommand = new RelayCommand(PickLogo);
            ClearLogoCommand = new RelayCommand(ClearLogo);
            AddDepartmentCommand = new RelayCommand(AddDepartment);

            LoadDepartments();
        }

        public Company Company { get; }

        // ---------------------------------------------------------------- departments

        /// <summary>The company's departments — the backbone of the evaluation module and the employee dropdown.</summary>
        public ObservableCollection<DepartmentRowViewModel> Departments { get; } = new ObservableCollection<DepartmentRowViewModel>();

        private string _newDepartmentName = string.Empty;

        /// <summary>The name typed into the "add department" box.</summary>
        public string NewDepartmentName
        {
            get => _newDepartmentName;
            set { if (_newDepartmentName != value) { _newDepartmentName = value; Raise(); } }
        }

        /// <summary>Departments are FK'd to a saved company, so the list is live only once it exists.</summary>
        public bool CanManageDepartments => Company.Id > 0;

        /// <summary>Shown in place of the editor for a brand-new, unsaved company.</summary>
        public bool ShowDepartmentHint => Company.Id <= 0;

        public ICommand AddDepartmentCommand { get; }

        private void LoadDepartments()
        {
            Departments.Clear();
            if (!CanManageDepartments)
            {
                return;
            }

            foreach (Department department in _services.Departments.GetForCompany(Company.Id))
            {
                Departments.Add(new DepartmentRowViewModel(department, RenameDepartment, RemoveDepartment));
            }
        }

        private void AddDepartment()
        {
            if (string.IsNullOrWhiteSpace(NewDepartmentName))
            {
                return;
            }

            Result<long> result = _services.Departments.Save(
                new Department { CompanyId = Company.Id, Name = NewDepartmentName.Trim() });
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
                return;
            }

            NewDepartmentName = string.Empty;
            LoadDepartments();
        }

        private void RenameDepartment(DepartmentRowViewModel row)
        {
            Result<long> result = _services.Departments.Save(row.ToEntity());
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
            }

            LoadDepartments();
        }

        private void RemoveDepartment(DepartmentRowViewModel row)
        {
            if (!Dialogs.Confirm($"Supprimer le département « {row.Name} » ?"))
            {
                return;
            }

            Result result = _services.Departments.Remove(row.Id);
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
            }

            LoadDepartments();
        }

        public string Title => _isNew ? "Nouvelle entreprise" : "Modifier l'entreprise";

        /// <summary>Raised so the view refreshes the logo preview after a change.</summary>
        public byte[] Logo
        {
            get => Company.Logo;
            private set { Company.Logo = value; Raise(); }
        }

        /// <summary>BTPH-sector opt-in. Turning it off also switches CACOBATPH off.</summary>
        public bool BtphSector
        {
            get => Company.BtphSector;
            set
            {
                if (Company.BtphSector == value) return;
                Company.BtphSector = value;
                if (!value) CacobatphEnabled = false; // CACOBATPH is meaningless without the sector
                Raise();
                Raise(nameof(CanEnableCacobatph));
            }
        }

        /// <summary>The optional CACOBATPH contributions/declarations toggle (BTPH only).</summary>
        public bool CacobatphEnabled
        {
            get => Company.CacobatphEnabled;
            set { if (Company.CacobatphEnabled != value) { Company.CacobatphEnabled = value; Raise(); } }
        }

        /// <summary>CACOBATPH can only be enabled once the company is flagged BTPH.</summary>
        public bool CanEnableCacobatph => BtphSector;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand PickLogoCommand { get; }
        public ICommand ClearLogoCommand { get; }

        public Action<bool> RequestClose { get; set; }

        private void PickLogo()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Title = "Choisir un logo"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Logo = File.ReadAllBytes(dialog.FileName);
                }
                catch
                {
                    Dialogs.Error("Impossible de lire ce fichier image.");
                }
            }
        }

        private void ClearLogo() => Logo = null;

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Company.NameFr))
            {
                Dialogs.Error("Le nom de l'entreprise est obligatoire.");
                return;
            }

            if (string.IsNullOrWhiteSpace(Company.Currency))
            {
                Company.Currency = "DZD";
            }

            if (_isNew)
            {
                // Safety net (the list also guards): a Mono-société license/trial caps
                // the app at one company; adding more needs a Multi-sociétés license.
                if (!_services.LicenseGate.CanAddCompany(_services.Companies.GetAll().Count))
                {
                    Dialogs.Info(
                        OptiPaie.Desktop.Localization.TranslationSource.Instance["License_CompanyLimitReached"],
                        OptiPaie.Desktop.Localization.TranslationSource.Instance["License_CompanyLimitTitle"]);
                    return;
                }

                Result<long> result = _services.Companies.Create(Company);
                if (!result.IsSuccess)
                {
                    Dialogs.Error(result.Error);
                    return;
                }
            }
            else
            {
                Result result = _services.Companies.Update(Company);
                if (!result.IsSuccess)
                {
                    Dialogs.Error(result.Error);
                    return;
                }
            }

            RequestClose?.Invoke(true);
        }
    }
}
