using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Payroll;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;

namespace OptiPaie.Tests
{
    /// <summary>
    /// CACOBATPH (optional BTPH-sector overlay). Proves the pure calculator's rates on the
    /// CNAS base, that it is inert for a non-positive base, and that the two per-company
    /// opt-in flags (migration 0024) persist. The overlay is off by default, so no existing
    /// payslip is affected — that invariant is covered by the unchanged payroll-engine tests.
    /// </summary>
    [TestFixture]
    public sealed class CacobatphTests
    {
        [Test]
        public void Compute_AppliesTheStatutoryRatesOnTheCnasBase()
        {
            CacobatphResult r = CacobatphCalculator.Compute(100000m);

            Assert.That(r.Base, Is.EqualTo(100000m));
            Assert.That(r.CongePaye, Is.EqualTo(12210m), "12,21 % employer");
            Assert.That(r.ChomageEmployer, Is.EqualTo(375m), "0,375 % employer");
            Assert.That(r.ChomageEmployee, Is.EqualTo(375m), "0,375 % employee");
            Assert.That(r.EmployerTotal, Is.EqualTo(12585m), "Congé Payé + Chômage employeur");
            Assert.That(r.EmployeeTotal, Is.EqualTo(375m), "only the employee Chômage share hits net");
        }

        [Test]
        public void Compute_NonPositiveBase_IsZero()
        {
            CacobatphResult r = CacobatphCalculator.Compute(0m);
            Assert.That(r.CongePaye, Is.EqualTo(0m));
            Assert.That(r.ChomageEmployer, Is.EqualTo(0m));
            Assert.That(r.ChomageEmployee, Is.EqualTo(0m));
        }

        [Test]
        public void CompanyFlags_DefaultOff_AndRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cacobatph-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                SqliteTypeHandlers.Register();
                var factory = new SqliteConnectionFactory(Path.Combine(dir, "t.db"));
                using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
                var uow = new UnitOfWorkFactory(factory);

                long defaultId, btphId;
                using (IUnitOfWork u = uow.Create())
                {
                    u.BeginTransaction();
                    // A plain company: both flags default OFF (so its payslips are unaffected).
                    defaultId = u.Companies.Insert(new Company { NameFr = "SARL Standard", Nif = "000000000000001" });
                    // A BTPH company that opted in.
                    btphId = u.Companies.Insert(new Company
                    {
                        NameFr = "SARL BTP", Nif = "000000000000002",
                        BtphSector = true, CacobatphEnabled = true
                    });
                    u.Commit();
                }

                using (IUnitOfWork u = uow.Create())
                {
                    Company plain = u.Companies.GetById(defaultId);
                    Assert.That(plain.BtphSector, Is.False);
                    Assert.That(plain.CacobatphEnabled, Is.False);

                    Company btph = u.Companies.GetById(btphId);
                    Assert.That(btph.BtphSector, Is.True);
                    Assert.That(btph.CacobatphEnabled, Is.True);
                }
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
