using System;
using SkiaSharp;

namespace OptiPaie.Desktop.Common
{
    /// <summary>
    /// Defensive normalisation for user-supplied images — today the company logo, which is stored
    /// raw (any format the file picker allowed, including BMP) and re-rendered on every payslip.
    /// <para>
    /// Decodes arbitrary input (PNG/JPEG/BMP/GIF/WEBP, a CMYK JPEG, …) and re-encodes it to a clean
    /// RGBA PNG that the document pipeline (QuestPDF → SkiaSharp) can always render. Returns null —
    /// and NEVER throws — when the bytes are absent, corrupt, truncated, or in a format SkiaSharp
    /// cannot decode. Callers then degrade gracefully (render without the image) instead of letting a
    /// decorative element abort a legally required document. QuestPDF's <c>.Image()</c> decodes lazily
    /// at GeneratePdf time and throws there, so the only safe guard is to validate the bytes BEFORE
    /// handing them over — which is exactly what this does.
    /// </para>
    /// </summary>
    public static class SafeImage
    {
        /// <param name="raw">Stored/uploaded image bytes in any format, or null.</param>
        /// <param name="problem">Null when the image is valid OR simply absent; otherwise a short reason.</param>
        /// <returns>Clean PNG bytes ready to render, or null when there is no usable image.</returns>
        public static byte[] ToRenderablePng(byte[] raw, out string problem)
        {
            problem = null;
            if (raw == null || raw.Length == 0)
            {
                return null; // no logo configured — not a problem, just nothing to draw
            }

            try
            {
                using (SKBitmap bitmap = SKBitmap.Decode(raw))
                {
                    if (bitmap == null)
                    {
                        problem = "format d'image non pris en charge ou fichier illisible";
                        return null;
                    }

                    using (SKImage image = SKImage.FromBitmap(bitmap))
                    using (SKData data = image.Encode(SKEncodedImageFormat.Png, 90))
                    {
                        byte[] png = data?.ToArray();
                        if (png == null || png.Length == 0)
                        {
                            problem = "échec de ré-encodage de l'image";
                            return null;
                        }
                        return png;
                    }
                }
            }
            catch (Exception ex)
            {
                // SkiaSharp native/decoder failure — treat as an unusable image, never a crash.
                problem = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
