using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A configurable leave type. Additive to the fixed <see cref="LeaveType"/> enum: existing
    /// requests keep their <c>LeaveType</c> (1..5) and are read exactly as before; new requests
    /// reference a definition by id. <see cref="BaseType"/> maps a definition onto the legacy
    /// <c>LeaveRequests.Type</c> column so its <c>CHECK (Type IN 1..5)</c> is never touched — no
    /// destructive rebuild. A definition carries the two indicators the spec requires
    /// (<see cref="PaymentCategory"/>, <see cref="DecrementsAnnualBalance"/>) plus an optional
    /// pre-filled legal duration.
    /// </summary>
    public sealed class LeaveTypeDefinition : EntityBase
    {
        /// <summary>Null = a global default type (applies to every company); otherwise company-specific.</summary>
        public long? CompanyId { get; set; }

        /// <summary>Stable machine code (e.g. ANNUAL, SICK, UNPAID, MATERNITY, FAMILY_MARRIAGE, PILGRIMAGE).</summary>
        public string Code { get; set; }

        public string LabelAr { get; set; }
        public string LabelFr { get; set; }

        /// <summary>The legacy 1..5 value written to <c>LeaveRequests.Type</c> (keeps the existing CHECK satisfied).</summary>
        public LeaveType BaseType { get; set; }

        public PaymentCategory PaymentCategory { get; set; } = PaymentCategory.EmployerPaid;

        /// <summary>True when the type consumes the annual-leave entitlement.</summary>
        public bool DecrementsAnnualBalance { get; set; }

        /// <summary>Pre-filled legal duration in days when the law fixes one (e.g. 3 for a family event, 150 for maternity); else null.</summary>
        public decimal? LegalDurationDays { get; set; }

        /// <summary>True for a right granted once in a career (e.g. pilgrimage).</summary>
        public bool OncePerCareer { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public bool IsDeleted { get; set; }
    }
}
