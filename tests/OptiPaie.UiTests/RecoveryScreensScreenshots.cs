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
    /// Render harness (NOT a pass/fail test — [Explicit]) for COMMIT B: the recovery screens, all the
    /// way to the screen. It renders, to real PNGs under docs/screenshots-recovery, the three screens
    /// the owner asked to see, in FR and AR:
    /// <list type="bullet">
    /// <item>the recovery code shown AT OWNER CREATION (OwnerSetupWindow, save step);</item>
    /// <item>the recovery code BACKFILLED at sign-in (RecoveryCodeWindow);</item>
    /// <item>RECOVERY used to reset a password (RecoveryWindow: reset, success, and the lost-both
    /// fingerprint fallback).</item>
    /// </list>
    ///   dotnet test tests/OptiPaie.UiTests/OptiPaie.UiTests.csproj -c Debug --filter "FullyQualifiedName~RecoveryScreensScreenshots"
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class RecoveryScreensScreenshots
    {
        private const string SampleCode = "H8K2M-NPQR3-VWX4Y-Z567A";
        private const string SampleFingerprint = "A1B2-C3D4-E5F6-7890-1A2B";

        [Test]
        public void Render_AllRecoveryScreens_FrAr()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            AppServices services = CompositionRoot.Build();
            TranslationSource.Instance.Attach(services.Localization);

            string outDir = Path.Combine(FindRepoRoot(), "docs", "screenshots-recovery");
            Directory.CreateDirectory(outDir);

            foreach (string lang in new[] { "fr", "ar" })
            {
                services.Localization.SetLanguage(lang);
                ThemeManager.Apply(false);
                bool rtl = services.Localization.IsRightToLeft;

                // 1. Owner creation — step 1 (create the owner).
                var setupCreate = new OwnerSetupViewModel(services);
                Render(new OwnerSetupWindow { DataContext = setupCreate }, rtl, outDir, $"owner-setup-create-{lang}.png");

                // 2. Owner creation — step 2 (the recovery code shown AT creation).
                var setupSave = new OwnerSetupViewModel(services);
                SetField(setupSave, "_recoveryCode", SampleCode);
                SetField(setupSave, "_isSaveStep", true);
                Render(new OwnerSetupWindow { DataContext = setupSave }, rtl, outDir, $"owner-setup-code-{lang}.png");

                // 3. Recovery code BACKFILLED at sign-in.
                var backfill = new RecoveryCodeViewModel(SampleCode);
                Render(new RecoveryCodeWindow { DataContext = backfill }, rtl, outDir, $"recovery-backfill-{lang}.png");

                // 4. Recovery screen — reset with the one-time code.
                var reset = new RecoveryViewModel(services);
                Render(new RecoveryWindow { DataContext = reset }, rtl, outDir, $"recovery-reset-{lang}.png");

                // 5. Recovery screen — success (fresh code after reset).
                var success = new RecoveryViewModel(services);
                SetField(success, "_newRecoveryCode", SampleCode);
                SetField(success, "_step", 1);
                Render(new RecoveryWindow { DataContext = success }, rtl, outDir, $"recovery-success-{lang}.png");

                // 6. Recovery screen — lost BOTH: install fingerprint + vendor procedure.
                var fallback = new RecoveryViewModel(services);
                SetField(fallback, "_fingerprint", SampleFingerprint);
                SetField(fallback, "_step", 2);
                Render(new RecoveryWindow { DataContext = fallback }, rtl, outDir, $"recovery-fallback-{lang}.png");
            }
        }

        private static void Render(Window window, bool rtl, string outDir, string fileName)
        {
            const double Width = 480, Scale = 1.4;
            try
            {
                var content = (FrameworkElement)window.Content;
                object dataContext = window.DataContext;
                window.Content = null; // detach so it can be hosted + rendered without showing the window
                content.DataContext = dataContext; // keep the DataContext the detached content lost from the Window

                var host = new Border
                {
                    Background = (Brush)Application.Current.Resources["Canvas"],
                    FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    Child = content
                };
                host.Measure(new Size(Width, double.PositiveInfinity));
                double h = Math.Max(520, host.DesiredSize.Height);
                host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();
                host.Measure(new Size(Width, double.PositiveInfinity)); h = Math.Max(520, host.DesiredSize.Height);
                host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();

                var rtb = new RenderTargetBitmap((int)(Width * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                rtb.Render(host);
                string file = Path.Combine(outDir, fileName);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                TestContext.WriteLine("Wrote " + file);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine("ERROR " + fileName + ": " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fi, Is.Not.Null, "field not found: " + field);
            fi.SetValue(target, value);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
        }
    }
}
