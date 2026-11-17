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
    /// Dialog/window audit render harness ([Explicit]). Constructs EVERY top-level Window in the
    /// Desktop assembly (edit windows, wizards, pickers, confirmations), gives it a best-effort real
    /// view-model where one can be built, and renders its content to PNGs in docs/audit-dialogs/ for
    /// French and Arabic. The log records each window's final width×height so anything that would not
    /// fit a 1366×768 laptop (content taller than ~720px) is flagged without opening the image.
    ///   dotnet test --filter "FullyQualifiedName~DialogAuditScreenshots" -c Debug
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class DialogAuditScreenshots
    {
        private const double Scale = 1.25;
        private const double FitHeight = 720; // usable height on a 1366×768 laptop after chrome

        [Test]
        public void Render_EveryWindow_FrAndAr()
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
            // Publish services so windows whose ctor/code-behind touches App.Services construct.
            typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, services);

            Assembly asm = typeof(App).Assembly;
            ThemeManager.Apply(false);

            List<Type> windows = asm.GetTypes()
                .Where(t => !t.IsAbstract && typeof(Window).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name)
                .ToList();

            string outDir = Path.Combine(FindRepoRoot(), "docs", "audit-dialogs");
            Directory.CreateDirectory(outDir);

            var log = new List<string>();
            foreach (Type wt in windows)
            {
                foreach (string lang in new[] { "fr", "ar" })
                {
                    try
                    {
                        services.Localization.SetLanguage(lang);
                        Window win;
                        try { win = (Window)Activator.CreateInstance(wt); }
                        catch (Exception ex) { if (lang == "fr") log.Add($"{wt.Name}: CTOR-FAIL {Innermost(ex).GetType().Name}: {Innermost(ex).Message}"); continue; }

                        if (win.DataContext == null)
                        {
                            object vm = TryBuildVm(asm, wt.Name, services);
                            if (vm != null) { win.DataContext = vm; Activate(vm); }
                        }

                        // Show once (LTR) so the window builds its VM/layout, then DETACH the content and
                        // host it in a Border with the flow direction — the known-good RTL pattern. Setting
                        // FlowDirection on the window itself and rendering its content double-applies the RTL
                        // mirror transform and reverses the text (a harness artifact, not a product bug).
                        win.WindowStyle = WindowStyle.SingleBorderWindow;
                        win.Left = -10000; win.Top = -10000; win.ShowInTaskbar = false; win.ShowActivated = false;
                        win.Show();
                        Pump();
                        win.UpdateLayout();

                        double w = double.IsNaN(win.Width) || win.Width < 50 ? Math.Max(360, win.ActualWidth) : win.Width;
                        var content = win.Content as FrameworkElement;
                        win.Content = null; // detach so it can be hosted + rendered without the window's own RTL transform

                        var host = new Border
                        {
                            Background = (Brush)Application.Current.Resources["Canvas"],
                            FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                            Width = w,
                            Child = content
                        };
                        host.Measure(new Size(w, double.PositiveInfinity));
                        double h = Math.Max(200, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();
                        Pump();
                        host.Measure(new Size(w, double.PositiveInfinity)); h = Math.Max(200, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();

                        var rtb = new RenderTargetBitmap((int)(w * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                        rtb.Render(host);
                        string file = Path.Combine(outDir, $"{wt.Name}-{lang}.png");
                        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                        win.Close();

                        string fit = h > FitHeight ? $"  ⚠TALL({(int)h}>720)" : "";
                        log.Add($"{wt.Name}/{lang}: {(int)w}x{(int)h}{fit}{(win.DataContext == null ? "  (no VM)" : "")}");
                    }
                    catch (Exception ex)
                    {
                        log.Add($"{wt.Name}/{lang}: ERROR {Innermost(ex).GetType().Name}: {Innermost(ex).Message}");
                    }
                }
            }

            File.WriteAllLines(Path.Combine(outDir, "_dialog-log.txt"), log);
            foreach (string l in log) TestContext.WriteLine(l);
        }

        /// <summary>Best-effort VM for a window named XxxWindow → XxxViewModel, trying (services) and
        /// (services, Action&lt;string&gt;) ctors. Returns null when nothing simple fits (window renders empty).</summary>
        private static object TryBuildVm(Assembly asm, string windowName, AppServices services)
        {
            string vmName = windowName.EndsWith("Window") ? windowName.Substring(0, windowName.Length - 6) + "ViewModel" : null;
            if (vmName == null) return null;
            Type t = asm.GetTypes().FirstOrDefault(x => x.Name == vmName);
            if (t == null) return null;
            Action<string> nav = _ => { };
            foreach (ConstructorInfo c in t.GetConstructors())
            {
                ParameterInfo[] p = c.GetParameters();
                try
                {
                    if (p.Length == 1 && p[0].ParameterType.IsInstanceOfType(services)) return c.Invoke(new object[] { services });
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
                vm.GetType().GetMethod("OnActivated", BindingFlags.Public | BindingFlags.Instance)?.Invoke(vm, null);
            }
            catch { }
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
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
