using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Employment-contract orchestration. Owns the contract lifecycle and the expiry
    /// rules, and is the module that keeps the SHARED employee record current:
    ///   • activating a contract copies its salary, type and position onto the employee
    ///     (so payroll immediately uses the terms in force) and supersedes the previous
    ///     active contract — one active contract per employee at a time;
    ///   • terminating a contract sets the employee's exit date;
    ///   • renewing chains a new active contract to the old one.
    /// No employee or company data is duplicated — the contract references the shared
    /// Employees table and writes back to it inside one transaction.
    /// </summary>
    public sealed class ContractService : IContractService
    {
        /// <summary>Default window (days) for the "expiring soon" flag on a summary.</summary>
        private const int DefaultAlertWindow = 30;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public ContractService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> Save(EmploymentContract contract)
        {
            if (contract == null)
            {
                return Result.Fail<long>("Aucun contrat.", "Contract_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (contract.EmployeeId <= 0 || !uow.Employees.ExistsById(contract.EmployeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Contract_EmployeeNotFound");
                }

                Result validation = Validate(contract);
                if (validation.IsFailure)
                {
                    return Result.Fail<long>(validation.Error, validation.ErrorCode);
                }

                if (contract.Id > 0)
                {
                    EmploymentContract existing = uow.Contracts.GetById(contract.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Contrat introuvable.", "Contract_NotFound");
                    }

                    if (existing.Status != ContractStatus.Draft)
                    {
                        // A contract in force keeps its legal terms — only the reference,
                        // signature date and notes may be corrected. Use Renew/Terminate
                        // to change the actual terms.
                        existing.Reference = contract.Reference;
                        existing.SignedDate = contract.SignedDate;
                        existing.Notes = contract.Notes;
                        uow.Contracts.Update(existing);
                        return Result.Ok(existing.Id);
                    }

                    contract.CreatedAtUtc = existing.CreatedAtUtc;
                    contract.Status = ContractStatus.Draft;
                    contract.PreviousContractId = existing.PreviousContractId;
                    uow.Contracts.Update(contract);
                    return Result.Ok(contract.Id);
                }

                contract.Status = ContractStatus.Draft;
                return Result.Ok(uow.Contracts.Insert(contract));
            }
        }

        public Result Activate(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmploymentContract contract = uow.Contracts.GetById(id);
                if (contract == null)
                {
                    return Result.Fail("Contrat introuvable.", "Contract_NotFound");
                }

                if (contract.Status == ContractStatus.Active)
                {
                    return Result.Ok();
                }

                if (contract.Status != ContractStatus.Draft)
                {
                    return Result.Fail("Seul un contrat en préparation peut être activé.", "Contract_NotActivatable");
                }

                uow.BeginTransaction();
                try
                {
                    SupersedeActive(uow, contract);
                    contract.Status = ContractStatus.Active;
                    uow.Contracts.Update(contract);
                    SyncEmployee(uow, contract);
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

        public Result Terminate(long id, DateTime effectiveDate, string reason)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmploymentContract contract = uow.Contracts.GetById(id);
                if (contract == null)
                {
                    return Result.Fail("Contrat introuvable.", "Contract_NotFound");
                }

                if (contract.Status != ContractStatus.Active)
                {
                    return Result.Fail("Seul un contrat en vigueur peut être résilié.", "Contract_NotActive");
                }

                if (effectiveDate.Date < contract.StartDate.Date)
                {
                    return Result.Fail("La date de fin ne peut pas précéder le début du contrat.", "Contract_EndBeforeStart");
                }

                uow.BeginTransaction();
                try
                {
                    contract.Status = ContractStatus.Terminated;
                    contract.EndDate = effectiveDate.Date;
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        contract.Notes = string.IsNullOrWhiteSpace(contract.Notes)
                            ? reason
                            : contract.Notes + Environment.NewLine + reason;
                    }

                    uow.Contracts.Update(contract);

                    Employee employee = uow.Employees.GetById(contract.EmployeeId);
                    if (employee != null)
                    {
                        employee.ExitDate = effectiveDate.Date;
                        employee.IsActive = false;
                        uow.Employees.Update(employee);
                    }

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

        public Result<long> Renew(long id, DateTime newStart, DateTime? newEnd, decimal newBaseSalary)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmploymentContract old = uow.Contracts.GetById(id);
                if (old == null)
                {
                    return Result.Fail<long>("Contrat introuvable.", "Contract_NotFound");
                }

                if (old.Status != ContractStatus.Active && old.Status != ContractStatus.Expired)
                {
                    return Result.Fail<long>(
                        "Seul un contrat en vigueur ou expiré peut être renouvelé.", "Contract_NotRenewable");
                }

                if (newBaseSalary <= 0m)
                {
                    return Result.Fail<long>("Le salaire doit être positif.", "Contract_SalaryInvalid");
                }

                if (newEnd.HasValue && newEnd.Value.Date < newStart.Date)
                {
                    return Result.Fail<long>("La date de fin doit suivre la date de début.", "Contract_EndBeforeStart");
                }

                if (old.Type != ContractType.Cdi && !newEnd.HasValue)
                {
                    return Result.Fail<long>("Un contrat à durée déterminée doit avoir une date de fin.", "Contract_EndRequired");
                }

                var renewal = new EmploymentContract
                {
                    EmployeeId = old.EmployeeId,
                    Type = old.Type,
                    Status = ContractStatus.Draft,
                    Reference = old.Reference,
                    Position = old.Position,
                    BaseSalary = newBaseSalary,
                    StartDate = newStart.Date,
                    EndDate = newEnd,
                    TrialPeriodDays = 0, // no trial period on a renewal
                    PreviousContractId = old.Id,
                    SignedDate = null
                };

                uow.BeginTransaction();
                try
                {
                    long newId = uow.Contracts.Insert(renewal);

                    // Activating supersedes the old contract (marked Renewed because it is
                    // this renewal's predecessor) and syncs the employee.
                    renewal = uow.Contracts.GetById(newId);
                    SupersedeActive(uow, renewal);
                    renewal.Status = ContractStatus.Active;
                    uow.Contracts.Update(renewal);
                    SyncEmployee(uow, renewal);

                    uow.Commit();
                    return Result.Ok(newId);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmploymentContract contract = uow.Contracts.GetById(id);
                if (contract == null)
                {
                    return Result.Ok();
                }

                if (contract.Status == ContractStatus.Active)
                {
                    return Result.Fail("Un contrat en vigueur ne peut pas être supprimé — résiliez-le d'abord.", "Contract_ActiveNotDeletable");
                }

                uow.Contracts.SoftDelete(id);
                return Result.Ok();
            }
        }

        public EmploymentContract Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Contracts.GetById(id);
            }
        }

        public ContractSummary GetSummary(long contractId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmploymentContract contract = uow.Contracts.GetById(contractId);
                if (contract == null) return null;

                ContractSummary summary = Summarise(contract, DateTime.Today);
                Employee employee = uow.Employees.GetById(contract.EmployeeId);
                if (employee != null)
                {
                    summary.EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
                }

                return summary;
            }
        }

        public IReadOnlyList<ContractSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                DateTime today = DateTime.Today;
                return uow.Contracts.GetByEmployee(employeeId).Select(c => Summarise(c, today)).ToList();
            }
        }

        public IReadOnlyList<ContractSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                DateTime today = DateTime.Today;
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                var result = new List<ContractSummary>();
                foreach (EmploymentContract contract in uow.Contracts.GetByCompany(companyId))
                {
                    ContractSummary summary = Summarise(contract, today);
                    names.TryGetValue(contract.EmployeeId, out string name);
                    summary.EmployeeName = name;
                    result.Add(summary);
                }

                return result;
            }
        }

        public IReadOnlyList<ContractSummary> GetExpiring(long companyId, int withinDays)
        {
            return GetByCompany(companyId)
                .Where(s => s.Status == ContractStatus.Active && s.EndDate.HasValue
                            && s.DaysUntilExpiry.HasValue && s.DaysUntilExpiry.Value <= withinDays)
                .OrderBy(s => s.DaysUntilExpiry)
                .ToList();
        }

        // -- cross-module synchronisation --------------------------------------

        /// <summary>Marks the employee's current active contract as superseded.</summary>
        private static void SupersedeActive(IUnitOfWork uow, EmploymentContract incoming)
        {
            foreach (EmploymentContract other in uow.Contracts.GetByEmployee(incoming.EmployeeId))
            {
                if (other.Id == incoming.Id) continue;
                if (other.Status != ContractStatus.Active) continue;

                // The predecessor of a renewal is "Renewed"; any other active contract is
                // simply closed as "Expired".
                other.Status = incoming.PreviousContractId == other.Id
                    ? ContractStatus.Renewed
                    : ContractStatus.Expired;
                uow.Contracts.Update(other);
            }
        }

        /// <summary>Writes the contract's terms onto the shared employee record.</summary>
        private static void SyncEmployee(IUnitOfWork uow, EmploymentContract contract)
        {
            Employee employee = uow.Employees.GetById(contract.EmployeeId);
            if (employee == null) return;

            employee.BaseSalary = contract.BaseSalary;
            employee.ContractType = contract.Type;
            if (!string.IsNullOrWhiteSpace(contract.Position))
            {
                employee.Poste = contract.Position;
            }

            // A contract in force means the employee is currently employed.
            employee.ExitDate = null;
            employee.IsActive = true;
            uow.Employees.Update(employee);
        }

        // -- internals ---------------------------------------------------------

        private static Result Validate(EmploymentContract contract)
        {
            if (contract.BaseSalary <= 0m)
            {
                return Result.Fail("Le salaire de base doit être positif.", "Contract_SalaryInvalid");
            }

            if (contract.StartDate == default(DateTime))
            {
                return Result.Fail("La date de début est obligatoire.", "Contract_StartRequired");
            }

            if (contract.Type == ContractType.Cdi)
            {
                // An open-ended contract has no end date.
                contract.EndDate = null;
            }
            else if (!contract.EndDate.HasValue)
            {
                return Result.Fail("Un contrat à durée déterminée doit avoir une date de fin.", "Contract_EndRequired");
            }

            if (contract.EndDate.HasValue && contract.EndDate.Value.Date < contract.StartDate.Date)
            {
                return Result.Fail("La date de fin doit suivre la date de début.", "Contract_EndBeforeStart");
            }

            if (contract.TrialPeriodDays < 0 || contract.TrialPeriodDays > 365)
            {
                return Result.Fail("Période d'essai invalide.", "Contract_TrialInvalid");
            }

            return Result.Ok();
        }

        private static ContractSummary Summarise(EmploymentContract contract, DateTime today)
        {
            int? days = contract.EndDate.HasValue
                ? (int?)(contract.EndDate.Value.Date - today).TotalDays
                : null;

            bool active = contract.Status == ContractStatus.Active;

            return new ContractSummary
            {
                ContractId = contract.Id,
                EmployeeId = contract.EmployeeId,
                Type = contract.Type,
                Status = contract.Status,
                Reference = contract.Reference,
                Position = contract.Position,
                BaseSalary = contract.BaseSalary,
                StartDate = contract.StartDate,
                EndDate = contract.EndDate,
                DaysUntilExpiry = days,
                IsOverdue = active && days.HasValue && days.Value < 0,
                IsExpiringSoon = active && days.HasValue && days.Value >= 0 && days.Value <= DefaultAlertWindow
            };
        }
    }
}
