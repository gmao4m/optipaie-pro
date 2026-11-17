using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.ViewModels.Attendance;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// The daily ("Today") attendance screen must open fast and respond to a click instantly. This
    /// seeds 100 employees in a TEMP database (never the live one), then measures the real VM cost:
    /// Load() — the one-query data fetch + 100-row build that happens when the screen opens — and a
    /// single status click (one write + recompute). Both are asserted under a safe ceiling and the
    /// actual numbers are logged so a regression is visible. Read-only w.r.t. the product.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public sealed class AttendanceTodayPerfTests
    {
        [Test]
        public void Daily_Opens_And_Clicks_Fast_With_100_Employees()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-today-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var config = new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr");
            AppServices services = CompositionRoot.Build(config);
            TranslationSource.Instance.Attach(services.Localization);

            long company = services.Companies.Create(new Company { NameFr = "SARL Perf", Nif = "000000000000000" }).Value;
            for (int i = 1; i <= 100; i++)
                services.Employees.Create(new Employee
                {
                    CompanyId = company, LastNameFr = "EMP" + i.ToString("000"), FirstNameFr = "Agent",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(DateTime.Today.Year - 1, 1, 1),
                    BaseSalary = 45000m, IsActive = true
                });

            services.CompanyContext.Reload();
            services.CompanyContext.Active = services.Companies.Get(company);

            // Pick a work day so all 100 rows are built (the heaviest case).
            DateTime day = DateTime.Today;
            for (int i = 0; i < 7 && !IsWorkday(day); i++) day = day.AddDays(1);

            var vm = new AttendanceDailyViewModel(services);

            // Warm once (JIT + brush freeze + first query plan), then measure steady-state.
            // Set the date and load explicitly (the setter no-ops when the date is already today).
            vm.Date = day;
            vm.Load();
            Assert.That(vm.Rows.Count, Is.EqualTo(100), "all 100 employees are expected on a work day");

            var times = new long[5];
            for (int i = 0; i < times.Length; i++)
            {
                var sw = Stopwatch.StartNew();
                vm.Load();
                sw.Stop();
                times[i] = sw.ElapsedMilliseconds;
            }
            Array.Sort(times);
            long medianOpen = times[times.Length / 2];

            // One click: set the first employee Absent (a real DB write + recompute).
            DailyChipViewModel absent = vm.Rows[0].PrimaryChips.First(c => c.Status == AttendanceStatus.Absent);
            var swClick = Stopwatch.StartNew();
            absent.PickCommand.Execute(null);
            swClick.Stop();

            TestContext.Out.WriteLine(
                "Daily 'Today' @100 employees : open(Load median of 5)=" + medianOpen + " ms  [" + string.Join("/", times) + "]" +
                " · one click(write+recompute)=" + swClick.ElapsedMilliseconds + " ms");

            Assert.That(vm.Rows[0].Status, Is.EqualTo(AttendanceStatus.Absent), "the click registered");
            Assert.That(medianOpen, Is.LessThan(500), "the daily screen must open in under half a second with 100 employees");
            Assert.That(swClick.ElapsedMilliseconds, Is.LessThan(250), "a click must register instantly");

            try { System.Data.SQLite.SQLiteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static bool IsWorkday(DateTime d) => d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday;
    }
}
