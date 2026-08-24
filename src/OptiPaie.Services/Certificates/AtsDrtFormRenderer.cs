using System;
using System.Collections.Generic;
using System.IO;
using OptiPaie.Core.Certificates;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace OptiPaie.Services.Certificates
{
    /// <summary>
    /// Draws ATS/DRT values at ABSOLUTE millimetre positions onto the pre-printed CNAS form —
    /// the method the client requires (no text flow, no bookmarks). Box grids are placed one
    /// character per cell, checkboxes get a mark centred in the square, dotted/rectangle values
    /// are auto-shrunk so they never overflow. Renders the real print PDF (black, values only,
    /// to overlay the paper form) and a proof PNG (values in red over the blank-form scan).
    /// </summary>
    public static class AtsDrtFormRenderer
    {
        private static readonly SKTypeface Font = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        private static readonly SKTypeface FontBold =
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

        private const double PtPerMm = 72.0 / 25.4; // PDF canvas is in points

        /// <summary>Loads the editable mm-coordinate layout from its JSON file.</summary>
        public static FormLayoutConfig LoadLayout(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<FormLayoutConfig>(json);
        }

        // ── public entry points ────────────────────────────────────────────

        /// <summary>The real, printable overlay: A4 pages, values only (black), at absolute mm.</summary>
        public static void RenderPdf(FormDefinition form, IDictionary<string, string> values,
            double offsetXmm, double offsetYmm, string outPdfPath)
        {
            using (var stream = new SKFileWStream(outPdfPath))
            {
                // SKFileWStream silently no-ops if the path can't be opened for writing (e.g. the
                // target PDF is already open in a viewer) — fail loudly so callers never report a
                // stale file as freshly generated.
                if (!stream.IsValid)
                    throw new IOException("Impossible d'écrire le PDF (fichier verrouillé ?) : " + outPdfPath);

                using (var doc = SKDocument.CreatePdf(stream))
                {
                    foreach (FormPage page in form.Pages)
                    {
                        SKCanvas canvas = doc.BeginPage((float)(page.WidthMm * PtPerMm), (float)(page.HeightMm * PtPerMm));
                        DrawPage(canvas, page, values, PtPerMm, offsetXmm, offsetYmm, SKColors.Black);
                        doc.EndPage();
                    }
                    doc.Close();
                }
            }
        }

        /// <summary>
        /// A printable millimetre calibration target (A4 portrait). The client prints this, lays it
        /// over a blank pre-printed form on a light table, and reads how many mm the grid is off — that
        /// value becomes the printer offset. The same <paramref name="offsetXmm"/>/<paramref name="offsetYmm"/>
        /// are applied here, so re-printing after entering an offset confirms the correction.
        /// </summary>
        public static void RenderCalibrationPdf(double offsetXmm, double offsetYmm, string outPdfPath)
        {
            const double wMm = 210, hMm = 297;
            using (var stream = new SKFileWStream(outPdfPath))
            {
                if (!stream.IsValid)
                    throw new IOException("Impossible d'écrire le PDF (fichier verrouillé ?) : " + outPdfPath);

                using (var doc = SKDocument.CreatePdf(stream))
                {
                    SKCanvas c = doc.BeginPage((float)(wMm * PtPerMm), (float)(hMm * PtPerMm));
                    DrawCalibration(c, PtPerMm, offsetXmm, offsetYmm);
                    doc.EndPage();
                    doc.Close();
                }
            }
        }

        /// <summary>PNG proof of the calibration target (for visual verification).</summary>
        public static void RenderCalibrationProofPng(double offsetXmm, double offsetYmm, int dpi, string outPngPath)
        {
            double u = dpi / 25.4;
            using (var bmp = new SKBitmap((int)Math.Round(210 * u), (int)Math.Round(297 * u)))
            {
                using (var canvas = new SKCanvas(bmp))
                {
                    canvas.Clear(SKColors.White);
                    DrawCalibration(canvas, u, offsetXmm, offsetYmm);
                }
                using (var img = SKImage.FromBitmap(bmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 92))
                using (var fs = File.OpenWrite(outPngPath))
                    data.SaveTo(fs);
            }
        }

        private static void DrawCalibration(SKCanvas c, double u, double ox, double oy)
        {
            double f = u * 25.4 / 72.0; // pt -> canvas units
            using (var thin = new SKPaint { Color = new SKColor(120, 120, 120), StrokeWidth = (float)(0.15 * u), IsAntialias = true })
            using (var bold = new SKPaint { Color = SKColors.Black, StrokeWidth = (float)(0.35 * u), IsAntialias = true })
            using (var label = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = (float)(7 * f), Typeface = Font })
            using (var title = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = (float)(11 * f), Typeface = FontBold })
            {
                for (int x = 0; x <= 200; x += 10)
                {
                    float px = (float)((x + ox) * u);
                    c.DrawLine(px, (float)((0 + oy) * u), px, (float)((260 + oy) * u), x % 50 == 0 ? bold : thin);
                    c.DrawText(x.ToString(), px + 1f, (float)((6 + oy) * u), label);
                }
                for (int y = 0; y <= 260; y += 10)
                {
                    float py = (float)((y + oy) * u);
                    c.DrawLine((float)((0 + ox) * u), py, (float)((200 + ox) * u), py, y % 50 == 0 ? bold : thin);
                    c.DrawText(y.ToString(), (float)((1 + ox) * u), py - 1f, label);
                }
                // origin crosshair at 100,100 mm
                float cx = (float)((100 + ox) * u), cy = (float)((100 + oy) * u);
                c.DrawLine(cx - (float)(10 * u), cy, cx + (float)(10 * u), cy, bold);
                c.DrawLine(cx, cy - (float)(10 * u), cx, cy + (float)(10 * u), bold);

                c.DrawText("MIRE DE CALAGE IMPRIMANTE — CNAS ATS / DRT", (float)((15 + ox) * u), (float)((278 + oy) * u), title);
                c.DrawText(string.Format("Décalage appliqué : X = {0:0.#} mm, Y = {1:0.#} mm.  Superposez sur un formulaire vierge.", ox, oy),
                    (float)((15 + ox) * u), (float)((286 + oy) * u), label);
            }
        }

        /// <summary>Proof image: the blank-form scan with the placed values in red, at <paramref name="dpi"/>.</summary>
        public static void RenderProofPng(FormPage page, IDictionary<string, string> values,
            string backgroundImagePath, double offsetXmm, double offsetYmm, int dpi, string outPngPath)
        {
            RenderProofPng(page, values, backgroundImagePath, offsetXmm, offsetYmm, dpi, outPngPath, new SKColor(210, 0, 0));
        }

        /// <summary>Print-preview image: the pre-printed form with the values in BLACK ink —
        /// exactly what the paper looks like after the printer lays the ink over it.</summary>
        public static void RenderPrintPreviewPng(FormPage page, IDictionary<string, string> values,
            string backgroundImagePath, double offsetXmm, double offsetYmm, int dpi, string outPngPath)
        {
            RenderProofPng(page, values, backgroundImagePath, offsetXmm, offsetYmm, dpi, outPngPath, SKColors.Black);
        }

        /// <summary>
        /// Same overlay at an explicit ink colour — pass black to preview exactly what physically
        /// prints onto the pre-printed form (the ink over the paper), red to make a proof stand out.
        /// </summary>
        public static void RenderProofPng(FormPage page, IDictionary<string, string> values,
            string backgroundImagePath, double offsetXmm, double offsetYmm, int dpi, string outPngPath, SKColor inkColor)
        {
            double u = dpi / 25.4;
            int wpx = (int)Math.Round(page.WidthMm * u);
            int hpx = (int)Math.Round(page.HeightMm * u);

            using (var bmp = new SKBitmap(wpx, hpx))
            {
                using (var canvas = new SKCanvas(bmp))
                {
                    canvas.Clear(SKColors.White);
                    if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                    {
                        using (var bg = SKBitmap.Decode(backgroundImagePath))
                        {
                            if (bg != null) canvas.DrawBitmap(bg, new SKRect(0, 0, wpx, hpx));
                        }
                    }
                    DrawPage(canvas, page, values, u, offsetXmm, offsetYmm, inkColor);
                }
                using (var img = SKImage.FromBitmap(bmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 92))
                using (var fs = File.OpenWrite(outPngPath))
                {
                    data.SaveTo(fs);
                }
            }
        }

        // ── drawing ────────────────────────────────────────────────────────

        private static void DrawPage(SKCanvas canvas, FormPage page, IDictionary<string, string> values,
            double unitPerMm, double offXmm, double offYmm, SKColor color)
        {
            double textSizeFactor = unitPerMm * 25.4 / 72.0; // pt -> canvas units

            foreach (FormField f in page.Fields)
            {
                values.TryGetValue(f.Name ?? string.Empty, out string text);
                string type = (f.Type ?? "dotted").ToLowerInvariant();
                // A checkbox is marked ONLY when its field carries a (truthy) value — never by default.
                if (string.IsNullOrWhiteSpace(text)) continue;

                using (var paint = new SKPaint
                {
                    Color = color,
                    IsAntialias = true,
                    Typeface = f.Bold ? FontBold : Font,
                    TextSize = (float)(f.FontSize * textSizeFactor)
                })
                {
                    switch (type)
                    {
                        case "grid": DrawGrid(canvas, f, text, unitPerMm, offXmm, offYmm, paint); break;
                        case "checkbox": DrawCheckbox(canvas, f, text, unitPerMm, offXmm, offYmm, paint); break;
                        case "rectangle": DrawInBox(canvas, f, text, unitPerMm, offXmm, offYmm, paint, f.WidthMm, true); break;
                        default: DrawInBox(canvas, f, text, unitPerMm, offXmm, offYmm, paint, f.MaxWidthMm, false); break;
                    }
                }
            }
        }

        /// <summary>One character per cell, each centred on its cell centre. Never a block string.</summary>
        private static void DrawGrid(SKCanvas c, FormField f, string text, double u, double ox, double oy, SKPaint p)
        {
            string digits = (text ?? string.Empty).Replace(" ", string.Empty);
            int cells = f.Cells > 0 ? f.Cells : digits.Length;
            int n = Math.Min(digits.Length, cells);
            float baseY = (float)((f.YMm + oy) * u);
            for (int i = 0; i < n; i++)
            {
                float cx = (float)((f.XMm + i * f.PitchMm + ox) * u);
                string ch = digits[i].ToString();
                float w = p.MeasureText(ch);
                c.DrawText(ch, cx - w / 2f, baseY, p);
            }
        }

        /// <summary>Mark centred inside the little square at (XMm, YMm).</summary>
        private static void DrawCheckbox(SKCanvas c, FormField f, string text, double u, double ox, double oy, SKPaint p)
        {
            string mark = string.IsNullOrEmpty(text) ? "X" : text;
            float cx = (float)((f.XMm + ox) * u);
            float cy = (float)((f.YMm + oy) * u);
            float w = p.MeasureText(mark);
            SKFontMetrics m = p.FontMetrics;
            float baseY = cy - (m.Ascent + m.Descent) / 2f;
            c.DrawText(mark, cx - w / 2f, baseY, p);
        }

        /// <summary>Dotted (left, shrink to MaxWidth) or rectangle (framed, vertically centred, shrink).</summary>
        private static void DrawInBox(SKCanvas c, FormField f, string text, double u, double ox, double oy,
            SKPaint p, double maxWidthMm, bool rectangle)
        {
            if (maxWidthMm > 0)
            {
                float maxW = (float)(maxWidthMm * u);
                float minSize = p.TextSize * 0.5f;
                while (p.MeasureText(text) > maxW && p.TextSize > minSize) p.TextSize -= 0.5f;
            }

            float x = (float)((f.XMm + ox) * u);
            if (rectangle && string.Equals(f.Align, "center", StringComparison.OrdinalIgnoreCase))
                x += (float)(f.WidthMm * u - p.MeasureText(text)) / 2f;

            float baseY;
            if (rectangle)
            {
                float cy = (float)((f.YMm + f.HeightMm / 2.0 + oy) * u);
                SKFontMetrics m = p.FontMetrics;
                baseY = cy - (m.Ascent + m.Descent) / 2f;
            }
            else
            {
                baseY = (float)((f.YMm + oy) * u);
            }

            // Hard-clip to the zone so a value that hit the auto-shrink floor still can NEVER
            // overflow into the adjacent field/column on the pre-printed form. The vertical band
            // is generous, so in practice only the horizontal zone edge ever clips.
            double zoneWidthMm = rectangle ? f.WidthMm : maxWidthMm;
            if (zoneWidthMm > 0)
            {
                float left = (float)((f.XMm + ox) * u);
                var zone = new SKRect(left, baseY - p.TextSize * 1.3f,
                    left + (float)(zoneWidthMm * u), baseY + p.TextSize * 0.5f);
                c.Save();
                c.ClipRect(zone);
                DrawFieldText(c, text, x, baseY, p, (float)(zoneWidthMm * u));
                c.Restore();
            }
            else
            {
                DrawFieldText(c, text, x, baseY, p, 0f);
            }
        }

        /// <summary>
        /// Draws a dotted/rectangle value. Latin/digits go through the plain (fast, proven) path;
        /// any text containing Arabic is shaped with HarfBuzz so the letters JOIN and read
        /// right-to-left (plain SkiaSharp draws Arabic detached and reversed). Left-anchored at
        /// <paramref name="x"/>, like every other field; the clip rect still bounds it to its zone.
        /// </summary>
        private static void DrawFieldText(SKCanvas c, string text, float x, float baseY, SKPaint p, float zoneWidthPx)
        {
            if (string.IsNullOrEmpty(text) || !ContainsArabic(text))
            {
                c.DrawText(text, x, baseY, p);
                return;
            }

            // Minimal bidi: split into Arabic (RTL, shaped) and non-Arabic (LTR, e.g. the year
            // digits) runs, then lay them out RIGHT-TO-LEFT so "جانفي 2025" keeps the Arabic joined
            // AND the year readable — a single shaped run would reverse the digits to "5202".
            var runs = SplitDirectionalRuns(text);
            float gap = p.TextSize * 0.28f; // visual separation between an Arabic run and a digit/latin run
            using (var shaper = new SKShaper(p.Typeface ?? Font))
            {
                var widths = new float[runs.Count];
                float total = 0f;
                for (int i = 0; i < runs.Count; i++)
                {
                    widths[i] = runs[i].Arabic ? shaper.Shape(runs[i].Text, p).Width : p.MeasureText(runs[i].Text);
                    total += widths[i];
                    if (i > 0) total += gap;
                }

                float penRight = x + total; // left-anchored block; first logical run sits rightmost
                for (int i = 0; i < runs.Count; i++)
                {
                    float runLeft = penRight - widths[i];
                    if (runs[i].Arabic) c.DrawShapedText(shaper, runs[i].Text, runLeft, baseY, p);
                    else c.DrawText(runs[i].Text, runLeft, baseY, p);
                    penRight = runLeft - gap;
                }
            }
        }

        private struct DirRun { public string Text; public bool Arabic; }

        // Split into maximal Arabic / non-Arabic runs, dropping the whitespace that separated them
        // (an explicit gap is added at layout time so spacing is consistent in either direction).
        private static System.Collections.Generic.List<DirRun> SplitDirectionalRuns(string s)
        {
            var runs = new System.Collections.Generic.List<DirRun>();
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                bool ar = IsArabicChar(s[i]);
                int j = i + 1;
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && IsArabicChar(s[j]) == ar) j++;
                runs.Add(new DirRun { Text = s.Substring(i, j - i), Arabic = ar });
                i = j;
            }
            return runs;
        }

        private static bool ContainsArabic(string s)
        {
            foreach (char ch in s)
                if (IsArabicChar(ch)) return true;
            return false;
        }

        private static bool IsArabicChar(char ch) =>
            (ch >= '؀' && ch <= 'ۿ') || (ch >= 'ݐ' && ch <= 'ݿ') ||
            (ch >= 'ࢠ' && ch <= 'ࣿ') || (ch >= 'ﭐ' && ch <= '﷿') ||
            (ch >= 'ﹰ' && ch <= '﻿');
    }
}
