using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace OptiPaie.Admin.Api
{
    /// <summary>
    /// Persists the owner's Supabase session (email + refresh token) on this machine,
    /// DPAPI-encrypted for the current Windows user, so the admin console signs in ONCE
    /// and stays signed in across launches. The password is never stored; only the
    /// short-lived refresh token, which is exchanged for a fresh access token at startup.
    /// </summary>
    public static class AdminSession
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OptiPaie.PRO.Admin.Session.v1");

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OptiPaie PRO Admin");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "session.dat");
            }
        }

        public static void Save(string email, string refreshToken)
        {
            try
            {
                if (string.IsNullOrEmpty(refreshToken)) { Clear(); return; }
                string json = JsonConvert.SerializeObject(new Data { Email = email, RefreshToken = refreshToken });
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, enc);
            }
            catch { /* best-effort: a lost session just means one more manual login */ }
        }

        public static bool TryLoad(out string email, out string refreshToken)
        {
            email = null;
            refreshToken = null;
            try
            {
                string f = FilePath;
                if (!File.Exists(f)) return false;
                byte[] dec = ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser);
                Data d = JsonConvert.DeserializeObject<Data>(Encoding.UTF8.GetString(dec));
                if (d == null || string.IsNullOrEmpty(d.RefreshToken)) return false;
                email = d.Email;
                refreshToken = d.RefreshToken;
                return true;
            }
            catch
            {
                return false; // corrupt / foreign / not-a-DPAPI blob — treat as no session
            }
        }

        public static void Clear()
        {
            try
            {
                string f = FilePath;
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
        }

        private sealed class Data
        {
            public string Email { get; set; }
            public string RefreshToken { get; set; }
        }
    }
}
