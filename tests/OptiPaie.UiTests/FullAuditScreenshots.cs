using System;
using System.Collections.Generic;
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
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Full-audit render harness ([Explicit]). Renders every main module screen, bound to its real
    /// view-model over the app's actual database, to PNGs in docs/audit-screenshots/ for French and
    /// Arabic — so every screen can be LOOKED at, not just read. Not a pass/fail test.
    ///   dotnet test --filter "FullyQualifiedName~FullAuditScreenshots" -c Debug
    ///
    /// RELIABLE FOR: layout, spacing, alignment, empty states, button placement, and — thanks to the
    /// off-screen Window host + dispatcher Pump() — DataGrid star-column widths (which collapse under a
    /// bare RenderTargetBitmap).
    /// KNOWN LIMITATION: on a few complex view-models the Arabic labels that are built as VM string
    /// properties (not live loc: bindings) can lag one language behind and render in French. This is a
    /// HARNESS timing artifact, not a product defect — the product renders those screens correctly in
    /// Arabic (verified by the per-screen DashboardScreenshots harness). For authoritative Arabic-label
    /// checks, use a dedicated per-view harness like DashboardScreenshots that reloads after SetLanguage.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class FullAuditScreenshots
    {
        private const double Width = 1360;
        private const double Scale = 1.25;

        // key, view-model type name, view type name
        private static readonly (string Key, string Vm, string View)[] Screens =
        {
            ("dashboard",    "DashboardViewModel",       "DashboardView"),
            ("employees",    "EmployeesViewModel",       "EmployeesView"),
            ("companies",    "CompaniesViewModel",       "CompaniesView"),
            ("payroll",      "PayrollViewModel",         "PayrollView"),
            ("archive",      "ArchiveViewModel",         "ArchiveView"),
            ("loans",        "LoanViewModel",            "LoanView"),
            ("attendance",   "AttendanceMatrixViewModel","AttendanceMatrixView"),
            ("leave",        "LeaveViewModel",           "LeaveView"),
            ("contracts",    "ContractViewModel",        "ContractView"),
            ("performance",  "PerformanceViewModel",     "PerformanceView"),
            ("assets",       "AssetViewModel",           "AssetView"),
            ("training",     "TrainingViewModel",        "TrainingView"),
            ("ats",          "AtsViewModel",             "AtsView"),
            ("certificates", "CertificateViewModel",     "CertificateView"),
            ("cnas",         "CnasHubViewModel",         "CnasHubView"),
            ("attestations", "AttestationsViewModel",    "AttestationsView"),
            ("reports",      "ReportsViewModel",         "ReportsView"),
            ("settings",     "SettingsViewModel",        "SettingsView"),
        };

        [Test]
        public void Render_EveryModule_FrAndAr()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            AppServices services = CompositionRoot.Build();
            TranslationSource.Instance.Attach(services.Localization);
            services.CompanyContext.Reload();

            Assembly asm = typeof(App).Assembly;
            string outDir = Path.Combine(FindRepoRoot(), "docs", "audit-screenshots");
            Directory.CreateDirectory(outDir);
            ThemeManager.Apply(false); // light only (the audit is about layout/RTL/consistency)

            var log = new List<string>();
            foreach ((string key, string vmName, string viewName) in Screens)
            {
                foreach (string lang in new[] { "fr", "ar" })
                {
                    try
                    {
                        services.Localization.SetLanguage(lang);

                        object vm = BuildVm(asm, vmName, services);
                        if (vm == null) { log.Add($"{key}/{lang}: VM {vmName} not built"); continue; }
                        Activate(vm);

                        Type viewType = asm.GetTypes().FirstOrDefault(t => t.Name == viewName);
                        if (viewType == null) { log.Add($"{key}/{lang}: view {viewName} not found"); continue; }
                        var view = (FrameworkElement)Activator.CreateInstance(viewType);
                        view.DataContext = vm;

                        var host = new Border
                        {
                            Background = (Brush)Application.Current.Resources["Canvas"],
                            FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                            Child = view
                        };

                        // Host in a REAL off-screen window: WPF then gives the tree a live presentation
                        // source, so DataGrid star columns compute their widths (they collapse to a
                        // minimum when measured with no source — a RenderTargetBitmap-only artifact).
                        var win = new Window
                        {
                            Width = Width + 2, Height = 980, Left = -10000, Top = -10000,
                            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                            Content = host
                        };
                        win.Show();
                        Pump();                 // let the DataGrid run its deferred column-width layout
                        win.UpdateLayout();
                        host.Measure(new Size(Width, double.PositiveInfinity));
                        double h = Math.Max(620, host.DesiredSize.Height);
                        win.Height = h + 2;
                        win.UpdateLayout();
                        host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();
                        Pump();

                        var rtb = new RenderTargetBitmap((int)(Width * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                        rtb.Render(host);
                        string file = Path.Combine(outDir, $"{key}-{lang}.png");
                        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                        win.Close();
                        log.Add($"{key}/{lang}: OK {(int)(Width * Scale)}x{(int)(h * Scale)}");
                    }
                    catch (Exception ex)
                    {
                        log.Add($"{key}/{lang}: ERROR {ex.GetType().Name}: {Innermost(ex).Message}");
                    }
                }
            }

            File.WriteAllLines(Path.Combine(outDir, "_render-log.txt"), log);
            foreach (string l in log) TestContext.WriteLine(l);
        }

        private static object BuildVm(Assembly asm, string vmName, AppServices services)
        {
            Type t = asm.GetTypes().FirstOrDefault(x => x.Name == vmName);
            if (t == null) return null;
            Action<string> nav = _ => { };
            foreach (ConstructorInfo c in t.GetConstructors())
            {
                ParameterInfo[] p = c.GetParameters();
                try
                {
                    if (p.Length == 1 && p[0].ParameterType.IsInstanceOfType(services))
                        return c.Invoke(new object[] { services });
                    if (p.Length == 2 && p[0].ParameterType.IsInstanceOfType(services) && typeof(Delegate).IsAssignableFrom(p[1].ParameterType))
                        return c.Invoke(new object[] { services, nav });
                }
                catch { }
            }
            return null;
        }

        private static void Activate(object vm)
        {
            try
            {
                MethodInfo sync = vm.GetType().GetMethod("LoadSynchronouslyForRender", BindingFlags.Public | BindingFlags.Instance);
                if (sync != null) { sync.Invoke(vm, null); return; }
                MethodInfo act = vm.GetType().GetMethod("OnActivated", BindingFlags.Public | BindingFlags.Instance);
                act?.Invoke(vm, null);
            }
            catch { }
        }

        /// <summary>Pumps the dispatcher to Loaded/Render priority so WPF runs deferred layout
        /// (DataGrid computes star column widths only on a real render pass).</summary>
        private static void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        private static Exception Innermost(Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; return ex; }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
