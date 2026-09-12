using System.Linq;
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

            // The NIF (fiscal id) is the one legal identifier that appears on every payslip, CNAS
            // declaration and attestation and is universally mandatory for a registered employer —
            // require it (15 digits) so official documents never go out without it. The other IDs
            // (NIS, RC) vary by entity, so they are only format-checked WHEN provided, never forced.
            string nif = (instance.Nif ?? string.Empty).Trim();
            if (nif.Length == 0)
            {
                result.AddError("Company_NifRequired",
                    "Le NIF (identifiant fiscal) de l'entreprise est obligatoire. / رقم التعريف الجبائي (NIF) إجباري.", nameof(instance.Nif));
            }
            else if (Digits(nif) != 15)
            {
                result.AddError("Company_NifInvalid",
                    "Le NIF doit comporter 15 chiffres. / يجب أن يتكوّن NIF من 15 رقمًا.", nameof(instance.Nif));
            }

            ValidateOptionalDigits(result, instance.Nis, nameof(instance.Nis), "NIS", "Company_NisInvalid", 15);

            return result;
        }

        private static int Digits(string s) => s.Count(char.IsDigit);

        /// <summary>Validates a legal-id FORMAT only when a value is present — never requires it.</summary>
        private static void ValidateOptionalDigits(ValidationResult result, string value, string field, string label, string code, int digits)
        {
            string v = (value ?? string.Empty).Trim();
            if (v.Length > 0 && Digits(v) != digits)
            {
                result.AddError(code, "Le " + label + " doit comporter " + digits + " chiffres. / يجب أن يتكوّن " + label + " من " + digits + " رقمًا.", field);
            }
        }
    }
}
