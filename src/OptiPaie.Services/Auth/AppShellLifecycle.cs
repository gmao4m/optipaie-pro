namespace OptiPaie.Services.Auth
{
    /// <summary>
    /// How the desktop app decides to exit — mirrors <c>System.Windows.ShutdownMode</c>
    /// with NO WPF dependency, so the startup/login lifecycle can be reasoned about in
    /// unit tests.
    /// </summary>
    public enum AppShutdownMode
    {
        /// <summary>WPF default: the process exits the moment the last window closes.</summary>
        OnLastWindowClose,

        /// <summary>The process exits only on an explicit request — never on a window close.</summary>
        OnExplicitShutdown
    }

    /// <summary>
    /// Single source of truth for the shutdown behaviour the app uses during the login gate.
    /// The WPF <c>App</c> reads this to set <c>Application.ShutdownMode</c>; tests assert it.
    /// <para>
    /// With <see cref="AppShutdownMode.OnLastWindowClose"/> (the WPF default) the process
    /// dies the instant the login window closes while it is the only open window — before
    /// MainWindow is created — which is the v1.26 production crash: a correct login silently
    /// closes the app. <see cref="AppShutdownMode.OnExplicitShutdown"/> keeps the process
    /// alive so the flow always reaches MainWindow.
    /// </para>
    /// </summary>
    public static class AppShellPolicy
    {
        public static AppShutdownMode ShutdownModeDuringGate => AppShutdownMode.OnExplicitShutdown;
    }

    /// <summary>
    /// A faithful, WPF-free model of the exact mechanism behind the login crash: the number
    /// of open windows combined with the shutdown mode. When the count reaches zero under
    /// <see cref="AppShutdownMode.OnLastWindowClose"/> the process terminates. Used to prove
    /// that the old mode kills the app when the login window closes on a successful sign-in,
    /// and that the fix does not.
    /// </summary>
    public sealed class WindowLifecycleModel
    {
        private readonly AppShutdownMode _mode;
        private int _open;

        public WindowLifecycleModel(AppShutdownMode mode)
        {
            _mode = mode;
        }

        /// <summary>True once the modelled process has exited.</summary>
        public bool Terminated { get; private set; }

        public int OpenWindows => _open;

        public void OpenWindow()
        {
            if (Terminated) return;
            _open++;
        }

        public void CloseWindow()
        {
            if (Terminated) return;
            if (_open > 0) _open--;
            if (_open == 0 && _mode == AppShutdownMode.OnLastWindowClose)
            {
                Terminated = true;
            }
        }
    }
}
