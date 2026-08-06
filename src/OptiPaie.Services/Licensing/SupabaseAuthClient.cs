using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OptiPaie.Services.Licensing
{
    /// <summary>Classified reason an auth call did not fully succeed (drives precise, well-placed UI messages).</summary>
    public enum AuthErrorKind
    {
        None = 0,
        Offline,
        AlreadyExists,
        InvalidCredentials,
        EmailNotConfirmed,
        WeakPassword,
        InvalidEmail,
        RateLimited,
        Other
    }

    /// <summary>Outcome of a customer sign-up / sign-in against Supabase Auth.</summary>
    public sealed class AuthResult
    {
        private AuthResult(bool success, AuthErrorKind kind, string message)
        {
            Success = success; Kind = kind; Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public AuthErrorKind Kind { get; }
        public string Message { get; }

        public bool IsOffline => Kind == AuthErrorKind.Offline;
        public bool AlreadyExists => Kind == AuthErrorKind.AlreadyExists;

        public static AuthResult Ok() => new AuthResult(true, AuthErrorKind.None, string.Empty);
        public static AuthResult Offline() => new AuthResult(false, AuthErrorKind.Offline, "Aucune connexion Internet.");
        public static AuthResult Fail(AuthErrorKind kind, string message) => new AuthResult(false, kind, message);
    }

    /// <summary>
    /// Minimal customer-facing Supabase Auth client for the desktop app: create an
    /// account (email + password + company name) or sign in. Uses only the PUBLIC
    /// project URL + publishable/anon key. It is used at first activation to register the
    /// customer with the owner's backend; the app itself runs offline from its cached,
    /// DPAPI-encrypted license and a local account. No password is ever stored locally in
    /// clear.
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
            if (!IsConfigured) return AuthResult.Fail(AuthErrorKind.Other, "Service de compte non configuré.");

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
                        AuthErrorKind kind = Classify((int)resp.StatusCode, raw);
                        return AuthResult.Fail(kind, Localize(kind, raw));
                    }
                }
            }
            catch (Exception)
            {
                return AuthResult.Offline();
            }
        }

        private static AuthErrorKind Classify(int status, string raw)
        {
            string r = (raw ?? string.Empty).ToLowerInvariant();

            if (status == 429 || r.Contains("rate limit") || r.Contains("over_email_send_rate_limit")) return AuthErrorKind.RateLimited;
            if (r.Contains("already registered") || r.Contains("already been registered") || r.Contains("user already")) return AuthErrorKind.AlreadyExists;
            if (r.Contains("invalid login credentials")) return AuthErrorKind.InvalidCredentials;
            if (r.Contains("email not confirmed")) return AuthErrorKind.EmailNotConfirmed;
            if (r.Contains("password should be at least") || r.Contains("weak")) return AuthErrorKind.WeakPassword;
            if (r.Contains("unable to validate email") || r.Contains("invalid email") || r.Contains("invalid format")) return AuthErrorKind.InvalidEmail;
            return AuthErrorKind.Other;
        }

        private static string Localize(AuthErrorKind kind, string raw)
        {
            switch (kind)
            {
                case AuthErrorKind.RateLimited: return "Trop de tentatives. Réessayez dans quelques minutes.";
                case AuthErrorKind.AlreadyExists: return "Un compte existe déjà pour cet email.";
                case AuthErrorKind.InvalidCredentials: return "Email ou mot de passe incorrect.";
                case AuthErrorKind.EmailNotConfirmed: return "Email non confirmé. Vérifiez votre boîte de réception.";
                case AuthErrorKind.WeakPassword: return "Mot de passe trop court (au moins 6 caractères).";
                case AuthErrorKind.InvalidEmail: return "Adresse email invalide.";
                default: return string.IsNullOrWhiteSpace(raw) ? "Échec de l'authentification." : raw;
            }
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
