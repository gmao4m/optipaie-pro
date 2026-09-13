using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NUnit.Framework;
using OptiPaie.Desktop;

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
                        runtimeSkips.Add(t.Name); // ctor needs a runtime dependency (services/DataContext) — not a resource bug
                }
            }

            TestContext.WriteLine("Views constructed OK: " + (candidates.Count - runtimeSkips.Count - resourceFailures.Count) +
                                  " / " + candidates.Count + "  (skipped ctor-needs-runtime: " + runtimeSkips.Count + ")");
            if (runtimeSkips.Count > 0) TestContext.WriteLine("  skipped: " + string.Join(", ", runtimeSkips));

            Assert.That(resourceFailures, Is.Empty,
                "Unresolved {StaticResource} / XAML render failures:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", resourceFailures));
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
