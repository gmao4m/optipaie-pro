using System;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>Vague 5 / IDX 20 — the employee validator now bounds the recruitment and birth dates.</summary>
    [TestFixture]
    public sealed class EmployeeValidatorDateBoundsTests
    {
        private static Employee ValidBase() => new Employee
        {
            CompanyId = 1, LastNameFr = "Test", FirstNameFr = "Ali",
            BaseSalary = 30000m, HireDate = DateTime.Today.AddYears(-2)
        };

        [Test]
        public void FutureHireDate_is_rejected()
        {
            var e = ValidBase(); e.HireDate = DateTime.Today.AddYears(1);
            var r = new EmployeeValidator().Validate(e);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(m => m.Code == ErrorCodes.EmployeeHireDateInFuture));
        }

        [Test]
        public void BirthDate_in_future_is_rejected()
        {
            var e = ValidBase(); e.BirthDate = DateTime.Today.AddDays(1);
            var r = new EmployeeValidator().Validate(e);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(m => m.Code == ErrorCodes.EmployeeBirthDateInvalid));
        }

        [Test]
        public void BirthDate_after_hire_is_rejected()
        {
            var e = ValidBase();
            e.HireDate = new DateTime(2015, 1, 1);
            e.BirthDate = new DateTime(2016, 1, 1);
            var r = new EmployeeValidator().Validate(e);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(m => m.Code == ErrorCodes.EmployeeBirthDateInvalid));
        }

        [Test]
        public void Coherent_dates_pass()
        {
            var e = ValidBase();
            e.HireDate = new DateTime(2015, 6, 1);
            e.BirthDate = new DateTime(1985, 3, 12);
            var r = new EmployeeValidator().Validate(e);
            Assert.IsTrue(r.Errors.All(m => m.Code != ErrorCodes.EmployeeHireDateInFuture
                                          && m.Code != ErrorCodes.EmployeeBirthDateInvalid));
        }
    }
}
