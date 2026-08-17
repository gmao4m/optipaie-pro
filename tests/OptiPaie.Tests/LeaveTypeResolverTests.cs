using System.Collections.Generic;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Leave;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The two indicators (payment category + "decrements the balance") that the list AND the form
    /// expose for every request come from one resolver — proven here so the badges can never lie.
    /// </summary>
    [TestFixture]
    public sealed class LeaveTypeResolverTests
    {
        private static readonly IReadOnlyDictionary<long, LeaveTypeDefinition> None = new Dictionary<long, LeaveTypeDefinition>();

        [Test]
        public void LegacyTypes_ExposeBothIndicators()
        {
            Assert.That(LeaveTypeResolver.Category(null, LeaveType.Annual, None), Is.EqualTo(PaymentCategory.EmployerPaid));
            Assert.That(LeaveTypeResolver.Decrements(null, LeaveType.Annual, None), Is.True, "congé annuel : décompté");

            Assert.That(LeaveTypeResolver.Category(null, LeaveType.Unpaid, None), Is.EqualTo(PaymentCategory.Unpaid));
            Assert.That(LeaveTypeResolver.Decrements(null, LeaveType.Unpaid, None), Is.False);

            foreach (LeaveType t in new[] { LeaveType.Sick, LeaveType.Maternity, LeaveType.Special })
            {
                Assert.That(LeaveTypeResolver.Category(null, t, None), Is.EqualTo(PaymentCategory.EmployerPaid), t.ToString());
                Assert.That(LeaveTypeResolver.Decrements(null, t, None), Is.False, t.ToString());
            }
        }

        [Test]
        public void ConfiguredType_OverridesWithItsOwnIndicators()
        {
            var def = new LeaveTypeDefinition { Id = 7, PaymentCategory = PaymentCategory.SocialSecurity, DecrementsAnnualBalance = false };
            var types = new Dictionary<long, LeaveTypeDefinition> { { 7, def } };

            Assert.That(LeaveTypeResolver.Category(7, LeaveType.Sick, types), Is.EqualTo(PaymentCategory.SocialSecurity));
            Assert.That(LeaveTypeResolver.Decrements(7, LeaveType.Annual, types), Is.False, "le type configuré prime sur l'ancien");
        }

        [Test]
        public void BadgeKeys_AreDistinctPerCategory()
        {
            Assert.That(LeaveTypeResolver.PaymentKey(PaymentCategory.EmployerPaid), Is.EqualTo("Leave_Pay_Employer"));
            Assert.That(LeaveTypeResolver.PaymentKey(PaymentCategory.SocialSecurity), Is.EqualTo("Leave_Pay_Cnas"));
            Assert.That(LeaveTypeResolver.PaymentKey(PaymentCategory.Unpaid), Is.EqualTo("Leave_Pay_Unpaid"));
            Assert.That(LeaveTypeResolver.DecrementKey(true), Is.EqualTo("Leave_Decrements"));
            Assert.That(LeaveTypeResolver.DecrementKey(false), Is.EqualTo("Leave_NoDecrement"));
        }
    }
}
