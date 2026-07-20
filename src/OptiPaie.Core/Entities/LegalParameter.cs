using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A configurable payroll legal parameter with an effective date (e.g. the CNAS
    /// employee/employer rate, SNMG). Effective-dating preserves the value that was
    /// in force when a past payslip was produced.
    /// <para>
    /// The IRG barème, abattement and lissage are intentionally NOT stored here —
    /// per the approved decisions they are compiled into the engine.
    /// </para>
    /// </summary>
    public sealed class LegalParameter : EntityBase
    {
        /// <summary>Stable parameter key (see Common constants), e.g. "CNAS_EMPLOYEE_RATE".</summary>
        public string ParamKey { get; set; }

        /// <summary>Parameter value, stored as invariant text and parsed by the consumer.</summary>
        public string ParamValue { get; set; }

        /// <summary>Date from which this value is effective.</summary>
        public DateTime EffectiveFrom { get; set; }

        /// <summary>Whether this is the currently active value for its key.</summary>
        public bool IsActive { get; set; }

        /// <summary>Human-readable description of the parameter.</summary>
        public string Description { get; set; }
    }
}
