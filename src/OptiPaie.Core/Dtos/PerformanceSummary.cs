using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>A review with its derived rating and the employee name (from the shared record).</summary>
    public sealed class PerformanceSummary
    {
        public long ReviewId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }

        public int PeriodYear { get; set; }
        public string PeriodLabel { get; set; }
        public PerformanceStatus Status { get; set; }
        public DateTime ReviewDate { get; set; }
        public string Reviewer { get; set; }

        public decimal OverallScore { get; set; }

        /// <summary>Rating band derived from the score (Excellent, Très bien, …).</summary>
        public string Rating { get; set; }

        public int CriteriaCount { get; set; }
    }

    /// <summary>
    /// A review with everything needed to display or print it: the criteria and the
    /// attendance context pulled live from the Attendance module (or null when that
    /// module is not enabled).
    /// </summary>
    public sealed class PerformanceDetail
    {
        public PerformanceReview Review { get; set; }
        public IReadOnlyList<PerformanceCriterion> Criteria { get; set; } = new List<PerformanceCriterion>();

        /// <summary>Attendance figures for the period, or null when unavailable.</summary>
        public AttendanceContext Attendance { get; set; }

        public string Rating { get; set; }
    }

    /// <summary>Read-only attendance snapshot shown inside a review — never stored here.</summary>
    public sealed class AttendanceContext
    {
        public int AbsentDays { get; set; }
        public int LateCount { get; set; }
        public decimal WorkedHours { get; set; }
        public decimal OvertimeHours { get; set; }
    }
}
