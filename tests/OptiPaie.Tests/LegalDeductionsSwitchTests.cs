using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Payroll;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The IRG/CNAS on-off switch. The engine change is a GATE around the legal rules (no formula
    /// touched): ON (the default) must be the historical pipeline exactly; OFF skips CNAS, IRG and
    /// IRG lissage so the net is the gross minus only the user-entered deductions. The setting is
    /// read FAIL-SAFE, written by administrators only, audited, and migrated ON for every database.
    /// </summary>
    [TestFixture]
    public sealed class LegalDeductionsSwitchTests
    {
        // ------------------------------------------------------------------ engine gate

        private static PayrollContext Context(bool? applyLegal)
        {
            var ctx = new PayrollContext
            {
                Period = new PayrollPeriod(2026, 7),
                BaseSalary = 60000m,
                WorkedDays = 31m,
                WorkableDays = 31m,
                Legal = new LegalSnapshot(0.09m, 0.26m, 24000m, RoundingPolicy.Centime),
                Elements = new List<PayrollElementInput>
                {
                    // A cotisable + taxable allowance and a user-entered net deduction (an advance).
                    new PayrollElementInput
                    {
                        ElementId = 101, LabelFr = "Prime de rendement", LabelAr = "منحة", ElementType = ElementType.Gain,
                        CalculationMethod = CalculationMethod.FixedAmount, Amount = 8000m,
                        IsCnasApplicable = true, IsIrgApplicable = true, CnasFactor = 1m, IrgFactor = 1m,
                        IsIncludedInGross = true, Periodicity = ElementPeriodicity.Monthly
                    },
                    new PayrollElementInput
                    {
                        ElementId = 102, LabelFr = "Acompte", LabelAr = "تسبيق", ElementType = ElementType.Deduction,
                        CalculationMethod = CalculationMethod.FixedAmount, Amount = 5000m,
                        IsIncludedInGross = false, Periodicity = ElementPeriodicity.Monthly
                    }
                }
            };
            if (applyLegal.HasValue) ctx.ApplyLegalDeductions = applyLegal.Value;
            return ctx;
        }

        private static string Errors(PayrollResult r) =>
            string.Join(" / ", r.Messages.Select(m => m.Severity + ":" + m.Code + ":" + m.Text));

        private static string Fingerprint(PayrollResult r) =>
            string.Join(";", new[]
            {
                r.Totals.SalaireBrut, r.Totals.BaseCotisable, r.Totals.CnasEmployee, r.Totals.CnasEmployer,
                r.Totals.BaseImposable, r.Totals.IrgBrut, r.Totals.Abattement, r.Totals.Irg, r.Totals.NetSalaire
            }.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            + "|" + string.Join(";", r.Lines.Select(l => l.LabelFr + "=" + l.Amount))
            + "|" + string.Join(";", r.Trace.Select(t => t.Key + "=" + t.Amount));

        [Test]
        public void Default_Is_On_And_Identical_To_Explicit_On()
        {
            Assert.That(new PayrollContext().ApplyLegalDeductions, Is.True, "the default must be ON");

            PayrollResult byDefault = new PayrollCalculationEngine().Calculate(Context(null));
            PayrollResult explicitOn = new PayrollCalculationEngine().Calculate(Context(true));

            Assert.That(byDefault.IsSuccess, Is.True, Errors(byDefault));
            Assert.That(Fingerprint(explicitOn), Is.EqualTo(Fingerprint(byDefault)), "ON must be the historical pipeline exactly");
            Assert.That(byDefault.LegalDeductionsApplied, Is.True);
            Assert.That(byDefault.Totals.CnasEmployee, Is.GreaterThan(0m));
            Assert.That(byDefault.Totals.Irg, Is.GreaterThan(0m));
            Assert.That(byDefault.Trace.Any(t => t.Key == "LEGAL_OFF"), Is.False, "no OFF trace when ON");
        }

        [Test]
        public void Off_Skips_Cnas_Both_Sides_And_Irg_And_Keeps_User_Deductions()
        {
            PayrollResult on = new PayrollCalculationEngine().Calculate(Context(true));
            PayrollResult off = new PayrollCalculationEngine().Calculate(Context(false));

            Assert.That(off.IsSuccess, Is.True, Errors(off));
            Assert.That(off.LegalDeductionsApplied, Is.False);

            // Same gross and same cotisable base: only the deductions are gated, not the earnings.
            Assert.That(off.Totals.SalaireBrut, Is.EqualTo(on.Totals.SalaireBrut));
            Assert.That(off.Totals.BaseCotisable, Is.EqualTo(on.Totals.BaseCotisable));

            Assert.That(off.Totals.CnasEmployee, Is.EqualTo(0m), "employee CNAS skipped");
            Assert.That(off.Totals.CnasEmployer, Is.EqualTo(0m), "employer CNAS skipped (same CNAS rule)");
            Assert.That(off.Totals.IrgBrut, Is.EqualTo(0m));
            Assert.That(off.Totals.Abattement, Is.EqualTo(0m));
            Assert.That(off.Totals.Irg, Is.EqualTo(0m));

            // Net = gross − only what the user entered (the 5 000 advance).
            Assert.That(off.Totals.NetSalaire, Is.EqualTo(off.Totals.SalaireBrut - 5000m));
            Assert.That(off.Lines.Single(l => l.LabelFr == "Acompte").Amount, Is.EqualTo(5000m), "the advance is still deducted");
            Assert.That(off.Trace.Any(t => t.Key == "LEGAL_OFF"), Is.True);
            Assert.That(off.Trace.Any(t => t.Key == "CNAS" || t.Key == "IRG_BRUT" || t.Key == "IRG_REGULAR"), Is.False,
                "the gated rules did not run");
        }

        [Test]
        public void Off_Also_Skips_Irg_On_Smoothed_Amounts()
        {
            PayrollContext ctx = Context(false);
            ctx.Elements.Add(new PayrollElementInput
            {
                ElementId = 103, LabelFr = "Prime annuelle", LabelAr = "منحة سنوية", ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.FixedAmount, Amount = 120000m,
                IsCnasApplicable = true, IsIrgApplicable = true, CnasFactor = 1m, IrgFactor = 1m,
                IsIncludedInGross = true, Periodicity = ElementPeriodicity.Annual,
                Lissage = new LissageInput(12, Enumerable.Empty<decimal>())
            });

            PayrollResult off = new PayrollCalculationEngine().Calculate(ctx);
            Assert.That(off.IsSuccess, Is.True, Errors(off));
            Assert.That(off.Totals.Irg, Is.EqualTo(0m), "IRG lissage is IRG: gated too");
            Assert.That(off.Trace.Any(t => t.Key == "LISSAGE"), Is.False);
        }

        // ------------------------------------------------------------------ PROOF 5: fail-safe read

        [TestCase(null), TestCase(""), TestCase("   "), TestCase("1"), TestCase("true"), TestCase("ON"),
         TestCase("yes"), TestCase("2"), TestCase("garbage"), TestCase("0.0"), TestCase("off ")]
        public void Anything_But_An_Explicit_Off_Reads_On(string raw)
        {
            Assert.That(LegalDeductionsSetting.IsEnabled(raw), Is.True, "«" + raw + "» must read ON");
        }

        [TestCase("0"), TestCase(" 0 "), TestCase("false"), TestCase("FALSE"), TestCase(" False ")]
        public void Only_An_Explicit_Off_Reads_Off(string raw)
        {
            Assert.That(LegalDeductionsSetting.IsEnabled(raw), Is.False);
        }

        [Test]
        public void Missing_Null_Corrupted_Row_And_Read_Failure_All_Read_On()
        {
            using (var db = new TempDb())
            {
                var svc = new ConfigurationService(db.Uow, AccessContext.Unrestricted);
                Assert.That(svc.GetLegalDeductionsEnabled(), Is.True, "freshly migrated: ON");

                db.Exec("DELETE FROM AppSettings WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';");
                Assert.That(svc.GetLegalDeductionsEnabled(), Is.True, "missing row → ON");

                db.Exec("INSERT INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES ('LEGAL_DEDUCTIONS_ENABLED', NULL, '2026-01-01 00:00:00');");
                Assert.That(svc.GetLegalDeductionsEnabled(), Is.True, "NULL → ON");

                db.Exec("UPDATE AppSettings SET SettingValue = 'Ã©#corrupt' WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';");
                Assert.That(svc.GetLegalDeductionsEnabled(), Is.True, "unparsable → ON");

                db.Exec("UPDATE AppSettings SET SettingValue = '0' WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';");
                Assert.That(svc.GetLegalDeductionsEnabled(), Is.False, "explicit OFF → OFF");
            }

            // A read FAILURE (the database cannot even be opened) must never disable the deductions.
            var broken = new ConfigurationService(new ThrowingUowFactory(), AccessContext.Unrestricted);
            Assert.That(broken.GetLegalDeductionsEnabled(), Is.True, "read failure → ON");
        }

        // ------------------------------------------------------------------ who may flip it + audit

        [Test]
        public void Only_Administrators_May_Change_It_And_Every_Change_Is_Audited()
        {
            using (var db = new TempDb())
            {
                foreach (UserRole role in new[] { UserRole.Hr, UserRole.Payroll, UserRole.Accountant, UserRole.ReadOnly })
                {
                    var denied = new ConfigurationService(db.Uow, new FakeAccess(role));
                    Assert.Throws<AccessDeniedException>(() => denied.SetLegalDeductionsEnabled(false), role + " must be refused");
                }
                Assert.Throws<AccessDeniedException>(() => new ConfigurationService(db.Uow).SetLegalDeductionsEnabled(false),
                    "no access context = fail closed");

                var audit = new RecordingAudit();
                var admin = new ConfigurationService(db.Uow, new FakeAccess(UserRole.Admin)) { Audit = audit };
                Assert.That(admin.GetLegalDeductionsEnabled(), Is.True, "refused attempts changed nothing");

                Assert.That(admin.SetLegalDeductionsEnabled(false).IsSuccess, Is.True);
                Assert.That(admin.GetLegalDeductionsEnabled(), Is.False);
                Assert.That(admin.SetLegalDeductionsEnabled(false).IsSuccess, Is.True); // no-op: no audit line
                Assert.That(admin.SetLegalDeductionsEnabled(true).IsSuccess, Is.True);
                Assert.That(admin.GetLegalDeductionsEnabled(), Is.True);

                Assert.That(audit.Records.Count, Is.EqualTo(2), "one audit line per actual change");
                Assert.That(audit.Records[0].OldValue, Is.EqualTo("ON"));
                Assert.That(audit.Records[0].NewValue, Is.EqualTo("OFF"));
                Assert.That(audit.Records[1].OldValue, Is.EqualTo("OFF"));
                Assert.That(audit.Records[1].NewValue, Is.EqualTo("ON"));
                Assert.That(audit.Records.All(r => r.EntityType == "AppSetting" && r.Action == AuditAction.Updated), Is.True);
            }
        }

        // ------------------------------------------------------------------ sanity guard

        [Test]
        public void Sanity_Guard_Does_Not_Block_A_Deliberately_Off_Payslip()
        {
            PayrollContext ctx = Context(false);
            ctx.Elements.RemoveAt(1); // no advance → net = gross, CNAS = 0: the two "missing deduction" rules
            PayrollResult off = new PayrollCalculationEngine().Calculate(ctx);
            PayslipSanityReport report = new PayslipSanityService().Inspect(off, 24000m, true);
            Assert.That(report.Warnings.Any(w => w.Code == PayslipSanityCode.ContributionZeroOnPaidSalary), Is.False);
            Assert.That(report.Warnings.Any(w => w.Code == PayslipSanityCode.NetEqualsGross), Is.False);

            // The same zero contribution with the deductions ON is still flagged.
            var zeroOn = new PayrollCalculationEngine().Calculate(new PayrollContext
            {
                Period = new PayrollPeriod(2026, 7), BaseSalary = 0m, WorkedDays = 31m, WorkableDays = 31m,
                Legal = new LegalSnapshot(0.09m, 0.26m, 24000m, RoundingPolicy.Centime),
                Elements = new List<PayrollElementInput>
                {
                    new PayrollElementInput
                    {
                        ElementId = 104, LabelFr = "Indemnité exonérée", LabelAr = "تعويض", ElementType = ElementType.Gain,
                        CalculationMethod = CalculationMethod.FixedAmount, Amount = 30000m,
                        IsIncludedInGross = true, Periodicity = ElementPeriodicity.Monthly
                    }
                }
            });
            Assert.That(new PayslipSanityService().Inspect(zeroOn, 24000m, true).Warnings
                .Any(w => w.Code == PayslipSanityCode.ContributionZeroOnPaidSalary), Is.True, "ON keeps the guard");
        }

        // ------------------------------------------------------------------ PROOF 3 (synthetic): upgrade

        [Test]
        public void Upgrading_A_Pre_Switch_Database_Reads_On_And_Backfills_Every_Payslip_On()
        {
            using (var db = new TempDb(throughVersion: 43))
            {
                Assert.That(db.Scalar("SELECT MAX(Version) FROM SchemaMigrations;"), Is.EqualTo(43L));
                // The run and payslips reference a real company and employees (foreign keys are enforced).
                db.Exec("INSERT INTO Companies (Id, NameFr, Nif, CreatedAtUtc) VALUES (1, 'SARL Upgrade', '000000000000000', '2026-01-01 00:00:00');");
                db.Exec("INSERT INTO Employees (Id, CompanyId, LastNameFr, FirstNameFr, HireDate, BaseSalary, IsActive, CreatedAtUtc) VALUES " +
                        "(10, 1, 'AMRANI', 'Salima', '2024-01-01', '60000', 1, '2026-01-01 00:00:00')," +
                        "(11, 1, 'BENALI', 'Karim',  '2024-01-01', '45000', 1, '2026-01-01 00:00:00');");
                db.Exec("INSERT INTO PayrollRuns (Id, CompanyId, PeriodYear, PeriodMonth, RunStatus, GeneratedAtUtc, EngineVersion, CreatedAtUtc) " +
                        "VALUES (1, 1, 2026, 3, 1, '2026-03-31 10:00:00', 'test', '2026-03-31 10:00:00');");
                db.Exec("INSERT INTO Payslips (Id, RunId, EmployeeId, SalaireBrut, CnasEmployee, Irg, NetSalaire, GeneratedAtUtc, CreatedAtUtc, Status) VALUES " +
                        "(1, 1, 10, '60000', '5400', '7042', '47558', '2026-03-31 10:00:00', '2026-03-31 10:00:00', 1)," +
                        "(2, 1, 11, '45000', '4050', '3878', '37072', '2026-03-31 10:00:00', '2026-03-31 10:00:00', 1);");

                int applied = db.MigrateToLatest();
                Assert.That(applied, Is.GreaterThanOrEqualTo(1), "0044 applied");

                Assert.That(new ConfigurationService(db.Uow).GetLegalDeductionsEnabled(), Is.True, "upgrade → ON");
                Assert.That(db.Scalar("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';"), Is.EqualTo("1"));
                Assert.That(db.Scalar("SELECT COUNT(*) FROM Payslips WHERE LegalDeductionsApplied = 1;"), Is.EqualTo(2L), "every payslip back-filled ON");
                Assert.That(db.Scalar("SELECT NetSalaire FROM Payslips WHERE Id = 1;"), Is.EqualTo("47558"), "money columns untouched");
            }
        }

        [Test]
        public void Migration_Never_Overwrites_A_Customer_Choice()
        {
            using (var db = new TempDb(throughVersion: 43))
            {
                // A value already present (e.g. a re-run, or set by a future build) is never overwritten.
                db.Exec("INSERT INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES ('LEGAL_DEDUCTIONS_ENABLED', '0', '2026-01-01 00:00:00');");
                db.MigrateToLatest();
                Assert.That(db.Scalar("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';"), Is.EqualTo("0"));
            }
        }

        // ------------------------------------------------------------------ helpers

        private sealed class TempDb : IDisposable
        {
            private readonly string _dir;
            private readonly SqliteConnectionFactory _factory;

            public TempDb(int? throughVersion = null)
            {
                _dir = Path.Combine(Path.GetTempPath(), "optipaie-legal-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                SqliteTypeHandlers.Register();
                _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
                using (SQLiteConnection c = _factory.CreateOpenConnection())
                {
                    if (throughVersion.HasValue) new MigrationRunner(c).Run(throughVersion.Value);
                    else new MigrationRunner(c).Run();
                }
                Uow = new UnitOfWorkFactory(_factory);
            }

            public IUnitOfWorkFactory Uow { get; }

            public int MigrateToLatest()
            {
                using (SQLiteConnection c = _factory.CreateOpenConnection()) return new MigrationRunner(c).Run();
            }

            public void Exec(string sql)
            {
                using (SQLiteConnection c = _factory.CreateOpenConnection())
                using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            }

            public object Scalar(string sql)
            {
                using (SQLiteConnection c = _factory.CreateOpenConnection())
                using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); }
            }

            public void Dispose()
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(_dir, true); } catch (IOException) { }
            }
        }

        private sealed class ThrowingUowFactory : IUnitOfWorkFactory
        {
            public IUnitOfWork Create() => throw new IOException("database unavailable");
        }

        private sealed class FakeAccess : IAccessContext
        {
            public FakeAccess(UserRole role) { CurrentRole = role; }
            public bool IsEnforced => true;
            public UserRole CurrentRole { get; }
            public bool IsOwner => false;
            public long? CurrentUserId => 1;
        }

        private sealed class RecordingAudit : IAuditSink
        {
            public readonly List<(string EntityType, AuditAction Action, string OldValue, string NewValue)> Records =
                new List<(string, AuditAction, string, string)>();

            public void Record(string entityType, long entityId, AuditAction action, string summary,
                string oldValue = null, string newValue = null)
                => Records.Add((entityType, action, oldValue, newValue));
        }
    }
}
