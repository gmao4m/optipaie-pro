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

            // The NIF (identifiant fiscal) is printed on every payslip, CNAS declaration and
            // attestation, so a registered employer must have one — it is REQUIRED (non-empty).
            // Its LENGTH is NOT constrained: Algerian NIFs vary in length, and forcing a fixed digit
            // count (formerly 15) wrongly REFUSED legitimate real values and blocked customers from
            // saving their own company data. NO identifier is length-checked at entry any more — NIS,
            // RC, article d'imposition, n° employeur CNAS, RIB are all accepted as typed. Where an
            // official CNAS/attestation FILE needs a fixed field width, that check lives at export/
            // print time (CnasIdentityRules, the ATS/DRT generation), reporting clearly rather than
            // refusing data entry.
            if (string.IsNullOrWhiteSpace(instance.Nif))
            {
                result.AddError("Company_NifRequired",
                    "Le NIF (identifiant fiscal) de l'entreprise est obligatoire. / رقم التعريف الجبائي (NIF) إجباري.", nameof(instance.Nif));
            }

            return result;
        }
    }
}
