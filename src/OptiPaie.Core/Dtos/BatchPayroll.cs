using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Dtos
{
    /// <summary>How serious a pre-run finding is for one employee.</summary>
    public enum BatchCheckSeverity
    {
        /// <summary>No problem — the employee will be processed.</summary>
        Ok = 0,

        /// <summary>Processed, but something may distort the result (e.g. no attendance).</summary>
        Warning = 1,

        /// <summary>Cannot be processed safely — skipped, never silently paid wrong.</summary>
        Blocking = 2
    }

    /// <summary>One employee's readiness for a batch run.</summary>
    public sealed class BatchEmployeeCheck
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public BatchCheckSeverity Severity { get; set; }

        /// <summary>Why it is blocked or warned; empty when Ok.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>True unless the finding is blocking.</summary>
        public bool WillProcess => Severity != BatchCheckSeverity.Blocking;
    }

    /// <summary>
    /// The pre-run plan: every active employee of the company with its readiness, so the
    /// user sees problems BEFORE running, not after.
    /// </summary>
    public sealed class BatchPayrollPlan
    {
        public long CompanyId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public bool AlreadyArchived { get; set; }
        public IReadOnlyList<BatchEmployeeCheck> Employees { get; set; } = new List<BatchEmployeeCheck>();

        public int TotalActive => Employees.Count;
        public int Ready => Employees.Count(e => e.WillProcess);
        public int Blocked => Employees.Count(e => e.Severity == BatchCheckSeverity.Blocking);
        public int Warnings => Employees.Count(e => e.Severity == BatchCheckSeverity.Warning);
    }

    /// <summary>What happened to one employee in a completed batch run.</summary>
    public enum BatchOutcome
    {
        Succeeded = 0,
        Skipped = 1,
        Failed = 2
    }

    public sealed class BatchEmployeeResult
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public BatchOutcome Outcome { get; set; }

        /// <summary>Reason for a skip/failure, or a warning note on a success; empty otherwise.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>The archived payslip id (0 when not generated).</summary>
        public long PayslipId { get; set; }

        public decimal Net { get; set; }
    }

    /// <summary>
    /// The result of a batch run — one shared payroll run record for the period, with a
    /// per-employee outcome so a partial failure is never mistaken for a complete run.
    /// </summary>
    public sealed class BatchPayrollResult
    {
        public long CompanyId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>The single PayrollRun the payslips were archived into (0 if none created).</summary>
        public long RunId { get; set; }

        public IReadOnlyList<BatchEmployeeResult> Results { get; set; } = new List<BatchEmployeeResult>();

        public int Succeeded => Results.Count(r => r.Outcome == BatchOutcome.Succeeded);
        public int Skipped => Results.Count(r => r.Outcome == BatchOutcome.Skipped);
        public int Failed => Results.Count(r => r.Outcome == BatchOutcome.Failed);
        public bool IsComplete => Failed == 0 && Skipped == 0;
    }

    /// <summary>Progress tick during a run, so the UI never freezes without feedback.</summary>
    public sealed class BatchProgress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentEmployee { get; set; } = string.Empty;
    }
}
