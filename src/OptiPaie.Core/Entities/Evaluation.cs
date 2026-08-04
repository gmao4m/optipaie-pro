using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One employee's evaluation within a period. The overall <see cref="TotalScore"/> is a
    /// normalised 0-100 value (so stars / 20 / percent criteria compare fairly); the
    /// classification band is derived from it. Its scored lines are <see cref="EvaluationScore"/>.
    /// </summary>
    public sealed class Evaluation : EntityBase
    {
        public long PeriodId { get; set; }

        public long EmployeeId { get; set; }

        /// <summary>Template the criteria were snapshotted from, if any.</summary>
        public long? TemplateId { get; set; }

        /// <summary>Employee's department at evaluation time (snapshot, for department reports).</summary>
        public string Department { get; set; }

        /// <summary>Weighting mode in effect for this evaluation (snapshot of the template's).</summary>
        public WeightingMode WeightingMode { get; set; } = WeightingMode.Simple;

        /// <summary>Normalised overall score, 0-100.</summary>
        public decimal TotalScore { get; set; }

        public EvaluationStatus Status { get; set; } = EvaluationStatus.Pending;

        public DateTime? EvaluatedDate { get; set; }

        /// <summary>Name of the person who evaluated.</summary>
        public string Evaluator { get; set; }

        /// <summary>Overall observation / note for the employee.</summary>
        public string Note { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
