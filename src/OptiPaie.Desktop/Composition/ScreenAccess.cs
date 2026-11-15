using OptiPaie.Core.Security;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// The one place a screen asks "may the signed-in role do this action?" — reading the SAME
    /// <see cref="IAccessContext"/> the services enforce with and the navigation hides from, through
    /// the SAME <see cref="AccessPolicy"/>. Three places, one truth. When enforcement is off (no
    /// login gate) or no context is wired, everything is permitted — the UI looks exactly as before.
    /// View models expose booleans from here and bind button Visibility to them, so a button that
    /// cannot work is never on the screen.
    /// </summary>
    public static class ScreenAccess
    {
        /// <summary>May the role create / edit / delete on this module?</summary>
        public static bool CanEdit(AppServices services, string moduleKey)
        {
            IAccessContext a = services != null ? services.Authorization : null;
            if (a == null || !a.IsEnforced) return true;
            return AccessPolicy.CanEdit(a.CurrentRole, moduleKey);
        }

        /// <summary>May the role print / export this module's documents?</summary>
        public static bool CanExport(AppServices services, string moduleKey)
        {
            IAccessContext a = services != null ? services.Authorization : null;
            if (a == null || !a.IsEnforced) return true;
            return AccessPolicy.CanExport(a.CurrentRole, moduleKey);
        }
    }
}
