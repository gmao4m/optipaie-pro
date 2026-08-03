using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Read-only readiness report for the CNAS declarations (DAC/DAS) of ONE company for a
    /// given year. It lists the data blockers that must be corrected before a declaration can
    /// be produced. Produced by <c>ICnasDeclarationService</c> — no calculation, no writing.
    /// </summary>
    public sealed class CnasReadinessReport
    {
        public CnasReadinessReport(
            long companyId,
            int year,
            string cnasEmployerNumber,
            bool employerNumberMissing,
            bool employerNumberMalformed,
            IReadOnlyList<CnasEmployeeReadiness> employees)
        {
            CompanyId = companyId;
            Year = year;
            CnasEmployerNumber = cnasEmployerNumber;
            EmployerNumberMissing = employerNumberMissing;
            EmployerNumberMalformed = employerNumberMalformed;
            Employees = employees ?? new List<CnasEmployeeReadiness>();
        }

        public long CompanyId { get; }
        public int Year { get; }

        /// <summary>The company's CNAS employer number as stored (may be empty/malformed).</summary>
        public string CnasEmployerNumber { get; }

        /// <summary>The company has no CNAS employer number.</summary>
        public bool EmployerNumberMissing { get; }

        /// <summary>The CNAS employer number is present but not the expected digit count.</summary>
        public bool EmployerNumberMalformed { get; }

        /// <summary>All employees relevant to the declared year (active, or paid at least once).</summary>
        public IReadOnlyList<CnasEmployeeReadiness> Employees { get; }

        /// <summary>Only the employees that have at least one blocking data issue.</summary>
        public IReadOnlyList<CnasEmployeeReadiness> EmployeesWithIssues =>
            Employees.Where(e => !e.IsReady).ToList();

        /// <summary>Count of employees whose CNAS data is complete.</summary>
        public int ReadyCount => Employees.Count(e => e.IsReady);

        /// <summary>True when nothing blocks the declaration for this company and year.</summary>
        public bool IsReady =>
            !EmployerNumberMissing && !EmployerNumberMalformed && Employees.All(e => e.IsReady);
    }

    /// <summary>
    /// One employee's CNAS data-readiness. Pure flags derived from already-stored data
    /// (identity fields + persisted payslips); it computes nothing statutory.
    /// </summary>
    public sealed class CnasEmployeeReadiness
    {
        public CnasEmployeeReadiness(
            long employeeId,
            string fullName,
            bool nssMissing,
            bool nssMalformed,
            bool birthDateMissing,
            int monthsBelowSnmg,
            int payslipMonths)
        {
            EmployeeId = employeeId;
            FullName = fullName;
            NssMissing = nssMissing;
            NssMalformed = nssMalformed;
            BirthDateMissing = birthDateMissing;
            MonthsBelowSnmg = monthsBelowSnmg;
            PayslipMonths = payslipMonths;
        }

        public long EmployeeId { get; }
        public string FullName { get; }

        /// <summary>No NSS recorded.</summary>
        public bool NssMissing { get; }

        /// <summary>NSS present but not the expected format (working assumption — see the audit).</summary>
        public bool NssMalformed { get; }

        /// <summary>No date of birth recorded.</summary>
        public bool BirthDateMissing { get; }

        /// <summary>Number of the year's payslip-months whose CNAS base is below the SNMG (justificatif requis).</summary>
        public int MonthsBelowSnmg { get; }

        /// <summary>Number of the year's months for which a payslip exists (context only).</summary>
        public int PayslipMonths { get; }

        /// <summary>True when this employee has no blocking CNAS data issue.</summary>
        public bool IsReady =>
            !NssMissing && !NssMalformed && !BirthDateMissing && MonthsBelowSnmg == 0;
    }
}
