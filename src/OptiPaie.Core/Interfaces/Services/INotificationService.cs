using System.Collections.Generic;
using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The central notification engine: gathers time-sensitive alerts from every module
    /// (contract expiries, pending leave, upcoming trainings, …) into one ranked list for
    /// the shell's bell. Read-only; never touches payroll.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// The current notifications for ONE company, most urgent (then soonest) first.
        /// <paramref name="companyId"/> is MANDATORY (throws for &lt;= 0) — the bell is strictly
        /// single-company, never an all-companies view (that would leak one client's employees
        /// into another's header).
        /// </summary>
        IReadOnlyList<Notification> GetNotifications(long companyId, int expiryWindowDays = 30);
    }
}
