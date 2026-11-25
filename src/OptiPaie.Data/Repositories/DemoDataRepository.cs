using System.Collections.Generic;
using System.Linq;
using Dapper;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper implementation of <see cref="IDemoDataRepository"/>.</summary>
    internal sealed class DemoDataRepository : RepositoryBase, IDemoDataRepository
    {
        // Row sets of the company being removed (@c = its id).
        private const string E = "(SELECT Id FROM Employees WHERE CompanyId = @c)";
        private const string P = "(SELECT Id FROM Payslips WHERE EmployeeId IN " + E +
                                 " OR RunId IN (SELECT Id FROM PayrollRuns WHERE CompanyId = @c))";
        private const string EV = "(SELECT Id FROM Evaluations WHERE EmployeeId IN " + E +
                                  " OR PeriodId IN (SELECT Id FROM EvalPeriods WHERE CompanyId = @c)" +
                                  " OR TemplateId IN (SELECT Id FROM EvalTemplates WHERE CompanyId = @c))";
        private const string CAND = "(SELECT Id FROM Candidates WHERE PostingId IN (SELECT Id FROM JobPostings WHERE CompanyId = @c))";

        // A candidate of ANOTHER company's posting whose hire points at one of these employees.
        private const string HiredLink = "HiredEmployeeId IN " + E + " AND Id NOT IN " + CAND;

        /// <summary>
        /// EVERY table that references a company, children before parents. Derived from the schema's
        /// foreign keys and *Id columns (see docs/demo-data/tables.md). Shared tables keep their global
        /// rows: LeaveTypes / Holidays / AuditLog lose ONLY the rows whose CompanyId is this company.
        /// </summary>
        private static readonly (string Table, string Where)[] Plan =
        {
            ("EvaluationScores",     "EvaluationId IN " + EV),
            ("Evaluations",          "Id IN " + EV),
            ("EvalCriteria",         "TemplateId IN (SELECT Id FROM EvalTemplates WHERE CompanyId = @c)"),
            ("EvalTemplates",        "CompanyId = @c"),
            ("EvalPeriods",          "CompanyId = @c"),
            ("Interviews",           "CandidateId IN " + CAND),
            ("CandidateAttachments", "CandidateId IN " + CAND),
            ("Candidates",           "Id IN " + CAND),
            ("JobPostings",          "CompanyId = @c"),
            ("TrainingParticipants", "SessionId IN (SELECT Id FROM TrainingSessions WHERE CompanyId = @c) OR EmployeeId IN " + E),
            ("TrainingSessions",     "CompanyId = @c"),
            ("AssetAssignments",     "AssetId IN (SELECT Id FROM Assets WHERE CompanyId = @c) OR EmployeeId IN " + E),
            ("Assets",               "CompanyId = @c"),
            ("ArchiveDocuments",     "PayslipId IN " + P),
            ("PayrollDetails",       "PayslipId IN " + P),
            ("LoanRepayments",       "LoanId IN (SELECT Id FROM Loans WHERE EmployeeId IN " + E + ") OR PayslipId IN " + P),
            ("Payslips",             "Id IN " + P),
            ("PayrollRuns",          "CompanyId = @c"),
            ("Loans",                "EmployeeId IN " + E),
            ("AttendanceRecords",    "EmployeeId IN " + E),
            ("LeaveRequests",        "EmployeeId IN " + E),
            ("EmploymentContracts",  "EmployeeId IN " + E),
            ("WorkCertificates",     "EmployeeId IN " + E),
            ("EmployeeElements",     "EmployeeId IN " + E),
            ("BehaviorLogs",         "CompanyId = @c OR EmployeeId IN " + E),
            ("Employees",            "CompanyId = @c"),
            ("Departments",          "CompanyId = @c"),
            ("Holidays",             "CompanyId = @c"),
            ("LeaveTypes",           "CompanyId = @c"),
            ("CompanyWorkCalendar",  "CompanyId = @c"),
            ("AuditLog",             "CompanyId = @c"),
            ("Companies",            "Id = @c"),
        };

        public DemoDataRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public IReadOnlyList<CompanyFingerprint> GetCompanyFingerprints() =>
            Connection.Query<CompanyFingerprint>(
                "SELECT Id, NameFr, Nif, Nis, Rc, ArticleImposition, CnasEmployerNumber, Email, IsDemo, IsDeleted " +
                "FROM Companies ORDER BY Id;", transaction: Transaction).ToList();

        public IReadOnlyList<string> GetEmployeeNames(long companyId) =>
            Connection.Query<string>(
                "SELECT IFNULL(LastNameFr,'') || '|' || IFNULL(FirstNameFr,'') FROM Employees WHERE CompanyId = @companyId;",
                new { companyId }, Transaction).ToList();

        public IReadOnlyList<string> GetDepartmentNames(long companyId) =>
            Connection.Query<string>("SELECT Name FROM Departments WHERE CompanyId = @companyId;",
                new { companyId }, Transaction).ToList();

        public IReadOnlyList<TableRows> CountDependents(long companyId)
        {
            HashSet<string> present = Tables();
            var counts = new List<TableRows>();
            foreach ((string table, string where) in Plan)
            {
                if (!present.Contains(table)) continue;
                long n = Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM " + table + " WHERE " + where + ";",
                    new { c = companyId }, Transaction);
                counts.Add(new TableRows(table, n));
            }
            if (present.Contains("Candidates"))
                counts.Add(new TableRows("Candidates.HiredEmployeeId",
                    Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Candidates WHERE " + HiredLink + ";", new { c = companyId }, Transaction)));
            return counts;
        }

        public IReadOnlyList<TableRows> DeleteCompanyAndDependents(long companyId)
        {
            HashSet<string> present = Tables();
            var deleted = new List<TableRows>();

            // A surviving candidate (another company's posting) hired as one of these employees keeps its
            // record but loses the dangling link — no orphan reference is left behind.
            if (present.Contains("Candidates"))
                deleted.Add(new TableRows("Candidates.HiredEmployeeId",
                    Connection.Execute("UPDATE Candidates SET HiredEmployeeId = NULL WHERE " + HiredLink + ";", new { c = companyId }, Transaction)));

            foreach ((string table, string where) in Plan)
            {
                if (!present.Contains(table)) continue;
                int n = Connection.Execute("DELETE FROM " + table + " WHERE " + where + ";", new { c = companyId }, Transaction);
                deleted.Add(new TableRows(table, n));
            }
            return deleted;
        }

        private HashSet<string> Tables() =>
            new HashSet<string>(Connection.Query<string>(
                "SELECT name FROM sqlite_master WHERE type = 'table';", transaction: Transaction));
    }
}
