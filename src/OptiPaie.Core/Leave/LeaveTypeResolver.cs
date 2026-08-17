using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Leave
{
    /// <summary>
    /// The single source of truth for a request's two visible indicators — payment category and
    /// "decrements the annual balance" — resolved from the configurable type (by id) or, when the
    /// request predates configurable types, the legacy <see cref="LeaveType"/> policy. Used by the
    /// service AND the UI so the list, the form and the payroll effect can never disagree.
    /// </summary>
    public static class LeaveTypeResolver
    {
        public static PaymentCategory Category(long? leaveTypeId, LeaveType legacyType, IReadOnlyDictionary<long, LeaveTypeDefinition> types)
        {
            if (leaveTypeId.HasValue && types != null && types.TryGetValue(leaveTypeId.Value, out var def))
                return def.PaymentCategory;
            return LeaveTypePolicy.IsPaid(legacyType) ? PaymentCategory.EmployerPaid : PaymentCategory.Unpaid;
        }

        public static bool Decrements(long? leaveTypeId, LeaveType legacyType, IReadOnlyDictionary<long, LeaveTypeDefinition> types)
        {
            if (leaveTypeId.HasValue && types != null && types.TryGetValue(leaveTypeId.Value, out var def))
                return def.DecrementsAnnualBalance;
            return LeaveTypePolicy.DecrementsAnnualBalance(legacyType);
        }

        /// <summary>Localization key of the payment-category badge.</summary>
        public static string PaymentKey(PaymentCategory category)
        {
            switch (category)
            {
                case PaymentCategory.EmployerPaid: return "Leave_Pay_Employer";
                case PaymentCategory.SocialSecurity: return "Leave_Pay_Cnas";
                default: return "Leave_Pay_Unpaid";
            }
        }

        /// <summary>Semantic colour bucket of the payment badge (shared with the app pills).</summary>
        public static string PaymentKind(PaymentCategory category)
        {
            switch (category)
            {
                case PaymentCategory.EmployerPaid: return "success";
                case PaymentCategory.SocialSecurity: return "info";
                default: return "danger";
            }
        }

        public static string DecrementKey(bool decrements) => decrements ? "Leave_Decrements" : "Leave_NoDecrement";
        public static string DecrementKind(bool decrements) => decrements ? "warning" : "neutral";
    }
}
