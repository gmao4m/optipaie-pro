namespace OptiPaie.Desktop.Views
{
    /// <summary>
    /// The blocking startup company gate: pick a company (single click opens it) or create
    /// the first one. It cannot be bypassed — closing it without a choice returns to the app's
    /// exit path rather than opening any data screen.
    /// </summary>
    public partial class CompanySelectionWindow
    {
        public CompanySelectionWindow()
        {
            InitializeComponent();
        }
    }
}
