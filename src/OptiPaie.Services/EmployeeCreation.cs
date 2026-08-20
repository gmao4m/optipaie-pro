using System;
using System.Linq;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Services.Validation;

namespace OptiPaie.Services
{
    /// <summary>
    /// The single validated + audited employee-creation path, operating on a GIVEN unit of
    /// work so it can run standalone (<see cref="EmployeeService.Create"/>) OR as one step of a
    /// larger atomic transaction (recruitment's <c>Hire</c>). This is how a hire goes through
    /// the exact same validation and audit as any other employee creation, while staying inside
    /// one transaction that rolls back as a whole on any failure.
    /// </summary>
    internal static class EmployeeCreation
    {
        public static Result<long> InsertValidated(
            IUnitOfWork uow, Employee employee, IValidator<Employee> validator, IAuditSink audit)
        {
            ValidationResult validation = validator.Validate(employee);
            if (!validation.IsValid)
            {
                return validation.ToFailure<long>();
            }

            if (!uow.Companies.ExistsById(employee.CompanyId))
            {
                return Result.Fail<long>("Entreprise introuvable.", ErrorCodes.EmployeeCompanyNotFound);
            }

            long id = uow.Employees.Insert(employee);
            (audit ?? NullAuditSink.Instance).Record(
                "Employee", id, AuditAction.Created,
                "Employé créé : " + (employee.LastNameFr + " " + employee.FirstNameFr).Trim());
            return Result.Ok(id);
        }

        /// <summary>
        /// Idempotently creates the draft contract for a fresh hire on the SAME unit of work.
        /// Mirrors <see cref="ContractService.CreateDraftFromEmployee"/>; returns the existing
        /// contract id if one is already present.
        /// </summary>
        public static Result<long> InsertDraftContract(IUnitOfWork uow, long employeeId, Employee employee)
        {
            EmploymentContract existing = uow.Contracts.GetByEmployee(employeeId).FirstOrDefault();
            if (existing != null)
            {
                return Result.Ok(existing.Id);
            }

            DateTime start = employee.HireDate == default(DateTime) ? DateTime.Today : employee.HireDate;
            var draft = new EmploymentContract
            {
                EmployeeId = employeeId,
                Type = employee.ContractType,
                Status = ContractStatus.Draft,
                Position = employee.Poste,
                BaseSalary = employee.BaseSalary,
                StartDate = start,
                EndDate = employee.ContractType == ContractType.Cdi ? (DateTime?)null : start.AddYears(1),
                TrialPeriodDays = 0
            };

            return Result.Ok(uow.Contracts.Insert(draft));
        }
    }
}
