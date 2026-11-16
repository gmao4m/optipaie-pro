using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Payroll
{
    /// <summary>
    /// The pre-issue sanity checks (PART 2). These never change a calculation — the
    /// engine is authoritative — they only detect a payslip that is almost certainly
    /// the result of a mistake and must not be issued in silence (e.g. a paid salary
    /// with zero contribution, as happened with the owner's payslip #22). Each warning
    /// carries a bilingual message and, where relevant, the offending element name.
    /// </summary>
    public enum PayslipSanityCode
    {
        /// <summary>Gross &gt; 0 but the employee CNAS contribution is 0.</summary>
        ContributionZeroOnPaidSalary = 1,

        /// <summary>Net equals gross exactly (nothing was withheld).</summary>
        NetEqualsGross = 2,

        /// <summary>Two applied lines on the payslip carry the same name.</summary>
        DuplicateLineName = 3,

        /// <summary>A large gain is exempt from BOTH the contribution base and the taxable base.</summary>
        LargeExemptGain = 4,

        /// <summary>The contribution base is below the SNMG for a full-time month.</summary>
        BelowSnmgFullTime = 5
    }

    /// <summary>One sanity finding, with a bilingual message and the element it concerns.</summary>
    public sealed class PayslipSanityWarning
    {
        public PayslipSanityCode Code { get; }
        public string MessageFr { get; }
        public string MessageAr { get; }

        /// <summary>The offending element name, when the finding points at one. May be null.</summary>
        public string ElementName { get; }

        public PayslipSanityWarning(PayslipSanityCode code, string messageFr, string messageAr, string elementName = null)
        {
            Code = code;
            MessageFr = messageFr ?? string.Empty;
            MessageAr = messageAr ?? string.Empty;
            ElementName = elementName;
        }
    }

    /// <summary>The result of inspecting one payslip before issuing it.</summary>
    public sealed class PayslipSanityReport
    {
        public IReadOnlyList<PayslipSanityWarning> Warnings { get; }

        public bool HasWarnings => Warnings.Count > 0;

        public PayslipSanityReport(IReadOnlyList<PayslipSanityWarning> warnings)
        {
            Warnings = warnings ?? new List<PayslipSanityWarning>();
        }

        public static readonly PayslipSanityReport Clean =
            new PayslipSanityReport(new List<PayslipSanityWarning>());

        /// <summary>A stable, comma-separated list of the warning codes (for the audit trail).</summary>
        public string CodeList => string.Join(",", Warnings.Select(w => w.Code.ToString()));

        /// <summary>A French bullet summary of every finding.</summary>
        public string SummaryFr => string.Join(Environment.NewLine, Warnings.Select(w => " • " + w.MessageFr));

        /// <summary>An Arabic bullet summary of every finding.</summary>
        public string SummaryAr => string.Join(Environment.NewLine, Warnings.Select(w => " • " + w.MessageAr));
    }
}
