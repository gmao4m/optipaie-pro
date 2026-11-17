using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.ViewModels.Attendance;
using OptiPaie.Desktop.Views;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Renders the rebuilt attendance ("Today") screen and the three reports over a TEMP database
    /// (never the real one), in French and Arabic, for the scenarios the owner asked to see: a busy
    /// day with several exceptions, a quiet day, a rest day, and an empty company. Output in
    /// docs/attendance-today/. [Explicit], not a pass/fail test.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class AttendanceTodayScreenshots
    {
        private const double Width = 1360;
        private const double Scale = 1.25;

        [Test]
        public void Render_Scenarios()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-today-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var config = new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr");
            AppServices services = CompositionRoot.Build(config);
            TranslationSource.Instance.Attach(services.Localization);

            // --- seed: company A (8 employees + a weekend guard), empty company B, and company C
            //     (normal employees, used to show the pure rest-day banner) ---
            long companyA = services.Companies.Create(new Company { NameFr = "SARL Atlas Industrie", Nif = "000000000000000" }).Value;
            long companyB = services.Companies.Create(new Company { NameFr = "Nouvelle Société (vide)", Nif = "000000000000001" }).Value;
            long companyC = services.Companies.Create(new Company { NameFr = "SARL Horizon Bureau", Nif = "000000000000002" }).Value;

            string[] names = { "AMRANI Salima", "BENALI Karim", "CHERIF Mourad", "FERHAT Yasmine",
                               "HADDAD Bilal", "KADDOUR Amine", "MEDJANI Imene", "ZEROUAL Nadia" };
            var ids = new long[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string[] p = names[i].Split(' ');
                ids[i] = services.Employees.Create(new Employee
                {
                    CompanyId = companyA, LastNameFr = p[0], FirstNameFr = p[1], Poste = "Agent",
                    Department = i % 2 == 0 ? "Production" : "Administration",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 1, 1), BaseSalary = 45000m, IsActive = true
                }).Value;
            }

            // A security guard with his OWN weekly pattern: he works every day, Friday and Saturday
            // included. On the company's rest day he still appears, expected present.
            services.Employees.Create(new Employee
            {
                CompanyId = companyA, LastNameFr = "SAADI", FirstNameFr = "Lounes", Poste = "Gardien",
                Department = "Sécurité", Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 1, 1), BaseSalary = 40000m, IsActive = true,
                UsesOwnSchedule = true, SchedMon = true, SchedTue = true, SchedWed = true, SchedThu = true,
                SchedFri = true, SchedSat = true, SchedSun = true
            });

            // Company C: three ordinary employees (no override) — so a Friday shows the rest banner.
            foreach (string n in new[] { "TOUATI Rachid", "BELKACEM Lila", "OUARED Sofiane" })
            {
                string[] p = n.Split(' ');
                services.Employees.Create(new Employee
                {
                    CompanyId = companyC, LastNameFr = p[0], FirstNameFr = p[1], Poste = "Employé",
                    Department = "Bureau", Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 1, 1), BaseSalary = 45000m, IsActive = true
                });
            }

            int year = DateTime.Today.Year, month = DateTime.Today.Month;
            DateTime busy = FirstWeekday(year, month, DayOfWeek.Monday);
            DateTime quiet = FirstWeekday(year, month, DayOfWeek.Tuesday);
            DateTime rest = FirstWeekday(year, month, DayOfWeek.Friday); // Friday is a rest day by default

            // A busy day: several exceptions; the rest stay present (default, no record).
            services.Attendance.SetDayStatus(ids[0], busy, AttendanceStatus.Absent);
            services.Attendance.SetDayStatus(ids[1], busy, AttendanceStatus.Absent);
            services.Attendance.SetDayStatus(ids[2], busy, AttendanceStatus.Leave);
            services.Attendance.SetDayStatus(ids[3], busy, AttendanceStatus.HalfDay);
            services.Attendance.SetDayStatus(ids[4], busy, AttendanceStatus.Late);
            services.Attendance.SetDayStatus(ids[5], busy, AttendanceStatus.JustifiedAbsence);

            string outDir = Path.Combine(FindRepoRoot(), "docs", "attendance-today");
            Directory.CreateDirectory(outDir);
            var log = new System.Collections.Generic.List<string>();

            // --- screen scenarios ---
            services.CompanyContext.Reload();
            services.CompanyContext.Active = services.Companies.Get(companyA);

            foreach (string lang in new[] { "fr", "ar" })
            {
                Screen(services, lang, busy, Path.Combine(outDir, $"busy-{lang}.png"), log);
                // The company's Friday is a rest day, but the guard works it → only he shows, no banner.
                Screen(services, lang, rest, Path.Combine(outDir, $"guard-fri-{lang}.png"), log);
            }
            Screen(services, "fr", quiet, Path.Combine(outDir, "quiet-fr.png"), log);

            // Company C on a Friday: ordinary employees, nobody scheduled → the plain rest-day banner.
            services.CompanyContext.Active = services.Companies.Get(companyC);
            Screen(services, "fr", rest, Path.Combine(outDir, "rest-fr.png"), log);

            services.CompanyContext.Active = services.Companies.Get(companyB);
            Screen(services, "fr", busy, Path.Combine(outDir, "empty-fr.png"), log);
            Screen(services, "ar", busy, Path.Combine(outDir, "empty-ar.png"), log);

            // --- reports (company A, the busy month) ---
            services.CompanyContext.Active = services.Companies.Get(companyA);
            services.Localization.SetLanguage("fr");
            var reports = new AttendanceReportsViewModel(services, year, month);
            try
            {
                Document.Create(new AttendanceSheetDocument(reports.BuildSheetModel()).Compose)
                    .GenerateImages(i => Path.Combine(outDir, $"report-sheet-{i}.png"));
                Document.Create(new AttendanceSummaryDocument(reports.BuildSummaryModel()).Compose)
                    .GenerateImages(i => Path.Combine(outDir, $"report-summary-{i}.png"));
                Document.Create(new AttendancePeriodSummaryDocument(reports.BuildSummaryModel()).Compose)
                    .GenerateImages(i => Path.Combine(outDir, $"report-period-{i}.png"));
                log.Add("reports: OK");
            }
            catch (Exception ex) { log.Add("reports: ERROR " + ex.Message); }

            File.WriteAllLines(Path.Combine(outDir, "_log.txt"), log);
            foreach (string l in log) TestContext.WriteLine(l);

            try { System.Data.SQLite.SQLiteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static void Screen(AppServices services, string lang, DateTime date, string file, System.Collections.Generic.List<string> log)
        {
            try
            {
                services.Localization.SetLanguage(lang);
                var vm = new AttendanceMatrixViewModel(services);
                vm.OnActivated();
                vm.Daily.Date = date;

                var view = new AttendanceMatrixView { DataContext = vm };
                var host = new Border
                {
                    Background = (Brush)Application.Current.Resources["Canvas"],
                    FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    Width = Width,
                    Child = view
                };
                // Border-only measure/arrange (no Window host): the daily view is an ItemsControl, and the
                // month matrix is in a COLLAPSED Expander, so there is no DataGrid star-column to settle —
                // this avoids the dispatcher-pump hang that a live DataGrid causes off-screen.
                host.Measure(new Size(Width, double.PositiveInfinity));
                double h = Math.Max(640, host.DesiredSize.Height);
                host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();
                host.Measure(new Size(Width, double.PositiveInfinity));
                h = Math.Max(640, host.DesiredSize.Height);
                host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();

                var rtb = new RenderTargetBitmap((int)(Width * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                rtb.Render(host);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                log.Add(Path.GetFileName(file) + $": OK {(int)(Width * Scale)}x{(int)(h * Scale)}");
            }
            catch (Exception ex) { log.Add(Path.GetFileName(file) + ": ERROR " + Innermost(ex).Message); }
        }

        private static DateTime FirstWeekday(int y, int m, DayOfWeek dow)
        {
            var d = new DateTime(y, m, 1);
            while (d.DayOfWeek != dow) d = d.AddDays(1);
            return d;
        }

        private static void Pump()
        {
            for (int i = 0; i < 2; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        private static Exception Innermost(Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; return ex; }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
