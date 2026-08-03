using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Services;

namespace OptiPaie.Services
{
    /// <summary>
    /// CNAS declarations (DAC/DAS) — READ ONLY. Reads persisted payslips (through the archive)
    /// and employee/company identity to report declaration readiness. It never calls the
    /// payroll engine, never writes, and is always scoped to one explicit company.
    /// </summary>
    public sealed class CnasDeclarationService : ICnasDeclarationService
    {
        // Working assumptions — the exact CNAS formats are NOT yet confirmed
        // (see docs/audit-cnas-dac-das.md, section F). Kept here so they change in one place.
        private const int ExpectedNssLength = 12;       // NSS = 12 chiffres (clé incluse) — à confirmer
        private const int ExpectedEmployerDigits = 10;  // n° employeur = 10 chiffres — à confirmer

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
            bool employerMissing = employer.Length == 0;
            bool employerMalformed = !employerMissing && DigitsOnly(employer).Length != ExpectedEmployerDigits;

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

                string nss = (e.Nss ?? string.Empty).Trim();
                bool nssMissing = nss.Length == 0;
                bool nssMalformed = !nssMissing && !(nss.Length == ExpectedNssLength && nss.All(char.IsDigit));

                int monthsBelow = hasPayslips ? slips.Count(p => p.BaseCotisable < snmg) : 0;
                int payslipMonths = hasPayslips ? slips.Count : 0;

                rows.Add(new CnasEmployeeReadiness(
                    e.Id,
                    (e.LastNameFr + " " + e.FirstNameFr).Trim(),
                    nssMissing,
                    nssMalformed,
                    e.BirthDate == null,
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

        private static string DigitsOnly(string value) =>
            new string(value.Where(char.IsDigit).ToArray());
    }
}
