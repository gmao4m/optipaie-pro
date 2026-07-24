using System.Windows.Controls;
using OptiPaie.Desktop.Common;

namespace OptiPaie.Desktop.Views
{
    /// <summary>Performance reviews.</summary>
    public partial class PerformanceView : UserControl
    {
        public PerformanceView()
        {
            // Breadcrumbs bracket the XAML load and the first layout pass — the crash that
            // previously killed the app happened here (a read-only ProgressBar.Value binding
            // realised during layout with real data), so this pinpoints that phase on disk.
            CrashLog.Breadcrumb("PerformanceView: InitializeComponent start");
            InitializeComponent();
            CrashLog.Breadcrumb("PerformanceView: InitializeComponent done");
            Loaded += (s, e) => CrashLog.Breadcrumb("PerformanceView: Loaded (layout complete)");
        }
    }
}
