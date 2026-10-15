using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Corrects, cancels and restores a saved payslip. See <see cref="IPayslipRevisionService"/>.
    /// <para>
    /// Atomicity is the whole point: each operation opens ONE unit of work and one transaction, and
    /// the payslip status change plus the loan-repayment reversal/re-application are committed
    /// together through that single transaction. There is no code path on which the payslip is
    /// cancelled/superseded while the loan repayment survives, or the reverse — a throw anywhere
    /// rolls the entire operation back. The payroll ENGINE is never touched: a correction re-runs
    /// the existing engine (via <see cref="IPayrollService.Preview"/>) with amended inputs and this
    /// service only persists the result.
    /// </para>
    /// </summary>
    public sealed class PayslipRevisionService : IPayslipRevisionService
    {
        /// <summary>The system label of the automatic loan-recovery line (mirrors the worksheet/batch).</summary>
        private const string LoanLineLabel = "Remboursement prêt";

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IPayrollService _payroll;
        private readonly IConfigurationService _configuration;
        private readonly ILoanService _loans;

        public PayslipRevisionService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IPayrollService payroll,
            IConfigurationService configuration,
            ILoanService loans)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _payroll = Guard.AgainstNull(payroll, nameof(payroll));
            _configuration = Guard.AgainstNull(configuration, nameof(configuration));
            _loans = Guard.AgainstNull(loans, nameof(loans));
        }

        /// <summary>Optional audit sink (no-op unless wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        /// <summary>Resolves who is performing the action (session user/owner), evaluated at call time.
        /// Wired by composition to the same source as the audit actor; null stores no actor.</summary>
        public Func<string> ActorProvider { get; set; } = () => null;

        // ---------------------------------------------------------------- correct

        public Result<long> Correct(PayslipCorrectionRequest request)
        {
            if (request == null || request.Amended == null)
            {
                return Result.Fail<long>("Requête de correction absente.", "Payslip_Correction_RequestMissing");
            }

            string reason = (request.Reason ?? string.Empty).Trim();
            if (reason.Length == 0)
            {
                return Result.Fail<long>(
                    "Le motif de la correction est obligatoire.", "Payslip_Correction_ReasonRequired");
            }

            // Recompute with the SAME engine, amended inputs. Pure — no writes happen here.
            PayrollResult result = _payroll.Preview(request.Amended);
            if (result == null || !result.IsSuccess)
            {
                PayrollMessage error = result?.Errors.FirstOrDefault();
                return Result.Fail<long>(
                    error != null ? error.Text : "Le recalcul de la paie a échoué.",
                    error != null ? error.Code : "Payslip_Correction_RecalcFailed");
            }

            PayrollGenerationRequest amended = request.Amended;
            string actor = ResolveActor();

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    Payslip original = uow.Payslips.GetById(request.OriginalPayslipId);
                    if (original == null)
                    {
                        uow.Rollback();
                        return Result.Fail<long>("Bulletin introuvable.", "Payslip_NotFound");
                    }

                    if (original.Status != PayslipStatus.Active)
                    {
                        uow.Rollback();
                        return Result.Fail<long>(
                            "Seul un bulletin actif peut être corrigé.", "Payslip_NotActive");
                    }

                    PayrollRun run = uow.PayrollRuns.GetById(original.RunId);
                    if (run == null)
                    {
                        uow.Rollback();
                        return Result.Fail<long>("Période de paie introuvable.", "Payslip_RunNotFound");
                    }

                    // The amended request must describe exactly this payslip (company, employee, period):
                    // never let a correction write a different employee's or company's data.
                    if (run.CompanyId != amended.CompanyId || original.EmployeeId != amended.EmployeeId ||
                        run.PeriodYear != amended.Year || run.PeriodMonth != amended.Month)
                    {
                        uow.Rollback();
                        return Result.Fail<long>(
                            "La correction ne correspond pas au bulletin d'origine.", "Payslip_Correction_Mismatch");
                    }

                    LegalSnapshot snapshot = _configuration.GetLegalSnapshot();

                    // Supersede the original FIRST (kept, marked Superseded with the mandatory reason),
                    // THEN insert the corrected version — otherwise two rows would momentarily be Active
                    // for the same (run, employee) and the partial unique index would reject the insert.
                    original.Status = PayslipStatus.Superseded;
                    original.RevisionReason = reason;
                    original.RevisedByUser = actor;
                    original.RevisedAtUtc = DateTime.UtcNow;
                    uow.Payslips.UpdateStatus(original);

                    // New Active version, linked to the original it supersedes.
                    Payslip corrected = PayslipFactory.BuildPayslip(
                        run.Id, original.EmployeeId, result,
                        snapshot.CnasEmployeeRate, snapshot.CnasEmployerRate,
                        amended.WorkedDays, amended.WorkedHours,
                        PayslipStatus.Active, original.Id);
                    long newId = uow.Payslips.Insert(corrected);

                    foreach (PayrollLineResult line in result.Lines)
                    {
                        uow.PayrollDetails.Insert(PayslipFactory.BuildDetail(newId, line));
                    }

                    // Loan recovery: reverse what the original deducted, re-apply what the corrected
                    // version deducts — within THIS transaction, so money can never half-apply.
                    _loans.ReversePayrollRepayment(uow, original.EmployeeId, run.PeriodYear, run.PeriodMonth);
                    decimal withheld = LoanWithheld(result.Lines);
                    if (withheld > 0m)
                    {
                        _loans.ApplyPayrollRepayment(uow, original.EmployeeId, run.PeriodYear, run.PeriodMonth, withheld);
                    }

                    uow.Commit();

                    Audit.Record("Payslip", newId, AuditAction.Updated,
                        "Correction du bulletin " + PeriodText(run) + " : " + reason,
                        Money(original.NetSalaire), Money(corrected.NetSalaire));

                    return Result.Ok(newId);
                }
                catch (Exception)
                {
                    uow.Rollback();
                    return Result.Fail<long>(
                        "Erreur lors de la correction du bulletin.", "Payslip_Correction_PersistFailed");
                }
            }
        }

        // ----------------------------------------------------------------- cancel

        public Result Cancel(long payslipId, string reason)
        {
            string motive = (reason ?? string.Empty).Trim();
            if (motive.Length == 0)
            {
                return Result.Fail("Le motif de la suppression est obligatoire.", "Payslip_Cancel_ReasonRequired");
            }

            string actor = ResolveActor();

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    Payslip payslip = uow.Payslips.GetById(payslipId);
                    if (payslip == null)
                    {
                        uow.Rollback();
                        return Result.Fail("Bulletin introuvable.", "Payslip_NotFound");
                    }

                    if (payslip.Status != PayslipStatus.Active)
                    {
                        uow.Rollback();
                        return Result.Fail("Seul un bulletin actif peut être supprimé.", "Payslip_NotActive");
                    }

                    PayrollRun run = uow.PayrollRuns.GetById(payslip.RunId);
                    if (run == null)
                    {
                        uow.Rollback();
                        return Result.Fail("Période de paie introuvable.", "Payslip_RunNotFound");
                    }

                    payslip.Status = PayslipStatus.Cancelled;
                    payslip.RevisionReason = motive;
                    payslip.RevisedByUser = actor;
                    payslip.RevisedAtUtc = DateTime.UtcNow;
                    uow.Payslips.UpdateStatus(payslip);

                    // Reverse the loan recovery in the SAME transaction as the status change.
                    _loans.ReversePayrollRepayment(uow, payslip.EmployeeId, run.PeriodYear, run.PeriodMonth);

                    uow.Commit();

                    Audit.Record("Payslip", payslip.Id, AuditAction.Deleted,
                        "Bulletin annulé " + PeriodText(run) + " : " + motive,
                        Money(payslip.NetSalaire), null);

                    return Result.Ok();
                }
                catch (Exception)
                {
                    uow.Rollback();
                    return Result.Fail("Erreur lors de la suppression du bulletin.", "Payslip_Cancel_PersistFailed");
                }
            }
        }

        // ---------------------------------------------------------------- restore

        public Result Restore(long payslipId)
        {
            string actor = ResolveActor();

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    Payslip payslip = uow.Payslips.GetById(payslipId);
                    if (payslip == null)
                    {
                        uow.Rollback();
                        return Result.Fail("Bulletin introuvable.", "Payslip_NotFound");
                    }

                    if (payslip.Status != PayslipStatus.Cancelled)
                    {
                        uow.Rollback();
                        return Result.Fail("Seul un bulletin annulé peut être restauré.", "Payslip_NotCancelled");
                    }

                    // GetByRun is Active-only: if it already returns this employee, an active payslip
                    // exists for the period and restoring would create a duplicate — refuse.
                    bool activeExists = uow.Payslips.GetByRun(payslip.RunId)
                        .Any(p => p.EmployeeId == payslip.EmployeeId);
                    if (activeExists)
                    {
                        uow.Rollback();
                        return Result.Fail(
                            "Une paie active existe déjà pour cet employé sur cette période. Supprimez-la d'abord.",
                            "Payslip_Restore_ActiveExists");
                    }

                    PayrollRun run = uow.PayrollRuns.GetById(payslip.RunId);
                    if (run == null)
                    {
                        uow.Rollback();
                        return Result.Fail("Période de paie introuvable.", "Payslip_RunNotFound");
                    }

                    // Re-apply exactly what this payslip's own recovery line deducted, before clearing
                    // the revision metadata (we still need the stored detail lines).
                    decimal withheld = LoanWithheld(uow.PayrollDetails.GetByPayslip(payslip.Id));

                    payslip.Status = PayslipStatus.Active;
                    payslip.RevisionReason = null;
                    payslip.RevisedByUser = actor;
                    payslip.RevisedAtUtc = DateTime.UtcNow;
                    uow.Payslips.UpdateStatus(payslip);

                    if (withheld > 0m)
                    {
                        _loans.ApplyPayrollRepayment(uow, payslip.EmployeeId, run.PeriodYear, run.PeriodMonth, withheld);
                    }

                    uow.Commit();

                    Audit.Record("Payslip", payslip.Id, AuditAction.StatusChanged,
                        "Bulletin restauré " + PeriodText(run), null, Money(payslip.NetSalaire));

                    return Result.Ok();
                }
                catch (Exception)
                {
                    uow.Rollback();
                    return Result.Fail("Erreur lors de la restauration du bulletin.", "Payslip_Restore_PersistFailed");
                }
            }
        }

        // ---------------------------------------------------------------- history

        public IReadOnlyList<PayslipHistoryRow> GetHistory(long companyId, int year, int? month)
        {
            var rows = new List<PayslipHistoryRow>();

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                foreach (PayrollRun run in uow.PayrollRuns.Search(companyId, year, month))
                {
                    foreach (Payslip p in uow.Payslips.GetInactiveByRun(run.Id))
                    {
                        Employee e = uow.Employees.GetById(p.EmployeeId);
                        rows.Add(new PayslipHistoryRow
                        {
                            PayslipId = p.Id,
                            EmployeeId = p.EmployeeId,
                            EmployeeName = e == null ? ("#" + p.EmployeeId) : (e.LastNameFr + " " + e.FirstNameFr).Trim(),
                            Year = run.PeriodYear,
                            Month = run.PeriodMonth,
                            NetSalaire = p.NetSalaire,
                            Status = p.Status,
                            Reason = p.RevisionReason,
                            RevisedByUser = p.RevisedByUser,
                            RevisedAtUtc = p.RevisedAtUtc
                        });
                    }
                }
            }

            return rows;
        }

        // ---------------------------------------------------------------- helpers

        private static decimal LoanWithheld(IEnumerable<PayrollLineResult> lines)
        {
            return lines
                .Where(l => l.ElementType == ElementType.Deduction &&
                            string.Equals(l.LabelFr, LoanLineLabel, StringComparison.Ordinal))
                .Sum(l => l.Amount);
        }

        private static decimal LoanWithheld(IEnumerable<PayrollDetail> details)
        {
            return details
                .Where(d => d.ElementType == ElementType.Deduction &&
                            string.Equals(d.LabelFr, LoanLineLabel, StringComparison.Ordinal))
                .Sum(d => d.Amount);
        }

        private string ResolveActor()
        {
            try { return ActorProvider?.Invoke(); }
            catch { return null; }
        }

        private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        private static string PeriodText(PayrollRun run) =>
            run.PeriodMonth.ToString("00", CultureInfo.InvariantCulture) + "/" + run.PeriodYear;
    }
}
