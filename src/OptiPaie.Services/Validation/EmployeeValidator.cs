using System;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;

namespace OptiPaie.Services.Validation
{
    /// <summary>Validation rules for <see cref="Employee"/>.</summary>
    public sealed class EmployeeValidator : IValidator<Employee>
    {
        public ValidationResult Validate(Employee instance)
        {
            var result = new ValidationResult();

            if (instance == null)
            {
                result.AddError(ErrorCodes.EmployeeLastNameRequired, "Employee is required.");
                return result;
            }

            if (instance.CompanyId <= 0)
            {
                result.AddError(ErrorCodes.EmployeeCompanyRequired,
                    "L'entreprise de l'employé est obligatoire.", nameof(instance.CompanyId));
            }

            if (string.IsNullOrWhiteSpace(instance.LastNameFr))
            {
                result.AddError(ErrorCodes.EmployeeLastNameRequired,
                    "Le nom de l'employé est obligatoire.", nameof(instance.LastNameFr));
            }

            if (string.IsNullOrWhiteSpace(instance.FirstNameFr))
            {
                result.AddError(ErrorCodes.EmployeeFirstNameRequired,
                    "Le prénom de l'employé est obligatoire.", nameof(instance.FirstNameFr));
            }

            if (instance.BaseSalary < 0m)
            {
                result.AddError(ErrorCodes.EmployeeBaseSalaryInvalid,
                    "Le salaire de base ne peut pas être négatif.", nameof(instance.BaseSalary));
            }

            if (instance.HireDate == default(DateTime))
            {
                result.AddError(ErrorCodes.EmployeeHireDateRequired,
                    "La date de recrutement est obligatoire.", nameof(instance.HireDate));
            }

            if (instance.ExitDate.HasValue && instance.ExitDate.Value < instance.HireDate)
            {
                result.AddError(ErrorCodes.EmployeeExitBeforeHire,
                    "La date de sortie ne peut pas précéder la date de recrutement.", nameof(instance.ExitDate));
            }

            return result;
        }
    }
}
