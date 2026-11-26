using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Localization;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] End-to-end verification on a TEMP database (never the live one): seeds the full demo
    /// company through the real services, runs the money flow for real (payroll for every employee,
    /// payslip PDF FR + AR, CNAS DAC, dashboard, backup) with arithmetic invariants on every payslip,
    /// then opens every main screen bound to its real view-model, in French and Arabic.
    /// Writes docs/e2e/e2e-verification.txt.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class EndToEndVerificationHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly (string Key, string Vm, string View)[] Screens =
        {
            ("dashboard", "DashboardViewModel", "DashboardView"), ("employees", "EmployeesViewModel", "EmployeesView"),
            ("companies", "CompaniesViewModel", "CompaniesView"), ("payroll", "PayrollViewModel", "PayrollView"),
            ("archive", "ArchiveViewModel", "ArchiveView"), ("loans", "LoanViewModel", "LoanView"),
            ("attendance", "AttendanceMatrixViewModel", "AttendanceMatrixView"), ("leave", "LeaveViewModel", "LeaveView"),
            ("contracts", "ContractViewModel", "ContractView"), ("performance", "PerformanceViewModel", "PerformanceView"),
            ("assets", "AssetViewModel", "AssetView"), ("training", "TrainingViewModel", "TrainingView"),
            ("recruitment", "AtsViewModel", "AtsView"), ("certificates", "CertificateViewModel", "CertificateView"),
            ("cnas", "CnasHubViewModel", "CnasHubView"), ("attestations", "AttestationsViewModel", "AttestationsView"),
            ("reports", "ReportsViewModel", "ReportsView"), ("settings", "SettingsViewModel", "SettingsView"),
        };

        [Test]
        public void Verify_Everything_On_A_Temp_Demo_Database()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown; // closing a screen window must not end the app
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            typeof(App).Assembly.GetType("OptiPaie.Desktop.Documents.PdfFonts").GetMethod("Register", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var o = new StringBuilder();
            var fails = new List<string>();
            void Check(string section, bool ok, string what) { o.AppendLine((ok ? "  PASS " : "  FAIL ") + section + " — " + what); if (!ok) fails.Add(section + ": " + what); }

            try
            {
                // Safety first: the live database must be unreachable from this process.
                bool liveRefused = false;
                try { CompositionRoot.Build(); } catch (InvalidOperationException) { liveRefused = true; }
                Check("Sécurité des tests", liveRefused, "opening the LIVE database from a test host is refused");

                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                TranslationSource.Instance.Attach(services.Localization);
                ThemeManager.Apply(false);
                o.AppendLine("END-TO-END VERIFICATION — temp demo database, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv));
                o.AppendLine();

                // ---- seed the demo company through the real services
                var seeder = new OptiPaie.Services.DemoDataSeeder(services.Companies, services.Employees, services.Contracts, services.Attendance,
                    services.Leave, services.Loans, services.Assets, services.Training, services.Certificates, services.Performance);
                Result<long> seeded = seeder.Seed();
                Check("Démo / données", seeded.IsSuccess && seeded.Value > 0, "demo company seeded " + (seeded.IsSuccess ? "#" + seeded.Value : seeded.Error));
                long companyId = seeded.Value;
                services.CompanyContext.Reload();
                services.CompanyContext.Active = services.CompanyContext.Companies.First(c => c.Id == companyId);
                Company company = services.Companies.Get(companyId);
                IReadOnlyList<Employee> staff = services.Employees.GetByCompany(companyId);
                Check("Employés", staff.Count == 20, "employees = " + staff.Count);

                // ---- payroll for every employee, current month, with invariants
                int year = 2026, month = 11, ok = 0;
                decimal masse = 0m, cotisable = 0m;
                var payslipIds = new List<long>();
                foreach (Employee e in staff)
                {
                    PayrollGenerationRequest req = services.Batch.BuildRequest(companyId, e.Id, year, month);
                    PayrollResult preview = services.Payroll.Preview(req);
                    if (!preview.IsSuccess) { Check("Paie", false, e.LastNameFr + " preview failed: " + string.Join(",", preview.Messages.Select(m => m.Code))); continue; }
                    PayrollTotals t = preview.Totals;
                    decimal nonCotTaxable = preview.Lines.Where(l => !l.IsCnasApplicable && l.IsIrgApplicable && !l.ElementType.Equals(ElementType.Deduction)).Sum(l => l.Amount);
                    bool baseOk = t.BaseImposable == Math.Max(0m, t.BaseCotisable - t.CnasEmployee + nonCotTaxable) || preview.Lines.Any(l => l.IsCnasApplicable && !l.IsIrgApplicable);
                    bool cnasOk = t.CnasEmployee == Math.Round(t.BaseCotisable * 0.09m, 2);
                    decimal netDeductions = preview.Trace.Where(s => s.Key == "NET_DEDUCTIONS").Sum(s => s.Amount);
                    bool netOk = t.NetSalaire == t.SalaireBrut - t.CnasEmployee - t.Irg - netDeductions;
                    if (!baseOk || !cnasOk || !netOk) Check("Paie", false, $"{e.LastNameFr}: base {baseOk} cnas {cnasOk} net {netOk}");
                    Result<long> saved = services.Payroll.Generate(req, true);
                    if (saved.IsSuccess) { ok++; payslipIds.Add(saved.Value); masse += t.SalaireBrut; cotisable += t.BaseCotisable; }
                    else Check("Paie", false, e.LastNameFr + " save failed: " + saved.Error);
                }
                Check("Paie", ok == staff.Count, $"payslips generated {ok}/{staff.Count}, masse brute {masse:N2}, every payslip: IRG base = cotisable − CNAS (+ imposable non cotisable), CNAS = 9 %, net = brut − CNAS − IRG − retenues");

                // ---- payslip PDF, FR + AR (the 1.44.1 crash path)
                Payslip slip = services.Archive.GetPayslip(payslipIds.First());
                Employee first = staff.First(s => s.Id == slip.EmployeeId);
                var fiche = new FicheService();
                foreach (bool ar in new[] { false, true })
                {
                    string pdf = Path.Combine(dir, "fiche-" + (ar ? "ar" : "fr") + ".pdf");
                    fiche.ExportPdf(fiche.FromPayslip(company, first, slip, ar, year, month), pdf);
                    Check("Fiche de paie (PDF)", File.Exists(pdf) && new FileInfo(pdf).Length > 10000, (ar ? "AR" : "FR") + " PDF " + (File.Exists(pdf) ? new FileInfo(pdf).Length.ToString("N0", Inv) + " bytes" : "missing"));
                }
                Check("Archive", services.Archive.GetPayslip(payslipIds.Last()) != null, "saved payslips readable back from the archive");

                // ---- CNAS DAC from the saved payslips
                CnasDacReport dac = services.CnasDeclarations.BuildDac(companyId, year, new[] { month });
                Check("Déclarations CNAS", dac.Effectif == ok && dac.Assiette == cotisable, $"DAC effectif {dac.Effectif}, assiette {dac.Assiette:N2} (payslips sum {cotisable:N2})");

                // ---- dashboard
                DashboardOverview d = services.Dashboard.BuildOverview(companyId, new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)));
                Check("Tableau de bord", d.MasseSalariale > 0m && d.ActiveContracts > 0, $"masse {d.MasseSalariale:N2}, contrats actifs {d.ActiveContracts}, congés en attente {d.PendingLeave}, prêts actifs {d.ActiveLoans}");

                // ---- backup
                var backup = services.Backup.Backup(BackupType.Manual);
                Check("Sauvegarde", backup.IsSuccess && File.Exists(backup.Value.FilePath), "backup " + (backup.IsSuccess ? new FileInfo(backup.Value.FilePath).Length.ToString("N0", Inv) + " bytes" : backup.Error));

                // ---- demo removal detection (1.45.0)
                Check("Données démo (1.45.0)", services.DemoData.FindDemoCompany()?.Id == companyId, "demo company recognised by flag");

                // ---- every screen, FR + AR
                o.AppendLine();
                Assembly asm = typeof(App).Assembly;
                foreach ((string key, string vmName, string viewName) in Screens)
                    foreach (string lang in new[] { "fr", "ar" })
                    {
                        string res;
                        try
                        {
                            services.Localization.SetLanguage(lang);
                            object vm = BuildVm(asm, vmName, services);
                            if (vm == null) throw new InvalidOperationException("VM not built");
                            Activate(vm);
                            var view = (FrameworkElement)Activator.CreateInstance(asm.GetTypes().First(t => t.Name == viewName));
                            view.DataContext = vm;
                            var win = new Window { Width = 1362, Height = 900, Left = -10000, Top = -10000, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                                Content = new Border { Background = (Brush)Application.Current.Resources["Canvas"], FlowDirection = lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Child = view } };
                            win.Show(); Pump(); win.UpdateLayout(); Pump();
                            res = view.ActualHeight > 100 ? "OK" : "EMPTY (height " + view.ActualHeight + ")";
                            win.Close();
                        }
                        catch (Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; res = "ERROR " + ex.GetType().Name + ": " + ex.Message; }
                        Check("Écran " + key, res == "OK", lang + " " + res);
                    }
                services.Localization.SetLanguage("fr");

                o.AppendLine();
                o.AppendLine("FAILURES: " + fails.Count);
                foreach (string f in fails) o.AppendLine("  " + f);
            }
            finally
            {
                var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "OptiPaie.sln"))) root = root.Parent;
                Directory.CreateDirectory(Path.Combine(root.FullName, "docs", "e2e"));
                File.WriteAllText(Path.Combine(root.FullName, "docs", "e2e", "e2e-verification.txt"), o.ToString(), new UTF8Encoding(true));
                TestContext.Progress.WriteLine(o.ToString());
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            Assert.That(fails, Is.Empty);
        }

        private static object BuildVm(Assembly asm, string vmName, AppServices services)
        {
            Type t = asm.GetTypes().FirstOrDefault(x => x.Name == vmName);
            if (t == null) return null;
            Action<string> nav = _ => { };
            foreach (ConstructorInfo c in t.GetConstructors())
            {
                ParameterInfo[] p = c.GetParameters();
                if (p.Length == 1 && p[0].ParameterType.IsInstanceOfType(services)) return c.Invoke(new object[] { services });
                if (p.Length == 2 && p[0].ParameterType.IsInstanceOfType(services) && typeof(Delegate).IsAssignableFrom(p[1].ParameterType))
                    return c.Invoke(new object[] { services, nav });
            }
            return null;
        }

        private static void Activate(object vm)
        {
            MethodInfo sync = vm.GetType().GetMethod("LoadSynchronouslyForRender", BindingFlags.Public | BindingFlags.Instance);
            if (sync != null) { sync.Invoke(vm, null); return; }
            vm.GetType().GetMethod("OnActivated", BindingFlags.Public | BindingFlags.Instance)?.Invoke(vm, null);
        }

        private static void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }
    }
}
