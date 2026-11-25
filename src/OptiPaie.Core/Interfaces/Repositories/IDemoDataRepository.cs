using System.Collections.Generic;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>The identifying fields of a company, used to recognise the trial demo company.</summary>
    public sealed class CompanyFingerprint
    {
        public long Id { get; set; }
        public string NameFr { get; set; }
        public string Nif { get; set; }
        public string Nis { get; set; }
        public string Rc { get; set; }
        public string ArticleImposition { get; set; }
        public string CnasEmployerNumber { get; set; }
        public string Email { get; set; }
        public bool IsDemo { get; set; }
        public bool IsDeleted { get; set; }
    }

    /// <summary>One table touched by the demo-company removal, with its row count.</summary>
    public sealed class TableRows
    {
        public TableRows(string table, long rows) { Table = table; Rows = rows; }
        public string Table { get; }
        public long Rows { get; }
    }

    /// <summary>
    /// Reads and removes the trial demo company. Removal is ONE ordered plan of every table that
    /// references a company — directly or through its employees, runs, payslips, loans, assets, job
    /// postings, training sessions or evaluations — children before parents, inside the caller's
    /// transaction. The same plan drives the preview counts and the delete.
    /// </summary>
    public interface IDemoDataRepository
    {
        /// <summary>Every company, including soft-deleted ones (a deleted demo still has all its rows).</summary>
        IReadOnlyList<CompanyFingerprint> GetCompanyFingerprints();

        /// <summary>"LAST|First" for EVERY employee row of the company, soft-deleted included.</summary>
        IReadOnlyList<string> GetEmployeeNames(long companyId);

        /// <summary>The company's department names.</summary>
        IReadOnlyList<string> GetDepartmentNames(long companyId);

        /// <summary>Rows each table of the plan would lose (nothing is changed).</summary>
        IReadOnlyList<TableRows> CountDependents(long companyId);

        /// <summary>Deletes the company and every dependent row (plan order); returns rows per table.</summary>
        IReadOnlyList<TableRows> DeleteCompanyAndDependents(long companyId);
    }
}
