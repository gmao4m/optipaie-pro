using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OptiPaie.Admin.Api
{
    /// <summary>
    /// Minimal Supabase client for the admin console: owner password login (Auth) +
    /// REST (PostgREST) reads/writes + RPC. Uses the PUBLIC publishable key; the
    /// service-role key is never used. All calls require an authenticated session
    /// (RLS grants the owner full access).
    /// </summary>
    public sealed class SupabaseAdminClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string _url;
        private readonly string _key;
        private string _token;

        public SupabaseAdminClient(string url, string publishableKey)
        {
            _url = (url ?? string.Empty).TrimEnd('/');
            _key = publishableKey ?? string.Empty;
        }

        public static SupabaseAdminClient FromConfig()
        {
            string url = ConfigurationManager.AppSettings["Supabase.Url"];
            string key = ConfigurationManager.AppSettings["Supabase.Key"];
            return new SupabaseAdminClient(url, key);
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public string UserEmail { get; private set; }

        // ---- Auth ----
        public async Task SignInAsync(string email, string password)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, _url + "/auth/v1/token?grant_type=password"))
            {
                req.Headers.TryAddWithoutValidation("apikey", _key);
                req.Content = Json(new { email, password });
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(ExtractError(body, "Échec de la connexion."));
                    }

                    JObject o = JObject.Parse(body);
                    _token = (string)o["access_token"];
                    UserEmail = (string)(o["user"] != null ? o["user"]["email"] : null) ?? email;
                    if (string.IsNullOrEmpty(_token))
                    {
                        throw new InvalidOperationException("Réponse d'authentification invalide.");
                    }
                }
            }
        }

        public void SignOut() { _token = null; UserEmail = null; }

        // ---- REST ----
        public Task<T[]> SelectAsync<T>(string table, string query)
        {
            return SendJsonAsync<T[]>(HttpMethod.Get, "/rest/v1/" + table + "?" + query, null, null);
        }

        public Task<T> SelectSingleAsync<T>(string table, string query) where T : class
        {
            return SelectAsync<T>(table, query).ContinueWith(t =>
                t.Result != null && t.Result.Length > 0 ? t.Result[0] : null);
        }

        /// <summary>Select with an exact total count (from the Content-Range header).</summary>
        public async Task<PagedResult<T>> SelectPagedAsync<T>(string table, string query, int from, int to)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, _url + "/rest/v1/" + table + "?" + query))
            {
                AddAuth(req);
                req.Headers.TryAddWithoutValidation("Prefer", "count=exact");
                req.Headers.TryAddWithoutValidation("Range-Unit", "items");
                req.Headers.TryAddWithoutValidation("Range", from + "-" + to);
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(ExtractError(body, "Erreur de lecture."));

                    int total = 0;
                    if (resp.Content.Headers.TryGetValues("Content-Range", out var vals))
                    {
                        foreach (string v in vals)
                        {
                            int slash = v.IndexOf('/');
                            if (slash >= 0) int.TryParse(v.Substring(slash + 1), out total);
                        }
                    }

                    T[] items = JsonConvert.DeserializeObject<T[]>(body) ?? new T[0];
                    return new PagedResult<T> { Items = items, Total = total };
                }
            }
        }

        public Task InsertAsync(string table, object row)
        {
            return SendJsonAsync<object>(HttpMethod.Post, "/rest/v1/" + table, row, "return=minimal");
        }

        public Task UpdateAsync(string table, string filter, object patch)
        {
            return SendJsonAsync<object>(new HttpMethod("PATCH"), "/rest/v1/" + table + "?" + filter, patch, "return=minimal");
        }

        public Task UpsertAsync(string table, object row, string onConflict)
        {
            return SendJsonAsync<object>(HttpMethod.Post, "/rest/v1/" + table + "?on_conflict=" + onConflict, row,
                "resolution=merge-duplicates,return=minimal");
        }

        public Task DeleteAsync(string table, string filter)
        {
            return SendJsonAsync<object>(HttpMethod.Delete, "/rest/v1/" + table + "?" + filter, null, "return=minimal");
        }

        // ---- RPC ----
        public Task<T> RpcAsync<T>(string fn, object args)
        {
            return SendJsonAsync<T>(HttpMethod.Post, "/rest/v1/rpc/" + fn, args, null);
        }

        // ---- plumbing ----
        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body, string prefer)
        {
            using (var req = new HttpRequestMessage(method, _url + path))
            {
                AddAuth(req);
                if (!string.IsNullOrEmpty(prefer)) req.Headers.TryAddWithoutValidation("Prefer", prefer);
                if (body != null) req.Content = Json(body);
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    string s = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(ExtractError(s, "Erreur serveur (" + (int)resp.StatusCode + ")."));
                    if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(s)) return default(T);
                    return JsonConvert.DeserializeObject<T>(s);
                }
            }
        }

        private void AddAuth(HttpRequestMessage req)
        {
            req.Headers.TryAddWithoutValidation("apikey", _key);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (_token ?? _key));
        }

        private static StringContent Json(object o) =>
            new StringContent(JsonConvert.SerializeObject(o), Encoding.UTF8, "application/json");

        private static string ExtractError(string body, string fallback)
        {
            try
            {
                JObject o = JObject.Parse(body);
                string msg = (string)(o["error_description"] ?? o["message"] ?? o["msg"] ?? o["error"] ?? o["hint"]);
                return string.IsNullOrEmpty(msg) ? fallback : msg;
            }
            catch { return fallback; }
        }
    }

    public sealed class PagedResult<T>
    {
        public T[] Items { get; set; }
        public int Total { get; set; }
    }
}
