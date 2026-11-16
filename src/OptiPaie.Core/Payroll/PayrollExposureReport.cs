using System.Collections.Generic;

namespace OptiPaie.Core.Payroll
{
    /// <summary>
    /// A read-only exposure diagnostic (PART 3). It never changes a calculation; it lets a
    /// customer see, from their own element catalog, where an exempt allowance could be
    /// escaping the law — so the owner never has to read anyone's database. The
    /// months-below-SNMG exposure is already surfaced by the CNAS readiness report
    /// (<c>CnasEmployeeReadiness.MonthsBelowSnmg</c>); this report adds the catalog side.
    /// </summary>
    public sealed class PayrollExposureReport
    {
        public IReadOnlyList<ExemptElementInfo> ExemptElements { get; }

        /// <summary>Elements carrying a non-null ExemptionCeiling — which the engine does NOT enforce today.</summary>
        public IReadOnlyList<CeilingElementInfo> CeilingElements { get; }

        public bool HasExposure => ExemptElements.Count > 0 || CeilingElements.Count > 0;

        public PayrollExposureReport(
            IReadOnlyList<ExemptElementInfo> exemptElements,
            IReadOnlyList<CeilingElementInfo> ceilingElements)
        {
            ExemptElements = exemptElements ?? new List<ExemptElementInfo>();
            CeilingElements = ceilingElements ?? new List<CeilingElementInfo>();
        }
    }

    /// <summary>An active element exempt from the contribution base and/or the taxable base.</summary>
    public sealed class ExemptElementInfo
    {
        public string Name { get; }
        public bool ExemptFromContribution { get; }
        public bool ExemptFromTax { get; }

        /// <summary>The fixed monthly amount when the element is a fixed-amount gain; null when it varies.</summary>
        public decimal? FixedMonthlyAmount { get; }

        public string NoteFr { get; }
        public string NoteAr { get; }

        public ExemptElementInfo(string name, bool exemptFromContribution, bool exemptFromTax,
            decimal? fixedMonthlyAmount, string noteFr, string noteAr)
        {
            Name = name ?? string.Empty;
            ExemptFromContribution = exemptFromContribution;
            ExemptFromTax = exemptFromTax;
            FixedMonthlyAmount = fixedMonthlyAmount;
            NoteFr = noteFr ?? string.Empty;
            NoteAr = noteAr ?? string.Empty;
        }
    }

    /// <summary>An element that carries an exemption ceiling the engine does not currently apply.</summary>
    public sealed class CeilingElementInfo
    {
        public string Name { get; }
        public decimal Ceiling { get; }
        public string NoteFr { get; }
        public string NoteAr { get; }

        public CeilingElementInfo(string name, decimal ceiling, string noteFr, string noteAr)
        {
            Name = name ?? string.Empty;
            Ceiling = ceiling;
            NoteFr = noteFr ?? string.Empty;
            NoteAr = noteAr ?? string.Empty;
        }
    }
}
