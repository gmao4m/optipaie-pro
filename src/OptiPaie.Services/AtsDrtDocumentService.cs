using System;
using System.Collections.Generic;
using System.IO;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Certificates;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Services.Certificates;
using Entities = OptiPaie.Core.Entities;

namespace OptiPaie.Services
{
    /// <summary>
    /// Maps OptiPaie's stored employee/company files onto the ATS/DRT certificate models, then
    /// prints the values at ABSOLUTE millimetre coordinates onto the pre-printed CNAS form
    /// (AtsDrtFormRenderer + the editable Assets/AtsDrtTemplates/form-layout.json). No text flow,
    /// no bookmarks, no Word — a support agent can nudge any coordinate in the JSON without rebuilding.
    /// </summary>
    public sealed class AtsDrtDocumentService : IAtsDrtDocumentService
    {
        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IArchiveService _archive;
        private FormLayoutConfig _layout;

        public AtsDrtDocumentService(ICompanyService companies, IEmployeeService employees, IArchiveService archive)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _archive = Guard.AgainstNull(archive, nameof(archive));
        }

        public Company MapCompany(long companyId)
        {
            Entities.Company c = _companies.Get(companyId);
            if (c == null) return null;

            return new Company
            {
                Id = unchecked((int)c.Id),
                ManagerName = c.ManagerName,
                EmployerNumber = FormattingHelpers.NormalizeDigits(c.CnasEmployerNumber),
                CompanyName = c.NameFr,
                Address = c.AddressFr,
                Location = c.City
            };
        }

        public Employee MapEmployee(long employeeId)
        {
            Entities.Employee e = _employees.Get(employeeId);
            if (e == null) return null;

            return new Employee
            {
                Id = unchecked((int)e.Id),
                CompanyId = unchecked((int)e.CompanyId),
                LastName = e.LastNameFr,
                FirstName = e.FirstNameFr,
                BirthDate = e.BirthDate,
                BirthPlace = e.BirthPlace,
                SocialSecurityNumber = FormattingHelpers.NormalizeDigits(e.Nss),
                Address = e.Address,
                HireDate = e.HireDate,
                Position = e.Poste
            };
        }

        public List<MonthlyContribution> BuildMonthGrid(DateTime startDate, int numberOfMonths, bool arabicMonthNames)
        {
            return new CertificateService(new WeekendConfig())
                .BuildEmptyMonthGrid(startDate, numberOfMonths, arabicMonthNames);
        }

        public List<MonthlyContribution> BuildContributionsFromPayroll(
            long companyId, long employeeId, DateTime windowStart, int monthCount, bool arabicMonthNames)
        {
            int count = Math.Min(12, Math.Max(1, monthCount));
            DateTime start = new DateTime(windowStart.Year, windowStart.Month, 1);
            int startKey = start.Year * 12 + (start.Month - 1);

            // Persisted per-month payslip for THIS employee, across the window's year(s). Same source
            // and same frozen figures the CNAS declaration reads (Payslip.BaseCotisable) — never a
            // second computation. Scoped to the company (defence in depth).
            var byPeriod = new Dictionary<int, Entities.Payslip>();
            DateTime last = start.AddMonths(count - 1);
            for (int y = start.Year; y <= last.Year; y++)
            {
                foreach (Entities.PayrollRun run in _archive.SearchRuns(companyId, y, null))
                {
                    if (run.CompanyId != companyId) continue;
                    int key = run.PeriodYear * 12 + (run.PeriodMonth - 1);
                    if (key < startKey || key >= startKey + count) continue; // outside the window
                    Entities.PayrollRun loaded = _archive.GetRun(run.Id);
                    if (loaded == null) continue;
                    foreach (Entities.Payslip slip in loaded.Payslips)
                        if (slip.EmployeeId == employeeId) byPeriod[key] = slip; // latest run for the period wins
                }
            }

            // Labels for the window (reuse the certificate's month-label logic).
            List<MonthlyContribution> labels = new CertificateService(new WeekendConfig())
                .BuildEmptyMonthGrid(start, count, arabicMonthNames);

            // COMPACT: only months that actually have a payslip become active rows (real data), in
            // chronological order. Every remaining row stays inactive → the renderer strikes it with
            // "/". A LABELLED row therefore always carries real figures, so a "/" (not applicable)
            // can never be confused with a data failure.
            var rows = new List<MonthlyContribution>();
            for (int i = 0; i < count; i++)
            {
                if (!byPeriod.TryGetValue(startKey + i, out Entities.Payslip slip)) continue;
                rows.Add(new MonthlyContribution
                {
                    IsActive = true,
                    MonthLabel = labels[i].MonthLabel,
                    DaysWorked = slip.WorkedDays,
                    ContributionBase = slip.BaseCotisable, // identical persisted figure to the CNAS DAC
                    AbsenceReason = null                    // free-text motif — left for the employer
                });
            }
            while (rows.Count < 12) rows.Add(new MonthlyContribution { IsActive = false });
            return rows;
        }

        public string GenerateAts(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, List<MonthlyContribution> contributions, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath, bool isWorkStoppage = false)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            AtsCertificateData data = service.BuildAts(company, employee, stoppage, hasResumedWork, contributions, isWorkStoppage);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapAts(data);
            AtsDrtFormRenderer.RenderPdf(GetForm("ATS"), values, offsetXmm, offsetYmm, outputPdfPath, FormsDir());
            return outputPdfPath;
        }

        public string GenerateDrt(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            DrtCertificateData data = service.BuildDrt(company, employee, stoppage, hasResumedWork);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapDrt(data);
            AtsDrtFormRenderer.RenderPdf(GetForm("DRT"), values, offsetXmm, offsetYmm, outputPdfPath, FormsDir());
            return outputPdfPath;
        }

        public string GenerateCalibrationSheet(double offsetXmm, double offsetYmm, string outputPdfPath)
        {
            AtsDrtFormRenderer.RenderCalibrationPdf(offsetXmm, offsetYmm, outputPdfPath);
            return outputPdfPath;
        }

        // ── layout ──────────────────────────────────────────────────────────

        private FormDefinition GetForm(string name)
        {
            if (_layout == null) _layout = AtsDrtFormRenderer.LoadLayout(LayoutPath());
            if (!_layout.Forms.TryGetValue(name, out FormDefinition form) || form == null)
                throw new InvalidOperationException($"Form '{name}' not found in form-layout.json.");
            return form;
        }

        /// <summary>
        /// The editable coordinate file ships loose next to the exe (Content, PreserveNewest),
        /// exactly like the old templates — resolve it off the app base directory.
        /// </summary>
        private static string LayoutPath() => Path.Combine(FormsDir(), "form-layout.json");

        /// <summary>Folder shipped next to the exe holding form-layout.json AND the official form
        /// images (Content, PreserveNewest) drawn full-page as the printed background.</summary>
        private static string FormsDir() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AtsDrtTemplates");
    }
}
