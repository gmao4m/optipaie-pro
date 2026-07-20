using System;
using System.Globalization;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// One editable worksheet line (base salary or an element), modelled like an
    /// accountant's payroll grid: Libellé · Base · Taux · Gain · Retenue.
    ///
    /// Taux supports two modes, auto-detected from the text the user types:
    ///   "2"    → multiplier   → Amount = Base × 2        (23 000 × 2 = 46 000)
    ///   "10%"  → percentage   → Amount = Base × 10 / 100 (10 000 × 10% = 1 000)
    /// Empty Taux → Amount = Base. Everything refreshes instantly.
    /// </summary>
    public sealed class PayrollLineVM : ObservableObject
    {
        private readonly Action _changed;
        private string _rubrique;
        private decimal? _base;
        private string _taux;

        public PayrollLineVM(Action changed)
        {
            _changed = changed;
        }

        public bool IsBaseSalary { get; set; }
        public bool IsManual { get; set; }
        public long ElementId { get; set; }
        public bool IsGain { get; set; }

        public string Rubrique
        {
            get => _rubrique;
            set => Set(ref _rubrique, value);
        }

        public decimal? Base
        {
            get => _base;
            set { if (Set(ref _base, value)) { RaiseAmounts(); _changed?.Invoke(); } }
        }

        /// <summary>Rate text — "2" (multiplier) or "10%" (percentage).</summary>
        public string Taux
        {
            get => _taux;
            set { if (Set(ref _taux, value)) { RaiseAmounts(); _changed?.Invoke(); } }
        }

        public decimal Amount
        {
            get
            {
                decimal b = _base ?? 0m;
                decimal? factor = ParseFactor(_taux);
                return factor.HasValue ? b * factor.Value : b;
            }
        }

        public decimal? Gain => IsGain ? Amount : (decimal?)null;
        public decimal? Retenue => IsGain ? (decimal?)null : Amount;

        public bool CanRemove => !IsBaseSalary;

        private void RaiseAmounts()
        {
            Raise(nameof(Amount));
            Raise(nameof(Gain));
            Raise(nameof(Retenue));
        }

        /// <summary>Turns the Taux text into a multiplier factor: "10%" → 0.10, "2" → 2, empty → none.</summary>
        public static decimal? ParseFactor(string taux)
        {
            if (string.IsNullOrWhiteSpace(taux))
            {
                return null;
            }

            string s = taux.Trim();
            bool percent = s.EndsWith("%");
            string number = (percent ? s.Substring(0, s.Length - 1) : s).Trim().Replace(" ", "").Replace(',', '.');

            if (decimal.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
            {
                return percent ? value / 100m : value;
            }

            return null;
        }
    }
}
