using System.Windows;
using OptiPaie.Desktop.Localization;

namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// A small themed single-field prompt (used for the mandatory refusal/cancellation motive).
    /// Returns the entered text via <see cref="Value"/> and <see cref="Window.DialogResult"/>.
    /// </summary>
    public partial class TextPromptWindow : Window
    {
        private readonly bool _required;

        public TextPromptWindow(string title, string label, string initial, bool required)
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = label;
            InputBox.Text = initial ?? string.Empty;
            _required = required;
            Loaded += (s, e) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        /// <summary>The entered text, trimmed.</summary>
        public string Value => (InputBox.Text ?? string.Empty).Trim();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_required && string.IsNullOrWhiteSpace(InputBox.Text))
            {
                ErrorText.Text = TranslationSource.Instance["Common_Required"];
                ErrorText.Visibility = Visibility.Visible;
                InputBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
