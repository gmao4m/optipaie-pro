using System.Collections.Generic;
using System.Linq;
using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Payroll
{
    /// <summary>
    /// Decides whether the pre-run attendance review is worth stopping for. One rule, used by
    /// both the single-employee save and the whole-company batch, so they never disagree.
    ///
    /// The review opens only when:
    ///   • a real number changes a payslip — unjustified absence, a half day, or any deduction; or
    ///   • attendance is genuinely in use (someone has records) yet is MISSING for someone else —
    ///     the "you forgot to enter so-and-so" anomaly that only shows up across a company run.
    ///
    /// A company that records NO attendance at all is NOT stopped: full-month pay is its intended
    /// model, not an error, so the run goes straight through instead of showing a table of zeros.
    /// This is a gate on a confirmation dialog only — it never changes a single computed figure.
    /// </summary>
    public static class PayrollReviewPolicy
    {
        public static bool NeedsReview(IReadOnlyCollection<PayrollReviewRow> rows)
        {
            if (rows == null || rows.Count == 0) return false;

            bool anyDeduction = rows.Any(r => r.Absent > 0m || r.Half > 0m || r.Deduction > 0m);
            bool anyRecorded = rows.Any(r => !r.NoAttendance);
            bool anyMissing = rows.Any(r => r.NoAttendance);

            return anyDeduction || (anyRecorded && anyMissing);
        }
    }
}
