using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
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
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private string _token;
        private string _refreshToken;
        private DateTime _expiresAtUtc = DateTime.MinValue;

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
                    UserEmail = (string)(o["user"] != null ? o["user"]["email"] : null) ?? email;
                    StoreSession(o);
                    if (string.IsNullOrEmpty(_token))
                    {
                        throw new InvalidOperationException("Réponse d'authentification invalide.");
                    }
                }
            }
        }

        public void SignOut()
        {
            _token = null;
            _refreshToken = null;
            _expiresAtUtc = DateTime.MinValue;
            UserEmail = null;
            AdminSession.Clear();
        }

        /// <summary>
        /// Resumes a previously saved session so the console opens straight to the dashboard
        /// without a login. Returns true once a fresh access token is obtained; false (and
        /// clears the stored session) if the saved refresh token is no longer valid.
        /// </summary>
        public async Task<bool> TryRestoreSessionAsync()
        {
            if (!AdminSession.TryLoad(out string email, out string refresh))
            {
                return false;
            }

            _refreshToken = refresh;
            UserEmail = email;
            bool ok = await TryRefreshAsync(true).ConfigureAwait(false);
            if (!ok)
            {
                AdminSession.Clear();
                UserEmail = null;
            }
            return ok && IsAuthenticated;
        }

        /// <summary>Stores the access + refresh tokens and computes the local expiry (with a 60 s safety margin).</summary>
        private void StoreSession(JObject o)
        {
            _token = (string)o["access_token"];
            _refreshToken = (string)o["refresh_token"] ?? _refreshToken;
            int expiresIn = (int?)o["expires_in"] ?? 3600;
            _expiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));

            // Persist the refresh token (DPAPI) so the console stays signed in next launch.
            AdminSession.Save(UserEmail, _refreshToken);
        }

        /// <summary>
        /// Proactively refreshes the access token when it is at/near expiry, so a long
        /// admin session never surfaces a "jwt expired" error. A no-op without a session.
        /// </summary>
        private async Task EnsureFreshTokenAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken) || DateTime.UtcNow < _expiresAtUtc)
            {
                return;
            }

            await TryRefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Exchanges the refresh token for a new access token. Serialized so concurrent
        /// callers refresh once. <paramref name="force"/> is set by the 401 backstop so it
        /// refreshes even when the token still looks locally fresh (the server rejected it —
        /// clock skew or early revocation); proactive callers leave it false.
        /// </summary>
        private async Task<bool> TryRefreshAsync(bool force = false)
        {
            if (string.IsNullOrEmpty(_refreshToken))
            {
                return false;
            }

            await _refreshLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // A proactive caller can skip if another request already refreshed while we
                // waited on the lock; a forced (post-401) caller must always refresh.
                if (!force && !string.IsNullOrEmpty(_token) && DateTime.UtcNow < _expiresAtUtc)
                {
                    return true;
                }

                using (var req = new HttpRequestMessage(HttpMethod.Post, _url + "/auth/v1/token?grant_type=refresh_token"))
                {
                    req.Headers.TryAddWithoutValidation("apikey", _key);
                    req.Content = Json(new { refresh_token = _refreshToken });
                    using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                    {
                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            // Only a definitive auth rejection (invalid/expired refresh token)
                            // invalidates the session. Transient errors (429 rate-limit, 5xx)
                            // keep the still-valid refresh token so the session survives.
                            int code = (int)resp.StatusCode;
                            if (code == 400 || code == 401 || code == 403)
                            {
                                _token = null;
                                _refreshToken = null;
                                _expiresAtUtc = DateTime.MinValue;
                                AdminSession.Clear(); // saved refresh token is no longer valid
                            }
                            return false;
                        }

                        StoreSession(JObject.Parse(body));
                        return !string.IsNullOrEmpty(_token);
                    }
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

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
            return await SelectPagedAsync<T>(table, query, from, to, false).ConfigureAwait(false);
        }

        private async Task<PagedResult<T>> SelectPagedAsync<T>(string table, string query, int from, int to, bool isRetry)
        {
            await EnsureFreshTokenAsync().ConfigureAwait(false);
            using (var req = new HttpRequestMessage(HttpMethod.Get, _url + "/rest/v1/" + table + "?" + query))
            {
                AddAuth(req);
                req.Headers.TryAddWithoutValidation("Prefer", "count=exact");
                req.Headers.TryAddWithoutValidation("Range-Unit", "items");
                req.Headers.TryAddWithoutValidation("Range", from + "-" + to);
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    if (resp.StatusCode == HttpStatusCode.Unauthorized && !isRetry && await TryRefreshAsync(true).ConfigureAwait(false))
                    {
                        return await SelectPagedAsync<T>(table, query, from, to, true).ConfigureAwait(false);
                    }

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
        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body, string prefer, bool isRetry = false)
        {
            await EnsureFreshTokenAsync().ConfigureAwait(false);
            using (var req = new HttpRequestMessage(method, _url + path))
            {
                AddAuth(req);
                if (!string.IsNullOrEmpty(prefer)) req.Headers.TryAddWithoutValidation("Prefer", prefer);
                if (body != null) req.Content = Json(body);
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    // Backstop: if the token expired between the proactive check and the call,
                    // refresh once and replay the request transparently.
                    if (resp.StatusCode == HttpStatusCode.Unauthorized && !isRetry && await TryRefreshAsync(true).ConfigureAwait(false))
                    {
                        return await SendJsonAsync<T>(method, path, body, prefer, true).ConfigureAwait(false);
                    }

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
