using System;
using System.Collections.Generic;
using System.Globalization;

namespace OptiPaie.Core.Certificates
{
    /// <summary>Everything needed to render an ATS certificate.</summary>
    public class AtsCertificateData
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public DateTime LastWorkedDate { get; set; }      // = DATEAT on the original tool
        public bool HasResumedWork { get; set; }
        public DateTime? ResumeDate { get; set; }          // set only if HasResumedWork = true
        public List<MonthlyContribution> Contributions { get; set; } = new List<MonthlyContribution>();
        public DateTime IssueDate { get; set; }
    }

    /// <summary>Everything needed to render a DRT certificate.</summary>
    public class DrtCertificateData
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public DateTime LastWorkedDate { get; set; }
        public bool HasResumedWork { get; set; }
        public DateTime? ResumeDate { get; set; }          // set only if HasResumedWork = true
        public DateTime IssueDate { get; set; }
    }

    /// <summary>Ported verbatim from the source ATS/DRT tool (AtsDrt.Domain.CertificateService).</summary>
    public class CertificateService
    {
        private readonly WeekendConfig _weekendConfig;

        public CertificateService(WeekendConfig weekendConfig)
        {
            _weekendConfig = weekendConfig ?? new WeekendConfig();
        }

        /// <summary>
        /// Builds ATS data. The original tool asks the same "resumed work or not"
        /// question on the ATS form as it does on DRT (Optbtn2OUI/Optbtn2NON in
        /// frmCotis) — this mirrors that exactly.
        /// </summary>
        public AtsCertificateData BuildAts(
            Company company,
            Employee employee,
            WorkStoppage stoppage,
            bool hasResumedWork,
            List<MonthlyContribution> contributions)
        {
            var data = new AtsCertificateData
            {
                Company = company,
                Employee = employee,
                LastWorkedDate = BusinessDayCalculator.PreviousBusinessDay(stoppage.StoppageDate, _weekendConfig),
                HasResumedWork = hasResumedWork,
                Contributions = contributions ?? new List<MonthlyContribution>(),
                IssueDate = DateTime.Today
            };

            if (hasResumedWork)
                data.ResumeDate = ComputeResumeDate(stoppage);

            return data;
        }

        public DrtCertificateData BuildDrt(
            Company company,
            Employee employee,
            WorkStoppage stoppage,
            bool hasResumedWork)
        {
            var data = new DrtCertificateData
            {
                Company = company,
                Employee = employee,
                LastWorkedDate = BusinessDayCalculator.PreviousBusinessDay(stoppage.StoppageDate, _weekendConfig),
                HasResumedWork = hasResumedWork,
                IssueDate = DateTime.Today
            };

            if (hasResumedWork)
                data.ResumeDate = ComputeResumeDate(stoppage);

            return data;
        }

        private DateTime ComputeResumeDate(WorkStoppage stoppage)
        {
            var rawResumeDate = stoppage.StoppageDate.AddDays(stoppage.NumberOfDays);
            return BusinessDayCalculator.NextBusinessDayOnOrAfter(rawResumeDate, _weekendConfig);
        }

        /// <summary>
        /// Generates the 12-row month grid used on the ATS certificate, starting from
        /// <paramref name="startDate"/> for <paramref name="numberOfMonths"/> months
        /// (1-12). Rows beyond numberOfMonths are marked IsActive=false — these print
        /// as "/" on the certificate, matching the original tool exactly.
        /// </summary>
        public List<MonthlyContribution> BuildEmptyMonthGrid(DateTime startDate, int numberOfMonths, bool useArabicMonthNames = false)
        {
            if (numberOfMonths < 1 || numberOfMonths > 12)
                throw new ArgumentOutOfRangeException(nameof(numberOfMonths), "Must be between 1 and 12.");

            var rows = new List<MonthlyContribution>();

            for (int i = 0; i < 12; i++)
            {
                var row = new MonthlyContribution { IsActive = i < numberOfMonths };
                if (row.IsActive)
                {
                    var monthDate = startDate.AddMonths(i);
                    row.MonthLabel = useArabicMonthNames
                        ? FormatArabicMonthLabel(monthDate)
                        : FormatFrenchMonthLabel(monthDate);
                }
                rows.Add(row);
            }

            return rows;
        }

        private static string FormatFrenchMonthLabel(DateTime date)
        {
            var frenchCulture = CultureInfo.GetCultureInfo("fr-FR");
            var label = date.ToString("MMMM yyyy", frenchCulture);
            return frenchCulture.TextInfo.ToTitleCase(label);
        }

        // Algerian Arabic convention for Gregorian month names (distinct from the
        // Mashriq convention like "يناير") — matches Algerian administrative documents.
        private static readonly string[] AlgerianArabicMonths =
        {
            "جانفي", "فيفري", "مارس", "أفريل", "ماي", "جوان",
            "جويلية", "أوت", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        private static string FormatArabicMonthLabel(DateTime date) =>
            $"{AlgerianArabicMonths[date.Month - 1]} {date.Year}";
    }
}
