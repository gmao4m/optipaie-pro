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
        /// <summary>All current notifications, most urgent (then soonest) first.</summary>
        IReadOnlyList<Notification> GetNotifications(int expiryWindowDays = 30);
    }
}
