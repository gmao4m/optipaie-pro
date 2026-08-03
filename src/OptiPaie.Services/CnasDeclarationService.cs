using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Services.Cnas;

namespace OptiPaie.Services
{
    /// <summary>
    /// CNAS declarations (DAC/DAS) — READ ONLY. Reads persisted payslips (through the archive)
    /// and employee/company identity to report declaration readiness. It never calls the
    /// payroll engine, never writes, and is always scoped to one explicit company.
    /// </summary>
    public sealed class CnasDeclarationService : ICnasDeclarationService
    {
        // Identity validation (NSS, birth date, employer number) lives in CnasIdentityRules —
        // the single source of truth shared with the DAS export controller (étape 8).

        // Fallback used ONLY when a payslip carries no measured WorkedHours: a standard month of
        // ~40 h/week (= 173,33 h) reproduces the 520 H of a full quarter in the reference files.
        // It is an ESTIMATION, never measured — every quarter that uses it is flagged IsEstimated.
        // À CONFIRMER (voir docs/cnas-das-data-gaps.md §1).
        private const decimal EstimatedMonthlyHours = 173.33m;

        // Official CNAS contribution split (décret 94-187 modifié) — DISPLAY ONLY, "à confirmer".
        // These are NOT applied to any payslip and change NO rate; they are shown next to the
        // app's configured rates so the 26 % (config) vs 25,5 % (official patronale) gap can be
        // arbitrated with an accountant. Patronale sums to 0,255 ; salariale to 0,09.
        private static readonly (string Name, decimal Pat, decimal Sal)[] OfficialSplit =
        {
            ("Assurances sociales", 0.115m, 0.015m),
            ("Accidents du travail", 0.0125m, 0m),
            ("Retraite", 0.11m, 0.0675m),
            ("Retraite anticipée", 0.0025m, 0.0025m),
            ("Chômage", 0.01m, 0.005m),
            ("Œuvres sociales", 0.005m, 0m),
        };
        private const decimal OfficialPatronaleRate = 0.255m; // 25 % + 0,5 % œuvres sociales
        private const decimal OfficialSalarialeRate = 0.09m;

        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IArchiveService _archive;
        private readonly IConfigurationService _configuration;

        public CnasDeclarationService(
            ICompanyService companies,
            IEmployeeService employees,
            IArchiveService archive,
            IConfigurationService configuration)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _archive = Guard.AgainstNull(archive, nameof(archive));
            _configuration = Guard.AgainstNull(configuration, nameof(configuration));
        }

