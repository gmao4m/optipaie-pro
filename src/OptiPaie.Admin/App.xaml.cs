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
            Api = SupabaseAdminClient.FromConfig();
            new LoginWindow().Show();
        }
    }
}
