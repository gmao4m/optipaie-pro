using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// THE reference case for IRG: a real customer's payslip, checked line by line against the one his
    /// accountant produced (transport SARL, driver, married, entered 12/05/2010). Every earning line agrees;
    /// the accountant's IRG base is 33 600 − 3 024 = 30 576, IRG 353.70, net 38 422.30. These figures are
    /// hard-coded on purpose: they come from the accountant's payslip, not from our engine.
    ///
    /// The customer's catalog marked IEP, IFSP and Prime de risque / nuisance — all three COTISABLE — as
    /// "Imposable 10 %" (IrgPercent = 10, i.e. IrgFactor 0.10), which is how PayrollService maps them.
    /// Cotisable ⇒ taxable in full: the engine must not let 90 % of those allowances escape the IRG base.
    /// </summary>
    [TestFixture]
    public sealed class IrgReferenceCaseTests
    {
        private static PayrollElementInput Line(string label, decimal amount, decimal cnasFactor, decimal irgFactor) =>
            new PayrollElementInput
            {
                LabelFr = label, LabelAr = label, ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.FixedAmount, Amount = amount,
                IsCnasApplicable = cnasFactor > 0m, IsIrgApplicable = irgFactor > 0m,
                CnasFactor = cnasFactor, IrgFactor = irgFactor,
                IsIncludedInGross = true, Periodicity = ElementPeriodicity.Monthly
            };

        /// <summary>The customer's payslip exactly as PayrollService hands it to the engine.</summary>
        public static PayrollContext CustomerCase() =>
            PayrollTestFactory.Context(24000m,
                Line("Indemnité de zone (IFRI) 13 × 200", 2600m, 0m, 0m),
                Line("IEP 24 000 × 15 %", 3600m, 1m, 0.10m),
                Line("IFSP 24 000 × 10 %", 2400m, 1m, 0.10m),
                Line("Prime de risque / nuisance 15 %", 3600m, 1m, 0.10m),
                Line("F-Déplacement 1  3 × 1 700", 5100m, 0m, 0m),
                Line("Salaire unique", 500m, 0m, 0m));

        [Test]
        public void CustomerCase_MatchesTheAccountantsPayslip()
        {
            PayrollResult r = new PayrollCalculationEngine().Calculate(CustomerCase());
            Assert.That(r.IsSuccess, Is.True);

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(41800.00m), "gross");
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(33600.00m), "cotisable");
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(3024.00m), "CNAS 9 %");
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(30576.00m), "IRG base = 33 600 − 3 024");
            Assert.That(r.Totals.Irg, Is.EqualTo(353.70m), "IRG");
            Assert.That(r.Totals.CnasEmployee + r.Totals.Irg, Is.EqualTo(3377.70m), "total retenues (CNAS + IRG)");
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(38422.30m), "net");
        }

        [Test]
        public void ACotisableLine_IsTaxedInFull_WhateverItsTaxableShare()
        {
            // A partial taxable share (the editor's 10 / 30 / 50 %) on a cotisable line counts 100 %.
            foreach (decimal share in new[] { 0.10m, 0.30m, 0.50m, 1m })
            {
                PayrollResult r = new PayrollCalculationEngine().Calculate(
                    PayrollTestFactory.Context(40000m, Line("Prime cotisable", 10000m, 1m, share)));
                Assert.That(r.Totals.BaseImposable, Is.EqualTo(50000m - 4500m), "share " + share);
            }
        }

        [Test]
        public void ACotisableLine_DeclaredZeroPercent_IsAnExplicitExemption_AndIsHonoured()
        {
            PayrollResult r = new PayrollCalculationEngine().Calculate(
                PayrollTestFactory.Context(40000m, Line("Indemnité exonérée par la loi", 10000m, 1m, 0m)));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(50000m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(40000m - 4500m));
        }

        [Test]
        public void TheBaremeAppliesToTheBaseRoundedDownToTenDinars()
        {
            // Same IRG for 30 570 and 30 579: the barème sees 30 570 (the displayed base is unchanged).
            var calc = new OptiPaie.PayrollEngine.Legal.IrgCalculator(new OptiPaie.PayrollEngine.Money.MoneyEngine(OptiPaie.Core.Primitives.RoundingPolicy.Centime));
            var profile = new OptiPaie.PayrollEngine.Legal.BuiltInLegalProfileProvider().GetProfile(new OptiPaie.Core.Primitives.PayrollPeriod(2026, 6));
            Assert.That(calc.Compute(30570m, profile).Irg, Is.EqualTo(353.70m));
            Assert.That(calc.Compute(30576m, profile).Irg, Is.EqualTo(353.70m));
            Assert.That(calc.Compute(30579.99m, profile).Irg, Is.EqualTo(353.70m));
            Assert.That(calc.Compute(30580m, profile).Irg, Is.GreaterThan(353.70m));
        }

        [Test]
        public void ANonCotisableLine_KeepsItsOwnTaxTreatment()
        {
            // Exempt by law (panier / transport within limits, family allowance, travel expenses): untouched.
            PayrollResult exempt = new PayrollCalculationEngine().Calculate(
                PayrollTestFactory.Context(40000m, Line("Prime de panier", 4000m, 0m, 0m)));
            Assert.That(exempt.Totals.BaseImposable, Is.EqualTo(36400m));

            // Taxable but not cotisable, partially: still honoured as configured.
            PayrollResult partial = new PayrollCalculationEngine().Calculate(
                PayrollTestFactory.Context(40000m, Line("Indemnité imposable 50 %", 4000m, 0m, 0.50m)));
            Assert.That(partial.Totals.BaseImposable, Is.EqualTo(38400m));
        }

        [Test]
        public void ExposureAudit_FindsTheCustomerPayslip_AndItsExactShortfall()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-irg-audit-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, "customer.db");
            try
            {
                OptiPaie.Data.Context.SqliteTypeHandlers.Register();
                var factory = new OptiPaie.Data.Context.SqliteConnectionFactory(db);
                using (var c = factory.CreateOpenConnection())
                {
                    new OptiPaie.Data.Migrations.MigrationRunner(c).Run();
                    void X(string sql) { using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); } }
                    X("INSERT INTO Companies (Id, NameFr, CreatedAtUtc) VALUES (1, 'SARL Transport', '2026-01-01');");
                    X("INSERT INTO Employees (Id, CompanyId, LastNameFr, FirstNameFr, Gender, MaritalStatus, PaymentMode, ContractType, HireDate, BaseSalary, IsActive, CreatedAtUtc) " +
                      "VALUES (1, 1, 'CHAUFFEUR', 'Ali', 1, 2, 1, 1, '2010-05-12', '24000', 1, '2026-01-01');");
                    X("INSERT INTO PayrollElements (Id, NameFr, NameAr, ElementType, CalculationMethod, Periodicity, IsCnasApplicable, IsIrgApplicable, IrgPercent, IsIncludedInGross, IsEditable, IsEnabled, IsSystem, DisplayOrder, CreatedAtUtc) VALUES " +
                      "(901,'IEP','IEP',1,1,1,1,1,'10',1,1,1,0,1,'2026-01-01'),(902,'IFSP','IFSP',1,1,1,1,1,'10',1,1,1,0,2,'2026-01-01'),(903,'Nuisance','Nuisance',1,1,1,1,1,'10',1,1,1,0,3,'2026-01-01');");
                    X("INSERT INTO PayrollRuns (Id, CompanyId, PeriodYear, PeriodMonth, CreatedAtUtc) VALUES (1, 1, 2026, 10, '2026-10-31');");
                    // Exactly what the old engine saved (the customer's wrong payslip).
                    X("INSERT INTO Payslips (Id, RunId, EmployeeId, SalaireBrut, BaseCotisable, CnasEmployee, BaseImposable, Irg, NetSalaire, GeneratedAtUtc, CreatedAtUtc, Status) " +
                      "VALUES (1, 1, 1, '41800', '33600', '3024', '21936', '0', '38776', '2026-10-31', '2026-10-31', 1);");
                    X("INSERT INTO PayrollDetails (PayslipId, ElementId, LabelFr, ElementType, Amount, IsCnasApplicable, IsIrgApplicable, CreatedAtUtc) VALUES " +
                      "(1, NULL, 'Salaire de base', 1, '24000', 1, 1, '2026-10-31'),(1, 901, 'IEP', 1, '3600', 1, 1, '2026-10-31')," +
                      "(1, 902, 'IFSP', 1, '2400', 1, 1, '2026-10-31'),(1, 903, 'Nuisance', 1, '3600', 1, 1, '2026-10-31')," +
                      "(1, NULL, 'Zone IFRI', 1, '2600', 0, 0, '2026-10-31'),(1, NULL, 'F-Déplacement', 1, '5100', 0, 0, '2026-10-31'),(1, NULL, 'Salaire unique', 1, '500', 0, 0, '2026-10-31');");
                }
                System.Data.SQLite.SQLiteConnection.ClearAllPools();

                IrgExposureHarness.Result r = IrgExposureHarness.Audit(db, "unit");
                Assert.That(r.BelowCriterion, Is.EqualTo(1), r.Report);
                Assert.That(r.Affected, Is.EqualTo(1), r.Report);
                Assert.That(r.Shortfall, Is.EqualTo(353.70m), r.Report);
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        /// <summary>[Explicit] Writes the engine's own trace for the customer case (IRG_TRACE_TAG = before|after).</summary>
        [Test, Explicit]
        public void Write_Trace()
        {
            string tag = System.Environment.GetEnvironmentVariable("IRG_TRACE_TAG") ?? "run";
            PayrollContext ctx = CustomerCase();
            PayrollResult r = new PayrollCalculationEngine().Calculate(ctx);
            var inv = CultureInfo.InvariantCulture;
            var o = new StringBuilder();
            o.AppendLine("IRG reference case — engine trace (" + tag + ")");
            o.AppendLine();
            o.AppendLine("INPUT LINES (as PayrollService maps the customer's catalog):");
            o.AppendLine(string.Format(inv, "  {0,-38}{1,12}{2,9}{3,9}{4,14}", "line", "amount", "CNAS f", "IRG f", "amount×IRG f"));
            o.AppendLine(string.Format(inv, "  {0,-38}{1,12:N2}{2,9}{3,9}{4,14:N2}", "Salaire de base", ctx.BaseSalary, "1", "1", ctx.BaseSalary));
            foreach (PayrollElementInput e in ctx.Elements)
                o.AppendLine(string.Format(inv, "  {0,-38}{1,12:N2}{2,9:0.##}{3,9:0.##}{4,14:N2}", e.LabelFr, e.Amount, e.CnasFactor, e.IrgFactor,
                    (e.Amount ?? 0m) * (e.IsIrgApplicable ? e.IrgFactor : 0m)));
            o.AppendLine();
            o.AppendLine("ENGINE TRACE (every step, in order):");
            foreach (PayrollCalculationStep s in r.Trace)
                o.AppendLine(string.Format(inv, "  {0,-16}{1,14:N2}   {2}", s.Key, s.Amount, s.Detail));
            o.AppendLine();
            o.AppendLine(string.Format(inv, "TOTALS  brut {0:N2} | cotisable {1:N2} | CNAS {2:N2} | imposable {3:N2} | IRG {4:N2} | retenues {5:N2} | net {6:N2}",
                r.Totals.SalaireBrut, r.Totals.BaseCotisable, r.Totals.CnasEmployee, r.Totals.BaseImposable, r.Totals.Irg, r.Totals.CnasEmployee + r.Totals.Irg, r.Totals.NetSalaire));
            foreach (PayrollMessage m in r.Messages) o.AppendLine("MESSAGE " + m.Code + " — " + m.Text);

            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            string outDir = Path.Combine(dir.FullName, "docs", "irg-reference");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "trace-" + tag + ".txt"), o.ToString(), new UTF8Encoding(true));
            TestContext.Progress.WriteLine(o.ToString());
        }
    }
}
