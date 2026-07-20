using System;
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
        }

        public Company Company { get; }

        public string Title => _isNew ? "Nouvelle entreprise" : "Modifier l'entreprise";

        /// <summary>Raised so the view refreshes the logo preview after a change.</summary>
        public byte[] Logo
        {
            get => Company.Logo;
            private set { Company.Logo = value; Raise(); }
        }

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
