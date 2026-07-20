using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;

namespace OptiPaie.PayrollEngine.Pipeline
{
    /// <summary>
    /// The mutable state shared by the calculation rules as they run in order.
    /// Holds the input, the active legal profile, the money engine and IRG
    /// calculator, the working lines, the running totals, the trace and messages.
    /// </summary>
    internal sealed class PayrollCalculationContext
    {
        public PayrollCalculationContext(
            PayrollContext input,
            LegalProfile profile,
            MoneyEngine money,
            IrgCalculator irgCalculator)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Money = money ?? throw new ArgumentNullException(nameof(money));
            IrgCalculator = irgCalculator ?? throw new ArgumentNullException(nameof(irgCalculator));
        }

        public PayrollContext Input { get; }
        public LegalProfile Profile { get; }
        public MoneyEngine Money { get; }
        public IrgCalculator IrgCalculator { get; }

        public List<WorkingLine> Lines { get; } = new List<WorkingLine>();
        public List<PayrollCalculationStep> Trace { get; } = new List<PayrollCalculationStep>();
        public List<PayrollMessage> Messages { get; } = new List<PayrollMessage>();

        // Running totals (all rounded through the money engine at the statutory points).
        public decimal EffectiveBaseSalary { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal CotisableBase { get; set; }
        public decimal CnasEmployee { get; set; }
        public decimal CnasEmployer { get; set; }
        public decimal RegularTaxableBase { get; set; }
        public decimal IrgBrut { get; set; }
        public decimal Abattement { get; set; }
        public decimal IrgRegular { get; set; }
        public decimal IrgLissage { get; set; }
        public decimal Irg { get; set; }
        public decimal NetDeductions { get; set; }
        public decimal NetSalaire { get; set; }

        /// <summary>Appends a step to the explain-calculation trace.</summary>
        public void AddTrace(string key, decimal amount, string detail)
        {
            Trace.Add(new PayrollCalculationStep(key, amount, detail));
        }
    }
}
