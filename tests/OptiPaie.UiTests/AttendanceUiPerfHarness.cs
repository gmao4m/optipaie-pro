using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.ViewModels.Attendance;
using OptiPaie.Desktop.Views;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] measures the REAL perceived cost of the pointage screen — the window, not the query:
    /// from "DataContext set + activated" to "grid laid out and rendered" (click→interactive), plus a
    /// scroll re-layout, at 20 / 150 / 500 employees. Drives the actual AttendanceMatrixView +
    /// AttendanceMatrixViewModel against a temp seeded DB on an STA thread. Read-only w.r.t. the product.
    /// </summary>
    [TestFixture, Explicit, Apartment(ApartmentState.STA)]
    public sealed class AttendanceUiPerfHarness
    {
        [OneTimeSetUp]
        public void EnsureAppResources()
        {
            if (Application.Current == null)
            {
                var app = new OptiPaie.Desktop.App();
                typeof(OptiPaie.Desktop.App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
        }

        [Test]
        public void Measure_Open_And_Scroll_At_20_150_500()
        {
            WarmUpWpf(); // absorb one-time WPF/JIT cold-start so each measure reflects steady-state
            foreach (int n in new[] { 20, 150, 500 }) MeasureOne(n);
        }

        private static void WarmUpWpf()
        {
            var dg = new DataGrid { ItemsSource = new[] { new { A = 1 }, new { A = 2 } } };
            var w = new Window { Content = dg, Width = 400, Height = 300, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            w.Measure(new Size(400, 300)); w.Arrange(new Rect(0, 0, 400, 300)); w.UpdateLayout();
            Render(w, 400, 300);
            w.Content = null;
        }

        private void MeasureOne(int employeeCount)
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-att-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            IUnitOfWorkFactory uow = new UnitOfWorkFactory(factory);

            var companies = new CompanyService(uow, new CompanyValidator());
            var employees = new EmployeeService(uow, new EmployeeValidator());
            var attendance = new AttendanceService(uow);

            long companyId = companies.Create(new Company { NameFr = "SARL UI", Nif = "000000000000000" }).Value;
            var ids = new List<long>();
            for (int i = 1; i <= employeeCount; i++)
                ids.Add(employees.Create(new Employee
                {
                    CompanyId = companyId, LastNameFr = "EMP" + i.ToString("000"), FirstNameFr = "Prenom", Poste = "Agent",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(DateTime.Today.Year - 1, 1, 1), BaseSalary = 40000m, IsActive = true
                }).Value);

            // A full past month of attendance (realistic density).
            DateTime refMonth = DateTime.Today.AddMonths(-2);
            var entries = new List<AttendanceDayStatus>();
            foreach (long id in ids)
                for (int d = 1; d <= DateTime.DaysInMonth(refMonth.Year, refMonth.Month); d++)
                {
                    var day = new DateTime(refMonth.Year, refMonth.Month, d);
                    if (day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday) continue;
                    entries.Add(new AttendanceDayStatus(id, day, AttendanceStatus.Present));
                }
            attendance.SetDayStatusBulk(entries);

            AppServices services = ComposeMinimal(companies, employees, attendance);
            services.CompanyContext.Reload();
            services.CompanyContext.Active = companies.Get(companyId);

            // --- VM activate (data load + E×D cell-VM build) ---
            var vm = new AttendanceMatrixViewModel(services);
            vm.SelectedYear = refMonth.Year;
            vm.SelectedMonth = refMonth.Month;
            var swVm = Stopwatch.StartNew();
            vm.OnActivated();
            swVm.Stop();

            // --- view + first full layout (BuildColumns XamlReader.Parse + grid realize) ---
            var swOpen = Stopwatch.StartNew();
            var view = new AttendanceMatrixView { DataContext = vm };
            var window = new Window { Content = view, Width = 1400, Height = 850, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            window.Measure(new Size(1400, 850));
            window.Arrange(new Rect(0, 0, 1400, 850));
            window.UpdateLayout();
            Render(window, 1400, 850); // force cell realization
            swOpen.Stop();

            // --- scroll re-layout (realize a new band of rows) ---
            long scrollMs = -1;
            ScrollViewer sv = FindScrollViewer(view);
            if (sv != null)
            {
                var swScroll = Stopwatch.StartNew();
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
                window.UpdateLayout();
                Render(window, 1400, 850);
                swScroll.Stop();
                scrollMs = swScroll.ElapsedMilliseconds;
            }

            TestContext.Out.WriteLine(
                employeeCount + " employees : activate(data+cellVMs)=" + swVm.ElapsedMilliseconds +
                " ms · open(view+layout+render)=" + swOpen.ElapsedMilliseconds +
                " ms · CLICK→INTERACTIVE≈" + (swVm.ElapsedMilliseconds + swOpen.ElapsedMilliseconds) +
                " ms · scroll-to-bottom relayout=" + scrollMs + " ms · rows=" + vm.Rows.Count + " cols=" + (vm.DayCount + 5));

            window.Content = null;
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static void Render(Window w, int width, int height)
        {
            try
            {
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(w);
            }
            catch { /* rendering is a best-effort realization trigger */ }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                ScrollViewer hit = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Builds an AppServices with only the three services the matrix uses; everything else null.</summary>
        private static AppServices ComposeMinimal(CompanyService companies, EmployeeService employees, AttendanceService attendance)
        {
            ConstructorInfo ctor = typeof(AppServices).GetConstructors().Single();
            ParameterInfo[] ps = ctor.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                switch (ps[i].Name)
                {
                    case "companies": args[i] = companies; break;
                    case "employees": args[i] = employees; break;
                    case "attendance": args[i] = attendance; break;
                    default: args[i] = null; break;
                }
            }
            return (AppServices)ctor.Invoke(args);
        }
    }
}
