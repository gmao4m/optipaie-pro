using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Loan / salary-advance orchestration. Owns ALL loan rules so every screen and the
    /// payroll chain agree:
    ///   • the outstanding balance is ALWAYS derived (principal − recorded repayments);
    ///   • the monthly instalment fed to payroll is capped at the balance;
    ///   • a payroll run records the recovery exactly once per (loan, period);
    ///   • a loan settles automatically when its balance reaches zero.
    /// The payroll integration is input-only: the engine is never modified — the desktop
    /// worksheet simply reads <see cref="GetMonthlyDeduction"/> and, on save, calls
    /// <see cref="RecordPayrollDeductions"/>.
    /// </summary>
    public sealed class LoanService : ILoanService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly OptiPaie.Core.Security.IAccessContext _access;

        public LoanService(IUnitOfWorkFactory unitOfWorkFactory, OptiPaie.Core.Security.IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _access = access;
        }

        /// <summary>Optional audit sink (no-op unless wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        public Result<long> Save(Loan loan)
        {
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLoans);
            if (loan == null)
            {
                return Result.Fail<long>("Aucun prêt.", "Loan_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (loan.EmployeeId <= 0 || !uow.Employees.ExistsById(loan.EmployeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Loan_EmployeeNotFound");
                }

                if (loan.Principal <= 0m)
                {
                    return Result.Fail<long>("Le montant du prêt doit être positif.", "Loan_PrincipalInvalid");
                }

                if (loan.MonthlyInstallment <= 0m)
                {
                    return Result.Fail<long>("La mensualité doit être positive.", "Loan_InstallmentInvalid");
                }

                if (loan.MonthlyInstallment > loan.Principal)
                {
                    return Result.Fail<long>(
                        "La mensualité ne peut pas dépasser le montant du prêt.", "Loan_InstallmentTooLarge");
                }

                if (loan.StartMonth < 1 || loan.StartMonth > 12)
                {
                    return Result.Fail<long>("Mois de début invalide.", "Loan_StartMonthInvalid");
                }

                if (loan.StartYear < 2000 || loan.StartYear > 2100)
                {
                    return Result.Fail<long>("Année de début invalide.", "Loan_StartYearInvalid");
                }

                if (loan.Id > 0)
                {
                    Loan existing = uow.Loans.GetById(loan.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Prêt introuvable.", "Loan_NotFound");
                    }

                    decimal repaid = Repaid(uow, loan.Id);
                    if (loan.Principal < repaid)
                    {
                        return Result.Fail<long>(
                            "Le montant ne peut pas être inférieur au total déjà remboursé.", "Loan_PrincipalBelowRepaid");
                    }

                    loan.CreatedAtUtc = existing.CreatedAtUtc;
                    // Status is managed through SetStatus / repayments, not by an edit.
                    loan.Status = existing.Status;
                    try
                    {
                        uow.Loans.Update(loan);
                    }
                    catch (ConcurrencyException)
                    {
                        return Result.Fail<long>(ConcurrencyException.UserMessage, ErrorCodes.Concurrency);
                    }
                    ReconcileStatus(uow, loan.Id);
                    Audit.Record("Loan", loan.Id, AuditAction.Updated, "Prêt modifié");
                    return Result.Ok(loan.Id);
                }

                loan.Status = LoanStatus.Active;
                long newId = uow.Loans.Insert(loan);
                Audit.Record("Loan", newId, AuditAction.Created, "Prêt créé", null, loan.Principal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Result.Ok(newId);
            }
        }

        public Result SetStatus(long id, LoanStatus status)
        {
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLoans);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Loan loan = uow.Loans.GetById(id);
                if (loan == null)
                {
                    return Result.Fail("Prêt introuvable.", "Loan_NotFound");
                }

                LoanStatus old = loan.Status;
                loan.Status = status;
                uow.Loans.Update(loan);
                Audit.Record("Loan", id, AuditAction.StatusChanged, "Statut du prêt modifié", old.ToString(), status.ToString());
                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLoans);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Loan loan = uow.Loans.GetById(id);
                if (loan == null)
                {
                    return Result.Ok();
                }

                uow.BeginTransaction();
                try
                {
                    foreach (LoanRepayment repayment in uow.Loans.GetRepayments(id))
                    {
                        uow.Loans.SoftDeleteRepayment(repayment.Id);
                    }

                    uow.Loans.SoftDelete(id);
                    uow.Commit();
                    Audit.Record("Loan", id, AuditAction.Deleted, "Prêt supprimé (avec son historique de remboursement)");
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Loan Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Loans.GetById(id);
            }
        }

        public LoanSummary GetSummary(long loanId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Loan loan = uow.Loans.GetById(loanId);
                return loan == null ? null : Summarise(uow, loan, uow.Loans.GetIssuedPeriods(loan.EmployeeId));
            }
        }

        public IReadOnlyList<LoanSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                IReadOnlyCollection<int> issued = uow.Loans.GetIssuedPeriods(employeeId);
                return uow.Loans.GetByEmployee(employeeId).Select(l => Summarise(uow, l, issued)).ToList();
            }
        }

        public IReadOnlyList<LoanSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                var issuedByEmployee = new Dictionary<long, IReadOnlyCollection<int>>();
                var result = new List<LoanSummary>();
                foreach (Loan loan in uow.Loans.GetByCompany(companyId))
                {
                    if (!issuedByEmployee.TryGetValue(loan.EmployeeId, out IReadOnlyCollection<int> issued))
                    {
                        issued = uow.Loans.GetIssuedPeriods(loan.EmployeeId);
                        issuedByEmployee[loan.EmployeeId] = issued;
                    }

                    LoanSummary summary = Summarise(uow, loan, issued);
                    names.TryGetValue(loan.EmployeeId, out string name);
                    summary.EmployeeName = name;
                    result.Add(summary);
                }

                return result;
            }
        }

        public LoanPortfolio GetActivePortfolio(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Loans.GetActivePortfolio(companyId);
            }
        }

        public LoanLedgerReport BuildLedger(long companyId, long? employeeId, int fromYear, int fromMonth, int toYear, int toMonth)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Company company = uow.Companies.GetById(companyId);
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                int from = Period(fromYear, fromMonth);
                int to = Period(toYear, toMonth);

                var report = new LoanLedgerReport
                {
                    CompanyId = companyId,
                    CompanyName = company?.NameFr,
                    EmployeeName = employeeId.HasValue && names.TryGetValue(employeeId.Value, out string en) ? en : null,
                    FromYear = fromYear,
                    FromMonth = fromMonth,
                    ToYear = toYear,
                    ToMonth = toMonth
                };

                IEnumerable<Loan> loans = employeeId.HasValue
                    ? uow.Loans.GetByEmployee(employeeId.Value)
                    : uow.Loans.GetByCompany(companyId);

                var issuedCache = new Dictionary<long, IReadOnlyCollection<int>>();
                var lines = new List<LoanLedgerLine>();

                foreach (Loan loan in loans)
                {
                    // Company isolation: an employee-scoped query must still belong to this company.
                    if (!names.ContainsKey(loan.EmployeeId)) continue;

                    // Not yet granted by the end of the window — nothing to report.
                    if (Period(loan.StartYear, loan.StartMonth) > to) continue;

                    List<LoanRepayment> repayments = uow.Loans.GetRepayments(loan.Id).ToList();
                    decimal recoveredTotal = repayments.Sum(r => r.Amount);
                    decimal recoveredInPeriod = repayments
                        .Where(r => { int p = Period(r.Year, r.Month); return p >= from && p <= to; })
                        .Sum(r => r.Amount);
                    decimal outstanding = Math.Max(0m, loan.Principal - recoveredTotal);

                    bool grantedInWindow = Period(loan.StartYear, loan.StartMonth) >= from;
                    bool relevant = outstanding > 0m || recoveredInPeriod > 0m || grantedInWindow;
                    if (!relevant) continue;

                    if (!issuedCache.TryGetValue(loan.EmployeeId, out IReadOnlyCollection<int> issued))
                    {
                        issued = uow.Loans.GetIssuedPeriods(loan.EmployeeId);
                        issuedCache[loan.EmployeeId] = issued;
                    }

                    int missed = MissedInstallments(loan, repayments, outstanding, issued);
                    names.TryGetValue(loan.EmployeeId, out string name);

                    lines.Add(new LoanLedgerLine
                    {
                        LoanId = loan.Id,
                        EmployeeName = name,
                        Type = loan.Type,
                        Status = loan.Status,
                        Lent = loan.Principal,
                        RecoveredInPeriod = recoveredInPeriod,
                        RecoveredTotal = recoveredTotal,
                        Remaining = outstanding,
                        IsLate = missed > 0,
                        MissedInstallments = missed
                    });
                }

                // Stable, readable order: late first, then by employee, then newest loan.
                report.Lines = lines
                    .OrderByDescending(l => l.IsLate)
                    .ThenBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(l => l.LoanId)
                    .ToList();

                report.TotalLent = lines.Sum(l => l.Lent);
                report.TotalRecoveredInPeriod = lines.Sum(l => l.RecoveredInPeriod);
                report.TotalRecovered = lines.Sum(l => l.RecoveredTotal);
                report.TotalRemaining = lines.Sum(l => l.Remaining);
                report.LateCount = lines.Count(l => l.IsLate);

                return report;
            }
        }

        public IReadOnlyList<LoanRepayment> GetRepayments(long loanId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Loans.GetRepayments(loanId).ToList();
            }
        }

        public Result AddManualRepayment(long loanId, int year, int month, decimal amount)
        {
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLoans);
            if (amount <= 0m)
            {
                return Result.Fail("Le montant doit être positif.", "Loan_RepaymentInvalid");
            }

            if (month < 1 || month > 12)
            {
                return Result.Fail("Mois invalide.", "Loan_RepaymentMonthInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Loan loan = uow.Loans.GetById(loanId);
                if (loan == null)
                {
                    return Result.Fail("Prêt introuvable.", "Loan_NotFound");
                }

                if (uow.Loans.GetRepayment(loanId, year, month) != null)
                {
                    return Result.Fail("Un remboursement existe déjà pour cette période.", "Loan_RepaymentExists");
                }

                decimal outstanding = loan.Principal - Repaid(uow, loanId);
                if (amount > outstanding)
                {
                    return Result.Fail(
                        "Le montant dépasse le solde restant dû.", "Loan_RepaymentExceedsBalance");
                }

                uow.BeginTransaction();
                try
                {
                    uow.Loans.InsertRepayment(new LoanRepayment
                    {
                        LoanId = loanId,
                        Year = year,
                        Month = month,
                        Amount = amount,
                        IsManual = true
                    });

                    ReconcileStatus(uow, loanId);
                    uow.Commit();
                    Audit.Record("Loan", loanId, AuditAction.Updated, "Remboursement manuel ajouté (" + month.ToString("00") + "/" + year + ")", null, amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result RemoveRepayment(long repaymentId)
        {
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLoans);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LoanRepayment repayment = uow.Loans.GetRepaymentById(repaymentId);
                if (repayment == null)
                {
                    return Result.Ok();
                }

                uow.BeginTransaction();
                try
                {
                    uow.Loans.SoftDeleteRepayment(repaymentId);
                    ReconcileStatus(uow, repayment.LoanId);
                    uow.Commit();
                    Audit.Record("Loan", repayment.LoanId, AuditAction.Updated, "Remboursement supprimé", repayment.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public decimal GetMonthlyDeduction(long employeeId, int year, int month)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                decimal total = 0m;
                int period = Period(year, month);

                foreach (Loan loan in uow.Loans.GetByEmployee(employeeId))
                {
                    LoanRepayment existing = uow.Loans.GetRepayment(loan.Id, year, month);
                    if (existing != null)
                    {
                        // Reproducible: an already-recorded period keeps its exact amount.
                        total += existing.Amount;
                        continue;
                    }

                    if (loan.Status != LoanStatus.Active) continue;
                    if (period < Period(loan.StartYear, loan.StartMonth)) continue;

                    decimal outstanding = loan.Principal - Repaid(uow, loan.Id);
                    if (outstanding <= 0m) continue;

                    total += Math.Min(loan.MonthlyInstallment, outstanding);
                }

                return total;
            }
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Internal payroll-pipeline write: invoked only by the guarded payroll run (RunPayroll) when persisting loan deductions, never as a standalone user action.")]
        public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month)
        {
            // No explicit amount → each active loan recovers its theoretical instalment (batch path,
            // where the recovery line is never edited so theoretical == actually withheld).
            return RecordPayrollDeductions(employeeId, year, month, null, null);
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Internal payroll-pipeline write: invoked only by the guarded payroll run (RunPayroll) when persisting loan deductions, never as a standalone user action.")]
        public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month, decimal actualWithheld)
        {
            // Non-nullable public overload: the caller passes the amount actually withheld on the
            // payslip, which is then distributed across the loans (capped at each outstanding).
            return RecordPayrollDeductions(employeeId, year, month, (decimal?)actualWithheld, null);
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Internal payroll-pipeline write: invoked only by the guarded payroll run (RunPayroll) when persisting loan deductions, never as a standalone user action.")]
        public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month, decimal actualWithheld, long payslipId)
        {
            // Same as above, plus the id of the payslip that deducted the recovery, so each new
            // repayment row carries a hard link to its bulletin (migration 0042).
            return RecordPayrollDeductions(employeeId, year, month, (decimal?)actualWithheld, payslipId > 0 ? (long?)payslipId : null);
        }

        private Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month, decimal? actualWithheld, long? payslipId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    decimal total = ApplyCore(uow, employeeId, year, month, actualWithheld, payslipId);
                    uow.Commit();
                    return Result.Ok(total);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public void ReversePayrollRepayment(IUnitOfWork uow, long employeeId, int year, int month)
        {
            Guard.AgainstNull(uow, nameof(uow));
            ReverseCore(uow, employeeId, year, month);
        }

        public decimal ApplyPayrollRepayment(IUnitOfWork uow, long employeeId, int year, int month, decimal withheld)
        {
            Guard.AgainstNull(uow, nameof(uow));
            // Always an explicit amount here (the corrected/restored payslip's recovery line), so the
            // loan schedule is credited exactly what that payslip deducts — never the theoretical one.
            return ApplyCore(uow, employeeId, year, month, Math.Max(0m, withheld), null);
        }

        public decimal ApplyPayrollRepayment(IUnitOfWork uow, long employeeId, int year, int month, decimal withheld, long payslipId)
        {
            Guard.AgainstNull(uow, nameof(uow));
            return ApplyCore(uow, employeeId, year, month, Math.Max(0m, withheld), payslipId > 0 ? (long?)payslipId : null);
        }

        /// <summary>
        /// Records the period's recovery for the employee's active loans, enlisting in the given
        /// unit of work (no commit). When <paramref name="actualWithheld"/> is null each loan takes
        /// its theoretical instalment; otherwise that amount is the pot distributed across the loans
        /// in order, each capped at its outstanding — so the schedule is credited exactly what the
        /// payslip deducted. Idempotent per (loan, period). Returns the total applied.
        /// </summary>
        private static decimal ApplyCore(IUnitOfWork uow, long employeeId, int year, int month, decimal? actualWithheld, long? payslipId)
        {
            int period = Period(year, month);
            List<Loan> loans = uow.Loans.GetByEmployee(employeeId).ToList();
            decimal? remaining = actualWithheld.HasValue ? Math.Max(0m, actualWithheld.Value) : (decimal?)null;
            decimal total = 0m;

            foreach (Loan loan in loans)
            {
                if (loan.Status != LoanStatus.Active) continue;
                if (period < Period(loan.StartYear, loan.StartMonth)) continue;

                // Idempotent: never record the same period twice.
                if (uow.Loans.GetRepayment(loan.Id, year, month) != null) continue;

                decimal outstanding = loan.Principal - Repaid(uow, loan.Id);
                if (outstanding <= 0m)
                {
                    SettleIfNeeded(uow, loan, 0m);
                    continue;
                }

                decimal amount = remaining.HasValue
                    ? Math.Min(remaining.Value, outstanding)               // exactly what was withheld
                    : Math.Min(loan.MonthlyInstallment, outstanding);      // theoretical instalment

                if (amount <= 0m) continue;

                uow.Loans.InsertRepayment(new LoanRepayment
                {
                    LoanId = loan.Id,
                    Year = year,
                    Month = month,
                    Amount = amount,
                    IsManual = false,
                    PayslipId = payslipId
                });

                total += amount;
                if (remaining.HasValue) remaining -= amount;
                SettleIfNeeded(uow, loan, outstanding - amount);
            }

            return total;
        }

        /// <summary>
        /// Soft-deletes the PAYROLL (non-manual) repayment of each of the employee's loans for the
        /// period and reconciles the loan status, enlisting in the given unit of work (no commit). A
        /// manual repayment the user entered by hand is deliberately left intact.
        /// </summary>
        private static void ReverseCore(IUnitOfWork uow, long employeeId, int year, int month)
        {
            foreach (Loan loan in uow.Loans.GetByEmployee(employeeId).ToList())
            {
                LoanRepayment repayment = uow.Loans.GetRepayment(loan.Id, year, month);
                if (repayment == null || repayment.IsManual) continue;

                uow.Loans.SoftDeleteRepayment(repayment.Id);
                ReconcileStatus(uow, loan.Id);
            }
        }

        public decimal GetOutstanding(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                decimal total = 0m;
                foreach (Loan loan in uow.Loans.GetByEmployee(employeeId))
                {
                    if (loan.Status == LoanStatus.Cancelled) continue;
                    total += Math.Max(0m, loan.Principal - Repaid(uow, loan.Id));
                }

                return total;
            }
        }

        // -- internals ---------------------------------------------------------

        private static LoanSummary Summarise(IUnitOfWork uow, Loan loan, IReadOnlyCollection<int> issuedPeriods)
        {
            List<LoanRepayment> repayments = uow.Loans.GetRepayments(loan.Id).ToList();
            decimal repaid = repayments.Sum(r => r.Amount);
            decimal outstanding = Math.Max(0m, loan.Principal - repaid);

            int remaining = 0;
            if (loan.MonthlyInstallment > 0m && outstanding > 0m)
            {
                remaining = (int)Math.Ceiling(outstanding / loan.MonthlyInstallment);
            }

            var summary = new LoanSummary
            {
                LoanId = loan.Id,
                EmployeeId = loan.EmployeeId,
                Status = loan.Status,
                Principal = loan.Principal,
                MonthlyInstallment = loan.MonthlyInstallment,
                Repaid = repaid,
                Outstanding = outstanding,
                RemainingInstallments = remaining
            };

            ApplyLate(summary, loan, repayments, outstanding, issuedPeriods);
            return summary;
        }

        /// <summary>
        /// Flags the loan as late when an instalment was due in a month whose payroll was already
        /// ISSUED and nothing was deducted. A month counts as missed when a live payslip exists for
        /// the employee in that month (<paramref name="issuedPeriods"/>), the month is on or after the
        /// loan's start, and the loan has no repayment recorded for it. Only an Active loan with a
        /// positive balance can be late — a suspended, cancelled or settled loan owes nothing now.
        /// </summary>
        private static void ApplyLate(
            LoanSummary summary, Loan loan, List<LoanRepayment> repayments, decimal outstanding,
            IReadOnlyCollection<int> issuedPeriods)
        {
            int missed = MissedInstallments(loan, repayments, outstanding, issuedPeriods);
            if (missed <= 0) return;

            summary.MissedInstallments = missed;
            summary.MissedAmount = Math.Min(outstanding, missed * loan.MonthlyInstallment);
            summary.IsLate = true;
        }

        /// <summary>
        /// Number of ISSUED-payroll months, on or after the loan's start, that carry no repayment for
        /// this loan — i.e. instalments the payroll could have taken and did not. Zero unless the loan
        /// is Active with a positive balance and a positive instalment.
        /// </summary>
        private static int MissedInstallments(
            Loan loan, List<LoanRepayment> repayments, decimal outstanding, IReadOnlyCollection<int> issuedPeriods)
        {
            if (loan.Status != LoanStatus.Active || outstanding <= 0m || loan.MonthlyInstallment <= 0m) return 0;
            if (issuedPeriods == null || issuedPeriods.Count == 0) return 0;

            int start = Period(loan.StartYear, loan.StartMonth);
            var paid = new HashSet<int>();
            foreach (LoanRepayment r in repayments) paid.Add(Period(r.Year, r.Month));

            int missed = 0;
            foreach (int p in issuedPeriods)
            {
                if (p < start) continue;
                if (paid.Contains(p)) continue;
                missed++;
            }

            return missed;
        }

        private static decimal Repaid(IUnitOfWork uow, long loanId)
        {
            return uow.Loans.GetRepayments(loanId).Sum(r => r.Amount);
        }

        /// <summary>Recomputes the status from the balance after repayments changed.</summary>
        private static void ReconcileStatus(IUnitOfWork uow, long loanId)
        {
            Loan loan = uow.Loans.GetById(loanId);
            if (loan == null || loan.Status == LoanStatus.Cancelled) return;

            decimal outstanding = loan.Principal - Repaid(uow, loanId);

            LoanStatus target = outstanding <= 0m ? LoanStatus.Settled : LoanStatus.Active;

            // Never resurrect a suspended loan here — only toggle between Active/Settled.
            if (loan.Status == LoanStatus.Suspended && target == LoanStatus.Active) return;

            if (loan.Status != target)
            {
                loan.Status = target;
                uow.Loans.Update(loan);
            }
        }

        private static void SettleIfNeeded(IUnitOfWork uow, Loan loan, decimal remainingAfter)
        {
            if (remainingAfter <= 0m && loan.Status == LoanStatus.Active)
            {
                loan.Status = LoanStatus.Settled;
                uow.Loans.Update(loan);
            }
        }

        private static int Period(int year, int month) => (year * 12) + (month - 1);
    }
}
