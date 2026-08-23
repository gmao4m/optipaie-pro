using System;
using System.Collections.Generic;
using System.IO;
using OptiPaie.Core.Certificates;
using SkiaSharp;

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
                    DrawPage(canvas, page, values, u, offsetXmm, offsetYmm, new SKColor(210, 0, 0));
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
                c.DrawText(text, x, baseY, p);
                c.Restore();
            }
            else
            {
                c.DrawText(text, x, baseY, p);
            }
        }
    }
}
