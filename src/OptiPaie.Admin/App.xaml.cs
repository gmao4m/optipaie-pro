using System.Windows;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Views;

namespace OptiPaie.Admin
{
    public partial class App : Application
    {
        /// <summary>The shared Supabase client (authenticated after login).</summary>
        public static SupabaseAdminClient Api { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Force modern TLS so the owner login + REST calls to Supabase always negotiate
            // TLS 1.2+ (the .NET Framework default can fall back to 1.0/1.1, which Supabase
            // rejects, surfacing as a bogus connection failure).
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls13; } catch { }

            Api = SupabaseAdminClient.FromConfig();
            new LoginWindow().Show();
        }
    }
}
