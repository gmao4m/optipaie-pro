using System;
using System.Text;
using NUnit.Framework;
using OptiPaie.Services.Cnas;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The fixed-width DAS encoder (étape 5) — spec placement, ASCII-only, no truncation, and a
    /// pure-ASCII / no-BOM writer. These lock the primitives the DAS files are built from before
    /// any aggregation exists; the byte-exact "juge de paix" test comes later once there is data.
    /// </summary>
    [TestFixture]
    public sealed class DasEncoderTests
    {
        // ---------------------------------------------------------------- Spec integrity

        [Test]
        public void Spec_detail_fields_stay_within_195_and_never_overlap()
        {
            AssertNoOverlap(DasFileSpec.Detail.All, DasFileSpec.DetailLength);
        }

        [Test]
        public void Spec_header_fields_stay_within_221_and_never_overlap()
        {
            AssertNoOverlap(DasFileSpec.Header.All, DasFileSpec.HeaderLength);
        }

        private static void AssertNoOverlap(DasFieldSpec[] fields, int total)
        {
            var occupied = new bool[total];
            foreach (DasFieldSpec f in fields)
            {
                Assert.That(f.Offset, Is.GreaterThanOrEqualTo(0), f.Name);
                Assert.That(f.Offset + f.Length, Is.LessThanOrEqualTo(total), f.Name + " dépasse " + total);
                for (int i = f.Offset; i < f.Offset + f.Length; i++)
                {
                    Assert.That(occupied[i], Is.False, "Chevauchement à la position " + i + " (" + f.Name + ")");
                    occupied[i] = true;
                }
            }
        }

        // ---------------------------------------------------------------- Placement

        [Test]
        public void LineBuilder_places_fields_by_offset_and_leaves_the_rest_as_spaces()
        {
            string line = new DasLineBuilder(DasFileSpec.DetailLength)
                .Put(DasFileSpec.Detail.EmployerNumber, "1639259938")
                .Put(DasFileSpec.Detail.Year, "2021")
                .Put(DasFileSpec.Detail.Nss, "950075025342")
                .Put(DasFileSpec.Detail.LastName, "BENALI")
                .Build();

            Assert.That(line.Length, Is.EqualTo(195));
            Assert.That(line.Substring(0, 10), Is.EqualTo("1639259938"));
            Assert.That(line.Substring(10, 4), Is.EqualTo("2021"));
            Assert.That(line.Substring(20, 12), Is.EqualTo("950075025342"));
            Assert.That(line.Substring(32, 25), Is.EqualTo("BENALI".PadRight(25))); // left-aligned, space-filled
            Assert.That(line.Substring(14, 6), Is.EqualTo("      "));                // untouched field stays spaces
        }

        [Test]
        public void LineBuilder_right_aligns_amounts_space_filled_with_no_leading_zeros()
        {
            string full = new DasLineBuilder(DasFileSpec.DetailLength)
                .Put(DasFileSpec.Detail.SalaryT3, DasFormat.Amount(84000.00m)).Build();
            Assert.That(full.Substring(122, 7), Is.EqualTo("8400000"));

            string small = new DasLineBuilder(DasFileSpec.DetailLength)
                .Put(DasFileSpec.Detail.SalaryT1, DasFormat.Amount(93.33m)).Build();
            Assert.That(small.Substring(94, 7), Is.EqualTo("   9333")); // right, spaces, NO leading zeros
        }

        [Test]
        public void LineBuilder_left_aligns_text_space_filled()
        {
            string line = new DasLineBuilder(DasFileSpec.DetailLength)
                .Put(DasFileSpec.Detail.FirstName, "AMINA").Build();
            Assert.That(line.Substring(57, 25), Is.EqualTo("AMINA".PadRight(25)));
        }

        // ---------------------------------------------------------------- Refusals

        [Test]
        public void LineBuilder_refuses_non_ascii()
        {
            Assert.Throws<DasEncodingException>(() =>
                new DasLineBuilder(DasFileSpec.DetailLength).Put(DasFileSpec.Detail.LastName, "BENÂLI"));
        }

        [Test]
        public void LineBuilder_refuses_a_value_wider_than_its_field_and_never_truncates()
        {
            // 100 000,00 DA → 8 digits of centimes into a 7-wide quarterly salary → refuse.
            string tooBig = DasFormat.Amount(100000.00m); // "10000000"
            DasEncodingException ex = Assert.Throws<DasEncodingException>(() =>
                new DasLineBuilder(DasFileSpec.DetailLength).Put(DasFileSpec.Detail.SalaryT1, tooBig));
            Assert.That(ex.Message, Does.Contain("refusé"));

            // 99 999,99 DA is exactly the field capacity → accepted.
            Assert.That(DasFormat.Amount(99999.99m), Is.EqualTo("9999999"));
            Assert.DoesNotThrow(() =>
                new DasLineBuilder(DasFileSpec.DetailLength).Put(DasFileSpec.Detail.SalaryT1, DasFormat.Amount(99999.99m)));
        }

        // ---------------------------------------------------------------- Formatting

        [Test]
        public void Format_amount_is_integer_centimes()
        {
            Assert.That(DasFormat.Amount(84000.00m), Is.EqualTo("8400000"));
            Assert.That(DasFormat.Amount(11454.54m), Is.EqualTo("1145454"));
            Assert.That(DasFormat.Amount(0m), Is.EqualTo("0"));
        }

        [Test]
        public void Format_date_is_ddMMyyyy_or_empty()
        {
            Assert.That(DasFormat.Date(new DateTime(2021, 6, 20)), Is.EqualTo("20062021"));
            Assert.That(DasFormat.Date(null), Is.EqualTo(string.Empty));
        }

        // ---------------------------------------------------------------- ASCII writer / no BOM

        [Test]
        public void Writer_emits_pure_ascii_with_no_bom()
        {
            byte[] header = DasAsciiWriter.WriteHeader(new string('A', DasFileSpec.HeaderLength));

            // No UTF-8 BOM (EF BB BF) and no UTF-16 BOM (FF FE / FE FF): the first byte is the content.
            Assert.That(header[0], Is.EqualTo((byte)'A'));
            Assert.That(header[0], Is.Not.EqualTo((byte)0xEF));
            Assert.That(header[0], Is.Not.EqualTo((byte)0xFF));
            Assert.That(header[0], Is.Not.EqualTo((byte)0xFE));
            foreach (byte b in header)
            {
                Assert.That(b, Is.LessThanOrEqualTo((byte)127));
            }
        }

        [Test]
        public void Writer_header_ends_with_crlf_and_detail_has_no_trailing_crlf()
        {
            byte[] header = DasAsciiWriter.WriteHeader("H");
            Assert.That(header[header.Length - 2], Is.EqualTo((byte)'\r'));
            Assert.That(header[header.Length - 1], Is.EqualTo((byte)'\n'));

            byte[] detail = DasAsciiWriter.WriteDetail(new[] { "AAA", "BBB", "CCC" });
            string text = Encoding.ASCII.GetString(detail);
            Assert.That(text, Is.EqualTo("AAA\r\nBBB\r\nCCC")); // CR LF between lines, none after the last
            Assert.That(text.EndsWith("\r\n"), Is.False);
        }

        // ---------------------------------------------------------------- Post-write verify

        private static byte[] GoodHeader() => DasAsciiWriter.WriteHeader(new string('A', DasFileSpec.HeaderLength));
        private static byte[] GoodDetail() =>
            DasAsciiWriter.WriteDetail(new[] { new string('B', DasFileSpec.DetailLength), new string('C', DasFileSpec.DetailLength) });

        [Test]
        public void Structure_accepts_well_formed_files()
        {
            Assert.That(DasStructure.Verify(GoodHeader(), GoodDetail(), out string reason), Is.True, reason);
        }

        [Test]
        public void Structure_rejects_a_wrong_detail_line_length()
        {
            byte[] detail = DasAsciiWriter.WriteDetail(new[] { new string('B', DasFileSpec.DetailLength - 1) });
            Assert.That(DasStructure.Verify(GoodHeader(), detail, out string reason), Is.False);
            Assert.That(reason, Does.Contain("ligne"));
        }

        [Test]
        public void Structure_rejects_a_trailing_crlf_on_detail()
        {
            byte[] detail = Encoding.ASCII.GetBytes(new string('B', DasFileSpec.DetailLength) + "\r\n");
            Assert.That(DasStructure.Verify(GoodHeader(), detail, out string reason), Is.False);
            Assert.That(reason, Does.Contain("CR LF final"));
        }

        [Test]
        public void Structure_rejects_a_bom()
        {
            byte[] good = GoodHeader();
            byte[] withBom = new byte[good.Length + 3];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            Array.Copy(good, 0, withBom, 3, good.Length);
            Assert.That(DasStructure.Verify(withBom, GoodDetail(), out string reason), Is.False);
            Assert.That(reason, Does.Contain("BOM"));
        }
    }
}
