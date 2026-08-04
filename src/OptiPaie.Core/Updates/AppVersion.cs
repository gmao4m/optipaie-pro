using System;
using System.Globalization;
using System.Reflection;

namespace OptiPaie.Core.Updates
{
    /// <summary>
    /// A simple, pure semantic version (major.minor.patch[.build]) with comparison,
    /// used for update decisions and downgrade prevention. Testable in isolation.
    /// </summary>
    public sealed class AppVersion : IComparable<AppVersion>
    {
        public AppVersion(int major, int minor, int patch, int build)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Build = build;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public int Build { get; }

        public static bool TryParse(string text, out AppVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            {
                s = s.Substring(1);
            }

            // Ignore any pre-release/build suffix after '-' or '+'.
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }

            string[] parts = s.Split('.');
            int[] nums = new int[4];
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]))
                {
                    return false;
                }
            }

            version = new AppVersion(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        public int CompareTo(AppVersion other)
        {
            if (other == null)
            {
                return 1;
            }

            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            return Build.CompareTo(other.Build);
        }

        public bool IsNewerThan(AppVersion other)
        {
            return CompareTo(other) > 0;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> parses and is strictly newer than
        /// <paramref name="current"/>. Returns false for equal or older (downgrade guard).
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            if (!TryParse(candidate, out AppVersion c))
            {
                return false;
            }

            if (!TryParse(current, out AppVersion cur))
            {
                // No parseable current → treat any valid candidate as newer.
                return true;
            }

            return c.IsNewerThan(cur);
        }

        /// <summary>
        /// The running application's PRODUCT version (e.g. "1.14.0"), read from the entry
        /// assembly's InformationalVersion — which mirrors &lt;Version&gt; in Directory.Build.props,
        /// the single place to bump per release. AssemblyVersion/FileVersion are intentionally
        /// frozen (binding stability) and must NOT be used for update comparison. Any
        /// "+build"/"-pre" suffix (e.g. SourceLink commit hash) is stripped.
        /// </summary>
        public static string CurrentProduct()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                string raw = null;
                if (asm != null)
                {
                    AssemblyInformationalVersionAttribute info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (info != null) raw = info.InformationalVersion;
                    if (string.IsNullOrWhiteSpace(raw) && asm.GetName().Version != null) raw = asm.GetName().Version.ToString();
                }

                if (TryParse(raw, out AppVersion v)) return v.ToString();
                return string.IsNullOrWhiteSpace(raw) ? "1.0.0" : raw;
            }
            catch
            {
                return "1.0.0";
            }
        }

        public override string ToString()
        {
            return Build > 0
                ? Major + "." + Minor + "." + Patch + "." + Build
                : Major + "." + Minor + "." + Patch;
        }
    }
}
