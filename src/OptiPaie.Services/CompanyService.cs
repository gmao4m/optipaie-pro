using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Services.Validation;

namespace OptiPaie.Services
{
    /// <summary>Manages companies: validation, then persistence via the unit of work.</summary>
    public sealed class CompanyService : ICompanyService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IValidator<Company> _validator;

        public CompanyService(IUnitOfWorkFactory unitOfWorkFactory, IValidator<Company> validator)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _validator = Guard.AgainstNull(validator, nameof(validator));
        }

        public Result<long> Create(Company company)
        {
            ValidationResult validation = _validator.Validate(company);
            if (!validation.IsValid)
            {
                return validation.ToFailure<long>();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long id = uow.Companies.Insert(company);
                return Result.Ok(id);
            }
        }

        public Result Update(Company company)
        {
            ValidationResult validation = _validator.Validate(company);
            if (!validation.IsValid)
            {
                return validation.ToFailure();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Companies.ExistsById(company.Id))
                {
                    return Result.Fail("Entreprise introuvable.", ErrorCodes.NotFound);
                }

                uow.Companies.Update(company);
                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Companies.ExistsById(id))
                {
                    return Result.Fail("Entreprise introuvable.", ErrorCodes.NotFound);
                }

                // Never make a whole client file disappear. Refuse the deletion while the company
                // still holds employees (and therefore their payslips, declarations and history);
                // an employee that has any payslip can't be removed either (see EmployeeService), so
                // the employee count is the single, honest gate. Message is bilingual FR/AR.
                int employees = uow.Employees.GetByCompany(id, true).Count();
                if (employees > 0)
                {
                    return Result.Fail(
                        "Impossible de supprimer cette entreprise : elle contient encore " + employees +
                        " employé(s) et leur historique (bulletins, déclarations). Supprimez ou déplacez d'abord les employés.\n" +
                        "لا يمكن حذف هذه المؤسسة: فهي لا تزال تحتوي على " + employees +
                        " موظّف(ين) وسجلّاتهم (كشوف الأجور، التصريحات). احذف الموظفين أو انقلهم أولاً.",
                        "Company_HasDependencies");
                }

                uow.Companies.SoftDelete(id);
                return Result.Ok();
            }
        }

        public Company Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Companies.GetById(id);
            }
        }

        public IReadOnlyList<Company> GetAll()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Companies.GetAll().ToList();
            }
        }
    }
}
