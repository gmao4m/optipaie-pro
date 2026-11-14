using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Security;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 1 — authorization fails closed. <c>AccessContext.Unrestricted</c> is the one explicit
    /// opt-out of service-layer enforcement; letting it creep into production is exactly the
    /// fail-open hole this phase removes. This test scans the shipped source and FAILS if it is used
    /// anywhere except an allow-listed first-run bootstrap site (currently none). Tests may use it
    /// freely; this guard ignores the test projects.
    /// </summary>
    [TestFixture]
    public sealed class UnrestrictedAccessGuardTests
    {
        // Production files permitted to use AccessContext.Unrestricted, each with a reason.
        // Add a first-run bootstrap here (with its reason) only if one is ever genuinely needed.
        private static readonly Dictionary<string, string> Allow =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // (empty) — no production call site currently needs Unrestricted: first-run admin
                // creation runs through the real session context with the login gate still off.
            };

        [Test]
        public void Require_NullContext_FailsClosed()
        {
            // A missing access context is a wiring omission, never "no enforcement": it is DENIED.
            Assert.Throws<AccessDeniedException>(() => AccessPolicy.Require(null, Permission.ManageCompanies));
            Assert.Throws<AccessDeniedException>(() => AccessPolicy.Require(null, Permission.RunPayroll));

            // The explicit sentinel is the deliberate opt-out and permits everything.
            Assert.DoesNotThrow(() => AccessPolicy.Require(AccessContext.Unrestricted, Permission.ManageCompanies));
        }

        [Test]
        public void Unrestricted_IsNeverUsedInProduction_ExceptAllowlistedBootstrap()
        {
            string root = TestPaths.RepoRoot();
            string src = Path.Combine(root, "src");
            Assert.That(Directory.Exists(src), Is.True, "src folder not found at " + src);

            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string rel = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/"))
                {
                    continue;
                }
                // The file that DEFINES the sentinel legitimately names it.
                if (rel.EndsWith("/Security/AccessContext.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.IndexOf("AccessContext.Unrestricted", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///"))
                    {
                        continue; // a comment mentioning it, not a call site
                    }
                    if (Allow.ContainsKey(rel))
                    {
                        continue;
                    }
                    offenders.Add(rel + ":" + (i + 1) + "  " + trimmed);
                }
            }

            if (Allow.Count > 0)
            {
                TestContext.WriteLine("Allow-listed Unrestricted sites:");
                foreach (var kv in Allow) TestContext.WriteLine("  " + kv.Key + " — " + kv.Value);
            }

            Assert.That(offenders, Is.Empty,
                "AccessContext.Unrestricted must not be used in production outside the allow-list:\n" +
                string.Join("\n", offenders));
        }
    }
}
