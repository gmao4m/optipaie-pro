using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>A training session with participant counts (derived).</summary>
    public sealed class TrainingSummary
    {
        public long SessionId { get; set; }
        public long CompanyId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Provider { get; set; }
        public TrainingStatus Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal Cost { get; set; }

        public int ParticipantCount { get; set; }
        public int CompletedCount { get; set; }
    }

    /// <summary>A participant row with the employee name resolved from the shared record.</summary>
    public sealed class TrainingParticipantSummary
    {
        public long ParticipantId { get; set; }
        public long SessionId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public TrainingResult Result { get; set; }
        public string Score { get; set; }
        public string CertificateRef { get; set; }
    }

    /// <summary>One line of an employee's training history (for the employee-centric view).</summary>
    public sealed class TrainingHistoryItem
    {
        public long SessionId { get; set; }
        public string Title { get; set; }
        public string Provider { get; set; }
        public DateTime StartDate { get; set; }
        public TrainingResult Result { get; set; }
        public string CertificateRef { get; set; }
    }
}
