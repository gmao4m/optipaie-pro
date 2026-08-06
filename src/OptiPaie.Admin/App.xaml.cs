using System.Windows;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Views;

namespace OptiPaie.Admin
{
    public partial class App : Application
    {
        /// <summary>The shared Supabase client (authenticated after login).</summary>
        public static SupabaseAdminClient Api { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Force modern TLS so the owner login + REST calls to Supabase always negotiate
            // TLS 1.2+ (the .NET Framework default can fall back to 1.0/1.1, which Supabase
            // rejects, surfacing as a bogus connection failure).
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls13; } catch { }

            Api = SupabaseAdminClient.FromConfig();

            // Sign-in-once: keep the process alive while we try to resume a saved session,
            // then open straight to the console (or the login screen if there is none / it expired).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool restored = false;
            try { restored = await Api.TryRestoreSessionAsync(); } catch { restored = false; }

            if (restored)
            {
                var main = new Shell.MainWindow();
                MainWindow = main;
                main.Show();
            }
            else
            {
                new LoginWindow().Show();
            }

            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
    }
}
