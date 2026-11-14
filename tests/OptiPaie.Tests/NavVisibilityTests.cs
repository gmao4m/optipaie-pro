using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 5 — the UI follows the role. The shell navigation shows an entry only when
    /// AccessPolicy.CanOpen is true for the signed-in role (the SAME source the services enforce
    /// with), so a visible entry can never disagree with what the service layer allows. This test
    /// pins, for every role, the EXACT set of openable module keys — no extra entry, none missing.
    /// </summary>
    [TestFixture]
    public sealed class NavVisibilityTests
    {
        // Every top-level navigation key the shell can show.
        private static readonly string[] AllKeys =
        {
            "dashboard","companies","employees","payroll","cnas","attestations",
            "ats","attendance","leave","loans","performance","contracts","training",
            "assets","work_certificate","archive","reports","settings"
        };

        private static HashSet<string> OpenableFor(UserRole role) =>
            AllKeys.Where(k => AccessPolicy.CanOpen(role, k)).ToHashSet();

        [Test]
        public void Admin_SeesEverything()
        {
            Assert.That(OpenableFor(UserRole.Admin), Is.EquivalentTo(AllKeys));
        }

        [Test]
        public void Hr_SeesAll_Except_Admin_And_Cnas()
        {
            Assert.That(OpenableFor(UserRole.Hr), Is.EquivalentTo(
                AllKeys.Except(new[] { "companies", "settings", "cnas" })));
        }

        [Test]
        public void Payroll_SeesAll_Except_Admin_And_HrOnlyModules()
        {
            Assert.That(OpenableFor(UserRole.Payroll), Is.EquivalentTo(
                AllKeys.Except(new[] { "companies", "settings", "performance", "ats", "training", "assets" })));
        }

        [Test]
        public void Accountant_SeesAll_Except_Admin_Performance_Recruitment()
        {
            Assert.That(OpenableFor(UserRole.Accountant), Is.EquivalentTo(
                AllKeys.Except(new[] { "companies", "settings", "performance", "ats" })));
        }

        [Test]
        public void ReadOnly_SeesEverything_Except_Admin()
        {
            Assert.That(OpenableFor(UserRole.ReadOnly), Is.EquivalentTo(
                AllKeys.Except(new[] { "companies", "settings" })));
        }

        [Test]
        public void AdminOnlyScreens_AreHiddenFromAllNonAdminRoles()
        {
            foreach (UserRole r in new[] { UserRole.Hr, UserRole.Payroll, UserRole.Accountant, UserRole.ReadOnly })
            {
                Assert.That(AccessPolicy.CanOpen(r, "companies"), Is.False, r + " companies");
                Assert.That(AccessPolicy.CanOpen(r, "settings"), Is.False, r + " settings");
                Assert.That(AccessPolicy.CanOpen(r, "users"), Is.False, r + " users");
            }
        }
    }
}
