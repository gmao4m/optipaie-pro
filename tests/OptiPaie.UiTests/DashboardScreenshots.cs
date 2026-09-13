using System;
using System.IO;
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
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Render harness (NOT a pass/fail test — [Explicit]). Renders the real DashboardView, bound to a
    /// real DashboardViewModel over the app's actual database, to PNGs in docs/screenshots-dashboard/
    /// for every French/Arabic × light/dark combination, so the redesigned screen can be inspected.
    ///   dotnet test --filter "FullyQualifiedName~DashboardScreenshots" -c Debug
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class DashboardScreenshots
    {
        private const double Width = 1360;
        private const double Scale = 1.5; // supersample for crisp text

        private static string _bc;
        private static void Bc(string s) { try { File.AppendAllText(_bc, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine); } catch { } }

        [Test]
        public void Render_AllLanguageAndThemeCombinations()
        {
            _bc = Path.Combine(Path.GetTempPath(), "dash-shot-breadcrumbs.log");
            try { File.Delete(_bc); } catch { }
            Bc("start");

            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            Bc("app+resources ready");
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            Bc("before CompositionRoot.Build");
            AppServices services = CompositionRoot.Build();
            Bc("after CompositionRoot.Build");
            TranslationSource.Instance.Attach(services.Localization);
            services.CompanyContext.Reload(); // pick the first company
            Bc("company reloaded, activeId=" + services.CompanyContext.ActiveId);

            string outDir = Path.Combine(FindRepoRoot(), "docs", "screenshots-dashboard");
            Directory.CreateDirectory(outDir);

            foreach (string lang in new[] { "fr", "ar" })
            {
                foreach (bool dark in new[] { false, true })
                {
                    Bc("combo " + lang + "/" + (dark ? "dark" : "light") + " begin");
                    try
                    {
                        services.Localization.SetLanguage(lang);
                        ThemeManager.Apply(dark);
                        Bc("  theme+lang set");

                        // Load FIRST, then bind: bindings pull the already-final values synchronously
                        // during layout, so no dispatcher pump is needed (PushFrame blocks headlessly).
                        Bc("  load (sync)");
                        var vm = new DashboardViewModel(services, _ => { });
                        vm.LoadSynchronouslyForRender();
                        Bc("  loaded");

                        var view = new DashboardView { DataContext = vm };
                        var host = new Border
                        {
                            Background = (Brush)Application.Current.Resources["Canvas"],
                            FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                            Child = view
                        };

                        host.Measure(new Size(Width, double.PositiveInfinity));
                        double height = Math.Max(600, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, Width, height));
                        host.UpdateLayout();
                        host.Measure(new Size(Width, double.PositiveInfinity)); // 2nd pass settles wrap/auto heights
                        height = Math.Max(600, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, Width, height));
                        host.UpdateLayout();

                        var rtb = new RenderTargetBitmap((int)(Width * Scale), (int)(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                        rtb.Render(host);

                        string file = Path.Combine(outDir, $"dashboard-{lang}-{(dark ? "dark" : "light")}.png");
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                        Bc("  wrote " + file + " h=" + (int)height);
                        TestContext.WriteLine("Wrote " + file + "  (" + (int)(Width * Scale) + "×" + (int)(height * Scale) + ")");
                    }
                    catch (Exception ex)
                    {
                        Bc("  ERROR " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
