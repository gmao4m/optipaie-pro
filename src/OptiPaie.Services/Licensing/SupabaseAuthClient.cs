using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OptiPaie.Services.Licensing
{
    /// <summary>Outcome of a customer sign-up / sign-in against Supabase Auth.</summary>
    public sealed class AuthResult
    {
        private AuthResult(bool success, bool isOffline, bool alreadyExists, string message)
        {
            Success = success; IsOffline = isOffline; AlreadyExists = alreadyExists; Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool IsOffline { get; }
        public bool AlreadyExists { get; }
        public string Message { get; }

        public static AuthResult Ok() => new AuthResult(true, false, false, string.Empty);
        public static AuthResult Exists() => new AuthResult(false, false, true, "Un compte existe déjà pour cet email.");
        public static AuthResult Offline() => new AuthResult(false, true, false, "Aucune connexion Internet.");
        public static AuthResult Fail(string message) => new AuthResult(false, false, false, message);
    }

    /// <summary>
    /// Minimal customer-facing Supabase Auth client for the desktop app: create an
    /// account (email + password + company name) or sign in. Uses only the PUBLIC
    /// project URL + publishable/anon key. It is used ONCE, at first activation — after
    /// that the app runs offline from its cached, DPAPI-encrypted license. No password
    /// is ever stored locally.
    /// </summary>
    public sealed class SupabaseAuthClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        private readonly string _authUrl;
        private readonly string _anonKey;

        public SupabaseAuthClient(string projectUrl, string anonKey)
        {
            _authUrl = string.IsNullOrWhiteSpace(projectUrl) ? string.Empty : projectUrl.TrimEnd('/') + "/auth/v1";
            _anonKey = anonKey ?? string.Empty;
        }

        /// <summary>True once the project URL + key are present (so the UI can fall back gracefully).</summary>
        public bool IsConfigured =>
            _authUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_anonKey);

        /// <summary>
        /// Derives the project origin (<c>https://ref.supabase.co</c>) from the configured
        /// Edge Functions base URL — handling both <c>…supabase.co/functions/v1</c> and the
        /// <c>ref.functions.supabase.co</c> host form. Auth lives at <c>/auth/v1</c>.
        /// </summary>
        public static string DeriveProjectUrl(string functionsBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(functionsBaseUrl)) return string.Empty;
            string url = functionsBaseUrl.Trim();
            int i = url.IndexOf("/functions/v1", StringComparison.OrdinalIgnoreCase);
            if (i > 0) url = url.Substring(0, i);
            url = url.Replace(".functions.supabase.co", ".supabase.co");
            return url.TrimEnd('/');
        }

        public Task<AuthResult> SignUpAsync(string email, string password, string companyName, CancellationToken ct) =>
            PostAsync("/signup", new { email, password, data = new { company_name = companyName } }, ct);

        public Task<AuthResult> SignInAsync(string email, string password, CancellationToken ct) =>
            PostAsync("/token?grant_type=password", new { email, password }, ct);

        private async Task<AuthResult> PostAsync(string path, object body, CancellationToken ct)
        {
            if (!IsConfigured) return AuthResult.Fail("Service de compte non configuré.");

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, _authUrl + path))
                {
                    req.Headers.TryAddWithoutValidation("apikey", _anonKey);
                    req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage resp = await Http.SendAsync(req, ct).ConfigureAwait(false))
                    {
                        string s = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode) return AuthResult.Ok();

                        string raw = ExtractError(s);
                        if (LooksAlreadyRegistered(raw)) return AuthResult.Exists();
                        return AuthResult.Fail(Localize(raw));
                    }
                }
            }
            catch (Exception)
            {
                return AuthResult.Offline();
            }
        }

        private static bool LooksAlreadyRegistered(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string r = raw.ToLowerInvariant();
            return r.Contains("already registered") || r.Contains("already been registered") || r.Contains("user already");
        }

        private static string Localize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Échec de l'authentification.";
            string r = raw.ToLowerInvariant();
            if (r.Contains("invalid login credentials")) return "Email ou mot de passe incorrect.";
            if (r.Contains("password should be at least") || r.Contains("weak")) return "Mot de passe trop court (au moins 6 caractères).";
            if (r.Contains("unable to validate email") || r.Contains("invalid email")) return "Adresse email invalide.";
            if (r.Contains("email not confirmed")) return "Email non confirmé. Vérifiez votre boîte de réception.";
            return raw;
        }

        private static string ExtractError(string body)
        {
            try
            {
                JObject o = JObject.Parse(body);
                return (string)(o["error_description"] ?? o["msg"] ?? o["message"] ?? o["error"] ?? o["error_code"]);
            }
            catch { return body; }
        }
    }
}
