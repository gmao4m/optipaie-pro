using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.Pipeline
{
    /// <summary>
    /// Mutable working representation of one payslip line during calculation
    /// (the base salary line and each resolved element). Mapped to an immutable
    /// <see cref="PayrollLineResult"/> when the result is built.
    /// </summary>
    internal sealed class WorkingLine
    {
        public long? ElementId { get; set; }
        public string LabelFr { get; set; }
        public string LabelAr { get; set; }
        public ElementType ElementType { get; set; }
        public decimal? Base { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal Amount { get; set; }
        public bool IsCnasApplicable { get; set; }
        public bool IsIrgApplicable { get; set; }

        /// <summary>Share of this line in the cotisable base, 0..1 (1 = fully, 0 = not).</summary>
        public decimal CnasFactor { get; set; } = 1m;

        /// <summary>Share of this line in the taxable base, 0..1.</summary>
        public decimal IrgFactor { get; set; } = 1m;

        public bool IsIncludedInGross { get; set; }
        public bool IsLissage { get; set; }
        public LissageInput Lissage { get; set; }
        public int DisplayOrder { get; set; }

        /// <summary>+1 for a gain, -1 for a deduction (its sign in the bases).</summary>
        public int Sign => ElementType == ElementType.Gain ? 1 : -1;
    }
}
