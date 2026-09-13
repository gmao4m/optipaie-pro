using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OptiPaie.Desktop.Common
{
    /// <summary>
    /// Non-fatal error dialog for a caught UI-thread exception. Built ENTIRELY in code with no theme
    /// or <c>{StaticResource}</c> dependency, so it can display even when a resource / XAML failure is
    /// the very thing being reported. Shows the real exception type and message (not a blank
    /// "une erreur est survenue"), keeps the log path visible, and offers a one-click copy of the full
    /// technical detail (type · message · stack · inner exceptions) for support. The app stays open.
    /// </summary>
    internal static class CrashDialog
    {
        /// <param name="ex">The caught exception (its type + message are shown).</param>
        /// <param name="logDir">The crash-log directory, shown to the user.</param>
        /// <param name="fullDetails">The full dump copied by the « Copier les détails » button.</param>
        public static void Show(Exception ex, string logDir, string fullDetails)
        {
            try
            {
                string typeAndMessage = ex == null
                    ? "(no exception object)"
                    : ex.GetType().FullName + Environment.NewLine + ex.Message;

                var panel = new StackPanel { Margin = new Thickness(18) };

                panel.Children.Add(new TextBlock
                {
                    Text = "حدث خطأ تقني ولم يُغلق البرنامج." + Environment.NewLine +
                           "Une erreur technique est survenue ; l'application reste ouverte.",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var detail = new TextBox
                {
                    Text = typeAndMessage,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    Height = 120,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                panel.Children.Add(detail);

                panel.Children.Add(new TextBlock
                {
                    Text = "السجل / Journal : " + logDir,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Opacity = 0.75,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                var copy = new Button
                {
                    Content = "نسخ التفاصيل / Copier les détails",
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var ok = new Button { Content = "OK", Padding = new Thickness(24, 4, 24, 4), IsDefault = true };
                buttons.Children.Add(copy);
                buttons.Children.Add(ok);
                panel.Children.Add(buttons);

                var window = new Window
                {
                    Title = "OptiPaie PRO",
                    Content = panel,
                    Width = 560,
                    SizeToContent = SizeToContent.Height,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = true
                };

                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(string.IsNullOrEmpty(fullDetails) ? typeAndMessage : fullDetails); }
                    catch { /* clipboard may be locked by another app — ignore */ }
                };
                ok.Click += (s, e) => window.Close();

                window.ShowDialog();
            }
            catch
            {
                // Showing the dialog must NEVER itself crash the crash handler.
            }
        }
    }
}
