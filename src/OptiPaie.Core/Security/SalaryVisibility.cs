using System;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// The single salary-masking rule, applied at every surface that could show an identified
    /// employee's pay (screens, grids, PDFs, exports, reports, documents). A role that lacks
    /// <see cref="Permission.ViewSalary"/> (read-only) receives the <see cref="Masked"/> placeholder
    /// instead of a figure — the SAME gate everywhere, so a role that cannot see a salary cannot see
    /// it through any door. When enforcement is off / no context is wired, salaries are shown
    /// (existing installs look exactly as before).
    /// </summary>
    public static class SalaryVisibility
    {
        /// <summary>What a masked salary looks like — never a number, so it can never be mistaken for pay.</summary>
        public const string Masked = "—";

        /// <summary>Whether the context may see an identified employee's pay. Gate off / null ⇒ yes.</summary>
        public static bool CanSee(IAccessContext context) =>
            context == null || !context.IsEnforced || AccessPolicy.CanSeeSalary(context.CurrentRole);

        /// <summary>
        /// Formats an amount for display, or returns <see cref="Masked"/> when the role may not see
        /// pay. The one function every salary-bearing surface routes its figure through.
        /// </summary>
        public static string Text(decimal amount, IAccessContext context, IFormatProvider culture, string format = "N2") =>
            CanSee(context) ? amount.ToString(format, culture) : Masked;

        /// <summary>As <see cref="Text(decimal, IAccessContext, IFormatProvider, string)"/>, from an explicit visibility flag.</summary>
        public static string Text(decimal amount, bool canSee, IFormatProvider culture, string format = "N2") =>
            canSee ? amount.ToString(format, culture) : Masked;
    }
}
