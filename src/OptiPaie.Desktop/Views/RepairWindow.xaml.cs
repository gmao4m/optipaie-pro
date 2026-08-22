namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// Shown at startup when the installation is incomplete. Two modes, both in plain wording
    /// (no technical terms): "repair" offers a single « إصلاح » button that restores the install
    /// and reopens the app; "fallback" (shown only after a repair already failed) simply asks the
    /// user to contact support. <see cref="RepairRequested"/> is true when the user chose to repair.
    /// </summary>
    public partial class RepairWindow
    {
        public bool RepairRequested { get; private set; }

        public RepairWindow(bool fallback)
        {
            InitializeComponent();

            if (fallback)
            {
                TitleText.Text = "تعذّر إتمام الإصلاح";
                BodyText.Text = "يُرجى التواصل مع الدعم لإعادة تثبيت التطبيق.";
                ActionButton.Content = "إغلاق";
            }
            else
            {
                TitleText.Text = "التطبيق يحتاج إلى إصلاح بسيط";
                BodyText.Text = "اضغط « إصلاح » لإتمامه، وسيُعاد فتح التطبيق تلقائيًا.";
                ActionButton.Content = "إصلاح";
            }
        }

        private void ActionButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            RepairRequested = ActionButton.Content as string == "إصلاح";
            try { DialogResult = RepairRequested; } catch { Close(); }
        }
    }
}
