using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Services.Validation;

namespace OptiPaie.Services
{
    /// <summary>Manages employees and their payroll element assignments.</summary>
    public sealed class EmployeeService : IEmployeeService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IValidator<Employee> _validator;

        public EmployeeService(IUnitOfWorkFactory unitOfWorkFactory, IValidator<Employee> validator)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _validator = Guard.AgainstNull(validator, nameof(validator));
        }

        /// <summary>Audit trail (who created/changed/deleted an employee or their salary). Best-effort.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        private static string NameOf(Employee e) => e == null ? string.Empty : (e.LastNameFr + " " + e.FirstNameFr).Trim();

        private static string SalaryValue(EmployeeElement e) =>
            e == null ? null : (e.Amount.HasValue ? e.Amount.Value.ToString("0.##", CultureInfo.InvariantCulture) : "-");

        public Result<long> Create(Employee employee)
        {
            // The single validated + audited creation path (shared with recruitment's Hire).
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return EmployeeCreation.InsertValidated(uow, employee, _validator, Audit);
            }
        }

        public Result Update(Employee employee)
        {
            ValidationResult validation = _validator.Validate(employee);
            if (!validation.IsValid)
            {
                return validation.ToFailure();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Employees.ExistsById(employee.Id))
                {
                    return Result.Fail("Employé introuvable.", ErrorCodes.NotFound);
                }

                if (!uow.Companies.ExistsById(employee.CompanyId))
                {
                    return Result.Fail("Entreprise introuvable.", ErrorCodes.EmployeeCompanyNotFound);
                }

                uow.Employees.Update(employee);
                Audit.Record("Employee", employee.Id, AuditAction.Updated, "Employé modifié : " + NameOf(employee));
                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee existing = uow.Employees.GetById(id);
                if (existing == null)
                {
                    return Result.Fail("Employé introuvable.", ErrorCodes.NotFound);
                }

                // An employee that has any payslip carries CNAS declaration history: deleting them
                // (soft-delete hides them from GetByCompany) makes the DAS skip their payslips while
                // the DAC still counts them, corrupting the annual cross-check. Refuse; the user marks
                // a departure with an exit date instead (which keeps the payslips declarable).
                if (uow.Payslips.GetByEmployee(id).Any())
                {
                    return Result.Fail(
                        "Impossible de supprimer cet employé : il possède des bulletins de paie (historique CNAS/DAS). " +
                        "Renseignez plutôt sa date de sortie.\n" +
                        "لا يمكن حذف هذا الموظف لأنّ له كشوف أجور (سجلّ CNAS/DAS). ضع تاريخ مغادرته بدلاً من حذفه.",
                        "Employee_HasPayslips");
                }

                uow.Employees.SoftDelete(id);
                Audit.Record("Employee", id, AuditAction.Deleted, "Employé supprimé : " + NameOf(existing));
                return Result.Ok();
            }
        }

        public Employee Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Employees.GetById(id);
            }
        }

        public IReadOnlyList<Employee> GetByCompany(long companyId, bool includeInactive = true)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Employees.GetByCompany(companyId, includeInactive).ToList();
            }
        }

        public IReadOnlyList<EmployeeElement> GetElements(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.EmployeeElements.GetByEmployee(employeeId).ToList();
            }
        }

        public Result<long> AssignElement(EmployeeElement assignment)
        {
            Guard.AgainstNull(assignment, nameof(assignment));

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Employees.ExistsById(assignment.EmployeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", ErrorCodes.EmployeeElementEmployeeNotFound);
                }

                if (!uow.PayrollElements.ExistsById(assignment.ElementId))
                {
                    return Result.Fail<long>("Rubrique introuvable.", ErrorCodes.EmployeeElementElementNotFound);
                }

                if (uow.EmployeeElements.GetByEmployeeAndElement(assignment.EmployeeId, assignment.ElementId) != null)
                {
                    return Result.Fail<long>("Cette rubrique est déjà affectée à l'employé.", ErrorCodes.EmployeeElementDuplicate);
                }

                long id = uow.EmployeeElements.Insert(assignment);
                Audit.Record("Salary", assignment.EmployeeId, AuditAction.Created,
                    "Rubrique de salaire affectée", null, SalaryValue(assignment));
                return Result.Ok(id);
            }
        }

        public Result UpdateElement(EmployeeElement assignment)
        {
            Guard.AgainstNull(assignment, nameof(assignment));

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmployeeElement existing = uow.EmployeeElements.GetById(assignment.Id);
                if (existing == null)
                {
                    return Result.Fail("Affectation introuvable.", ErrorCodes.NotFound);
                }

                uow.EmployeeElements.Update(assignment);
                Audit.Record("Salary", existing.EmployeeId, AuditAction.Updated,
                    "Rubrique de salaire modifiée", SalaryValue(existing), SalaryValue(assignment));
                return Result.Ok();
            }
        }

        public Result RemoveElement(long employeeElementId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                EmployeeElement existing = uow.EmployeeElements.GetById(employeeElementId);
                if (existing == null)
                {
                    return Result.Fail("Affectation introuvable.", ErrorCodes.NotFound);
                }

                uow.EmployeeElements.Delete(employeeElementId);
                Audit.Record("Salary", existing.EmployeeId, AuditAction.Deleted,
                    "Rubrique de salaire retirée", SalaryValue(existing), null);
                return Result.Ok();
            }
        }
    }
}
