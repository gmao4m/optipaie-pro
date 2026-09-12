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
        private string _base;
        private string _taux;

        public PayrollLineVM(Action changed)
        {
            _changed = changed;
        }

        public bool IsBaseSalary { get; set; }
        public bool IsManual { get; set; }

        /// <summary>
        /// True for the automatic "Remboursement prêt" line added by the Loans module.
        /// The engine still treats it as an ordinary manual deduction; this flag only
        /// tells the worksheet to record the recovery against the loan when payroll is
        /// saved.
        /// </summary>
        public bool IsLoan { get; set; }
        public long ElementId { get; set; }
        public bool IsGain { get; set; }

        public string Rubrique
        {
            get => _rubrique;
            set => Set(ref _rubrique, value);
        }

        /// <summary>
        /// Base as free text — bound directly to the grid (no value converter), exactly like
        /// <see cref="Taux"/>. A raw string never rejects a keystroke, so the comma (26,5) or dot
        /// (26.5) is always accepted whatever the Windows locale; the numeric value is parsed on the
        /// fly by <see cref="BaseValue"/>. (A decimal binding + converter got the separator eaten
        /// mid-typing inside the DataGrid — the exact blocker the client hit.)
        /// </summary>
        public string Base
        {
            get => _base;
            set { if (Set(ref _base, value)) { _engineAmount = null; RaiseAmounts(); _changed?.Invoke(); } }
        }

        /// <summary>The Base text as a number — comma or dot accepted; 0 when blank/invalid.</summary>
        public decimal BaseValue =>
            OptiPaie.Common.Text.FlexibleNumber.TryParse(_base, out decimal b) ? b : 0m;

        /// <summary>Rate text — "2" (multiplier) or "10%" (percentage).</summary>
        public string Taux
        {
            get => _taux;
            set { if (Set(ref _taux, value)) { _engineAmount = null; RaiseAmounts(); _changed?.Invoke(); } }
        }

        /// <summary>
        /// The RAW worksheet amount (Base × Taux). This is the ENGINE INPUT — what
        /// <c>BuildRequest</c> sends as the base override / line amount — and is never prorated
        /// here. The engine does any proration. Displayed figures use <see cref="Gain"/>/<see
        /// cref="Retenue"/>, which prefer the engine's own computed amount (see <see cref="SetEngineAmount"/>).
        /// </summary>
        public decimal Amount
        {
            get
            {
                decimal b = BaseValue;
                decimal? factor = ParseFactor(_taux);
                return factor.HasValue ? b * factor.Value : b;
            }
        }

        private decimal? _engineAmount;

        /// <summary>
        /// Pushes back the EXACT amount the engine computed for this line (prorated for a partial
        /// month) after each recompute, so the worksheet's Gain/Retenue columns show the same
        /// figures as the fiche, the totals, the archived bulletin and the CNAS base — never the
        /// raw, un-prorated Base×Taux. It is display-only: <see cref="Amount"/> (the engine input)
        /// is untouched. Cleared on any Base/Taux edit so a fresh keystroke shows the typed value
        /// until the next recompute sets it again.
        /// </summary>
        public void SetEngineAmount(decimal? amount)
        {
            _engineAmount = amount;
            RaiseAmounts();
        }

        /// <summary>What is shown in the Gain/Retenue column: the engine's amount when known,
        /// otherwise the raw Base×Taux (before the first recompute).</summary>
        private decimal DisplayAmount => _engineAmount ?? Amount;

        public decimal? Gain => IsGain ? DisplayAmount : (decimal?)null;
        public decimal? Retenue => IsGain ? (decimal?)null : DisplayAmount;

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
            string number = percent ? s.Substring(0, s.Length - 1) : s;

            // Accept comma OR dot as the decimal separator, whatever the Windows locale.
            if (OptiPaie.Common.Text.FlexibleNumber.TryParse(number, out decimal value))
            {
                return percent ? value / 100m : value;
            }

            return null;
        }
    }
}
