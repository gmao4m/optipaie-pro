using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;

namespace OptiPaie.Services
{
    /// <summary>
    /// Retrieves and stores archived payroll for search and reprint. Contains no
    /// calculation logic — it only reads and persists already-produced data.
    /// <para>
    /// The archive is entirely payslip (salary) data, so it is NOT an all-or-nothing module: it
    /// lists only what the signed-in role may view, and hands over a stored document only to a role
    /// that may EXPORT payroll. A role that cannot see a payslip (read-only) gets an empty archive;
    /// a role that may view but not export it (HR) sees the list but cannot pull the document bytes.
    /// With no context / the gate off, everything is returned (legacy behaviour unchanged).
    /// </para>
    /// </summary>
    public sealed class ArchiveService : IArchiveService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IAccessContext _access;

        public ArchiveService(IUnitOfWorkFactory unitOfWorkFactory, IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _access = access;
        }

        /// <summary>The role may SEE payslip/payroll data (the archive list). Gate off ⇒ true.</summary>
        private bool CanView() =>
            _access == null || !_access.IsEnforced || AccessPolicy.CanOpen(_access.CurrentRole, "payroll");

        /// <summary>The role may EXPORT/reprint payslip data (pull the stored document). Gate off ⇒ true.</summary>
        private bool CanExport() =>
            _access == null || !_access.IsEnforced || AccessPolicy.CanExportPayroll(_access.CurrentRole);

        public IReadOnlyList<PayrollRun> SearchRuns(long? companyId, int? year, int? month)
        {
            if (!CanView())
            {
                return new List<PayrollRun>();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.PayrollRuns.Search(companyId, year, month).ToList();
            }
        }

        public PayrollRun GetRun(long runId)
        {
            if (!CanView())
            {
                return null;
            }

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
            if (!CanView())
            {
                return null;
            }

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
            if (!CanView())
            {
                return new List<Payslip>();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Payslips.GetByEmployee(employeeId).ToList();
            }
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Archive storage of an already-rendered document; invoked only by the guarded payroll/archival pipeline, never as a standalone user action.")]
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
            // The stored PDF is the reprint/export source — guarded as strictly as exporting a
            // payslip. A role that may view the list but not export (HR) cannot pull the bytes.
            if (!CanExport())
            {
                return null;
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.ArchiveDocuments.GetByPayslipAndLanguage(payslipId, languageCode);
            }
        }
    }
}
