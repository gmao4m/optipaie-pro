using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Guard rails for the two string-resource files (French neutral + Arabic).
    /// Runs inside the normal test suite — <c>dotnet test</c> / CI is the "build gate",
    /// so a regression here fails the pipeline automatically (no separate script needed).
    ///
    /// Protects two invariants that drift silently and only surface as MSB3568 warnings
    /// or as untranslated French text inside the Arabic UI:
    ///   1. No key is declared twice inside the same .resx file.
    ///   2. Every French key has an Arabic counterpart and vice-versa (name parity).
    /// </summary>
    [TestFixture]
    public class LocalizationResxParityTests
    {
        private static string FrPath() =>
            Path.Combine(TestPaths.RepoRoot(), "src", "OptiPaie.Localization", "Resources", "Strings.resx");

        private static string ArPath() =>
            Path.Combine(TestPaths.RepoRoot(), "src", "OptiPaie.Localization", "Resources", "Strings.ar.resx");

        /// <summary>All string-resource key names in declaration order (binary/typed entries skipped).</summary>
        private static List<string> ReadKeys(string path)
        {
            var doc = XDocument.Load(path);
            return doc.Root
                .Elements("data")
                // skip non-string (typed / binary) resources — they are not translatable strings
                .Where(d => d.Attribute("type") == null && d.Attribute("mimetype") == null)
                .Select(d => (string)d.Attribute("name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }

        private static List<string> Duplicates(IEnumerable<string> keys) =>
            keys.GroupBy(k => k, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} (×{g.Count()})")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        [Test]
        public void No_duplicate_keys_within_each_resx_file()
        {
            var frDupes = Duplicates(ReadKeys(FrPath()));
            var arDupes = Duplicates(ReadKeys(ArPath()));

            Assert.Multiple(() =>
            {
                Assert.That(frDupes, Is.Empty,
                    "Clés dupliquées dans Strings.resx (FR) — provoque MSB3568, la 2e est ignorée :\n  "
                    + string.Join("\n  ", frDupes));
                Assert.That(arDupes, Is.Empty,
                    "Clés dupliquées dans Strings.ar.resx (AR) — provoque MSB3568, la 2e est ignorée :\n  "
                    + string.Join("\n  ", arDupes));
            });
        }

        [Test]
        public void French_and_Arabic_resx_have_the_same_set_of_keys()
        {
            var fr = new HashSet<string>(ReadKeys(FrPath()), StringComparer.Ordinal);
            var ar = new HashSet<string>(ReadKeys(ArPath()), StringComparer.Ordinal);

            // (b of the owner's request) a key added to FR but NOT to AR => shows in French inside the Arabic UI
            var missingInArabic = fr.Except(ar).OrderBy(s => s, StringComparer.Ordinal).ToList();
            // dead / mis-named resources present only in AR
            var missingInFrench = ar.Except(fr).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(missingInArabic, Is.Empty,
                    $"{missingInArabic.Count} clé(s) présente(s) en FR et absente(s) en AR "
                    + "(ajoutez la traduction dans Strings.ar.resx) :\n  "
                    + string.Join("\n  ", missingInArabic));
                Assert.That(missingInFrench, Is.Empty,
                    $"{missingInFrench.Count} clé(s) présente(s) en AR et absente(s) en FR "
                    + "(clé morte ou mal nommée dans Strings.ar.resx) :\n  "
                    + string.Join("\n  ", missingInFrench));
            });
        }
    }
}
