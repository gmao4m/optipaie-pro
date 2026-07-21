using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Employment-contract operations. Owns the contract lifecycle and the expiry
    /// rules, and keeps the SHARED employee in sync: activating a contract writes its
    /// salary, type and position onto the employee, terminating one sets the exit date.
    /// </summary>
    public interface IContractService
    {
        /// <summary>Creates or updates a contract (a contract in force has limited edits).</summary>
        Result<long> Save(EmploymentContract contract);

        /// <summary>
        /// Puts a contract in force: supersedes the employee's current active contract
        /// and copies this contract's terms onto the shared employee record.
        /// </summary>
        Result Activate(long id);

        /// <summary>Ends a contract early and sets the employee's exit date.</summary>
        Result Terminate(long id, DateTime effectiveDate, string reason);

        /// <summary>
        /// Creates a renewal contract linked to an existing one, marks the old one
        /// renewed and activates the new one. Returns the new contract id.
        /// </summary>
        Result<long> Renew(long id, DateTime newStart, DateTime? newEnd, decimal newBaseSalary);

        /// <summary>Soft-deletes a contract (a contract in force cannot be deleted).</summary>
        Result Delete(long id);

        EmploymentContract Get(long id);

        ContractSummary GetSummary(long contractId);

        /// <summary>Contracts of one employee with their derived positions, newest first.</summary>
        IReadOnlyList<ContractSummary> GetByEmployee(long employeeId);

        /// <summary>Contracts of a whole company with their derived positions.</summary>
        IReadOnlyList<ContractSummary> GetByCompany(long companyId);

        /// <summary>
        /// Active fixed-term contracts of a company expiring within <paramref name="withinDays"/>
        /// (including any already overdue), soonest first.
        /// </summary>
        IReadOnlyList<ContractSummary> GetExpiring(long companyId, int withinDays);
    }
}
