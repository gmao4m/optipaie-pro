using System;
using System.Collections.Generic;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Edit dialog for a payroll item type. Deliberately minimal: Libellé, Nature
    /// (Gain / Retenue), Cotisable (Oui / Non), and Imposable as a configurable share
    /// (100 / 50 / 30 / 10 / 0 %) for partially-taxable items. Maps onto the existing
    /// PayrollElement — no data-model change, no engine change.
    /// </summary>
    public sealed class PayrollItemEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly PayrollElement _element;
        private readonly bool _isNew;

        public PayrollItemEditViewModel(AppServices services, PayrollElement element, bool isNew)
        {
            _services = services;
            _element = element;
            _isNew = isNew;

            Natures = new List<EnumOption>
            {
                new EnumOption(true, L("ElementType_Gain")),
                new EnumOption(false, L("ElementType_Deduction"))
            };

            ImposableOptions = new List<EnumOption>
            {
                new EnumOption(100m, L("PayItem_Imposable_100")),
                new EnumOption(50m, L("PayItem_Imposable_50")),
                new EnumOption(30m, L("PayItem_Imposable_30")),
                new EnumOption(10m, L("PayItem_Imposable_10")),
                new EnumOption(0m, L("PayItem_Imposable_0"))
            };

            Libelle = element.NameFr;
            // Show the Arabic label only when it is a real translation (legacy data copied the French
            // name into NameAr, so an equal value means "not translated yet").
            LibelleAr = string.Equals(element.NameAr, element.NameFr, StringComparison.Ordinal) ? string.Empty : element.NameAr;
            IsGain = element.ElementType != ElementType.Deduction;
            Cotisable = element.IsCnasApplicable;
            ImposablePercent = element.IrgPercent.HasValue
                ? element.IrgPercent.Value
                : (element.IsIrgApplicable ? 100m : 0m);

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public List<EnumOption> Natures { get; }
        public List<EnumOption> ImposableOptions { get; }

        public string Title => _isNew ? L("Payroll_NewElement") : L("PayItem_Edit");

        public string Libelle { get; set; }
        /// <summary>Optional Arabic label; when empty the French label is used on the Arabic payslip.</summary>
        public string LibelleAr { get; set; }
        public bool IsGain { get; set; }
        public bool Cotisable { get; set; }
        public decimal ImposablePercent { get; set; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public Action<bool> RequestClose { get; set; }

        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Libelle))
            {
                Dialogs.Error(L("PayItem_LabelRequired"));
                return;
            }

            string name = Libelle.Trim();
            _element.NameFr = name;
            _element.NameAr = string.IsNullOrWhiteSpace(LibelleAr) ? name : LibelleAr.Trim();
            _element.ElementType = IsGain ? ElementType.Gain : ElementType.Deduction;
            _element.IsIncludedInGross = IsGain;

            _element.IsCnasApplicable = Cotisable;
            _element.CnasPercent = null;

            _element.IsIrgApplicable = ImposablePercent > 0m;
            _element.IrgPercent = (ImposablePercent >= 100m || ImposablePercent <= 0m) ? (decimal?)null : ImposablePercent;

            _element.CalculationMethod = CalculationMethod.QuantityUnitPrice;
            _element.DefaultAmount = null;
            _element.DefaultRate = null;
            _element.DefaultQuantity = null;
            _element.DefaultUnitPrice = null;
            _element.IsEditable = true;
            _element.IsPrintable = true;
            _element.Periodicity = ElementPeriodicity.Monthly;
            _element.IsSystem = false;
            _element.IsDeleted = false;

            if (_isNew)
            {
                _element.IsEnabled = true;
                Result<long> created = _services.PayrollElements.Create(_element);
                if (!created.IsSuccess)
                {
                    Dialogs.Error(created.Error);
                    return;
                }
            }
            else
            {
                Result updated = _services.PayrollElements.Update(_element);
                if (!updated.IsSuccess)
                {
                    Dialogs.Error(updated.Error);
                    return;
                }
            }

            RequestClose?.Invoke(true);
        }
    }
}
