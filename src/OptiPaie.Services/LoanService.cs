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

        public LoanService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        /// <summary>Optional audit sink (no-op unless wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        public Result<long> Save(Loan loan)
        {
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
                    uow.Loans.Update(loan);
                    ReconcileStatus(uow, loan.Id);
                    return Result.Ok(loan.Id);
                }

                loan.Status = LoanStatus.Active;
                return Result.Ok(uow.Loans.Insert(loan));
            }
        }

        public Result SetStatus(long id, LoanStatus status)
        {
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
                return loan == null ? null : Summarise(uow, loan);
            }
        }

        public IReadOnlyList<LoanSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Loans.GetByEmployee(employeeId).Select(l => Summarise(uow, l)).ToList();
            }
        }

        public IReadOnlyList<LoanSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                var result = new List<LoanSummary>();
                foreach (Loan loan in uow.Loans.GetByCompany(companyId))
                {
                    LoanSummary summary = Summarise(uow, loan);
                    names.TryGetValue(loan.EmployeeId, out string name);
                    summary.EmployeeName = name;
                    result.Add(summary);
                }

                return result;
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

        public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                int period = Period(year, month);
                List<Loan> loans = uow.Loans.GetByEmployee(employeeId).ToList();

                uow.BeginTransaction();
                try
                {
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

                        decimal amount = Math.Min(loan.MonthlyInstallment, outstanding);
                        uow.Loans.InsertRepayment(new LoanRepayment
                        {
                            LoanId = loan.Id,
                            Year = year,
                            Month = month,
                            Amount = amount,
                            IsManual = false
                        });

                        total += amount;
                        SettleIfNeeded(uow, loan, outstanding - amount);
                    }

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

        private static LoanSummary Summarise(IUnitOfWork uow, Loan loan)
        {
            decimal repaid = Repaid(uow, loan.Id);
            decimal outstanding = Math.Max(0m, loan.Principal - repaid);

            int remaining = 0;
            if (loan.MonthlyInstallment > 0m && outstanding > 0m)
            {
                remaining = (int)Math.Ceiling(outstanding / loan.MonthlyInstallment);
            }

            return new LoanSummary
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
