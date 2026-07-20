using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Retrieves and stores archived payroll for search and reprint. Contains no
    /// calculation logic — it only reads and persists already-produced data.
    /// </summary>
    public sealed class ArchiveService : IArchiveService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public ArchiveService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public IReadOnlyList<PayrollRun> SearchRuns(long? companyId, int? year, int? month)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.PayrollRuns.Search(companyId, year, month).ToList();
            }
        }

        public PayrollRun GetRun(long runId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PayrollRun run = uow.PayrollRuns.GetById(runId);
                if (run == null)
                {
                    return null;
                }

                run.Payslips = uow.Payslips.GetByRun(runId).ToList();
                return run;
            }
        }

        public Payslip GetPayslip(long payslipId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Payslip payslip = uow.Payslips.GetById(payslipId);
                if (payslip == null)
                {
                    return null;
                }

                payslip.Details = uow.PayrollDetails.GetByPayslip(payslipId).ToList();
                return payslip;
            }
        }

        public IReadOnlyList<Payslip> GetPayslipsByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Payslips.GetByEmployee(employeeId).ToList();
            }
        }

        public Result<long> StoreDocument(ArchiveDocument document)
        {
            Guard.AgainstNull(document, nameof(document));

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long id = uow.ArchiveDocuments.Insert(document);
                return Result.Ok(id);
            }
        }

        public ArchiveDocument GetDocument(long payslipId, string languageCode)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.ArchiveDocuments.GetByPayslipAndLanguage(payslipId, languageCode);
            }
        }
    }
}
