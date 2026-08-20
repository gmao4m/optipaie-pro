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
        /// Aggregates the snapshot for ONE company. <paramref name="companyId"/> is MANDATORY
        /// (throws for &lt;= 0) — the dashboard is strictly single-company, never an all-companies
        /// total. <paramref name="expiryWindowDays"/> is the horizon for "expiring soon".
        /// </summary>
        DashboardSnapshot Build(long companyId, int expiryWindowDays = 30);
    }
}
