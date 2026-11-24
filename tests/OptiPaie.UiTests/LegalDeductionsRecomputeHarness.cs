using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Composition;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// PROOF 1 harness for the IRG/CNAS switch ([Explicit]). Recomputes the REAL payslips on a
    /// COPY of the live database (never the live file) and writes a deterministic dump of every
    /// engine output (totals, lines, messages, trace) to docs/legal-deductions/recompute-&lt;tag&gt;.txt.
    ///
    /// It is run twice with the SAME inputs: once on the unmodified engine (tag "before") and once
    /// after the switch was added (tag "after", setting at its default ON). The two dumps must be
    /// byte-identical. It also compares every recomputed total with what is STORED on the payslip.
    /// Uses only APIs that existed before the change, so the "before" run compiles unchanged.
    ///   set LEGAL_PROOF_TAG=before|after, then:
    ///   dotnet test tests/OptiPaie.UiTests --filter "FullyQualifiedName~LegalDeductionsRecomputeHarness"
    /// </summary>
    [TestFixture, Explicit]
    public sealed class LegalDeductionsRecomputeHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [Test]
        public void Recompute_Real_Payslips_On_A_Copy()
        {
            string tag = Environment.GetEnvironmentVariable("LEGAL_PROOF_TAG");
            if (string.IsNullOrWhiteSpace(tag)) tag = "run";

            string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OptiPaie DZ", AppConstants.DatabaseFileName);
            Assert.That(File.Exists(live), Is.True, "live database not found");

            // COPY the live database into a private temp data directory. The live file is only read.
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-legal-proof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, AppConstants.DatabaseFileName);
            File.Copy(live, copy);

            var outDir = Path.Combine(FindRepoRoot(), "docs", "legal-deductions");
            Directory.CreateDirectory(outDir);

            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));

                var stored = new List<StoredSlip>();
                using (var c = new SQLiteConnection("Data Source=" + copy + ";Version=3;"))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT p.Id, p.EmployeeId, r.CompanyId, r.PeriodYear, r.PeriodMonth, " +
                            "p.SalaireBrut, p.CnasEmployee, p.CnasEmployer, p.Irg, p.NetSalaire " +
                            "FROM Payslips p JOIN PayrollRuns r ON r.Id = p.RunId WHERE p.Status = 1 ORDER BY p.Id;";
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                stored.Add(new StoredSlip
                                {
                                    Id = Convert.ToInt64(rd.GetValue(0), Inv),
                                    EmployeeId = Convert.ToInt64(rd.GetValue(1), Inv),
                                    CompanyId = Convert.ToInt64(rd.GetValue(2), Inv),
                                    Year = Convert.ToInt32(rd.GetValue(3), Inv),
                                    Month = Convert.ToInt32(rd.GetValue(4), Inv),
                                    Brut = Dec(rd.GetValue(5)), Cnas = Dec(rd.GetValue(6)), CnasEmp = Dec(rd.GetValue(7)),
                                    Irg = Dec(rd.GetValue(8)), Net = Dec(rd.GetValue(9))
                                });
                            }
                        }
                    }
                }

                var dump = new StringBuilder();
                var compare = new StringBuilder();
                compare.AppendLine("payslip | stored brut/cnas/irg/net | recomputed brut/cnas/irg/net | match");
                int matches = 0;

                foreach (StoredSlip s in stored)
                {
                    PayrollGenerationRequest req = services.Batch.BuildRequest(s.CompanyId, s.EmployeeId, s.Year, s.Month);
                    PayrollResult r = services.Payroll.Preview(req);

                    dump.Append("#").Append(s.Id).Append(" emp=").Append(s.EmployeeId)
                        .Append(" ").Append(s.Year).Append("-").Append(s.Month.ToString("00", Inv))
                        .Append(" ok=").Append(r.IsSuccess).AppendLine();
                    if (r.Totals != null)
                    {
                        PayrollTotals t = r.Totals;
                        dump.Append("  T ").Append(string.Join("|", new[]
                        {
                            t.SalaireBrut, t.BaseCotisable, t.CnasEmployee, t.CnasEmployer, t.BaseImposable,
                            t.IrgBrut, t.Abattement, t.Irg, t.NetSalaire
                        }.Select(D))).AppendLine();
                    }
                    foreach (PayrollLineResult l in r.Lines ?? new List<PayrollLineResult>())
                        dump.Append("  L ").Append(l.ElementId).Append("|").Append(l.LabelFr).Append("|").Append(l.ElementType)
                            .Append("|").Append(D(l.Base)).Append("|").Append(D(l.Rate)).Append("|").Append(D(l.Quantity))
                            .Append("|").Append(D(l.UnitPrice)).Append("|").Append(D(l.Amount)).Append("|")
                            .Append(l.IsCnasApplicable).Append("|").Append(l.IsIrgApplicable).Append("|").Append(l.DisplayOrder)
                            .AppendLine();
                    foreach (PayrollMessage m in r.Messages ?? new List<PayrollMessage>())
                        dump.Append("  M ").Append(m.Severity).Append("|").Append(m.Code).AppendLine();
                    foreach (PayrollCalculationStep st in r.Trace ?? new List<PayrollCalculationStep>())
                        dump.Append("  S ").Append(st.Key).Append("|").Append(D(st.Amount)).AppendLine();

                    bool same = r.Totals != null && r.Totals.SalaireBrut == s.Brut && r.Totals.CnasEmployee == s.Cnas
                                && r.Totals.Irg == s.Irg && r.Totals.NetSalaire == s.Net;
                    if (same) matches++;
                    compare.AppendLine(string.Format(Inv, "#{0} | {1} / {2} / {3} / {4} | {5} / {6} / {7} / {8} | {9}",
                        s.Id, D(s.Brut), D(s.Cnas), D(s.Irg), D(s.Net),
                        D(r.Totals?.SalaireBrut ?? 0m), D(r.Totals?.CnasEmployee ?? 0m), D(r.Totals?.Irg ?? 0m), D(r.Totals?.NetSalaire ?? 0m),
                        same ? "=" : "DIFF"));
                }

                string dumpPath = Path.Combine(outDir, "recompute-" + tag + ".txt");
                File.WriteAllText(dumpPath, dump.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outDir, "stored-vs-recomputed-" + tag + ".txt"),
                    compare.ToString() + "matches: " + matches + "/" + stored.Count + Environment.NewLine, new UTF8Encoding(false));

                string sha = Sha(File.ReadAllBytes(dumpPath));
                TestContext.Out.WriteLine("payslips=" + stored.Count + " storedMatches=" + matches + " dumpSha256=" + sha);
                Assert.That(stored.Count, Is.GreaterThan(0));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private sealed class StoredSlip
        {
            public long Id, EmployeeId, CompanyId;
            public int Year, Month;
            public decimal Brut, Cnas, CnasEmp, Irg, Net;
        }

        private static decimal Dec(object v) =>
            v == null || v is DBNull ? 0m : decimal.Parse(Convert.ToString(v, Inv), NumberStyles.Any, Inv);

        private static string D(decimal v) => v.ToString(Inv);
        private static string D(decimal? v) => v.HasValue ? v.Value.ToString(Inv) : "null";

        private static string Sha(byte[] b)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
