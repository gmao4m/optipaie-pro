using System.Windows;
using System.Windows.Controls.Primitives;

namespace OptiPaie.Desktop.Behaviors
{
    /// <summary>
    /// Attached behavior: a button carrying <c>CopyBehavior.Text</c> copies that text to the
    /// clipboard when clicked — so the accountant never has to select a figure by hand when
    /// filling the CNAS portal.
    /// </summary>
    public static class CopyBehavior
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(CopyBehavior), new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
        public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ButtonBase button))
            {
                return;
            }

            button.Click -= OnClick;
            button.Click += OnClick;
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            string text = GetText((DependencyObject)sender);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // The clipboard can be transiently locked by another process — ignore.
            }
        }
    }
}
