using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Dtos
{
    /// <summary>The kind of blocker that stops a DAS export. The screen maps it to a localized,
    /// one-sentence, accountant-readable message and groups the refusal list by this type.</summary>
    public enum DasBlockerType
    {
        // Per-employee — the accountant fixes the employee's file.
        NssMissing,
        NssMalformed,
        BirthDateMissing,
        NameNotAscii,
        AmountExceedsField,
        // Company-level.
        EmployerNumberMissing,
        EmployerNumberMalformed,
        NothingToDeclare,
        // Structural safety net — the built figures are internally inconsistent (should never
        // reach the user; if it does, no file is written).
        TotalsInconsistent,
        WorkerCountInconsistent,
        DacCrossCheckFailed
    }

    /// <summary>One blocking reason. Employee-level blockers carry the id (to open the fiche) and
    /// name; company/structural ones leave the id at 0. Detail holds optional context (e.g. "T2").</summary>
    public sealed class DasBlocker
    {
        public DasBlocker(DasBlockerType type, long employeeId, string employeeName, string detail)
        {
            Type = type;
            EmployeeId = employeeId;
            EmployeeName = employeeName ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public DasBlockerType Type { get; }
        public long EmployeeId { get; }
        public string EmployeeName { get; }
        public string Detail { get; }

        public bool IsEmployeeLevel => EmployeeId > 0;
    }

    /// <summary>
    /// Result of validating a company/year for DAS export. Valid ⇒ the files may be written.
    /// Otherwise <see cref="Blockers"/> lists every reason, nominatively where possible, so the
    /// screen can present an actionable, grouped list. No file is ever written while invalid.
    /// </summary>
    public sealed class DasExportValidation
    {
        public DasExportValidation(IReadOnlyList<DasBlocker> blockers)
        {
            Blockers = blockers ?? new List<DasBlocker>();
        }

        public IReadOnlyList<DasBlocker> Blockers { get; }

        public bool IsValid => Blockers.Count == 0;

        /// <summary>Distinct employees to correct (for a quick "N salariés à corriger" headline).</summary>
        public int EmployeesToFix =>
            Blockers.Where(b => b.IsEmployeeLevel).Select(b => b.EmployeeId).Distinct().Count();
    }
}
