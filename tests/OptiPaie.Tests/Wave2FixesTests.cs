using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>Wave 2 (crédibilité) — company legal-id validation.</summary>
    [TestFixture]
    public sealed class Wave2FixesTests
    {
        private readonly CompanyValidator _validator = new CompanyValidator();

        [Test]
        public void ACompanyWithOnlyAName_IsRejected_NifIsRequired()
        {
            var result = _validator.Validate(new Company { NameFr = "SARL Sans Identifiants" });
            Assert.That(result.IsValid, Is.False, "a company can no longer be saved with just a name");
        }

        [Test]
        public void AValidNif_IsAccepted()
        {
            var result = _validator.Validate(new Company { NameFr = "SARL Test", Nif = "000123456789012" }); // 15 digits
            Assert.That(result.IsValid, Is.True, "a valid 15-digit NIF must be accepted");
        }

        [Test]
        public void A_NifOfAnyLength_IsAccepted_NoInventedFixedWidth()
        {
            // Corrected: a real Algerian NIF varies in length. Forcing 15 digits blocked customers,
            // so the length is no longer constrained — only the presence of a NIF is required.
            Assert.That(_validator.Validate(new Company { NameFr = "SARL Test", Nif = "12345" }).IsValid, Is.True,
                "un NIF plus court que 15 chiffres doit être accepté");
            Assert.That(_validator.Validate(new Company { NameFr = "SARL Test", Nif = "00091602547893100000" }).IsValid, Is.True,
                "un NIF plus long que 15 chiffres doit être accepté");
        }

        [Test]
        public void A_Nis_OfAnyLength_IsAccepted_AndEmptyIsAllowed()
        {
            Assert.That(_validator.Validate(new Company { NameFr = "T", Nif = "1", Nis = "999" }).IsValid, Is.True,
                "le NIS n'est plus contraint en longueur");
            Assert.That(_validator.Validate(new Company { NameFr = "T", Nif = "1", Nis = "" }).IsValid, Is.True,
                "et le NIS reste facultatif");
        }
    }
}
