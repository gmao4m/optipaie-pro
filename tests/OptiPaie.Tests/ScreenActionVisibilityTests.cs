using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;

namespace OptiPaie.Tests
{
    /// <summary>
    /// COMMIT E — no button that cannot work. The screens show a create/edit/delete or print/export
    /// button only when <see cref="AccessPolicy.CanEdit"/> / <see cref="AccessPolicy.CanExport"/> is
    /// true for the signed-in role — the SAME source the navigation hides from and the services
    /// enforce with (three places, one truth). This test pins, for EVERY role and EVERY module, the
    /// exact action set those two methods yield, so a button can never be shown for an action the
    /// service layer would reject, nor hidden for one it would allow.
    /// </summary>
    [TestFixture]
    public sealed class ScreenActionVisibilityTests
    {
        private static readonly UserRole[] Roles =
            { UserRole.Admin, UserRole.Hr, UserRole.Payroll, UserRole.Accountant, UserRole.ReadOnly };

        // Expected EDITORS (may create/edit/delete) per module — the "E" column of the role matrix.
        private static readonly Dictionary<string, UserRole[]> Editors = new Dictionary<string, UserRole[]>
        {
            { "companies",        new[] { UserRole.Admin } },
            { "settings",         new[] { UserRole.Admin } },
            { "users",            new[] { UserRole.Admin } },
            { "employees",        new[] { UserRole.Admin, UserRole.Hr } },
            { "payroll-elements", new[] { UserRole.Admin, UserRole.Payroll } },
            { "payroll",          new[] { UserRole.Admin, UserRole.Payroll } },
            { "cnas",             new[] { UserRole.Admin, UserRole.Payroll } },
            { "attendance",       new[] { UserRole.Admin, UserRole.Hr, UserRole.Payroll } },
            { "leave",            new[] { UserRole.Admin, UserRole.Hr } },
            { "loans",            new[] { UserRole.Admin, UserRole.Payroll } },
            { "performance",      new[] { UserRole.Admin, UserRole.Hr } },
            { "contracts",        new[] { UserRole.Admin, UserRole.Hr } },
            { "training",         new[] { UserRole.Admin, UserRole.Hr } },
            { "assets",           new[] { UserRole.Admin, UserRole.Hr } },
            { "work_certificate", new[] { UserRole.Admin, UserRole.Hr } },
            { "ats",              new[] { UserRole.Admin, UserRole.Hr } },
            { "recruitment",      new[] { UserRole.Admin, UserRole.Hr } },
            // Read-only screens — no one edits through this path.
            { "dashboard",        new UserRole[0] },
            { "archive",          new UserRole[0] },
            { "reports",          new UserRole[0] },
            { "attestations",     new UserRole[0] },
        };

        // Expected EXPORTERS (may print/export) per salary/CNAS module — the strict "P" column.
        private static readonly Dictionary<string, UserRole[]> Exporters = new Dictionary<string, UserRole[]>
        {
            { "payroll", new[] { UserRole.Admin, UserRole.Payroll, UserRole.Accountant } },
            { "cnas",    new[] { UserRole.Admin, UserRole.Payroll, UserRole.Accountant } },
        };

        [Test]
        public void EveryRole_SeesExactly_TheEditButtons_ItsPermissionsAllow()
        {
            foreach (KeyValuePair<string, UserRole[]> entry in Editors)
            {
                foreach (UserRole role in Roles)
                {
                    bool expected = entry.Value.Contains(role);
                    Assert.That(AccessPolicy.CanEdit(role, entry.Key), Is.EqualTo(expected),
                        role + " edit '" + entry.Key + "'");
                }
            }
        }

        [Test]
        public void EveryRole_SeesExactly_ThePrintExportButtons_ItsPermissionsAllow()
        {
            foreach (KeyValuePair<string, UserRole[]> entry in Exporters)
            {
                foreach (UserRole role in Roles)
                {
                    bool expected = entry.Value.Contains(role);
                    Assert.That(AccessPolicy.CanExport(role, entry.Key), Is.EqualTo(expected),
                        role + " export '" + entry.Key + "'");
                }
            }
        }

        [Test]
        public void EditVisibility_NeverExceeds_WhatTheServiceAllows()
        {
            // The UI source (CanEdit) must agree exactly with the service source (IsAllowed on the
            // module's edit permission) for every role and module — a visible edit button can never
            // disagree with what the service layer would permit.
            foreach (string module in Editors.Keys)
            {
                Permission? perm = AccessPolicy.EditPermissionFor(module);
                foreach (UserRole role in Roles)
                {
                    bool uiShows = AccessPolicy.CanEdit(role, module);
                    bool serviceAllows = perm.HasValue && AccessPolicy.IsAllowed(role, perm.Value);
                    Assert.That(uiShows, Is.EqualTo(serviceAllows), role + " / " + module);
                }
            }
        }

        [Test]
        public void ReadOnly_CanEditNothing_AndCanExportNoSalaryOrCnas()
        {
            foreach (string module in Editors.Keys)
            {
                Assert.That(AccessPolicy.CanEdit(UserRole.ReadOnly, module), Is.False, "read-only edit " + module);
            }
            Assert.That(AccessPolicy.CanExport(UserRole.ReadOnly, "payroll"), Is.False);
            Assert.That(AccessPolicy.CanExport(UserRole.ReadOnly, "cnas"), Is.False);
        }
    }
}
