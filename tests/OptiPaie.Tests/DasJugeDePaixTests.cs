using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Services.Cnas;

namespace OptiPaie.Tests
{
    /// <summary>
    /// "Juge de paix" (étape 7) — the test that judges the whole DAS spec. It transcribes the real
    /// 2021 reference employees' data (with only NSS and birth date anonymized, exactly as the
    /// committed fixtures) and asserts the builder reproduces the two files BYTE FOR BYTE. A failure
    /// means the SPEC is wrong, not the fixture — the message pinpoints the offset and bytes so the
    /// discrepancy can be judged, never papered over. If the fixtures are missing, the test is
    /// ignored (never red for someone without them).
    /// </summary>
    [TestFixture]
    public sealed class DasJugeDePaixTests
    {
        private static string FixtureDir =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Das");

        [Test]
        public void Builder_reproduces_the_reference_files_byte_for_byte()
        {
            string detailPath = Path.Combine(FixtureDir, "das_sample_detail.txt");
            string headerPath = Path.Combine(FixtureDir, "das_sample_entete.txt");
            if (!File.Exists(detailPath) || !File.Exists(headerPath))
            {
                Assert.Ignore("Fixtures DAS anonymisées absentes — test ignoré (jamais rouge sans les fichiers).");
            }

            CnasDasReport report = BuildReferenceReport();
            DasFiles produced = new DasFileBuilder().Build(report, new DasHeaderOptions("N", "16000", string.Empty));

            // The header has no anomaly — it must be byte-identical.
            AssertBytesEqual("entête", File.ReadAllBytes(headerPath), produced.HeaderBytes);

            // The detail must be byte-identical EVERYWHERE except one nominative, documented cell.
            AssertDetailEqualExceptKnownAnomaly(File.ReadAllBytes(detailPath), produced.DetailBytes);
        }

        /// <summary>
        /// The ONE documented reference anomaly (arbitrage 2026-08-03): line 3, field Salaire T3,
        /// value 933333 — LEFT-aligned in the 2021 reference file while every other numeric field is
        /// RIGHT-aligned. The encoder stays right-aligned (spec-correct); this exact cell is not
        /// reproduced. The exception is pinned to this precise offset and to this exact deviation —
        /// it is NOT a general tolerance: any other differing byte, or any other deviation on the
        /// cell itself, fails the test.
        /// </summary>
        private static void AssertDetailEqualExceptKnownAnomaly(byte[] expected, byte[] produced)
        {
            Assert.That(produced.Length, Is.EqualTo(expected.Length), "longueur du fichier détail");

            // Absolute offset of line 3 (index 2), field Salaire T3: 2×(195+CRLF) + 122 = 516.
            int anomalyStart = 2 * (DasFileSpec.DetailLength + 2) + DasFileSpec.Detail.SalaryT3.Offset;
            int anomalyLength = DasFileSpec.Detail.SalaryT3.Length;

            for (int i = 0; i < expected.Length; i++)
            {
                bool inAnomaly = i >= anomalyStart && i < anomalyStart + anomalyLength;
                if (!inAnomaly && expected[i] != produced[i])
                {
                    Assert.Fail("détail : écart INATTENDU (hors anomalie documentée) — " + Diff("détail", expected, produced, i));
                }
            }

            // On the anomaly cell: the fixture stays the real left-aligned bytes, our output is the
            // spec-correct right-aligned bytes, and both hold the SAME value (only alignment differs).
            string fixtureCell = Encoding.ASCII.GetString(expected, anomalyStart, anomalyLength);
            string producedCell = Encoding.ASCII.GetString(produced, anomalyStart, anomalyLength);
            Assert.That(anomalyStart, Is.EqualTo(516), "l'anomalie documentée est à l'offset 516");
            Assert.That(fixtureCell, Is.EqualTo("933333 "), "la fixture doit rester le réel cadré à gauche (jamais retouchée)");
            Assert.That(producedCell, Is.EqualTo(" 933333"), "l'encodeur doit produire le montant cadré à droite (spec)");
            Assert.That(producedCell.Trim(), Is.EqualTo(fixtureCell.Trim()), "même valeur des deux côtés — seul l'alignement diffère");

            TestContext.WriteLine("ANOMALIE DE RÉFÉRENCE CONNUE (arbitrage 2026-08-03) — offset 516, ligne 3, Salaire T3 : "
                + "fixture (réel) «" + fixtureCell + "» cadré à gauche ; encodeur «" + producedCell
                + "» cadré à droite. Même valeur (933333). Tout le reste du fichier est identique à l'octet près.");
        }

