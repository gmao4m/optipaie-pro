using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OptiPaie.Desktop.ViewModels;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// First-launch owner setup. Password entry stays in the view (PasswordBox is never bound), and
    /// copy/print of the recovery code are pure view operations handled here.
    /// </summary>
    public partial class OwnerSetupWindow
    {
        public OwnerSetupWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is OwnerSetupViewModel vm)
                {
                    vm.PasswordAccessor = () => PasswordBox.Password;
                    vm.ConfirmPasswordAccessor = () => ConfirmBox.Password;
                }
                EmailBox.Focus();
            };
        }

        private OwnerSetupViewModel ViewModel => DataContext as OwnerSetupViewModel;

        private void OnCopyCode(object sender, RoutedEventArgs e)
        {
            string code = ViewModel?.RecoveryCode;
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            try { Clipboard.SetText(code); }
            catch (Exception ex) { OptiPaie.Desktop.Common.CrashLog.Breadcrumb("Copy recovery code failed: " + ex.Message); }
        }

        private void OnPrintCode(object sender, RoutedEventArgs e)
        {
            string code = ViewModel?.RecoveryCode;
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
                    "Code de récupération du compte propriétaire / رمز استرجاع حساب المالك"))
                { FontSize = 13 });
                document.Blocks.Add(new Paragraph(new Run(code))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 20, 0, 20)
                });
                document.Blocks.Add(new Paragraph(new Run(
                    "Conservez ce code en lieu sûr. Il n'est affiché qu'une seule fois. / " +
                    "احفظ هذا الرمز في مكان آمن. يُعرض مرّة واحدة فقط."))
                { FontSize = 12 });

                IDocumentPaginatorSource paginator = document;
                dialog.PrintDocument(paginator.DocumentPaginator, "OptiPaie PRO — Recovery code");
            }
            catch (Exception ex)
            {
                OptiPaie.Desktop.Common.CrashLog.Breadcrumb("Print recovery code failed: " + ex.Message);
            }
        }
    }
}
