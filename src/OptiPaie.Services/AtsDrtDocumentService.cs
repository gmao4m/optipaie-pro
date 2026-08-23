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
        private FormLayoutConfig _layout;

        public AtsDrtDocumentService(ICompanyService companies, IEmployeeService employees)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
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

        public string GenerateAts(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, List<MonthlyContribution> contributions, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            AtsCertificateData data = service.BuildAts(company, employee, stoppage, hasResumedWork, contributions);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapAts(data);
            AtsDrtFormRenderer.RenderPdf(GetForm("ATS"), values, offsetXmm, offsetYmm, outputPdfPath);
            return outputPdfPath;
        }

        public string GenerateDrt(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            DrtCertificateData data = service.BuildDrt(company, employee, stoppage, hasResumedWork);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapDrt(data);
            AtsDrtFormRenderer.RenderPdf(GetForm("DRT"), values, offsetXmm, offsetYmm, outputPdfPath);
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
        private static string LayoutPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AtsDrtTemplates", "form-layout.json");
    }
}
