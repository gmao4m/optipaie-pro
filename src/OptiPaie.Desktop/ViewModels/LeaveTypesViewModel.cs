using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Leave;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A payment category with its Arabic label (for the combo).</summary>
    public sealed class PaymentCategoryOption
    {
        public PaymentCategoryOption(PaymentCategory value)
        {
            Value = value;
            Label = TranslationSource.Instance[LeaveTypeResolver.PaymentKey(value)];
        }

        public PaymentCategory Value { get; }
        public string Label { get; }
    }

    /// <summary>Row of the leave-types catalogue with its two indicators visible.</summary>
    public sealed class LeaveTypeRowViewModel
    {
        public LeaveTypeRowViewModel(LeaveTypeDefinition type)
        {
            Type = type;
            PaymentLabel = TranslationSource.Instance[LeaveTypeResolver.PaymentKey(type.PaymentCategory)];
            DecrementsLabel = TranslationSource.Instance[LeaveTypeResolver.DecrementKey(type.DecrementsAnnualBalance)];
            BaseLabel = LeaveLabels.Type(type.BaseType);
        }

        public LeaveTypeDefinition Type { get; }
        public string LabelAr => Type.LabelAr;
        public string BaseLabel { get; }
        public string PaymentLabel { get; }
        public string DecrementsLabel { get; }
        public bool IsActive => Type.IsActive;
        public string DurationText => Type.LegalDurationDays.HasValue
            ? Type.LegalDurationDays.Value.ToString("0.##", CultureInfo.InvariantCulture) : "—";
    }

    /// <summary>Manage configurable leave types for a company: create, edit, (de)activate.</summary>
    public sealed class LeaveTypesViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _companyId;

        private LeaveTypeRowViewModel _selected;
        private long _editId;
        private string _editLabelAr = string.Empty;
        private string _editLabelFr = string.Empty;
        private string _editLegalDuration = string.Empty;
        private LeaveTypeOption _editBaseType;
        private PaymentCategoryOption _editCategory;
        private bool _editDecrements;
        private bool _editOnce;
        private bool _editActive = true;
        private string _statusMessage = string.Empty;

        public LeaveTypesViewModel(AppServices services, long companyId)
        {
            _services = services;
            _companyId = companyId;

            foreach (LeaveType t in Enum.GetValues(typeof(LeaveType))) BaseTypes.Add(new LeaveTypeOption(t));
            foreach (PaymentCategory c in Enum.GetValues(typeof(PaymentCategory))) Categories.Add(new PaymentCategoryOption(c));
            _editBaseType = BaseTypes.FirstOrDefault();
            _editCategory = Categories.FirstOrDefault();

            NewCommand = new RelayCommand(NewType);
            SaveCommand = new RelayCommand(Save);
            DeactivateCommand = new RelayCommand(Deactivate, () => _editId > 0 && _editActive);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public ObservableCollection<LeaveTypeRowViewModel> Types { get; } = new ObservableCollection<LeaveTypeRowViewModel>();
        public ObservableCollection<LeaveTypeOption> BaseTypes { get; } = new ObservableCollection<LeaveTypeOption>();
        public ObservableCollection<PaymentCategoryOption> Categories { get; } = new ObservableCollection<PaymentCategoryOption>();

        public LeaveTypeRowViewModel SelectedType
        {
            get => _selected;
            set { if (Set(ref _selected, value) && value != null) Populate(value.Type); }
        }

        public string EditLabelAr { get => _editLabelAr; set => Set(ref _editLabelAr, value); }
        public string EditLabelFr { get => _editLabelFr; set => Set(ref _editLabelFr, value); }
        public string EditLegalDuration { get => _editLegalDuration; set => Set(ref _editLegalDuration, value); }
        public LeaveTypeOption EditBaseType { get => _editBaseType; set => Set(ref _editBaseType, value); }
        public PaymentCategoryOption EditCategory { get => _editCategory; set => Set(ref _editCategory, value); }
        public bool EditDecrements { get => _editDecrements; set => Set(ref _editDecrements, value); }
        public bool EditOnce { get => _editOnce; set => Set(ref _editOnce, value); }
        public bool EditActive { get => _editActive; set => Set(ref _editActive, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeactivateCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Types.Clear();
            foreach (LeaveTypeDefinition t in _services.LeaveTypes.GetAll(_companyId).OrderBy(t => t.SortOrder))
                Types.Add(new LeaveTypeRowViewModel(t));
            StatusMessage = Types.Count + " type(s)";
        }

        private void Populate(LeaveTypeDefinition t)
        {
            _editId = t.Id;
            EditLabelAr = t.LabelAr;
            EditLabelFr = t.LabelFr;
            EditLegalDuration = t.LegalDurationDays.HasValue ? t.LegalDurationDays.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
            EditBaseType = BaseTypes.FirstOrDefault(b => b.Value == t.BaseType) ?? BaseTypes.FirstOrDefault();
            EditCategory = Categories.FirstOrDefault(c => c.Value == t.PaymentCategory) ?? Categories.FirstOrDefault();
            EditDecrements = t.DecrementsAnnualBalance;
            EditOnce = t.OncePerCareer;
            EditActive = t.IsActive;
            CommandManager.InvalidateRequerySuggested();
        }

        private void NewType()
        {
            _editId = 0;
            EditLabelAr = EditLabelFr = EditLegalDuration = string.Empty;
            EditBaseType = BaseTypes.FirstOrDefault(b => b.Value == LeaveType.Special) ?? BaseTypes.FirstOrDefault();
            EditCategory = Categories.FirstOrDefault(c => c.Value == PaymentCategory.EmployerPaid) ?? Categories.FirstOrDefault();
            EditDecrements = false;
            EditOnce = false;
            EditActive = true;
            SelectedType = null;
            CommandManager.InvalidateRequerySuggested();
        }

        private void Save()
        {
            decimal? duration = null;
            if (!string.IsNullOrWhiteSpace(_editLegalDuration))
            {
                if (!decimal.TryParse(_editLegalDuration, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) || d < 0m)
                {
                    Dialogs.Error("Durée légale invalide.");
                    return;
                }
                duration = d;
            }

            var type = new LeaveTypeDefinition
            {
                Id = _editId,
                CompanyId = _companyId,
                Code = _editId > 0 ? null : "CUSTOM",
                LabelAr = _editLabelAr,
                LabelFr = _editLabelFr,
                BaseType = _editBaseType != null ? _editBaseType.Value : LeaveType.Special,
                PaymentCategory = _editCategory != null ? _editCategory.Value : PaymentCategory.EmployerPaid,
                DecrementsAnnualBalance = _editDecrements,
                LegalDurationDays = duration,
                OncePerCareer = _editOnce,
                IsActive = _editActive
            };

            // Keep the existing code when editing.
            if (_editId > 0)
            {
                LeaveTypeDefinition existing = _services.LeaveTypes.Get(_editId);
                if (existing != null) { type.Code = existing.Code; type.SortOrder = existing.SortOrder; type.CompanyId = existing.CompanyId; }
            }

            Result<long> result = _services.LeaveTypes.Save(type);
            if (result.IsFailure) { Dialogs.Error(result.Error); return; }

            _editId = result.Value;
            Load();
            StatusMessage = "Type enregistré.";
        }

        private void Deactivate()
        {
            if (_editId <= 0) return;
            if (!Dialogs.Confirm("Désactiver ce type ? Il ne sera plus proposé à la saisie.")) return;
            Result r = _services.LeaveTypes.SetActive(_editId, false);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            EditActive = false;
            Load();
            StatusMessage = "Type désactivé.";
        }
    }
}
