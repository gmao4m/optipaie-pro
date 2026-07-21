using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A certificate type with its French label.</summary>
    public sealed class CertificateTypeOption
    {
        public CertificateTypeOption(CertificateType value) { Value = value; Label = CertificateLabels.Type(value); }
        public CertificateType Value { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Creates or edits a certificate. Only the metadata is captured — the body is
    /// rendered live from the shared records — except for a free "Document libre".
    /// </summary>
    public sealed class CertificateEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly WorkCertificate _certificate;

        private Employee _selectedEmployee;
        private CertificateTypeOption _selectedType;
        private DateTime _issueDate;
        private string _reference;
        private string _purpose;
        private string _body;

        public CertificateEditViewModel(AppServices services, IReadOnlyList<Employee> employees, WorkCertificate existing)
        {
            _services = services;
            _certificate = existing ?? new WorkCertificate();

            foreach (Employee e in employees) Employees.Add(e);
            foreach (CertificateType t in Enum.GetValues(typeof(CertificateType))) Types.Add(new CertificateTypeOption(t));

            if (existing != null)
            {
                _selectedEmployee = Employees.FirstOrDefault(e => e.Id == existing.EmployeeId);
                _selectedType = Types.FirstOrDefault(o => o.Value == existing.Type);
                _issueDate = existing.IssueDate;
                _reference = existing.Reference;
                _purpose = existing.Purpose;
                _body = existing.Body;
                Title = "Modifier l'attestation";
            }
            else
            {
                _selectedEmployee = Employees.FirstOrDefault();
                _selectedType = Types.FirstOrDefault(o => o.Value == CertificateType.WorkCertificate);
                _issueDate = DateTime.Today;
                _purpose = "pour servir et valoir ce que de droit";
                Title = "Nouvelle attestation";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<CertificateTypeOption> Types { get; } = new ObservableCollection<CertificateTypeOption>();

        public bool CanChooseEmployee => _certificate.Id == 0;

        public Employee SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }

        public CertificateTypeOption SelectedType
        {
            get => _selectedType;
            set { if (Set(ref _selectedType, value)) Raise(nameof(IsCustom)); }
        }

        /// <summary>A free document exposes the body editor; the others render it live.</summary>
        public bool IsCustom => _selectedType != null && _selectedType.Value == CertificateType.Custom;

        public DateTime IssueDate { get => _issueDate; set => Set(ref _issueDate, value); }
        public string Reference { get => _reference; set => Set(ref _reference, value); }
        public string Purpose { get => _purpose; set => Set(ref _purpose, value); }
        public string Body { get => _body; set => Set(ref _body, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (_selectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return;
            }

            if (_selectedType == null)
            {
                Dialogs.Error("Sélectionnez un type d'attestation.");
                return;
            }

            _certificate.EmployeeId = _selectedEmployee.Id;
            _certificate.Type = _selectedType.Value;
            _certificate.IssueDate = _issueDate;
            _certificate.Reference = _reference;
            _certificate.Purpose = _purpose;
            _certificate.Body = _body;

            Result<long> result = _services.Certificates.Save(_certificate);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }
}
