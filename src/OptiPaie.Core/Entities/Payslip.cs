using System;
using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One employee's computed bulletin de paie within a <see cref="PayrollRun"/>.
    /// Stores the frozen statutory totals and the rates/engine version used, so an
    /// archived payslip remains reproducible after the law changes.
    /// </summary>
    public sealed class Payslip : EntityBase
    {
        /// <summary>Foreign key to the parent run.</summary>
        public long RunId { get; set; }

        /// <summary>Foreign key to the employee.</summary>
        public long EmployeeId { get; set; }

        /// <summary>Salaire Brut — sum of all gains.</summary>
        public decimal SalaireBrut { get; set; }

        /// <summary>Base Cotisable — sum of CNAS-applicable gains.</summary>
        public decimal BaseCotisable { get; set; }

        /// <summary>CNAS employee contribution (retenue salariale).</summary>
        public decimal CnasEmployee { get; set; }

        /// <summary>CNAS employer contribution (charge patronale). Stored for future DAS; not part of net.</summary>
        public decimal CnasEmployer { get; set; }

        /// <summary>Base Imposable — taxable base after CNAS deduction.</summary>
        public decimal BaseImposable { get; set; }

        /// <summary>IRG before abattement (gross tax from the barème). Stored for transparency.</summary>
        public decimal IrgBrut { get; set; }

        /// <summary>Abattement applied to the IRG (40%, clamped 1 000–1 500/month).</summary>
        public decimal Abattement { get; set; }

        /// <summary>Final IRG after exemption/smoothing rules.</summary>
        public decimal Irg { get; set; }

        /// <summary>Net salary to pay (Salaire Net à Payer).</summary>
        public decimal NetSalaire { get; set; }

        /// <summary>The CNAS employee rate used at generation time (legal traceability).</summary>
        public decimal CnasEmployeeRateUsed { get; set; }

        /// <summary>The CNAS employer rate used at generation time (legal traceability).</summary>
        public decimal CnasEmployerRateUsed { get; set; }

        /// <summary>Worked days used in the calculation (for proration traceability).</summary>
        public decimal WorkedDays { get; set; }

        /// <summary>Worked hours used in the calculation.</summary>
        public decimal WorkedHours { get; set; }

        /// <summary>Version of the payroll engine that produced this payslip.</summary>
        public string EngineVersion { get; set; }

        /// <summary>UTC timestamp when the payslip was generated.</summary>
        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>
        /// Lifecycle state (migration 0036). Defaults to <see cref="PayslipStatus.Active"/> —
        /// the only state read by declarations, the archive and every downstream artifact.
        /// Superseded/Cancelled rows are kept for audit and excluded from the active view.
        /// </summary>
        public PayslipStatus Status { get; set; } = PayslipStatus.Active;

        /// <summary>When this is a corrected version, the id of the original payslip it replaces;
        /// otherwise null. Drives the bilingual "bulletin rectificatif" reprint mark.</summary>
        public long? SupersedesPayslipId { get; set; }

        /// <summary>Mandatory typed reason captured when the payslip was corrected or cancelled.</summary>
        public string RevisionReason { get; set; }

        /// <summary>Who performed the correction/cancellation (session user or owner), for audit.</summary>
        public string RevisedByUser { get; set; }

        /// <summary>UTC timestamp of the correction/cancellation, or null when never revised.</summary>
        public DateTime? RevisedAtUtc { get; set; }

        /// <summary>
        /// The detail lines making up this payslip. Populated by services; not mapped by Dapper.
        /// </summary>
        public IList<PayrollDetail> Details { get; set; } = new List<PayrollDetail>();
    }
}