        public CnasReadinessReport CheckReadiness(long companyId, int year)
        {
            // RISK #1 — a declaration must never read across companies: no default, no null.
            if (companyId <= 0)
            {
                throw new ArgumentException(
                    "Aucune entreprise active : une déclaration CNAS doit être limitée à une entreprise explicite.",
                    nameof(companyId));
            }

            Company company = _companies.Get(companyId);
            if (company == null)
            {
                throw new ArgumentException("Entreprise introuvable.", nameof(companyId));
            }

            string employer = (company.CnasEmployerNumber ?? string.Empty).Trim();
            bool employerMissing = CnasIdentityRules.IsEmployerNumberMissing(company.CnasEmployerNumber);
            bool employerMalformed = CnasIdentityRules.IsEmployerNumberMalformed(company.CnasEmployerNumber);

            decimal snmg = _configuration.GetSnmg();

            // The year's persisted payslips grouped by employee — read only, company-scoped.
            Dictionary<long, List<Payslip>> byEmployee = LoadYearPayslips(companyId, year);

            var rows = new List<CnasEmployeeReadiness>();
            foreach (Employee e in _employees.GetByCompany(companyId, true))
            {
                bool hasPayslips = byEmployee.TryGetValue(e.Id, out List<Payslip> slips);

                // Only employees relevant to year N: still active, or paid at least once in N.
                if (!e.IsActive && !hasPayslips)
                {
                    continue;
                }

                bool nssMissing = CnasIdentityRules.IsNssMissing(e.Nss);
                bool nssMalformed = CnasIdentityRules.IsNssMalformed(e.Nss);

                int monthsBelow = hasPayslips ? slips.Count(p => p.BaseCotisable < snmg) : 0;
                int payslipMonths = hasPayslips ? slips.Count : 0;

                rows.Add(new CnasEmployeeReadiness(
                    e.Id,
                    (e.LastNameFr + " " + e.FirstNameFr).Trim(),
                    nssMissing,
                    nssMalformed,
                    CnasIdentityRules.IsBirthDateMissing(e.BirthDate),
                    monthsBelow,
                    payslipMonths));
            }

            return new CnasReadinessReport(
                companyId,
                year,
                employer,
                employerMissing,
                employerMalformed,
                rows.OrderBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase).ToList());
        }

        public CnasDacReport BuildDac(long companyId, int year, IReadOnlyList<int> months)
        {
            // RISK #1 — a declaration must never read across companies: no default, no null.
            if (companyId <= 0)
            {
                throw new ArgumentException(
                    "Aucune entreprise active : une déclaration CNAS doit être limitée à une entreprise explicite.",
                    nameof(companyId));
            }

            Company company = _companies.Get(companyId);
            if (company == null)
            {
                throw new ArgumentException("Entreprise introuvable.", nameof(companyId));
            }

            string employer = (company.CnasEmployerNumber ?? string.Empty).Trim();
            bool employerMissing = CnasIdentityRules.IsEmployerNumberMissing(company.CnasEmployerNumber);
            bool employerMalformed = CnasIdentityRules.IsEmployerNumberMalformed(company.CnasEmployerNumber);

            IReadOnlyList<int> monthSet = months ?? new List<int>();
            List<Payslip> slips = LoadPeriodPayslips(companyId, year, monthSet);

            decimal assiette = slips.Sum(p => p.BaseCotisable);
            decimal frozenEmployee = slips.Sum(p => p.CnasEmployee);
            decimal frozenEmployer = slips.Sum(p => p.CnasEmployer);
            int effectif = slips.Select(p => p.EmployeeId).Distinct().Count();

            // Applied = the app's CONFIGURED rates (LegalParameters). Not modified here.
            decimal rateSalariale = _configuration.GetCnasEmployeeRate();
            decimal ratePatronale = _configuration.GetCnasEmployerRate();

            List<CnasContributionBranch> branches = OfficialSplit
                .Select(b => new CnasContributionBranch(b.Name, b.Pat, b.Sal, assiette))
                .ToList();

            return new CnasDacReport(
                companyId, year, monthSet.OrderBy(m => m).ToList(),
                employer, employerMissing, employerMalformed,
                effectif, assiette, rateSalariale, ratePatronale,
                frozenEmployee, frozenEmployer,
                branches, OfficialSalarialeRate, OfficialPatronaleRate);
        }

        public IReadOnlyList<CnasMovementRow> BuildMovements(long companyId, int year, IReadOnlyList<int> months)
        {
            // RISK #1 — a declaration must never read across companies: no default, no null.
            if (companyId <= 0)
            {
                throw new ArgumentException(
                    "Aucune entreprise active : une déclaration CNAS doit être limitée à une entreprise explicite.",
                    nameof(companyId));
            }

            HashSet<int> monthSet = (months == null || months.Count == 0) ? null : new HashSet<int>(months);
            bool InPeriod(DateTime d) => d.Year == year && (monthSet == null || monthSet.Contains(d.Month));

            var rows = new List<CnasMovementRow>();
            // Include inactive employees so an exit that ends the contract still surfaces.
            foreach (Employee e in _employees.GetByCompany(companyId, true))
            {
                string nss = (e.Nss ?? string.Empty).Trim();
                if (InPeriod(e.HireDate))
                {
                    rows.Add(new CnasMovementRow(e.Id, nss, e.LastNameFr, e.FirstNameFr, true, e.HireDate));
                }
                if (e.ExitDate.HasValue && InPeriod(e.ExitDate.Value))
                {
                    rows.Add(new CnasMovementRow(e.Id, nss, e.LastNameFr, e.FirstNameFr, false, e.ExitDate.Value));
                }
            }

            return rows
                .OrderBy(r => r.Date)
                .ThenBy(r => r.LastName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public CnasDasReport BuildDas(long companyId, int year)
        {
            // RISK #1 — a declaration must never read across companies: no default, no null.
            if (companyId <= 0)
            {
                throw new ArgumentException(
                    "Aucune entreprise active : une déclaration CNAS doit être limitée à une entreprise explicite.",
                    nameof(companyId));
            }

            Company company = _companies.Get(companyId);
            if (company == null)
            {
                throw new ArgumentException("Entreprise introuvable.", nameof(companyId));
            }

            string employer = (company.CnasEmployerNumber ?? string.Empty).Trim();

            Dictionary<long, Employee> employeesById = _employees.GetByCompany(companyId, true)
                .ToDictionary(e => e.Id);
            Dictionary<long, List<MonthlySlip>> byEmployee = LoadYearByMonth(companyId, year);

            var employees = new List<CnasDasEmployee>();
            var quarterTotals = new decimal[4];
            bool anyEstimated = false;

            foreach (KeyValuePair<long, List<MonthlySlip>> kv in byEmployee)
            {
                if (!employeesById.TryGetValue(kv.Key, out Employee e))
                {
                    continue; // a payslip without a matching current employee — skip defensively
                }

                var salary = new decimal[4];
                var hours = new decimal[4];
                var estimated = new bool[4];
                foreach (MonthlySlip ms in kv.Value)
                {
                    int q = (ms.Month - 1) / 3; // 0..3
                    if (q < 0 || q > 3) continue;
                    salary[q] += ms.Slip.BaseCotisable;
                    if (ms.Slip.WorkedHours > 0m)
                    {
                        hours[q] += ms.Slip.WorkedHours;      // measured
                    }
                    else
                    {
                        hours[q] += EstimatedMonthlyHours;    // estimated — flagged below
                        estimated[q] = true;
                    }
                }

                var quarters = new List<CnasDasQuarter>(4);
                decimal annual = 0m;
                bool employeeEstimated = false;
                for (int q = 0; q < 4; q++)
                {
                    int wholeHours = (int)Math.Round(hours[q], 0, MidpointRounding.AwayFromZero);
                    quarters.Add(new CnasDasQuarter(q + 1, salary[q], wholeHours, estimated[q]));
                    annual += salary[q];
                    quarterTotals[q] += salary[q];
                    if (estimated[q]) employeeEstimated = true;
                }
                if (employeeEstimated) anyEstimated = true;

                employees.Add(new CnasDasEmployee(
                    e.Id, (e.Nss ?? string.Empty).Trim(), e.LastNameFr, e.FirstNameFr,
                    e.BirthDate, e.HireDate, e.ExitDate, quarters, annual, employeeEstimated));
            }

            employees = employees
                .OrderBy(x => x.LastName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.FirstName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            decimal annualTotal = quarterTotals[0] + quarterTotals[1] + quarterTotals[2] + quarterTotals[3];
            return new CnasDasReport(companyId, year, employer, employees,
                quarterTotals.ToList(), annualTotal, employees.Count, anyEstimated);
        }

        private static readonly int[] AllMonths = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        public DasExportValidation ValidateDasForExport(long companyId, int year)
        {
            if (companyId <= 0)
            {
                throw new ArgumentException(
                    "Aucune entreprise active : une déclaration CNAS doit être limitée à une entreprise explicite.",
                    nameof(companyId));
            }

            Company company = _companies.Get(companyId);
            if (company == null)
            {
                throw new ArgumentException("Entreprise introuvable.", nameof(companyId));
            }

            var blockers = new List<DasBlocker>();

            // Company-level: employer number (same rules as CheckReadiness).
            if (CnasIdentityRules.IsEmployerNumberMissing(company.CnasEmployerNumber))
                blockers.Add(new DasBlocker(DasBlockerType.EmployerNumberMissing, 0, null, null));
            else if (CnasIdentityRules.IsEmployerNumberMalformed(company.CnasEmployerNumber))
                blockers.Add(new DasBlocker(DasBlockerType.EmployerNumberMalformed, 0, null, null));

            CnasDasReport das = BuildDas(companyId, year);

            // Per-employee: identity (shared rules), ASCII name, amount capacity.
            foreach (CnasDasEmployee e in das.Employees)
            {
                string name = (e.LastName + " " + e.FirstName).Trim();

                if (CnasIdentityRules.IsNssMissing(e.Nss))
                    blockers.Add(new DasBlocker(DasBlockerType.NssMissing, e.EmployeeId, name, null));
                else if (CnasIdentityRules.IsNssMalformed(e.Nss))
                    blockers.Add(new DasBlocker(DasBlockerType.NssMalformed, e.EmployeeId, name, null));

                if (CnasIdentityRules.IsBirthDateMissing(e.BirthDate))
                    blockers.Add(new DasBlocker(DasBlockerType.BirthDateMissing, e.EmployeeId, name, null));

                if (!IsAscii(e.LastName) || !IsAscii(e.FirstName))
                    blockers.Add(new DasBlocker(DasBlockerType.NameNotAscii, e.EmployeeId, name, null));

                foreach (CnasDasQuarter q in e.Quarters)
                {
                    if (DasFormat.Amount(q.Salary).Length > DasFileSpec.QuarterSalaryLength)
                        blockers.Add(new DasBlocker(DasBlockerType.AmountExceedsField, e.EmployeeId, name, "T" + q.Number));
                }
            }

            // Structural safety net — the built figures must be internally consistent.
            var quarterCheck = new decimal[4];
            foreach (CnasDasEmployee e in das.Employees)
            {
                decimal empAnnual = 0m;
                for (int q = 0; q < 4; q++) { quarterCheck[q] += e.Quarters[q].Salary; empAnnual += e.Quarters[q].Salary; }
                if (empAnnual != e.AnnualSalary)
                    blockers.Add(new DasBlocker(DasBlockerType.TotalsInconsistent, e.EmployeeId, (e.LastName + " " + e.FirstName).Trim(), null));
            }
            for (int q = 0; q < 4; q++)
            {
                if (quarterCheck[q] != das.QuarterTotals[q])
                    blockers.Add(new DasBlocker(DasBlockerType.TotalsInconsistent, 0, null, "T" + (q + 1)));
            }
            if (das.AnnualTotal != quarterCheck[0] + quarterCheck[1] + quarterCheck[2] + quarterCheck[3])
                blockers.Add(new DasBlocker(DasBlockerType.TotalsInconsistent, 0, null, "annuel"));
            if (das.WorkerCount != das.Employees.Count)
                blockers.Add(new DasBlocker(DasBlockerType.WorkerCountInconsistent, 0, null, null));

            // Cross-check: the DAS annual total must equal the year's DAC cumul (two independent paths).
            decimal dacYear = BuildDac(companyId, year, AllMonths).Assiette;
            if (das.AnnualTotal != dacYear)
                blockers.Add(new DasBlocker(DasBlockerType.DacCrossCheckFailed, 0, null, null));

            return new DasExportValidation(blockers);
        }

        private static bool IsAscii(string s)
        {
            if (s == null) return true;
            foreach (char c in s)
            {
                if (c > (char)127) return false;
            }
            return true;
        }

        /// <summary>A persisted payslip together with the month of the run that produced it.</summary>
        private struct MonthlySlip
        {
            public MonthlySlip(int month, Payslip slip) { Month = month; Slip = slip; }
            public int Month { get; }
            public Payslip Slip { get; }
        }

        /// <summary>Year's payslips grouped by employee, each tagged with its run month (read-only, scoped).</summary>
        private Dictionary<long, List<MonthlySlip>> LoadYearByMonth(long companyId, int year)
        {
            var byEmployee = new Dictionary<long, List<MonthlySlip>>();
            foreach (PayrollRun run in _archive.SearchRuns(companyId, year, null))
            {
                if (run.CompanyId != companyId) continue; // defence in depth

                PayrollRun loaded = _archive.GetRun(run.Id);
                if (loaded == null) continue;

                foreach (Payslip slip in loaded.Payslips)
                {
                    if (!byEmployee.TryGetValue(slip.EmployeeId, out List<MonthlySlip> list))
                    {
                        list = new List<MonthlySlip>();
                        byEmployee[slip.EmployeeId] = list;
                    }
                    list.Add(new MonthlySlip(run.PeriodMonth, slip));
                }
            }

            return byEmployee;
        }

        /// <summary>The persisted payslips of a company for the given year+months (read-only, scoped).</summary>
        private List<Payslip> LoadPeriodPayslips(long companyId, int year, IReadOnlyList<int> months)
        {
            var result = new List<Payslip>();
            foreach (PayrollRun run in _archive.SearchRuns(companyId, year, null))
            {
                if (run.CompanyId != companyId) continue;                       // defence in depth
                if (months.Count > 0 && !months.Contains(run.PeriodMonth)) continue;

                PayrollRun loaded = _archive.GetRun(run.Id);
                if (loaded != null) result.AddRange(loaded.Payslips);
            }

            return result;
        }

        private Dictionary<long, List<Payslip>> LoadYearPayslips(long companyId, int year)
        {
            var byEmployee = new Dictionary<long, List<Payslip>>();

            foreach (PayrollRun run in _archive.SearchRuns(companyId, year, null))
            {
                // Defence in depth: never fold another company's run into this report.
                if (run.CompanyId != companyId)
                {
                    continue;
                }

                PayrollRun loaded = _archive.GetRun(run.Id);
                if (loaded == null)
                {
                    continue;
                }

                foreach (Payslip slip in loaded.Payslips)
                {
                    if (!byEmployee.TryGetValue(slip.EmployeeId, out List<Payslip> list))
                    {
                        list = new List<Payslip>();
                        byEmployee[slip.EmployeeId] = list;
                    }

                    list.Add(slip);
                }
            }

            return byEmployee;
        }
    }
}
