using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Read and reprint operations over archived payroll. Contains no calculation
    /// logic — it only retrieves and stores already-produced payroll data/documents.
    /// </summary>
    public interface IArchiveService
    {
        /// <summary>Searches payroll runs by optional company, year and month.</summary>
        IReadOnlyList<PayrollRun> SearchRuns(long? companyId, int? year, int? month);

        /// <summary>Returns a run with its payslips loaded, or null.</summary>
        PayrollRun GetRun(long runId);

        /// <summary>Returns a payslip with its detail lines loaded, or null.</summary>
        Payslip GetPayslip(long payslipId);

        /// <summary>Returns the payslips of an employee, newest first.</summary>
        IReadOnlyList<Payslip> GetPayslipsByEmployee(long employeeId);

        /// <summary>Stores a frozen archive document (PDF + snapshot) for a payslip.</summary>
        Result<long> StoreDocument(ArchiveDocument document);

        /// <summary>Returns the archived document for a payslip in a given language, or null.</summary>
        ArchiveDocument GetDocument(long payslipId, string languageCode);
    }
}
