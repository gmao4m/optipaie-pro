using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Corrects, cancels and restores a saved payslip, with full downstream consistency.
    /// <para>
    /// Every operation runs in ONE transaction: the payslip status change and the loan
    /// repayment reversal/re-application commit together or not at all, so loan balances
    /// can never be left half-applied. The payroll engine is never modified — a correction
    /// re-runs the SAME engine with amended inputs. CNAS DAC/DAS and the ATS history recompute
    /// automatically because they read only Active payslips.
    /// </para>
    /// </summary>
    public interface IPayslipRevisionService
    {
        /// <summary>
        /// Corrects a payslip: the original is marked Superseded (kept for audit) and a new Active
        /// version — recomputed by the engine from the amended request — is inserted and linked back
        /// to it. The loan recovery is reversed and re-applied from the corrected recovery line, all
        /// atomically. Returns the new payslip's id.
        /// </summary>
        Result<long> Correct(PayslipCorrectionRequest request);

        /// <summary>
        /// Cancels (voids) a payslip: it is marked Cancelled (never physically deleted), freeing its
        /// period slot for a fresh calculation, and its loan recovery is reversed — atomically. The
        /// record survives for audit and can be restored. A mandatory reason is required.
        /// </summary>
        Result Cancel(long payslipId, string reason);

        /// <summary>
        /// Restores a cancelled payslip to Active and re-applies its loan recovery — atomically.
        /// Refused when another Active payslip already exists for the same employee and period.
        /// </summary>
        Result Restore(long payslipId);

        /// <summary>Superseded/cancelled payslips of a company for a period, for the history view.</summary>
        IReadOnlyList<PayslipHistoryRow> GetHistory(long companyId, int year, int? month);
    }
}
