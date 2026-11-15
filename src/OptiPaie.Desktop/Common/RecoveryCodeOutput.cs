using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OptiPaie.Desktop.Common
{
    /// <summary>
    /// Copy / print of a one-time recovery code — pure view operations shared by every screen that
    /// displays the code (owner setup, backfill, recovery). Never throws; a failure is a breadcrumb,
    /// never a crash.
    /// </summary>
    public static class RecoveryCodeOutput
    {
        public static void Copy(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            try { Clipboard.SetText(code); }
            catch (Exception ex) { CrashLog.Breadcrumb("Copy recovery code failed: " + ex.Message); }
        }

        public static void Print(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            try
            {
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var document = new FlowDocument
                {
                    PagePadding = new Thickness(60),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    ColumnWidth = double.PositiveInfinity
                };
                document.Blocks.Add(new Paragraph(new Run("OptiPaie PRO")) { FontSize = 20, FontWeight = FontWeights.Bold });
                document.Blocks.Add(new Paragraph(new Run(
                    "Code de récupération du compte propriétaire / رمز استرجاع حساب المالك")) { FontSize = 13 });
                document.Blocks.Add(new Paragraph(new Run(code))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 20, 0, 20)
                });
                document.Blocks.Add(new Paragraph(new Run(
                    "Conservez ce code en lieu sûr. Il n'est affiché qu'une seule fois. / " +
                    "احفظ هذا الرمز في مكان آمن. يُعرض مرّة واحدة فقط.")) { FontSize = 12 });

                IDocumentPaginatorSource paginator = document;
                dialog.PrintDocument(paginator.DocumentPaginator, "OptiPaie PRO — Recovery code");
            }
            catch (Exception ex)
            {
                CrashLog.Breadcrumb("Print recovery code failed: " + ex.Message);
            }
        }
    }
}
