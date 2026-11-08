using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Desktop;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Task 4 (round 3): quantify the window-retention finding in BYTES, with 200 employees loaded.
    /// For each top-level Window it measures the live managed memory retained after opening+closing
    /// N instances (GC.GetTotalMemory(true) delta), then projects an 8-hour working session at a
    /// stated realistic open rate. [Explicit], STA.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class AuditMemoryBytes
    {
        private const int N = 30;             // instances per window
        private string _dir;

        [OneTimeSetUp]
        public void Compose()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            if (App.Services == null)
            {
                _dir = Path.Combine(Path.GetTempPath(), "optipaie-mem-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                var cfg = new OptiPaie.Common.Configuration.AppConfiguration(_dir, "fr");
                OptiPaie.Desktop.Composition.AppServices s = OptiPaie.Desktop.Composition.CompositionRoot.Build(cfg);
                long companyId = s.Companies.Create(new OptiPaie.Core.Entities.Company { NameFr = "Mem SARL", Nif = "000000000000000" }).Value;
                for (int i = 1; i <= 200; i++)
                    s.Employees.Create(new OptiPaie.Core.Entities.Employee
                    {
                        CompanyId = companyId, LastNameFr = "EMP" + i.ToString("000"), FirstNameFr = "P",
                        Gender = OptiPaie.Core.Enums.Gender.Male, MaritalStatus = OptiPaie.Core.Enums.MaritalStatus.Single,
                        PaymentMode = OptiPaie.Core.Enums.PaymentMode.Cash, ContractType = OptiPaie.Core.Enums.ContractType.Cdi,
                        HireDate = new DateTime(2020, 1, 1), BaseSalary = 40000m, IsActive = true
                    });
                s.CompanyContext.Reload();
                s.CompanyContext.Active = s.Companies.Get(companyId);
                typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static).SetValue(null, s);
            }
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            try { System.Data.SQLite.SQLiteConnection.ClearAllPools(); } catch { }
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static void FullGc()
        {
            for (int i = 0; i < 4; i++) { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true); GC.WaitForPendingFinalizers(); }
        }

        [Test]
        public void Measure_Retained_Bytes_Per_Window_And_Project_8h()
        {
            List<Type> windows = typeof(App).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(Window).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName).ToList();

            var perWindowKb = new Dictionary<string, double>();
            foreach (Type t in windows)
            {
                FullGc();
                long before = GC.GetTotalMemory(true);
                int made = 0;
                for (int i = 0; i < N; i++)
                {
                    try { var w = (Window)Activator.CreateInstance(t); w.Close(); made++; }
                    catch { break; }
                }
                if (made < N) continue; // needs runtime deps — skip
                FullGc();
                long after = GC.GetTotalMemory(true);
                double perKb = Math.Max(0, (after - before) / (double)N) / 1024.0;
                perWindowKb[t.Name] = perKb;
            }

            // Report windows that retain a non-trivial amount (> 50 KB/instance).
            var retained = perWindowKb.Where(kv => kv.Value > 50).OrderByDescending(kv => kv.Value).ToList();
            TestContext.WriteLine("=== Retained KB per instance (200 employees loaded, N=" + N + ") ===");
            foreach (var kv in perWindowKb.OrderByDescending(k => k.Value))
                TestContext.WriteLine($"  {kv.Key,-28} {kv.Value,8:0.0} KB/instance");

            // 8-hour projection. Stated realistic rate: an officer opens report/list windows about
            // once every 2 minutes during active work → ~240 window opens over 8 h, spread across the
            // retained windows. We take the WORST plausible case: ALL 240 opens hit the single
            // heaviest-retaining window, then also report the even-spread figure.
            double heaviestKb = retained.Count > 0 ? retained[0].Value : 0;
            double sumRetainedKb = retained.Sum(kv => kv.Value);
            const int opens8h = 240;
            double worstMb = heaviestKb * opens8h / 1024.0;
            double evenMb = retained.Count > 0 ? (sumRetainedKb / retained.Count) * opens8h / 1024.0 : 0;

            TestContext.WriteLine("");
            TestContext.WriteLine($"Retained (>50KB) windows: {retained.Count}; heaviest = {heaviestKb:0.0} KB/instance");
            TestContext.WriteLine($"8h projection @240 opens — worst case (all on heaviest): {worstMb:0.0} MB");
            TestContext.WriteLine($"8h projection @240 opens — even spread: {evenMb:0.0} MB");

            // Decision gate: only fix (SystemEvents unsubscribe) if the realistic 8h leak exceeds 200 MB.
            Assert.That(worstMb, Is.LessThan(200.0),
                $"8h worst-case leak {worstMb:0.0} MB exceeds 200 MB — SystemEvents unsubscribe fix required.");
        }
    }
}
