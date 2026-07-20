using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using OptiPaie.App.Common;
using OptiPaie.App.Shell;
using OptiPaie.App.Wizard;
using OptiPaie.Common.Constants;

namespace OptiPaie.App
{
    /// <summary>
    /// Application entry point. Initialises DevExpress, builds the service graph via
    /// the composition root, applies the saved language and skin, installs global
    /// exception handlers and runs the main window.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            // GDI text path (TextRenderer/ClearType) — crisp, no GDI+ softening. DPI
            // awareness itself is declared once in app.manifest (PerMonitorV2 + System
            // fallback) and enabled in App.config; it must NOT be set here in code, or
            // a runtime call would assert a weaker (System) awareness over the manifest.
            Application.SetCompatibleTextRenderingDefault(false);

            // One consistent typeface across the whole product (Segoe UI on Vista+,
            // gracefully falling back to Tahoma / the system default on older machines).
            Font appFont = ResolveAppFont();
            WindowsFormsSettings.DefaultFont = appFont;
            WindowsFormsSettings.DefaultMenuFont = appFont;

            // MUST run before ANY WinForms/DevExpress control (splash, skin, forms) is
            // created, otherwise WinForms throws "Cannot change the thread exception mode
            // after controls have been created."
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            WindowsFormsSettings.AllowHoverAnimation = DevExpress.Utils.DefaultBoolean.True;

            var splash = new SplashForm();
            splash.Show();
            Application.DoEvents();

            AppServices services;
            try
            {
                services = CompositionRoot.Build();
            }
            catch (Exception ex)
            {
                splash.Close();
                XtraMessageBox.Show(
                    "Erreur d'initialisation de l'application :\r\n" + ex.Message,
                    "OptiPaie DZ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ApplyStartupPreferences(services);
            InstallGlobalExceptionHandlers(services);

            splash.Close();
            splash.Dispose();

            // First-run wizard: the app is unusable until a first company exists.
            if (services.Companies.GetAll().Count == 0)
            {
                using (var wizard = new FirstRunWizardForm(services))
                {
                    if (wizard.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                }
            }

            Application.Run(new MainForm(services));
        }

        /// <summary>
        /// Resolves the application typeface: Segoe UI when present (Windows Vista+),
        /// otherwise Tahoma, otherwise the generic sans-serif — so text is always
        /// rendered with a real, installed font and never a blurry substitute.
        /// </summary>
        private static Font ResolveAppFont()
        {
            foreach (string name in new[] { "Segoe UI", "Tahoma" })
            {
                try
                {
                    using (var probe = new Font(name, 9f))
                    {
                        if (string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return new Font(name, 9f);
                        }
                    }
                }
                catch
                {
                    // Try the next candidate.
                }
            }

            return new Font(FontFamily.GenericSansSerif, 9f);
        }

        private static void ApplyStartupPreferences(AppServices services)
        {
            string language = services.Settings.GetLanguage();
            if (string.IsNullOrWhiteSpace(language))
            {
                language = AppConstants.DefaultLanguage;
            }

            services.Localization.SetLanguage(language);

            try
            {
                // Single, always-readable light theme (ignores any previously saved skin).
                ThemeManager.Apply(ThemeManager.Light);
            }
            catch (Exception ex)
            {
                // A missing/renamed skin must never prevent startup.
                services.Logger.Warn("Could not apply theme: " + ex.Message);
            }
        }

        private static void InstallGlobalExceptionHandlers(AppServices services)
        {
            // The exception MODE is set in Main() before any control is created.
            // Here we only attach the handlers (which require the logger from services
            // and create no controls), so this can safely run after the services build.
            Application.ThreadException += (sender, e) =>
            {
                services.Logger.Error("Unhandled UI exception.", e.Exception);
                XtraMessageBox.Show(
                    "Une erreur inattendue est survenue.\r\n" + e.Exception.Message,
                    "OptiPaie DZ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                services.Logger.Error("Unhandled domain exception.", e.ExceptionObject as Exception);
            };
        }
    }
}
