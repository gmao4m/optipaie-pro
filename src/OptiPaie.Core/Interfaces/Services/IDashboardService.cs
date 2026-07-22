using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Builds the executive dashboard snapshot by aggregating every HR module. Purely
    /// read-only; it consumes the other services and never modifies data or payroll.
    /// </summary>
    public interface IDashboardService
    {
        /// <summary>
        /// Aggregates a company-wide snapshot. <paramref name="expiryWindowDays"/> is the
        /// horizon for "expiring soon" and the deadlines widget.
        /// </summary>
        DashboardSnapshot Build(int expiryWindowDays = 30);
    }
}
