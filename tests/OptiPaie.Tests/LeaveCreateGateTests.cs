using NUnit.Framework;
using OptiPaie.Core.Leave;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Proof 8 — the "طلب جديد" button. The decision that drives it (open the form, or show a
    /// persistent reason) is a pure gate, so it is tested directly: it opens only with a company AND
    /// active employees, and returns a specific reason code otherwise (never a silent/fleeting failure).
    /// </summary>
    [TestFixture]
    public sealed class LeaveCreateGateTests
    {
        [Test]
        public void CanCreate_OnlyWithCompanyAndActiveEmployees()
        {
            Assert.That(LeaveCreateGate.CanCreate(true, 3), Is.True, "société + employés actifs → le formulaire s'ouvre");
            Assert.That(LeaveCreateGate.CanCreate(false, 3), Is.False, "pas de société → bloqué");
            Assert.That(LeaveCreateGate.CanCreate(true, 0), Is.False, "aucun employé actif → bloqué");
        }

        [Test]
        public void ReasonCode_IsSpecificAndShown_WhenBlocked()
        {
            Assert.That(LeaveCreateGate.ReasonCode(false, 0), Is.EqualTo("Leave_NeedCompany"));
            Assert.That(LeaveCreateGate.ReasonCode(true, 0), Is.EqualTo("Leave_NeedEmployee"));
            Assert.That(LeaveCreateGate.ReasonCode(true, 2), Is.Empty, "aucune raison affichée quand le bouton peut agir");
        }
    }
}
