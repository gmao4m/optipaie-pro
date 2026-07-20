using System.Windows;

namespace OptiPaie.Admin.Common
{
    public static class Dialogs
    {
        public static void Error(string message) =>
            MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

        public static void Info(string message, string caption = "OptiPaie PRO Admin") =>
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public static bool Confirm(string message) =>
            MessageBox.Show(message, "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
