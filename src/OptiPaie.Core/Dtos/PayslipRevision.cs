using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Inputs for correcting an archived payslip: the original to supersede, the amended payroll
    /// request recomputed by the engine, and the mandatory typed reason recorded for audit.
    /// </summary>
    public sealed class PayslipCorrectionRequest
    {
        /// <summary>The id of the Active payslip being corrected (it becomes Superseded).</summary>
        public long OriginalPayslipId { get; set; }

        /// <summary>The amended generation request — same shape the worksheet builds — recomputed
        /// by the unchanged engine to produce the corrected version.</summary>
        public PayrollGenerationRequest Amended { get; set; }

        /// <summary>Mandatory reason for the correction (stored on the superseded original).</summary>
        public string Reason { get; set; }
    }

    /// <summary>One non-active (superseded or cancelled) payslip, for the history view.</summary>
    public sealed class PayslipHistoryRow
    {
        public long PayslipId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal NetSalaire { get; set; }
        public PayslipStatus Status { get; set; }
        public string Reason { get; set; }
        public string RevisedByUser { get; set; }
        public DateTime? RevisedAtUtc { get; set; }
    }
}