        // The seven reference employees, in file order. Only NSS (9000000000NN) and birth date
        // (01011990) are anonymized — every salary, duration, date and total is the real value.
        private static CnasDasReport BuildReferenceReport()
        {
            var employees = new List<CnasDasEmployee>
            {
                Emp(1, "BOZZZZANA", "AMINA", new[] { 0, 58, 520, 433 }, new[] { 0m, 11454.54m, 84000.00m, 65848.49m }, new DateTime(2021, 6, 20), null),
                Emp(2, "RZZZZZHE", "HADJER", new[] { 0, 58, 520, 456 }, new[] { 0m, 11454.54m, 84000.00m, 70000.00m }, new DateTime(2021, 6, 20), null),
                Emp(3, "AZZZZZZI", "NABILA", new[] { 0, 58, 64, 0 }, new[] { 0m, 11454.54m, 9333.33m, 0m }, new DateTime(2021, 6, 20), new DateTime(2021, 7, 12)),
                Emp(4, "EZZZZI", "SAMIA", new[] { 0, 0, 318, 0 }, new[] { 0m, 0m, 36521.73m, 0m }, new DateTime(2021, 8, 5), new DateTime(2021, 9, 30)),
                Emp(5, "BZZZZS", "KARIMA", new[] { 0, 0, 0, 202 }, new[] { 0m, 0m, 0m, 22337.66m }, new DateTime(2021, 11, 15), null),
                Emp(6, "SZZZH", "KENZA", new[] { 0, 0, 0, 196 }, new[] { 0m, 0m, 0m, 26731.60m }, new DateTime(2021, 11, 15), null),
                Emp(7, "ABZZZZZZZANI", "LILA", new[] { 0, 0, 0, 185 }, new[] { 0m, 0m, 0m, 27272.73m }, new DateTime(2021, 11, 18), null),
            };

            var quarterTotals = new decimal[4];
            foreach (CnasDasEmployee e in employees)
            {
                for (int q = 0; q < 4; q++) quarterTotals[q] += e.Quarters[q].Salary;
            }
            decimal annual = quarterTotals[0] + quarterTotals[1] + quarterTotals[2] + quarterTotals[3];

            return new CnasDasReport(1L, 2021, "1639259938", employees,
                quarterTotals, annual, employees.Count, false);
        }

        private static CnasDasEmployee Emp(long id, string last, string first, int[] hours, decimal[] salary,
            DateTime hire, DateTime? exit)
        {
            var quarters = new List<CnasDasQuarter>(4);
            decimal annual = 0m;
            for (int q = 0; q < 4; q++)
            {
                quarters.Add(new CnasDasQuarter(q + 1, salary[q], hours[q], false));
                annual += salary[q];
            }
            string nss = "9000000000" + id.ToString("00");
            return new CnasDasEmployee(id, nss, last, first, new DateTime(1990, 1, 1), hire, exit, quarters, annual, false);
        }

        private static void AssertBytesEqual(string which, byte[] expected, byte[] produced)
        {
            int min = Math.Min(expected.Length, produced.Length);
            for (int i = 0; i < min; i++)
            {
                if (expected[i] != produced[i])
                {
                    Assert.Fail(Diff(which, expected, produced, i));
                }
            }
            if (expected.Length != produced.Length)
            {
                Assert.Fail(string.Format(
                    "{0} : longueurs différentes — attendu {1} octets, produit {2} (identiques sur les {3} premiers).",
                    which, expected.Length, produced.Length, min));
            }
        }

        private static string Diff(string which, byte[] expected, byte[] produced, int i)
        {
            int start = Math.Max(0, i - 12);
            int end = Math.Min(Math.Min(expected.Length, produced.Length), i + 13);
            return string.Format(
                "{0} : 1er écart à l'offset {1}.\n  attendu 0x{2:X2} '{3}'   produit 0x{4:X2} '{5}'\n  attendu [{6}..{7}] : «{8}»\n  produit [{6}..{7}] : «{9}»",
                which, i, expected[i], Ch(expected[i]), produced[i], Ch(produced[i]),
                start, end - 1, Window(expected, start, end), Window(produced, start, end));
        }

        private static string Window(byte[] b, int start, int end)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < end; i++) sb.Append(Ch(b[i]));
            return sb.ToString();
        }

        private static string Ch(byte b)
        {
            if (b == (byte)'\r') return "\\r";
            if (b == (byte)'\n') return "\\n";
            return b >= 32 && b < 127 ? ((char)b).ToString() : ".";
        }
    }
}
