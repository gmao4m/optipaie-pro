using System.Windows;

namespace OptiPaie.Desktop.Behaviors
{
    /// <summary>
    /// Shared dialog chrome. Applied to every <see cref="Window"/> via an implicit style
    /// in App.xaml so it is automatic for present and future dialogs: caps a (non-maximized)
    /// window's height/width to a fraction of the screen work area, so a tall form can never
    /// grow past the visible screen. Combined with a ScrollViewer around the form body and a
    /// fixed footer, this guarantees the Save/Cancel buttons stay reachable on small screens.
    /// The main (maximized) window is left untouched.
    /// </summary>
    public static class DialogChrome
    {
        private const double MaxHeightFraction = 0.90;
        private const double MaxWidthFraction = 0.96;

        public static readonly DependencyProperty FitScreenProperty =
            DependencyProperty.RegisterAttached("FitScreen", typeof(bool), typeof(DialogChrome),
                new PropertyMetadata(false, OnFitScreenChanged));

        public static void SetFitScreen(DependencyObject o, bool v) => o.SetValue(FitScreenProperty, v);
        public static bool GetFitScreen(DependencyObject o) => (bool)o.GetValue(FitScreenProperty);

        private static void OnFitScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Window window) || !(bool)e.NewValue)
            {
                return;
            }

            // SourceInitialized runs before the first layout pass, so the cap is in place
            // before the window sizes itself to its content.
            window.SourceInitialized += (s, _) => Apply(window);
            if (window.IsInitialized)
            {
                Apply(window);
            }
        }

        private static void Apply(Window window)
        {
            // Never constrain the maximized main shell — only owned dialogs.
            if (window.WindowState == WindowState.Maximized)
            {
                return;
            }

            Rect work = SystemParameters.WorkArea;
            double maxHeight = work.Height * MaxHeightFraction;
            double maxWidth = work.Width * MaxWidthFraction;

            if (window.MaxHeight > maxHeight)
            {
                window.MaxHeight = maxHeight;
            }
            if (window.MaxWidth > maxWidth)
            {
                window.MaxWidth = maxWidth;
            }
        }
    }
}
