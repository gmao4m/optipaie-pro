using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OptiPaie.Services.Certificates
{
    /// <summary>
    /// Fills bookmarks in an existing .docx template with real values. Ported verbatim
    /// from the source ATS/DRT tool (AtsDrt.Documents.DocxBookmarkFiller).
    ///
    /// The templates (Assets/AtsDrtTemplates/*.docx) are the actual official CNAS forms —
    /// this class does NOT redraw or recreate the document, it only inserts text at
    /// pre-existing bookmark positions. The result is therefore visually identical to the
    /// real paper form, because it IS the real form, just filled in.
    ///
    /// DRT has two separate, independent template files — DRT_Template_FR.docx and
    /// DRT_Template_AR.docx — each a standalone single page with its own bookmarks (same
    /// bookmark names in both, so CertificateBookmarkMapper.MapDrt works unchanged for either).
    /// </summary>
    public static class DocxBookmarkFiller
    {
        /// <summary>
        /// Copies templatePath to outputPath, then fills every bookmark in
        /// <paramref name="values"/> that exists in the document. Bookmarks not present in
        /// <paramref name="values"/> are left untouched (empty).
        /// </summary>
        public static void Fill(string templatePath, string outputPath, IDictionary<string, string> values)
        {
            File.Copy(templatePath, outputPath, overwrite: true);

            using (var doc = WordprocessingDocument.Open(outputPath, true))
            {
                var body = doc.MainDocumentPart.Document.Body;

                // ToList(): we're inserting siblings while iterating, so snapshot first.
                var bookmarkStarts = body.Descendants<BookmarkStart>().ToList();

                foreach (var bookmark in bookmarkStarts)
                {
                    if (bookmark.Name == null) continue;
                    if (!values.TryGetValue(bookmark.Name.Value, out var text)) continue;
                    if (string.IsNullOrEmpty(text)) continue;

                    var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                    bookmark.InsertAfterSelf(run);
                }

                doc.MainDocumentPart.Document.Save();
            }
        }

        /// <summary>
        /// Appends a new page at the end of the document containing free-text notes the
        /// user typed in the review screen. Does not touch the certificate page itself —
        /// this is purely additive.
        /// </summary>
        public static void AppendNotesPage(string docxPath, string noteText)
        {
            if (string.IsNullOrWhiteSpace(noteText)) return;

            using (var doc = WordprocessingDocument.Open(docxPath, true))
            {
                var body = doc.MainDocumentPart.Document.Body;

                var pageBreakParagraph = new Paragraph(new Run(new Break { Type = BreakValues.Page }));
                var headingParagraph = new Paragraph(new Run(new Text("ملاحظات إضافية / Notes additionnelles")
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
                var noteParagraph = new Paragraph(new Run(new Text(noteText) { Space = SpaceProcessingModeValues.Preserve }));

                body.AppendChild(pageBreakParagraph);
                body.AppendChild(headingParagraph);
                body.AppendChild(noteParagraph);

                doc.MainDocumentPart.Document.Save();
            }
        }
    }
}
