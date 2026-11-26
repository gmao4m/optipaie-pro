using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] "Will his NEXT payroll be right on 1.45.1 with his catalog untouched?" On a COPY of the
    /// customer's database (IRG_NEXT_DB), through the real app services, PREVIEWS (never saves) the next
    /// month's payslip of one employee (IRG_NEXT_EMPLOYEE, else the employee with a partially-taxed
    /// cotisable element on his latest payslip) and shows: every line with its CNAS/IRG treatment, the
    /// totals, the invariant base = cotisable − CNAS + taxable non-cotisable, and what the pre-1.45.1
    /// engine would have computed from the same lines (the partial share applied).
    /// </summary>
    [TestFixture, Explicit]
    public sealed class IrgNextPayrollHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [Test]
        public void Preview_Next_Payroll_On_A_Copy()
        {
            string source = Environment.GetEnvironmentVariable("IRG_NEXT_DB");
            Assert.That(File.Exists(source), Is.True, "IRG_NEXT_DB not found");
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-irg-next-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, AppConstants.DatabaseFileName);
            File.Copy(source, db); // the customer's file is only read
            foreach (string side in new[] { "-wal", "-shm" })
                if (File.Exists(source + side)) File.Copy(source + side, db + side);

            var o = new StringBuilder();
            try
            {
                // Partially-taxed cotisable elements and the latest payslip that used one (read before the app opens the copy).
                var shares = new Dictionary<long, decimal>();
                long employeeId = 0, companyId = 0; int year = 0, month = 0;
                using (var c = new SQLiteConnection("Data Source=" + db + ";Version=3;"))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, IrgPercent FROM PayrollElements WHERE (IsCnasApplicable = 1 OR CAST(IFNULL(CnasPercent,'0') AS REAL) > 0) " +
                                          "AND IrgPercent IS NOT NULL AND CAST(IrgPercent AS REAL) > 0 AND CAST(IrgPercent AS REAL) < 100;";
                        using (var rd = cmd.ExecuteReader()) while (rd.Read()) shares[Convert.ToInt64(rd.GetValue(0))] = Dec(rd.GetValue(1)) / 100m;
                    }
                    string forced = Environment.GetEnvironmentVariable("IRG_NEXT_EMPLOYEE");
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT p.EmployeeId, r.CompanyId, r.PeriodYear, r.PeriodMonth FROM Payslips p JOIN PayrollRuns r ON r.Id = p.RunId " +
                                          "WHERE p.Status = 1 " + (string.IsNullOrEmpty(forced) ? "AND EXISTS (SELECT 1 FROM PayrollDetails d WHERE d.PayslipId = p.Id AND d.ElementId IN (" +
                                          (shares.Count == 0 ? "-1" : string.Join(",", shares.Keys)) + ")) " : "AND p.EmployeeId = " + long.Parse(forced, Inv) + " ") +
                                          "ORDER BY r.PeriodYear DESC, r.PeriodMonth DESC LIMIT 1;";
                        using (var rd = cmd.ExecuteReader())
                            if (rd.Read()) { employeeId = Convert.ToInt64(rd.GetValue(0)); companyId = Convert.ToInt64(rd.GetValue(1)); year = Convert.ToInt32(rd.GetValue(2)); month = Convert.ToInt32(rd.GetValue(3)); }
                    }
                }
                SQLiteConnection.ClearAllPools();
                Assert.That(employeeId, Is.GreaterThan(0), "no payslip found for the chosen employee");
                if (++month > 12) { month = 1; year++; } // the NEXT payroll

                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                PayrollGenerationRequest req = services.Batch.BuildRequest(companyId, employeeId, year, month);
                PayrollResult r = services.Payroll.Preview(req); // preview only: nothing is saved
                Assert.That(r.IsSuccess, Is.True, string.Join(" ; ", (r.Messages ?? new List<PayrollMessage>()).Select(m => m.Code)));

                o.AppendLine(string.Format(Inv, "NEXT PAYROLL on 1.45.1 — employee #{0}, {1}-{2:00}, his catalog UNCHANGED (preview, nothing saved)", employeeId, year, month));
                o.AppendLine(string.Format(Inv, "  {0,-40}{1,12}  {2,-5}{3,-5}{4}", "line", "amount", "CNAS", "IRG", "catalog taxable share"));
                decimal missedOld = 0m, nonCotisableTaxable = 0m;
                foreach (PayrollLineResult l in r.Lines)
                {
                    decimal? share = l.ElementId.HasValue && shares.ContainsKey(l.ElementId.Value) ? shares[l.ElementId.Value] : (decimal?)null;
                    decimal sign = l.ElementType == OptiPaie.Core.Enums.ElementType.Gain ? 1m : -1m;
                    if (share.HasValue && l.IsIrgApplicable) missedOld += sign * l.Amount * (1m - share.Value);
                    if (!l.IsCnasApplicable && l.IsIrgApplicable) nonCotisableTaxable += sign * l.Amount;
                    o.AppendLine(string.Format(Inv, "  {0,-40}{1,12:N2}  {2,-5}{3,-5}{4}", l.LabelFr, l.Amount, l.IsCnasApplicable ? "oui" : "non",
                        l.IsIrgApplicable ? "oui" : "non", share.HasValue ? (share.Value * 100m).ToString("0.##", Inv) + " % → applied as 100 %" : ""));
                }
                PayrollTotals t = r.Totals;
                o.AppendLine(string.Format(Inv, "  brut {0:N2} | cotisable {1:N2} | CNAS {2:N2} | imposable {3:N2} | IRG {4:N2} | net {5:N2}",
                    t.SalaireBrut, t.BaseCotisable, t.CnasEmployee, t.BaseImposable, t.Irg, t.NetSalaire));
                decimal expected = t.BaseCotisable - t.CnasEmployee + nonCotisableTaxable;
                o.AppendLine(string.Format(Inv, "  check: cotisable − CNAS + taxable non-cotisable = {0:N2} → {1}", expected, expected == t.BaseImposable ? "EQUAL ✓" : "DIFFERENT ✗"));

                var calc = new IrgCalculator(new MoneyEngine(RoundingPolicy.Centime));
                decimal oldBase = t.BaseImposable - missedOld;
                var profile = new BuiltInLegalProfileProvider().GetProfile(new PayrollPeriod(year, month));
                o.AppendLine(string.Format(Inv, "  pre-1.45.1 engine on the same lines: imposable {0:N2} (−{1:N2}) → IRG ≈ {2:N2}",
                    oldBase, missedOld, calc.Compute(oldBase, profile).Irg));
                Assert.That(t.BaseImposable, Is.EqualTo(expected));

                var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "OptiPaie.sln"))) root = root.Parent;
                string outDir = Path.Combine(root.FullName, "docs", "irg-reference");
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "next-payroll-" + (Environment.GetEnvironmentVariable("IRG_EXPOSURE_TAG") ?? "run") + ".txt"), o.ToString(), new UTF8Encoding(true));
                TestContext.Progress.WriteLine(o.ToString());
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static decimal Dec(object v) =>
            v == null || v is DBNull ? 0m : decimal.Parse(Convert.ToString(v, Inv), NumberStyles.Any, Inv);
    }
}
