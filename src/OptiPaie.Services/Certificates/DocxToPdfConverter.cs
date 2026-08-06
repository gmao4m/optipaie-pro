using System;
using System.Runtime.InteropServices;
using Word = Microsoft.Office.Interop.Word;

namespace OptiPaie.Services.Certificates
{
    /// <summary>
    /// Converts a .docx to .pdf using Microsoft Word automation. Ported verbatim from the
    /// source ATS/DRT tool (AtsDrt.Documents.DocxToPdfConverter).
    ///
    /// Requires Microsoft Word to be installed on the machine — the same requirement the
    /// original tool has (it drives Word via COM automation to fill and print certificates).
    /// The filled .docx is always produced first, so if Word is missing the caller can still
    /// open/print the official form; only the PDF step needs Word.
    /// </summary>
    public static class DocxToPdfConverter
    {
        /// <summary>True when Microsoft Word is registered on this machine (COM ProgID present).</summary>
        public static bool IsWordAvailable()
        {
            try { return Type.GetTypeFromProgID("Word.Application") != null; }
            catch { return false; }
        }

        public static void ConvertToPdf(string docxPath, string pdfPath)
        {
            Word.Application wordApp = null;
            Word.Document doc = null;

            try
            {
                wordApp = new Word.Application { Visible = false, DisplayAlerts = Word.WdAlertLevel.wdAlertsNone };
                doc = wordApp.Documents.Open(docxPath, ReadOnly: true, Visible: false);

                doc.ExportAsFixedFormat(pdfPath, Word.WdExportFormat.wdExportFormatPDF);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    "تعذر التحويل إلى PDF. تأكد من تثبيت Microsoft Word على هذا الجهاز.", ex);
            }
            finally
            {
                if (doc != null)
                {
                    doc.Close(false);
                    Marshal.ReleaseComObject(doc);
                }
                if (wordApp != null)
                {
                    wordApp.Quit(false);
                    Marshal.ReleaseComObject(wordApp);
                }
            }
        }
    }
}
