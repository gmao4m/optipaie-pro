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
        public void A_NifThatIsNot15Digits_IsRejected()
        {
            Assert.That(_validator.Validate(new Company { NameFr = "SARL Test", Nif = "12345" }).IsValid, Is.False);
        }

        [Test]
        public void AMalformed_Nis_IsRejected_ButAnEmptyNisIsAllowed()
        {
            Assert.That(_validator.Validate(new Company { NameFr = "T", Nif = "000123456789012", Nis = "999" }).IsValid, Is.False,
                "a provided NIS must be well-formed");
            Assert.That(_validator.Validate(new Company { NameFr = "T", Nif = "000123456789012", Nis = "" }).IsValid, Is.True,
                "but the NIS is optional");
        }
    }
}
