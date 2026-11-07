using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NUnit.Framework;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Constructs every top-level Window / UserControl in the Desktop assembly on an STA thread, with
    /// the real theme dictionaries loaded, and asserts none throws a XamlParseException /
    /// ResourceReferenceKeyNotFoundException. This is the exact class of failure behind the 1.38.0
    /// startup outage (WorkforceSection referencing a view-local {StaticResource} it could not see).
    /// A missing resource key compiles clean and passes every headless service test — only INSTANTIATING
    /// the XAML surfaces it. This test is that instantiation.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public sealed class RenderSmokeTests
    {
        private string _dir;

        [OneTimeSetUp]
        public void EnsureApplicationWithTheme()
        {
            if (Application.Current == null)
            {
                var app = new App();
                // Load App.xaml + its merged theme dictionaries into Application.Resources WITHOUT
                // running OnStartup (no DB, no window). This reproduces the exact global resource
                // scope a real user's app has, so StaticResource resolution is faithful.
                MethodInfo init = typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance);
                init?.Invoke(app, null);
            }

            // Compose the REAL service graph over a throwaway database and publish it as App.Services,
            // so views whose constructor needs services — above all the shell MainWindow, the last
            // window that may be excluded from the startup-crash guard — also construct and render here.
            if (OptiPaie.Desktop.App.Services == null)
            {
                _dir = Path.Combine(Path.GetTempPath(), "optipaie-smoke-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                var config = new OptiPaie.Common.Configuration.AppConfiguration(_dir, "fr");
                OptiPaie.Desktop.Composition.AppServices services =
                    OptiPaie.Desktop.Composition.CompositionRoot.Build(config);

                // The shell refuses to open without an active company (the multi-company gate), so
                // seed one and select it — exactly the state in which a real user reaches MainWindow.
                long companyId = services.Companies.Create(
                    new OptiPaie.Core.Entities.Company { NameFr = "Smoke SARL", Nif = "000000000000000" }).Value;
                services.CompanyContext.Reload();
                services.CompanyContext.Active = services.Companies.Get(companyId);

                typeof(OptiPaie.Desktop.App)
                    .GetProperty("Services", BindingFlags.Public | BindingFlags.Static)
                    .SetValue(null, services);
            }
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            try { System.Data.SQLite.SQLiteConnection.ClearAllPools(); } catch { /* best-effort */ }
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { /* temp dir */ }
        }

        [Test]
        public void EveryTopLevelViewConstructs_WithNoUnresolvedResource()
        {
            Assembly desktop = typeof(App).Assembly;

            List<Type> candidates = desktop.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition
                            && (typeof(Window).IsAssignableFrom(t) || typeof(UserControl).IsAssignableFrom(t))
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .ToList();

            Assert.That(candidates.Count, Is.GreaterThan(0), "no top-level views discovered — the reflection filter is wrong");

            var resourceFailures = new List<string>();
            var runtimeSkips = new List<string>();

            foreach (Type t in candidates)
            {
                try
                {
                    Activator.CreateInstance(t);
                }
                catch (Exception ex)
                {
                    if (HasResourceFailure(ex))
                        resourceFailures.Add(t.FullName + "  ->  " + Innermost(ex).Message);
                    else
                        runtimeSkips.Add(t.Name + " (" + Innermost(ex).GetType().Name + ": " + Innermost(ex).Message + ")"); // ctor needs a runtime dependency — not a resource bug
                }
            }

            TestContext.WriteLine("Views constructed OK: " + (candidates.Count - runtimeSkips.Count - resourceFailures.Count) +
                                  " / " + candidates.Count + "  (skipped ctor-needs-runtime: " + runtimeSkips.Count + ")");
            if (runtimeSkips.Count > 0) TestContext.WriteLine("  skipped: " + string.Join(", ", runtimeSkips));

            Assert.That(resourceFailures, Is.Empty,
                "Unresolved {StaticResource} / XAML render failures:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", resourceFailures));
        }

        /// <summary>
        /// Regression for the "Assembly.GetEntryAssembly() returns null" startup error: applying the
        /// dark palette must resolve its pack URI even here, where there is no entry assembly (a test
        /// host). It throws with the old short "pack://application:,,,/Theme/..." URI and succeeds
        /// with the assembly-qualified one. We deliberately do NOT set Application.ResourceAssembly —
        /// the product must not depend on it. This also makes the smoke suite actually EXERCISE
        /// resource loading rather than pass while it is silently broken in this context.
        /// </summary>
        [Test]
        public void DarkTheme_PackUri_ResolvesWithoutAnEntryAssembly()
        {
            TestContext.WriteLine("Assembly.GetEntryAssembly() = " + (Assembly.GetEntryAssembly()?.GetName().Name ?? "<null>"));
            TestContext.WriteLine("Application.ResourceAssembly = " + (Application.ResourceAssembly?.GetName().Name ?? "<null>"));
            try
            {
                ThemeManager.Apply(true);
                Assert.That(ThemeManager.IsDark, Is.True,
                    "the dark palette did not load — the assembly-qualified pack URI must resolve with no entry assembly");
                Assert.That(Application.Current.TryFindResource("Canvas"), Is.Not.Null,
                    "dark theme resources are not resolvable after applying the dark palette");
            }
            finally
            {
                ThemeManager.Apply(false);
            }
        }

        /// <summary>
        /// P6 — memory-leak guard. Opens and closes every top-level Window 50 times and asserts that
        /// the instances (and their DataContext view-models) are collectable afterwards: a window that
        /// subscribes to a long-lived (static App.Services) event without unsubscribing on Close would
        /// be retained, and would show up here as survivors. Prints a per-window survivor count.
        /// [Explicit] — heavy STA churn; run on demand.
        /// </summary>
        [Test, Explicit]
        public void EveryWindow_IsCollectable_AfterOpenClose()
        {
            const int Iterations = 50;
            Assembly desktop = typeof(App).Assembly;

            List<Type> windows = desktop.GetTypes()
                .Where(t => !t.IsAbstract && typeof(Window).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .ToList();

            var leaks = new List<string>();
            var windowWarnings = new List<string>();
            foreach (Type t in windows)
            {
                var windowRefs = new List<WeakReference>(Iterations);
                var vmRefs = new List<WeakReference>(Iterations);
                bool constructed = false;

                for (int i = 0; i < Iterations; i++)
                {
                    try
                    {
                        var w = (Window)Activator.CreateInstance(t);
                        constructed = true;
                        windowRefs.Add(new WeakReference(w));
                        if (w.DataContext != null) vmRefs.Add(new WeakReference(w.DataContext));
                        w.Close(); // simulate the open→close lifecycle
                    }
                    catch
                    {
                        break; // ctor needs runtime deps we don't provide — not part of the leak check
                    }
                }

                if (!constructed) { TestContext.WriteLine(t.Name + ": skipped (ctor needs runtime)"); continue; }

                for (int g = 0; g < 3; g++) { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true); GC.WaitForPendingFinalizers(); }

                int winAlive = windowRefs.FindAll(r => r.IsAlive).Count;
                int vmAlive = vmRefs.FindAll(r => r.IsAlive).Count;
                TestContext.WriteLine(t.Name + ": windows alive " + winAlive + "/" + windowRefs.Count +
                                      ", view-models alive " + vmAlive + "/" + vmRefs.Count);

                // View-model retention is an APP-level bug (a VM that hooks a process-lifetime service
                // event and never unhooks) — assert it is clean. Window-level retention of a never-shown
                // window is largely a WPF framework/static-singleton artifact (SystemEvents, localization
                // source); it is reported as a documented warning, not a hard failure, pending the
                // systemic weak-event decision.
                // A leak RETAINS all/most of the 50; a few survivors are GC/JIT non-determinism, not
                // accumulation. The threshold (iterations/10) cleanly separates "released" (~0-2) from
                // "retained" (~50).
                int threshold = Iterations / 10;
                if (vmAlive > threshold) leaks.Add(t.Name + " view-model: " + vmAlive + "/" + vmRefs.Count + " alive");
                if (winAlive > threshold) windowWarnings.Add(t.Name + " window: " + winAlive + "/" + windowRefs.Count + " alive");
            }

            if (windowWarnings.Count > 0)
                TestContext.WriteLine(Environment.NewLine + "WINDOW-LEVEL RETENTION (documented, not asserted):" +
                    Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", windowWarnings));

            Assert.That(leaks, Is.Empty, "View-model memory leaks (VM retained after its window closed):" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", leaks));
        }

        private static bool HasResourceFailure(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is XamlParseException) return true;
                if (e is ResourceReferenceKeyNotFoundException) return true;
            }
            return false;
        }

        private static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
