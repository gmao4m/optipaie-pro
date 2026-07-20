using System;
using System.Collections.Generic;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A payroll run: one company for one period (month/year). Groups the
    /// per-employee <see cref="Payslip"/> records, enabling batch generation
    /// ("generate all employees in one action").
    /// </summary>
    public sealed class PayrollRun : EntityBase
    {
        /// <summary>Foreign key to the company.</summary>
        public long CompanyId { get; set; }

        /// <summary>Four-digit period year.</summary>
        public int PeriodYear { get; set; }

        /// <summary>Period month (1-12).</summary>
        public int PeriodMonth { get; set; }

        /// <summary>Lifecycle state of the run.</summary>
        public RunStatus RunStatus { get; set; }

        /// <summary>UTC timestamp when the run was generated. Null while a draft.</summary>
        public DateTime? GeneratedAtUtc { get; set; }

        /// <summary>Version of the payroll engine that produced the run (legal traceability).</summary>
        public string EngineVersion { get; set; }

        /// <summary>
        /// Per-employee payslips in this run. Populated by services; not mapped by Dapper.
        /// </summary>
        public IList<Payslip> Payslips { get; set; } = new List<Payslip>();

        /// <summary>Returns the strongly-typed payroll period of this run.</summary>
        public PayrollPeriod ToPeriod() => new PayrollPeriod(PeriodYear, PeriodMonth);
    }
}
