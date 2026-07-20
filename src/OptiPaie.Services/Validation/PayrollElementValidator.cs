using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Services.Validation
{
    /// <summary>Validation rules for <see cref="PayrollElement"/>.</summary>
    public sealed class PayrollElementValidator : IValidator<PayrollElement>
    {
        public ValidationResult Validate(PayrollElement instance)
        {
            var result = new ValidationResult();

            if (instance == null)
            {
                result.AddError(ErrorCodes.ElementNameRequired, "Element is required.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(instance.NameFr))
            {
                result.AddError(ErrorCodes.ElementNameRequired,
                    "Le libellé de la rubrique est obligatoire.", nameof(instance.NameFr));
            }

            bool needsBase = instance.CalculationMethod == CalculationMethod.Percentage
                             || instance.CalculationMethod == CalculationMethod.BaseRate;

            if (needsBase && instance.CalculationBase == null)
            {
                result.AddError(ErrorCodes.ElementBaseRequired,
                    "Une base de calcul est requise pour cette méthode.", nameof(instance.CalculationBase));
            }

            return result;
        }
    }
}
