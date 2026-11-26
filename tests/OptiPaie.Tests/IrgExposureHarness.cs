using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Dapper;
using NUnit.Framework;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;

namespace OptiPaie.Tests
{
    /// <summary>
    /// [Explicit] IRG exposure audit of ANY customer database: set IRG_EXPOSURE_DB to the database file
    /// (it is COPIED to a temp folder first — the original is only read by File.Copy). Reports:
    ///   1. the cause — cotisable catalog elements carrying a partial taxable share (IrgPercent 0 &lt; p &lt; 100);
    ///   2. every saved payslip whose stored IRG base is below (cotisable − CNAS), the user's criterion;
    ///   3. for payslips that used those elements: the IRG base they should have had, the IRG owed under the
    ///      1.45.1 rules, the IRG actually deducted, and the shortfall — per payslip, per month, in total.
    /// Lissage (rappels, primes exceptionnelles) is not re-evaluated: the shortfall covers the regular IRG.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class IrgExposureHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [Test]
        public void Audit_A_Copy()
        {
            string source = Environment.GetEnvironmentVariable("IRG_EXPOSURE_DB");
            Assert.That(File.Exists(source), Is.True, "IRG_EXPOSURE_DB not found");
            string tag = Environment.GetEnvironmentVariable("IRG_EXPOSURE_TAG") ?? "run";
            Result r = Audit(source, tag);

            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "OptiPaie.sln"))) root = root.Parent;
            string outDir = Path.Combine(root.FullName, "docs", "irg-reference");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "exposure-" + tag + ".txt"), r.Report, new UTF8Encoding(true));
            TestContext.Progress.WriteLine(r.Report);
        }

        public sealed class Result
        {
            public int Affected, BelowCriterion;
            public decimal Shortfall;
            public string Report;
        }

        /// <summary>Audits a COPY of <paramref name="source"/>; the original file is only read by File.Copy.</summary>
        public static Result Audit(string source, string tag)
        {

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-irg-exposure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, "copy.db");
            File.Copy(source, db);
            foreach (string side in new[] { "-wal", "-shm" })
                if (File.Exists(source + side)) File.Copy(source + side, db + side);

            var o = new StringBuilder();
            try
            {
                using (var c = new SQLiteConnection("Data Source=" + db + ";Version=3;"))
                {
                    c.Open();
                    var calc = new IrgCalculator(new MoneyEngine(RoundingPolicy.Centime));

                    // 1. The cause.
                    var partial = c.Query("SELECT Id, NameFr, IsCnasApplicable, CnasPercent, IrgPercent FROM PayrollElements " +
                                          "WHERE (IsCnasApplicable = 1 OR CAST(IFNULL(CnasPercent,'0') AS REAL) > 0) " +
                                          "AND IrgPercent IS NOT NULL AND CAST(IrgPercent AS REAL) > 0 AND CAST(IrgPercent AS REAL) < 100;")
                                   .Select(r => new { Id = (long)r.Id, Name = (string)r.NameFr, Pct = Dec(r.IrgPercent) }).ToList();
                    o.AppendLine("IRG EXPOSURE AUDIT — copy of " + Path.GetFileName(source) + " (" + tag + ")");
                    o.AppendLine();
                    o.AppendLine("[1] Cotisable elements with a partial taxable share (the cause): " + partial.Count);
                    foreach (var p in partial) o.AppendLine(string.Format(Inv, "    #{0} {1} — imposable {2} %", p.Id, p.Name, p.Pct));
                    var shareById = new Dictionary<long, decimal>();
                    foreach (var p in partial) shareById[(long)p.Id] = (decimal)p.Pct / 100m;

                    // 2 + 3. Every active payslip.
                    var slips = c.Query("SELECT p.Id, r.PeriodYear, r.PeriodMonth, p.EmployeeId, p.BaseCotisable, p.CnasEmployee, p.BaseImposable, " +
                                        "p.Irg, p.NetSalaire, p.LegalDeductionsApplied FROM Payslips p JOIN PayrollRuns r ON r.Id = p.RunId " +
                                        "WHERE p.Status = 1 ORDER BY r.PeriodYear, r.PeriodMonth, p.Id;").ToList();
                    var profile = new BuiltInLegalProfileProvider().GetProfile(new PayrollPeriod(2026, 1));
                    int below = 0, affected = 0;
                    decimal totalShortfall = 0m;
                    var byMonth = new SortedDictionary<string, decimal>();
                    var lines = new StringBuilder();
                    foreach (var s in slips)
                    {
                        decimal cot = Dec(s.BaseCotisable), cnas = Dec(s.CnasEmployee), imp = Dec(s.BaseImposable), irg = Dec(s.Irg);
                        bool legalOn = s.LegalDeductionsApplied == null || Convert.ToInt64(s.LegalDeductionsApplied) == 1;
                        if (legalOn && imp < cot - cnas) below++;
                        if (!legalOn || shareById.Count == 0) continue;

                        decimal missed = 0m;
                        foreach (var d in c.Query("SELECT ElementId, ElementType, Amount FROM PayrollDetails WHERE PayslipId = @id;", new { id = (long)s.Id }))
                        {
                            if (d.ElementId == null) continue;
                            long elementId = Convert.ToInt64(d.ElementId);
                            if (!shareById.TryGetValue(elementId, out decimal share)) continue;
                            decimal sign = Convert.ToInt64(d.ElementType) == 1 ? 1m : -1m;
                            missed += sign * Dec(d.Amount) * (1m - share);
                        }
                        if (missed == 0m) continue;

                        affected++;
                        decimal correctBase = imp + missed;
                        decimal owed = calc.Compute(correctBase, profile).Irg;
                        decimal shortfall = owed - irg;
                        totalShortfall += shortfall;
                        string month = s.PeriodYear + "-" + Convert.ToInt32(s.PeriodMonth).ToString("00", Inv);
                        byMonth[month] = (byMonth.TryGetValue(month, out decimal m) ? m : 0m) + shortfall;
                        lines.AppendLine(string.Format(Inv, "    #{0,-5} {1}  emp {2,-5} base {3,12:N2} → {4,12:N2}   IRG {5,10:N2} → {6,10:N2}   shortfall {7,10:N2}",
                            s.Id, month, s.EmployeeId, imp, correctBase, irg, owed, shortfall));
                    }

                    o.AppendLine();
                    o.AppendLine("[2] Active payslips: " + slips.Count + " ; with stored IRG base < cotisable − CNAS (legal deductions ON): " + below);
                    o.AppendLine();
                    o.AppendLine("[3] Payslips that used a partially-taxed cotisable element: " + affected);
                    o.Append(lines);
                    o.AppendLine("    by month:");
                    foreach (var kv in byMonth) o.AppendLine(string.Format(Inv, "      {0}  {1,12:N2}", kv.Key, kv.Value));
                    o.AppendLine(string.Format(Inv, "    TOTAL IRG NOT DEDUCTED: {0:N2} DA", totalShortfall));
                    return new Result { Affected = affected, BelowCriterion = below, Shortfall = totalShortfall, Report = o.ToString() };
                }
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
