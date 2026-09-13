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
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;
using OptiPaie.Services;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Render harness (NOT a pass/fail test — [Explicit]). Renders the real LeaveSettingsWindow bound
    /// to a real LeaveSettingsViewModel over an isolated temp database whose single company has TWO
    /// per-company regulatory options enabled — so the produced PNGs show those boxes CHECKED, proving
    /// the dialog reads the options scoped to the company. FR/AR × light/dark, written to the temp
    /// folder. This is the visual companion to the manual verification steps for the scoping fix.
    ///   dotnet test tests/OptiPaie.UiTests/OptiPaie.UiTests.csproj -c Debug --filter "FullyQualifiedName~LeaveSettingsScreenshots"
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class LeaveSettingsScreenshots
    {
        [Test]
        public void Render_LeaveSettingsDialog_FrAr_LightDark()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            AppServices services = CompositionRoot.Build();
            TranslationSource.Instance.Attach(services.Localization);

            // Isolated data: a temp DB, one company with two regulatory options ON.
            string outDir = Path.Combine(Path.GetTempPath(), "optipaie-leave-settings-shots");
            Directory.CreateDirectory(outDir);
            string dbDir = Path.Combine(outDir, "db-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dbDir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(dbDir, "s.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            var uowf = new UnitOfWorkFactory(factory);
            var leave = new LeaveService(uowf);

            long companyId;
            using (IUnitOfWork uow = uowf.Create())
            {
                uow.BeginTransaction();
                companyId = uow.Companies.Insert(new Company { NameFr = "SARL Démo", Nif = "000000000000000" });
                uow.Commit();
            }
            var seeded = leave.GetSettings(companyId);
            seeded.ExcludeHolidays = true;
            seeded.ReferenceJulyToJune = true;
            leave.SaveSettings(companyId, seeded);

            const double Width = 460, Scale = 1.5;
            foreach (string lang in new[] { "fr", "ar" })
            {
                foreach (bool dark in new[] { false, true })
                {
                    try
                    {
                        services.Localization.SetLanguage(lang);
                        ThemeManager.Apply(dark);

                        var vm = new LeaveSettingsViewModel(leave, companyId);
                        var window = new LeaveSettingsWindow { DataContext = vm };
                        var content = (FrameworkElement)window.Content;
                        window.Content = null; // detach so it can be hosted + rendered without showing the window

                        var host = new Border
                        {
                            Background = (Brush)Application.Current.Resources["Canvas"],
                            FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                            Child = content
                        };
                        host.Measure(new Size(Width, double.PositiveInfinity));
                        double h = Math.Max(560, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();
                        host.Measure(new Size(Width, double.PositiveInfinity)); h = Math.Max(560, host.DesiredSize.Height);
                        host.Arrange(new Rect(0, 0, Width, h)); host.UpdateLayout();

                        var rtb = new RenderTargetBitmap((int)(Width * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                        rtb.Render(host);
                        string file = Path.Combine(outDir, $"leave-settings-{lang}-{(dark ? "dark" : "light")}.png");
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = new FileStream(file, FileMode.Create)) enc.Save(fs);
                        TestContext.WriteLine("Wrote " + file);
                    }
                    catch (Exception ex)
                    {
                        TestContext.WriteLine("ERROR " + lang + "/" + (dark ? "dark" : "light") + ": " + ex.GetType().Name + " " + ex.Message);
                    }
                }
            }
        }
    }
}
