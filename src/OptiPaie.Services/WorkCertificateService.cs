using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Work-certificate orchestration. Stores only the issue metadata and auto-numbers a
    /// new certificate; the document content is assembled LIVE from the shared employee
    /// and company records when it is printed, so a certificate always reflects the
    /// current data. Nothing about the employee or company is duplicated here.
    /// </summary>
    public sealed class WorkCertificateService : IWorkCertificateService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public WorkCertificateService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> Save(WorkCertificate certificate)
        {
            if (certificate == null)
            {
                return Result.Fail<long>("Aucune attestation.", "Certificate_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(certificate.EmployeeId);
                if (employee == null)
                {
                    return Result.Fail<long>("Employé introuvable.", "Certificate_EmployeeNotFound");
                }

                if (certificate.Type == CertificateType.Custom && string.IsNullOrWhiteSpace(certificate.Body))
                {
                    return Result.Fail<long>("Le corps du document est obligatoire pour un document libre.", "Certificate_BodyRequired");
                }

                if (certificate.IssueDate == default(DateTime))
                {
                    certificate.IssueDate = DateTime.Today;
                }

                if (certificate.Id > 0)
                {
                    WorkCertificate existing = uow.Certificates.GetById(certificate.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Attestation introuvable.", "Certificate_NotFound");
                    }

                    certificate.CreatedAtUtc = existing.CreatedAtUtc;
                    uow.Certificates.Update(certificate);
                    return Result.Ok(certificate.Id);
                }

                if (string.IsNullOrWhiteSpace(certificate.Reference))
                {
                    certificate.Reference = NextReference(uow, employee.CompanyId, certificate.Type, certificate.IssueDate.Year);
                }

                return Result.Ok(uow.Certificates.Insert(certificate));
            }
        }

        public Result Delete(long certificateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Certificates.SoftDelete(certificateId);
                return Result.Ok();
            }
        }

        public WorkCertificate Get(long certificateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Certificates.GetById(certificateId);
            }
        }

        public CertificateRenderModel BuildRender(long certificateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                WorkCertificate certificate = uow.Certificates.GetById(certificateId);
                if (certificate == null) return null;

                Employee employee = uow.Employees.GetById(certificate.EmployeeId);
                if (employee == null) return null;

                Company company = uow.Companies.GetById(employee.CompanyId);

                // Seniority computed at the issue date, from the shared hire/exit dates.
                DateTime end = employee.ExitDate ?? certificate.IssueDate;
                int months = Math.Max(0, ((end.Year - employee.HireDate.Year) * 12) + end.Month - employee.HireDate.Month);
                if (end.Day < employee.HireDate.Day && months > 0) months--;

                return new CertificateRenderModel
                {
                    Certificate = certificate,
                    Employee = employee,
                    Company = company,
                    MonthlySalary = employee.BaseSalary,
                    SeniorityYears = months / 12,
                    SeniorityMonths = months % 12
                };
            }
        }

        public IReadOnlyList<CertificateSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Certificates.GetByEmployee(employeeId).Select(c => Summarise(uow, c)).ToList();
            }
        }

        public IReadOnlyList<CertificateSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Certificates.GetByCompany(companyId).Select(c => Summarise(uow, c)).ToList();
            }
        }

        // -- internals ---------------------------------------------------------

        private static CertificateSummary Summarise(IUnitOfWork uow, WorkCertificate certificate)
        {
            Employee employee = uow.Employees.GetById(certificate.EmployeeId);

            return new CertificateSummary
            {
                CertificateId = certificate.Id,
                EmployeeId = certificate.EmployeeId,
                EmployeeName = employee == null ? null : (employee.LastNameFr + " " + employee.FirstNameFr).Trim(),
                Type = certificate.Type,
                Reference = certificate.Reference,
                IssueDate = certificate.IssueDate,
                Purpose = certificate.Purpose
            };
        }

        /// <summary>Builds a per-company, per-year reference such as "ATT-2026-0007".</summary>
        private static string NextReference(IUnitOfWork uow, long companyId, CertificateType type, int year)
        {
            int sequence = uow.Certificates.CountForCompanyYear(companyId, year) + 1;
            return Prefix(type) + "-" + year.ToString("0000", CultureInfo.InvariantCulture) + "-" +
                   sequence.ToString("0000", CultureInfo.InvariantCulture);
        }

        private static string Prefix(CertificateType type)
        {
            switch (type)
            {
                case CertificateType.WorkCertificate: return "ATT";
                case CertificateType.WorkExperience: return "CERT";
                case CertificateType.SalaryCertificate: return "SAL";
                default: return "DOC";
            }
        }
    }
}
