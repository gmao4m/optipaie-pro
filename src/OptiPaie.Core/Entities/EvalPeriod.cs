using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>A recurring evaluation window (weekly / monthly / yearly) that groups the
    /// employee evaluations done for it.</summary>
    public sealed class EvalPeriod : EntityBase
    {
        public long CompanyId { get; set; }

        public string Name { get; set; }

        public PeriodCadence Cadence { get; set; } = PeriodCadence.Monthly;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public PeriodStatus Status { get; set; } = PeriodStatus.Open;

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
