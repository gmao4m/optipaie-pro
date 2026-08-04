using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A single 👍 / 👎 fact logged about an employee as it happens. Shown next to the
    /// evaluation screen so the behavioural criteria are scored on real events, not memory —
    /// the core of a fair, unbiased evaluation.
    /// </summary>
    public sealed class BehaviorLog : EntityBase
    {
        public long CompanyId { get; set; }

        public long EmployeeId { get; set; }

        /// <summary>True = positive (👍), false = negative (👎).</summary>
        public bool IsPositive { get; set; }

        /// <summary>Short note describing what happened (e.g. "a terminé avant l'échéance").</summary>
        public string Note { get; set; }

        /// <summary>When the behaviour occurred.</summary>
        public DateTime OccurredAt { get; set; }

        public bool IsDeleted { get; set; }
    }
}
