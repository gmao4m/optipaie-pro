using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>A job posting with candidate counts (derived).</summary>
    public sealed class JobPostingSummary
    {
        public long PostingId { get; set; }
        public long CompanyId { get; set; }
        public string Title { get; set; }
        public string Department { get; set; }
        public JobStatus Status { get; set; }
        public DateTime OpenDate { get; set; }
        public int Positions { get; set; }

        public int CandidateCount { get; set; }
        public int HiredCount { get; set; }
    }

    /// <summary>Result of hiring a candidate.</summary>
    public sealed class HireResult
    {
        /// <summary>The new shared employee's id.</summary>
        public long EmployeeId { get; set; }

        /// <summary>True when the posting became Filled as a result.</summary>
        public bool PostingFilled { get; set; }
    }
}
