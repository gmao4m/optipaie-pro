using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// The complete, immutable output of one payslip calculation: the statutory
    /// totals, the computed lines, the audit trace, the engine/legal versions and
    /// any validation/warning messages. <see cref="IsSuccess"/> is false when the
    /// engine produced an error (validation or verification failure).
    /// </summary>
    public sealed class PayrollResult
    {
        /// <summary>The statutory totals (Brut → Net).</summary>
        public PayrollTotals Totals { get; }

        /// <summary>The computed payslip lines, in display order.</summary>
        public IReadOnlyList<PayrollLineResult> Lines { get; }

        /// <summary>The step-by-step calculation trace ("explain calculation").</summary>
        public IReadOnlyList<PayrollCalculationStep> Trace { get; }

        /// <summary>Validation, warning and informational messages.</summary>
        public IReadOnlyList<PayrollMessage> Messages { get; }

        /// <summary>True when there are no error-severity messages.</summary>
        public bool IsSuccess { get; }

        /// <summary>Version of the engine binary that produced this result.</summary>
        public string EngineVersion { get; }

        /// <summary>Version of the legal profile applied (e.g. "DZ-2026").</summary>
        public string LegalVersion { get; }

        /// <summary>Version of the calculation algorithm.</summary>
        public string CalculationVersion { get; }

        /// <summary>UTC timestamp when the calculation was performed.</summary>
        public DateTime CalculatedAtUtc { get; }

        /// <summary>Error-severity messages.</summary>
        public IEnumerable<PayrollMessage> Errors =>
            Messages.Where(m => m.Severity == PayrollMessageSeverity.Error);

        /// <summary>Warning-severity messages.</summary>
        public IEnumerable<PayrollMessage> Warnings =>
            Messages.Where(m => m.Severity == PayrollMessageSeverity.Warning);

        /// <summary>Creates an immutable payroll result, defensively copying the collections.</summary>
        public PayrollResult(
            PayrollTotals totals,
            IEnumerable<PayrollLineResult> lines,
            IEnumerable<PayrollCalculationStep> trace,
            IEnumerable<PayrollMessage> messages,
            string engineVersion,
            string legalVersion,
            string calculationVersion,
            DateTime calculatedAtUtc)
        {
            Totals = totals ?? throw new ArgumentNullException(nameof(totals));
            Lines = new ReadOnlyCollection<PayrollLineResult>((lines ?? Enumerable.Empty<PayrollLineResult>()).ToList());
            Trace = new ReadOnlyCollection<PayrollCalculationStep>((trace ?? Enumerable.Empty<PayrollCalculationStep>()).ToList());
            Messages = new ReadOnlyCollection<PayrollMessage>((messages ?? Enumerable.Empty<PayrollMessage>()).ToList());
            EngineVersion = engineVersion ?? string.Empty;
            LegalVersion = legalVersion ?? string.Empty;
            CalculationVersion = calculationVersion ?? string.Empty;
            CalculatedAtUtc = calculatedAtUtc;
            IsSuccess = !Messages.Any(m => m.Severity == PayrollMessageSeverity.Error);
        }

        /// <summary>
        /// Builds a failed result (zero totals, no lines) carrying the blocking
        /// messages — used when validation stops the calculation.
        /// </summary>
        public static PayrollResult Failed(
            IEnumerable<PayrollMessage> messages,
            string engineVersion,
            string legalVersion,
            string calculationVersion)
        {
            var zero = new PayrollTotals(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
            return new PayrollResult(
                zero,
                Enumerable.Empty<PayrollLineResult>(),
                Enumerable.Empty<PayrollCalculationStep>(),
                messages,
                engineVersion,
                legalVersion,
                calculationVersion,
                DateTime.UtcNow);
        }
    }
}
