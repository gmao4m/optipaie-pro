using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] proofs for the duplicate free-text line fix, on a TEMP demo database (never the live
    /// one). Writes docs/duplicate-line/proof{2,3,4}-&lt;tag&gt;.txt, tag = LEGAL_PROOF_TAG, so the SAME
    /// harness can be run on the engine before the fix (to capture the real failure) and after it.
    ///   PROOF 2 — an automatic loan instalment PLUS two manual deduction lines generates;
    ///   PROOF 3 — the same CATALOG element twice is still refused, message unchanged;
    ///   PROOF 4 — one free-text line only behaves exactly as before.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class DuplicateLineProofHarness
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        [Test]
        public void Prove_Duplicate_Line_Fix()
        {
            string tag = Environment.GetEnvironmentVariable("LEGAL_PROOF_TAG");
            if (string.IsNullOrWhiteSpace(tag)) tag = "run";
            string outDir = Path.Combine(FindRepoRoot(), "docs", "duplicate-line");
            Directory.CreateDirectory(outDir);

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-dupline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                long companyId = services.Companies.Create(new Company { NameFr = "SARL Atlas Industrie", Nif = "000000000000000" }).Value;
                Func<string, string, decimal, long> hire = (last, first, salary) => services.Employees.Create(new Employee
                {
                    CompanyId = companyId, LastNameFr = last, FirstNameFr = first, Poste = "Agent",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2023, 1, 1), BaseSalary = salary, IsActive = true
                }).Value;

                long prime = services.PayrollElements.Create(new PayrollElement
                {
                    NameFr = "Prime de rendement", NameAr = "منحة المردودية", ElementType = ElementType.Gain,
                    CalculationMethod = CalculationMethod.FixedAmount, DefaultAmount = 8000m, Periodicity = ElementPeriodicity.Monthly,
                    IsCnasApplicable = true, IsIrgApplicable = true, IsIncludedInGross = true, IsEnabled = true, IsPrintable = true
                }).Value;

                DateTime m = DateTime.Today.AddMonths(-1);

                // ---------------- PROOF 2: loan instalment + two manual deductions ----------------
                long karim = hire("BENALI", "Karim", 60000m);
                Result<long> loan = services.Loans.Save(new Loan
                {
                    EmployeeId = karim, Type = LoanType.Loan, Status = LoanStatus.Active, Principal = 60000m,
                    MonthlyInstallment = 5000m, StartYear = m.Year, StartMonth = m.Month, Reason = "Prêt personnel"
                });
                Assert.That(loan.IsSuccess, Is.True, loan.Error);

                // Exactly how batch payroll builds the request (catalog assignments + the automatic instalment).
                PayrollGenerationRequest req = services.Batch.BuildRequest(companyId, karim, m.Year, m.Month);
                var p2 = new StringBuilder();
                bool loanLineFromBatch = req.Elements.Any(e => e.IsManual && e.ManualLabel == "Remboursement prêt");
                if (!loanLineFromBatch)
                {
                    // The licence gate on an unlicensed temp DB can hide the Loans module from BuildRequest; add the
                    // instalment line exactly as BatchPayrollService does (free-text line, id 0, same label).
                    req.Elements.Add(new PayrollElementEntry
                    {
                        ElementId = 0, IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Remboursement prêt",
                        LineAmount = services.Loans.GetMonthlyDeduction(karim, m.Year, m.Month)
                    });
                }
                req.Elements.Add(new PayrollElementEntry { IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Avance exceptionnelle", LineAmount = 2000m });
                req.Elements.Add(new PayrollElementEntry { IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Retenue matériel perdu", LineAmount = 1500m });

                Result<long> gen = services.Payroll.Generate(req, acknowledgeWarnings: true);
                p2.AppendLine("PROOF 2 — employee with an ACTIVE LOAN + two manual deduction lines (" + m.ToString("MMMM yyyy", Fr) + ")");
                p2.AppendLine("  automatic instalment line: " + (loanLineFromBatch ? "added by BatchPayrollService.BuildRequest" : "added exactly as BatchPayrollService does (licence gate hid the module in this temp DB)"));
                p2.AppendLine("  free-text (id-less) lines on the payslip: 3 (instalment + 2 manual)");
                p2.AppendLine("  generation: " + (gen.IsSuccess ? "SUCCESS (payslip #" + gen.Value + ")" : "REFUSED  [" + gen.ErrorCode + "] " + gen.Error));
                if (gen.IsSuccess)
                {
                    Payslip ps = services.Archive.GetPayslip(gen.Value);
                    p2.AppendLine();
                    foreach (PayrollDetail d in ps.Details)
                        p2.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", d.LabelFr, d.ElementType == ElementType.Gain ? "gain" : "retenue", Money(d.Amount)));
                    p2.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "SALAIRE BRUT", "", Money(ps.SalaireBrut)));
                    p2.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "CNAS salarié", "retenue", Money(ps.CnasEmployee)));
                    p2.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "IRG", "retenue", Money(ps.Irg)));
                    decimal user = ps.Details.Where(d => d.ElementType == ElementType.Deduction).Sum(d => d.Amount);
                    p2.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "NET À PAYER", "", Money(ps.NetSalaire)));
                    p2.AppendLine("  check: " + Money(ps.SalaireBrut) + " − " + Money(ps.CnasEmployee) + " − " + Money(ps.Irg) + " − " + Money(user) +
                                  " = " + Money(ps.SalaireBrut - ps.CnasEmployee - ps.Irg - user) +
                                  (ps.SalaireBrut - ps.CnasEmployee - ps.Irg - user == ps.NetSalaire ? "  ✓" : "  ✗"));
                }
                File.WriteAllText(Path.Combine(outDir, "proof2-" + tag + ".txt"), p2.ToString(), new UTF8Encoding(false));

                // ---------------- PROOF 3: the same CATALOG element twice ----------------
                long nadia = hire("ZEROUAL", "Nadia", 52000m);
                var twice = new PayrollGenerationRequest
                {
                    CompanyId = companyId, EmployeeId = nadia, Year = m.Year, Month = m.Month,
                    Elements = new List<PayrollElementEntry>
                    {
                        new PayrollElementEntry { ElementId = prime, LineAmount = 8000m },
                        new PayrollElementEntry { ElementId = prime, LineAmount = 8000m }
                    }
                };
                Result<long> dup = services.Payroll.Generate(twice, acknowledgeWarnings: true);
                string p3 = "PROOF 3 — the same catalog element (#" + prime + " « Prime de rendement ») applied twice\n" +
                            "  generation: " + (dup.IsSuccess ? "ACCEPTED (protection weakened!)" : "REFUSED") + "\n" +
                            "  code    : " + dup.ErrorCode + "\n" +
                            "  message : " + dup.Error + "\n";
                File.WriteAllText(Path.Combine(outDir, "proof3-" + tag + ".txt"), p3, new UTF8Encoding(false));

                // ---------------- PROOF 4: one free-text line only ----------------
                long lila = hire("BELKACEM", "Lila", 45000m);
                var single = new PayrollGenerationRequest
                {
                    CompanyId = companyId, EmployeeId = lila, Year = m.Year, Month = m.Month,
                    Elements = new List<PayrollElementEntry>
                    {
                        new PayrollElementEntry { IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Avance sur salaire", LineAmount = 3000m }
                    }
                };
                PayrollResult r4 = services.Payroll.Preview(single);
                var p4 = new StringBuilder();
                p4.AppendLine("PROOF 4 — one free-text deduction line only (base 45 000, avance 3 000)");
                p4.AppendLine("  success: " + r4.IsSuccess);
                if (r4.Totals != null)
                {
                    p4.AppendLine("  brut " + r4.Totals.SalaireBrut + " | CNAS " + r4.Totals.CnasEmployee + " | IRG " + r4.Totals.Irg + " | net " + r4.Totals.NetSalaire);
                    foreach (PayrollLineResult l in r4.Lines) p4.AppendLine("  line " + l.ElementId + "|" + l.LabelFr + "|" + l.ElementType + "|" + l.Amount);
                    foreach (PayrollCalculationStep s in r4.Trace) p4.AppendLine("  step " + s.Key + "|" + s.Amount);
                }
                foreach (PayrollMessage msg in r4.Messages) p4.AppendLine("  msg " + msg.Severity + "|" + msg.Code);
                File.WriteAllText(Path.Combine(outDir, "proof4-" + tag + ".txt"), p4.ToString(), new UTF8Encoding(false));

                // ---------------- EARNINGS SIDE: two free bonuses + loan instalment + one manual deduction ----------------
                long bilal = hire("HADDAD", "Bilal", 50000m);
                Assert.That(services.Loans.Save(new Loan
                {
                    EmployeeId = bilal, Type = LoanType.Loan, Status = LoanStatus.Active, Principal = 40000m,
                    MonthlyInstallment = 4000m, StartYear = m.Year, StartMonth = m.Month, Reason = "Prêt personnel"
                }).IsSuccess, Is.True);
                PayrollGenerationRequest er = services.Batch.BuildRequest(companyId, bilal, m.Year, m.Month);
                if (!er.Elements.Any(e => e.IsManual && e.ManualLabel == "Remboursement prêt"))
                    er.Elements.Add(new PayrollElementEntry
                    {
                        ElementId = 0, IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Remboursement prêt",
                        LineAmount = services.Loans.GetMonthlyDeduction(bilal, m.Year, m.Month)
                    });
                er.Elements.Add(new PayrollElementEntry { IsManual = true, ManualType = ElementType.Gain, ManualLabel = "Prime exceptionnelle", LineAmount = 6000m });
                er.Elements.Add(new PayrollElementEntry { IsManual = true, ManualType = ElementType.Gain, ManualLabel = "Indemnité de déplacement", LineAmount = 3500m });
                er.Elements.Add(new PayrollElementEntry { IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Avance exceptionnelle", LineAmount = 2500m });
                Result<long> eg = services.Payroll.Generate(er, acknowledgeWarnings: true);
                var pe = new StringBuilder();
                pe.AppendLine("EARNINGS SIDE — two FREE bonuses + loan instalment + one manual deduction (" + m.ToString("MMMM yyyy", Fr) + ")");
                pe.AppendLine("  free-text (id 0) lines: 4 — 2 gains, 2 deductions");
                pe.AppendLine("  generation: " + (eg.IsSuccess ? "SUCCESS (payslip #" + eg.Value + ")" : "REFUSED  [" + eg.ErrorCode + "] " + eg.Error));
                if (eg.IsSuccess)
                {
                    Payslip ep = services.Archive.GetPayslip(eg.Value);
                    foreach (PayrollDetail d in ep.Details)
                        pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", d.LabelFr, d.ElementType == ElementType.Gain ? "gain" : "retenue", Money(d.Amount)));
                    pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "SALAIRE BRUT", "", Money(ep.SalaireBrut)));
                    pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "Base cotisable", "", Money(ep.BaseCotisable)));
                    pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "CNAS salarié", "retenue", Money(ep.CnasEmployee)));
                    pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "IRG", "retenue", Money(ep.Irg)));
                    pe.AppendLine(string.Format("    {0,-26} {1,8} {2,14}", "NET À PAYER", "", Money(ep.NetSalaire)));
                    decimal gains = ep.Details.Where(d => d.ElementType == ElementType.Gain).Sum(d => d.Amount);
                    decimal deds = ep.Details.Where(d => d.ElementType == ElementType.Deduction).Sum(d => d.Amount);
                    pe.AppendLine("  check gross: lines " + Money(gains) + (gains == ep.SalaireBrut ? " = brut ✓" : " ≠ brut ✗"));
                    pe.AppendLine("  check net  : " + Money(ep.SalaireBrut) + " − " + Money(ep.CnasEmployee) + " − " + Money(ep.Irg) + " − " + Money(deds) +
                                  " = " + Money(ep.SalaireBrut - ep.CnasEmployee - ep.Irg - deds) + (ep.SalaireBrut - ep.CnasEmployee - ep.Irg - deds == ep.NetSalaire ? "  ✓" : "  ✗"));
                }
                File.WriteAllText(Path.Combine(outDir, "proof-earnings-" + tag + ".txt"), pe.ToString(), new UTF8Encoding(false));
                TestContext.Out.WriteLine(pe.ToString());

                TestContext.Out.WriteLine(p2.ToString());
                TestContext.Out.WriteLine(p3);
                TestContext.Out.WriteLine(p4.ToString());

                // Proof 3 must hold before AND after the fix.
                Assert.That(dup.IsSuccess, Is.False);
                Assert.That(dup.ErrorCode, Is.EqualTo("Payroll_DuplicateElement"));
                Assert.That(dup.Error, Is.EqualTo("Une rubrique de paie est dupliquée: Prime de rendement"));
                Assert.That(r4.IsSuccess, Is.True);
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static string Money(decimal v) => v.ToString("N2", Fr);

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
