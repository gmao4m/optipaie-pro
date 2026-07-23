using System;
using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Company-wide ("run the whole month") payroll orchestration. This is a thin loop ON
    /// TOP of the existing, tested per-employee path: for each active employee it builds the
    /// same <see cref="PayrollGenerationRequest"/> the single-employee worksheet builds
    /// (base salary + assigned elements + automatic attendance worked-days + automatic loan
    /// deduction) and calls the SAME <see cref="IPayrollService.Generate"/>. It never
    /// contains or duplicates any IRG/CNAS/lissage/abattement calculation — those live only
    /// in the engine, reached through Generate. If a batch figure ever differs from running
    /// that employee individually, the bug is here, not in the engine.
    /// </summary>
    public interface IBatchPayrollService
    {
        /// <summary>
        /// Pre-run inspection: every active employee of the company with its readiness
        /// (blocking issues like a missing salary or no active contract; warnings like no
        /// attendance recorded for the period). No side effects.
        /// </summary>
        BatchPayrollPlan Plan(long companyId, int year, int month);

        /// <summary>
        /// Processes every ready active employee, archiving all payslips into the one shared
        /// PayrollRun for the period. Blocking employees are skipped (never silently paid
        /// wrong); each outcome is reported. Reports progress as it goes.
        /// </summary>
        BatchPayrollResult Run(long companyId, int year, int month, IProgress<BatchProgress> progress = null);

        /// <summary>
        /// Builds the payroll request for one employee EXACTLY as the single-employee
        /// worksheet does (same inputs). Exposed so the orchestration can be verified against
        /// the single path field-for-field.
        /// </summary>
        PayrollGenerationRequest BuildRequest(long companyId, long employeeId, int year, int month);
    }
}
