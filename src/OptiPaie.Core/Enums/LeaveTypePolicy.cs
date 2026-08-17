namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The explicit, centralised policy of each leave type — kept in ONE place (not
    /// scattered across the service) so a type's meaning is unambiguous and testable.
    /// Every type carries two indicators, exactly as the spec requires:
    ///   • whether it CONSUMES the annual-leave balance (only Congé annuel does);
    ///   • whether it is PAID (only Congé sans solde is not).
    /// <para>
    /// New types cannot be added without loosening the <c>CHECK (Type IN 1..5)</c> the
    /// production schema puts on <c>LeaveRequests.Type</c> (which would need a destructive
    /// table rebuild), so the set stays these five and the policy is expressed here.
    /// </para>
    /// </summary>
    public static class LeaveTypePolicy
    {
        /// <summary>True when the type consumes the annual-leave entitlement (Congé annuel only).</summary>
        public static bool DecrementsAnnualBalance(LeaveType type) => type == LeaveType.Annual;

        /// <summary>True when the days are paid (salary kept intact) — everything except Congé sans solde.</summary>
        public static bool IsPaid(LeaveType type) => type != LeaveType.Unpaid;
    }
}
