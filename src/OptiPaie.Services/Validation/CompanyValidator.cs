using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;

namespace OptiPaie.Services.Validation
{
    /// <summary>Validation rules for <see cref="Company"/>.</summary>
    public sealed class CompanyValidator : IValidator<Company>
    {
        public ValidationResult Validate(Company instance)
        {
            var result = new ValidationResult();

            if (instance == null)
            {
                result.AddError(ErrorCodes.CompanyNameRequired, "Company is required.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(instance.NameFr))
            {
                result.AddError(ErrorCodes.CompanyNameRequired,
                    "Le nom de l'entreprise est obligatoire.", nameof(instance.NameFr));
            }

            return result;
        }
    }
}
