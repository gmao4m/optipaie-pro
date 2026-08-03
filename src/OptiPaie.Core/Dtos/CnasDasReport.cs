using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Read-only annual DAS aggregation for ONE company: every paid employee, their salary and
    /// worked hours per quarter, their entry/exit dates, plus the company quarter/annual totals
    /// and headcount. Aggregates persisted payslips only — no engine, no rate. It is the input
    /// the DAS text files are built from; it writes nothing itself.
    /// </summary>
    public sealed class CnasDasReport
    {
        public CnasDasReport(long companyId, int year, string employerNumber,
            IReadOnlyList<CnasDasEmployee> employees,
            IReadOnlyList<decimal> quarterTotals, decimal annualTotal, int workerCount,
            bool hasEstimatedDurations)
        {
            CompanyId = companyId;
            Year = year;
            EmployerNumber = employerNumber;
            Employees = employees ?? new List<CnasDasEmployee>();
            QuarterTotals = quarterTotals ?? new decimal[4];
            AnnualTotal = annualTotal;
            WorkerCount = workerCount;
            HasEstimatedDurations = hasEstimatedDurations;
        }

        public long CompanyId { get; }
        public int Year { get; }
        public string EmployerNumber { get; }

        public IReadOnlyList<CnasDasEmployee> Employees { get; }

        /// <summary>Company salary total per quarter (index 0 = T1 … 3 = T4).</summary>
        public IReadOnlyList<decimal> QuarterTotals { get; }
        public decimal AnnualTotal { get; }

        /// <summary>Number of paid employees = number of detail lines.</summary>
        public int WorkerCount { get; }

        /// <summary>True when at least one employee has an estimated (not measured) duration.</summary>
        public bool HasEstimatedDurations { get; }
    }

    /// <summary>One employee's annual DAS line: identity, four quarters, annual total.</summary>
    public sealed class CnasDasEmployee
    {
        public CnasDasEmployee(long employeeId, string nss, string lastName, string firstName,
            DateTime? birthDate, DateTime hireDate, DateTime? exitDate,
            IReadOnlyList<CnasDasQuarter> quarters, decimal annualSalary, bool hasEstimatedDuration)
        {
            EmployeeId = employeeId;
            Nss = nss;
            LastName = lastName;
            FirstName = firstName;
            BirthDate = birthDate;
            HireDate = hireDate;
            ExitDate = exitDate;
            Quarters = quarters ?? new List<CnasDasQuarter>();
            AnnualSalary = annualSalary;
            HasEstimatedDuration = hasEstimatedDuration;
        }

        public long EmployeeId { get; }
        public string Nss { get; }
        public string LastName { get; }
        public string FirstName { get; }
        public DateTime? BirthDate { get; }
        public DateTime HireDate { get; }
        public DateTime? ExitDate { get; }

        /// <summary>Always four quarters, T1..T4 in order.</summary>
        public IReadOnlyList<CnasDasQuarter> Quarters { get; }
        public decimal AnnualSalary { get; }

        /// <summary>True when any active quarter's duration was estimated rather than measured.</summary>
        public bool HasEstimatedDuration { get; }
    }

    /// <summary>One quarter of an employee's DAS line: salary, hours, and whether the hours are estimated.</summary>
    public sealed class CnasDasQuarter
    {
        public CnasDasQuarter(int number, decimal salary, int hours, bool isEstimated)
        {
            Number = number;
            Salary = salary;
            Hours = hours;
            IsEstimated = isEstimated;
        }

        public int Number { get; }        // 1..4
        public decimal Salary { get; }
        public int Hours { get; }

        /// <summary>
        /// True when the hours were ESTIMATED (a payslip carried no measured WorkedHours, so a
        /// standard-hours fallback was used). Never silently merged with a measured duration —
        /// the DAS screen surfaces it before export.
        /// </summary>
        public bool IsEstimated { get; }

        public bool HasActivity => Salary > 0m || Hours > 0;
    }
}
