using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One entry on an employee's career timeline: a promotion (old -> new position) or a
    /// reward/bonus. A promotion may be linked to the review that justified it; logging it
    /// here only PROMPTS a contract amendment in the Contracts module — it never edits a
    /// contract, and it never touches payroll.
    /// </summary>
    public sealed class PerformanceCareerEvent : EntityBase
    {
        public long EmployeeId { get; set; }

        public CareerEventType EventType { get; set; }

        public DateTime EventDate { get; set; }

        /// <summary>Position before a promotion.</summary>
        public string OldPosition { get; set; }

        /// <summary>Position after a promotion.</summary>
        public string NewPosition { get; set; }

        /// <summary>Reward amount (for a reward event).</summary>
        public decimal? Amount { get; set; }

        /// <summary>Reward category label (e.g. "Prime de rendement").</summary>
        public string RewardCategory { get; set; }

        public string Reason { get; set; }

        /// <summary>Review that justified this event, if any.</summary>
        public long? LinkedReviewId { get; set; }

        public bool IsDeleted { get; set; }
    }
}
