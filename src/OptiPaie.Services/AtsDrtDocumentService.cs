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
    /// Maps OptiPaie's stored employee/company files onto the ATS/DRT certificate models,
    /// then drives the verbatim-ported certificate logic + the official-template DOCX filler.
    /// The document format itself is untouched — only the pre-existing bookmarks are filled.
    /// </summary>
    public sealed class AtsDrtDocumentService : IAtsDrtDocumentService
    {
        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;

        public AtsDrtDocumentService(ICompanyService companies, IEmployeeService employees)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
        }

        public bool IsWordAvailable => DocxToPdfConverter.IsWordAvailable();

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
            bool hasResumedWork, List<MonthlyContribution> contributions, WeekendConfig weekend, string outputDocxPath)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            AtsCertificateData data = service.BuildAts(company, employee, stoppage, hasResumedWork, contributions);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapAts(data);
            DocxBookmarkFiller.Fill(TemplatePath("ATS_Template.docx"), outputDocxPath, values);
            return outputDocxPath;
        }

        public string GenerateDrt(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, bool arabic, WeekendConfig weekend, string outputDocxPath)
        {
            var service = new CertificateService(weekend ?? new WeekendConfig());
            DrtCertificateData data = service.BuildDrt(company, employee, stoppage, hasResumedWork);
            Dictionary<string, string> values = CertificateBookmarkMapper.MapDrt(data);
            string template = arabic ? "DRT_Template_AR.docx" : "DRT_Template_FR.docx";
            DocxBookmarkFiller.Fill(TemplatePath(template), outputDocxPath, values);
            return outputDocxPath;
        }

        public string ConvertToPdf(string docxPath, string pdfPath)
        {
            DocxToPdfConverter.ConvertToPdf(docxPath, pdfPath);
            return pdfPath;
        }

        /// <summary>
        /// The official templates are shipped loose next to the exe (Content, PreserveNewest),
        /// exactly as the source tool ships them — resolve them off the app base directory.
        /// </summary>
        private static string TemplatePath(string fileName) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AtsDrtTemplates", fileName);
    }
}
