using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Migrations;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;
using QuestPDF.Drawing;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] proof harness for the IRG/CNAS switch. Writes to docs/legal-deductions/:
    ///   PROOF 3 — the upgrade on a COPY of the REAL pre-migration (v43) database: schema → 44, the
    ///             setting reads ON, every payslip back-filled ON, money columns unchanged;
    ///   PROOF 2 — one full payslip on a DEMO database, computed ON then OFF from the same inputs,
    ///             with the real numbers (gross, deductions removed, advances kept, net);
    ///   screenshots — the setting (FR/AR, ON and read-only), the confirmation dialog (FR/AR), the
    ///             payslip computed OFF and ON, and the declarations notice.
    /// Never opens the live database for writing: the live file is only COPIED.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class LegalDeductionsProofHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private string _out;

        [OneTimeSetUp]
        public void Setup()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            typeof(App).Assembly.GetType("OptiPaie.Desktop.Documents.PdfFonts")
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            _out = Path.Combine(FindRepoRoot(), "docs", "legal-deductions");
            Directory.CreateDirectory(_out);
        }

        // ------------------------------------------------------------------ PROOF 3 on the real database

        [Test]
        public void Proof3_Upgrade_Of_A_Copy_Of_The_Real_Pre_Migration_Database()
        {
            string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OptiPaie DZ", AppConstants.DatabaseFileName);
            string dir = TempDir("optipaie-upgrade-real-");
            string copy = Path.Combine(dir, AppConstants.DatabaseFileName);
            File.Copy(live, copy);

            var log = new StringBuilder();
            const string money = "SELECT Id, SalaireBrut, BaseCotisable, CnasEmployee, CnasEmployer, BaseImposable, IrgBrut, " +
                                 "Abattement, Irg, NetSalaire FROM Payslips ORDER BY Id;";
            try
            {
                long versionBefore; int payslips; string moneyBefore;
                using (var c = Open(copy))
                {
                    versionBefore = Convert.ToInt64(Scalar(c, "SELECT MAX(Version) FROM SchemaMigrations;"), Inv);
                    payslips = Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM Payslips;"), Inv);
                    moneyBefore = HashRows(c, money);
                    Assert.That(Scalar(c, "SELECT COUNT(*) FROM pragma_table_info('Payslips') WHERE name = 'LegalDeductionsApplied';"),
                        Is.EqualTo(0L), "a genuine pre-migration database: the column does not exist yet");
                }

                int applied;
                using (var c = Open(copy)) applied = new MigrationRunner(c).Run();

                long versionAfter; long flaggedOn; object setting; string moneyAfter;
                using (var c = Open(copy))
                {
                    versionAfter = Convert.ToInt64(Scalar(c, "SELECT MAX(Version) FROM SchemaMigrations;"), Inv);
                    flaggedOn = Convert.ToInt64(Scalar(c, "SELECT COUNT(*) FROM Payslips WHERE LegalDeductionsApplied = 1;"), Inv);
                    setting = Scalar(c, "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LEGAL_DEDUCTIONS_ENABLED';");
                    moneyAfter = HashRows(c, money);
                }

                // Read it exactly the way the product does (fail-safe service read).
                bool readsOn = new OptiPaie.Services.ConfigurationService(
                    new OptiPaie.Data.Context.UnitOfWorkFactory(new OptiPaie.Data.Context.SqliteConnectionFactory(copy)))
                    .GetLegalDeductionsEnabled();

                log.AppendLine("PROOF 3 — upgrade of a COPY of the real database (" + live + ")");
                log.AppendLine("  schema before        : v" + versionBefore + " (no LegalDeductionsApplied column)");
                log.AppendLine("  migrations applied   : " + applied);
                log.AppendLine("  schema after         : v" + versionAfter);
                log.AppendLine("  setting stored       : '" + setting + "'  → service reads " + (readsOn ? "ON" : "OFF"));
                log.AppendLine("  payslips flagged ON  : " + flaggedOn + " / " + payslips);
                log.AppendLine("  money columns sha256 : before " + moneyBefore.Substring(0, 16) + "…  after " + moneyAfter.Substring(0, 16) +
                               "…  " + (moneyBefore == moneyAfter ? "UNCHANGED" : "CHANGED"));
                File.WriteAllText(Path.Combine(_out, "proof3-upgrade-real-db.txt"), log.ToString(), new UTF8Encoding(false));
                TestContext.Out.WriteLine(log.ToString());

                Assert.That(versionBefore, Is.EqualTo(43L));
                Assert.That(versionAfter, Is.EqualTo(44L));
                Assert.That(readsOn, Is.True);
                Assert.That(flaggedOn, Is.EqualTo(payslips));
                Assert.That(moneyAfter, Is.EqualTo(moneyBefore));
            }
            finally { Cleanup(dir); }
        }

        // ------------------------------------------------------------------ PROOF 2 + screenshots

        [Test]
        public void Proof2_Payslip_On_Versus_Off_With_Numbers_And_Screenshots()
        {
            string dir = TempDir("optipaie-legal-demo-");
            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                TranslationSource.Instance.Attach(services.Localization);
                ThemeManager.Apply(false);

                long companyId = services.Companies.Create(new Company
                {
                    NameFr = "SARL Atlas Industrie", NameAr = "شركة أطلس للصناعة", Nif = "000000000000000",
                    AddressFr = "Zone industrielle, Rouiba — Alger"
                }).Value;
                long employeeId = services.Employees.Create(new Employee
                {
                    CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", LastNameAr = "بن علي", FirstNameAr = "كريم",
                    Poste = "Technicien de maintenance", Category = "Agent de maîtrise", Nss = "000000000000",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Married, Dependents = 2, PaymentMode = PaymentMode.BankTransfer,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 3, 1), BaseSalary = 60000m, IsActive = true
                }).Value;
                services.CompanyContext.Reload();
                services.CompanyContext.Active = services.Companies.Get(companyId);

                // Catalog elements, as in production: a cotisable + taxable bonus and a salary advance. The loan
                // instalment stays the one free-text line (the engine refuses two id-0 lines on one payslip).
                long primeId = services.PayrollElements.Create(new PayrollElement
                {
                    NameFr = "Prime de rendement", NameAr = "منحة المردودية", ElementType = ElementType.Gain,
                    CalculationMethod = CalculationMethod.FixedAmount, DefaultAmount = 8000m, Periodicity = ElementPeriodicity.Monthly,
                    IsCnasApplicable = true, IsIrgApplicable = true, IsIncludedInGross = true, IsEnabled = true, IsPrintable = true
                }).Value;
                long acompteId = services.PayrollElements.Create(new PayrollElement
                {
                    NameFr = "Acompte sur salaire", NameAr = "تسبيق على الأجر", ElementType = ElementType.Deduction,
                    CalculationMethod = CalculationMethod.FixedAmount, DefaultAmount = 5000m, Periodicity = ElementPeriodicity.Monthly,
                    IsCnasApplicable = false, IsIrgApplicable = false, IsIncludedInGross = false, IsEnabled = true, IsPrintable = true
                }).Value;

                // The SAME inputs for both: base 60 000 prorated for 2 absent days (29/31), the bonus, and two
                // user-entered deductions — the salary advance and a loan instalment.
                DateTime mOn = DateTime.Today.AddMonths(-2), mOff = DateTime.Today.AddMonths(-1);
                Func<DateTime, PayrollGenerationRequest> request = m => new PayrollGenerationRequest
                {
                    CompanyId = companyId, EmployeeId = employeeId, Year = m.Year, Month = m.Month,
                    WorkedDays = 29m, WorkableDays = 31m,
                    Elements = new List<PayrollElementEntry>
                    {
                        new PayrollElementEntry { ElementId = primeId, LineAmount = 8000m },
                        new PayrollElementEntry { ElementId = acompteId, LineAmount = 5000m },
                        new PayrollElementEntry { IsManual = true, ManualType = ElementType.Deduction, ManualLabel = "Remboursement prêt", LineAmount = 3000m }
                    }
                };

                Assert.That(services.ConfigurationService.GetLegalDeductionsEnabled(), Is.True);
                Result<long> on = services.Payroll.Generate(request(mOn), acknowledgeWarnings: true);
                Assert.That(on.IsSuccess, Is.True, "ON generate: [" + on.ErrorCode + "] " + on.Error);

                Assert.That(services.ConfigurationService.SetLegalDeductionsEnabled(false).IsSuccess, Is.True);
                Result<long> off = services.Payroll.Generate(request(mOff), acknowledgeWarnings: false); // must NOT be blocked
                Assert.That(off.IsSuccess, Is.True, "an OFF payslip is issued without a sanity block: " + off.Error);

                Payslip pOn = services.Archive.GetPayslip(on.Value);
                Payslip pOff = services.Archive.GetPayslip(off.Value);
                Assert.That(pOn.LegalDeductionsApplied, Is.True);
                Assert.That(pOff.LegalDeductionsApplied, Is.False);

                decimal userDeductionsOn = pOn.Details.Where(d => d.ElementType == ElementType.Deduction).Sum(d => d.Amount);
                decimal userDeductionsOff = pOff.Details.Where(d => d.ElementType == ElementType.Deduction).Sum(d => d.Amount);

                var t = new StringBuilder();
                t.AppendLine("PROOF 2 — one payslip, the same inputs, computed with the switch ON and OFF (demo database)");
                t.AppendLine("  inputs: base 60 000 DA prorated 29/31 days (2 absent days), prime de rendement 8 000,");
                t.AppendLine("          acompte sur salaire 5 000, remboursement prêt 3 000");
                t.AppendLine();
                t.AppendLine(string.Format("  {0,-30}{1,16}{2,16}", "", "ON", "OFF"));
                Row(t, "Salaire brut", pOn.SalaireBrut, pOff.SalaireBrut);
                Row(t, "Base cotisable", pOn.BaseCotisable, pOff.BaseCotisable);
                Row(t, "CNAS salarié (9 %)", pOn.CnasEmployee, pOff.CnasEmployee);
                Row(t, "CNAS employeur (26 %)", pOn.CnasEmployer, pOff.CnasEmployer);
                Row(t, "IRG", pOn.Irg, pOff.Irg);
                Row(t, "Retenues saisies (acompte+prêt)", userDeductionsOn, userDeductionsOff);
                Row(t, "NET À PAYER", pOn.NetSalaire, pOff.NetSalaire);
                t.AppendLine();
                t.AppendLine("  OFF net = brut − retenues saisies = " + Money(pOff.SalaireBrut) + " − " + Money(userDeductionsOff) +
                             " = " + Money(pOff.SalaireBrut - userDeductionsOff) +
                             (pOff.SalaireBrut - userDeductionsOff == pOff.NetSalaire ? "  ✓" : "  ✗ MISMATCH"));
                t.AppendLine("  legal deductions removed by the switch: CNAS " + Money(pOn.CnasEmployee) + " + IRG " + Money(pOn.Irg) +
                             " = " + Money(pOn.CnasEmployee + pOn.Irg) + " (employer CNAS " + Money(pOn.CnasEmployer) + " no longer due)");
                File.WriteAllText(Path.Combine(_out, "proof2-on-vs-off.txt"), t.ToString(), new UTF8Encoding(false));
                TestContext.Out.WriteLine(t.ToString());

                Assert.That(pOff.SalaireBrut, Is.EqualTo(pOn.SalaireBrut), "the switch never changes the gross");
                Assert.That(pOff.CnasEmployee + pOff.CnasEmployer + pOff.Irg, Is.EqualTo(0m));
                Assert.That(userDeductionsOff, Is.EqualTo(8000m), "advance + instalment still deducted");
                Assert.That(pOff.NetSalaire, Is.EqualTo(pOff.SalaireBrut - userDeductionsOff));

                // --- payslip screenshots (the saved payslips, through the reprint path) ---
                Company company = services.Companies.Get(companyId);
                Employee employee = services.Employees.Get(employeeId);
                var fiche = new FicheService();
                Png(fiche.FromPayslip(company, employee, pOff, false, mOff.Year, mOff.Month), "payslip-OFF");
                Png(fiche.FromPayslip(company, employee, pOn, false, mOn.Year, mOn.Month), "payslip-ON");

                // --- setting card + confirmation dialog + declarations notice, FR and AR ---
                foreach (string lang in new[] { "fr", "ar" })
                {
                    services.Localization.SetLanguage(lang);
                    services.ConfigurationService.SetLegalDeductionsEnabled(true);

                    var settings = new SettingsViewModel(services);
                    settings.OnActivated();
                    typeof(SettingsViewModel).GetProperty("DeviceIdText").SetValue(settings, "•••• (masqué)"); // never publish this PC's device id
                    Shot(new SettingsView { DataContext = settings }, 1100, "setting-ON-" + lang, services);

                    services.ConfigurationService.SetLegalDeductionsEnabled(false);
                    settings.OnActivated();
                    typeof(SettingsViewModel).GetProperty("DeviceIdText").SetValue(settings, "•••• (masqué)"); // never publish this PC's device id
                    Shot(new SettingsView { DataContext = settings }, 1100, "setting-OFF-" + lang, services);

                    var dialog = new LegalDeductionsConfirmWindow();
                    var body = (FrameworkElement)dialog.Content;
                    dialog.Content = null;
                    Shot(new Border
                    {
                        Width = 460, BorderThickness = new Thickness(1),
                        BorderBrush = (Brush)Application.Current.Resources["BorderStrong"], Child = body
                    }, 460, "confirm-dialog-" + lang, services);
                    dialog.Close();

                    var hub = new CnasHubViewModel(services);
                    hub.OnActivated();
                    Shot(new CnasHubView { DataContext = hub }, 1100, "declarations-OFF-" + lang, services);
                }
            }
            finally { Cleanup(dir); }
        }

        // ------------------------------------------------------------------ helpers

        private void Png(FichePaieModel model, string name)
        {
            Document.Create(c => new FichePaieDocument(model).Compose(c))
                .WithMetadata(new DocumentMetadata { RasterDpi = 150 })
                .GenerateImages(i => Path.Combine(_out, name + (i == 0 ? "" : "-" + i) + ".png"));
        }

        private void Shot(FrameworkElement view, double width, string name, AppServices services)
        {
            var host = new Border
            {
                Background = (Brush)Application.Current.Resources["Canvas"],
                FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Width = width, Child = view
            };
            host.Measure(new Size(width, double.PositiveInfinity));
            double h = Math.Min(1400, Math.Max(120, host.DesiredSize.Height));
            host.Arrange(new Rect(0, 0, width, h)); host.UpdateLayout();
            const double scale = 1.5;
            var rtb = new RenderTargetBitmap((int)(width * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(host);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new FileStream(Path.Combine(_out, name + ".png"), FileMode.Create)) enc.Save(fs);
            host.Child = null;
        }

        private static void Row(StringBuilder t, string label, decimal on, decimal off) =>
            t.AppendLine(string.Format("  {0,-30}{1,16}{2,16}", label, Money(on), Money(off)));

        private static string Money(decimal v) => v.ToString("N2", Fr);

        private static SQLiteConnection Open(string path)
        {
            var c = new SQLiteConnection("Data Source=" + path + ";Version=3;");
            c.Open();
            return c;
        }

        private static object Scalar(SQLiteConnection c, string sql)
        {
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); }
        }

        private static string HashRows(SQLiteConnection c, string sql)
        {
            var sb = new StringBuilder();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        for (int i = 0; i < r.FieldCount; i++) sb.Append(Convert.ToString(r.GetValue(i), Inv)).Append('|');
                        sb.Append('\n');
                    }
            }
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static string TempDir(string prefix)
        {
            string d = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Cleanup(string dir)
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
