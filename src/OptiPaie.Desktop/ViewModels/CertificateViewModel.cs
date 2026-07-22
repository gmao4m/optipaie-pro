using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One issued certificate as shown in the list.</summary>
    public sealed class CertificateRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public CertificateRowViewModel(CertificateSummary summary)
        {
            Summary = summary;
        }

        public CertificateSummary Summary { get; }
        public long Id => Summary.CertificateId;
        public string EmployeeName => Summary.EmployeeName;
        public string TypeLabel => CertificateLabels.Type(Summary.Type);
        public string Reference => Summary.Reference;
        public string IssueText => Summary.IssueDate.ToString("dd/MM/yyyy", Fr);
        public string Purpose => Summary.Purpose;
    }

    /// <summary>French labels for the certificate type.</summary>
    public static class CertificateLabels
    {
        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        public static string Type(CertificateType type)
        {
            switch (type)
            {
                case CertificateType.WorkCertificate: return L("Enum_CertType_WorkCertificate");
                case CertificateType.WorkExperience: return L("Enum_CertType_WorkExperience");
                case CertificateType.SalaryCertificate: return L("Enum_CertType_SalaryCertificate");
                default: return L("Enum_CertType_Free");
            }
        }
    }

    /// <summary>
    /// Attestations — HR documents issued to employees. The body is rendered live from
    /// the shared employee and company records, so an attestation always reflects the
    /// current data (name, poste, salaire, ancienneté).
    /// </summary>
    public sealed class CertificateViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _selectedCompany;
        private CertificateRowViewModel _selectedCertificate;
        private string _countText = "0";
        private string _statusMessage = string.Empty;

        public CertificateViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedCertificate != null);
            PdfCommand = new RelayCommand(ExportPdf, () => _selectedCertificate != null);
            DeleteCommand = new RelayCommand(Delete, () => _selectedCertificate != null);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<CertificateRowViewModel> Certificates { get; } = new ObservableCollection<CertificateRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public CertificateRowViewModel SelectedCertificate
        {
            get => _selectedCertificate;
            set => Set(ref _selectedCertificate, value);
        }

        public string CountText { get => _countText; private set => Set(ref _countText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand PdfCommand { get; }
        public ICommand DeleteCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Load();
        }

        private void Load()
        {
            Certificates.Clear();
            if (_selectedCompany == null)
            {
                CountText = "0";
                return;
            }

            foreach (CertificateSummary summary in _services.Certificates.GetByCompany(_selectedCompany.Id))
            {
                Certificates.Add(new CertificateRowViewModel(summary));
            }

            SelectedCertificate = Certificates.FirstOrDefault();
            CountText = Certificates.Count.ToString();
            StatusMessage = Certificates.Count + " attestation(s) délivrée(s)";
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            if (employees.Count == 0)
            {
                Dialogs.Info("Aucun employé actif dans cette entreprise.");
                return;
            }

            ShowEditor(new CertificateEditViewModel(_services, employees, null));
        }

        private void Edit()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            ShowEditor(new CertificateEditViewModel(_services, employees, _services.Certificates.Get(_selectedCertificate.Id)));
        }

        private void ShowEditor(CertificateEditViewModel vm)
        {
            var window = new CertificateEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Attestation enregistrée.";
            }
        }

        private void ExportPdf()
        {
            CertificateRenderModel model = _services.Certificates.BuildRender(_selectedCertificate.Id);
            if (model == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = (_selectedCertificate.Reference ?? "Attestation") + ".pdf"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var document = new CertificateDocument(model);
                Document.Create(document.Compose).GeneratePdf(dialog.FileName);
                try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex) { _services.Logger.Warn("Ouverture PDF impossible : " + ex.Message); }
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF attestation", ex);
                Dialogs.Error("Impossible de générer le PDF : " + ex.Message);
            }
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement cette attestation ?"))
            {
                return;
            }

            Result result = _services.Certificates.Delete(_selectedCertificate.Id);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = "Attestation supprimée.";
        }
    }
}
