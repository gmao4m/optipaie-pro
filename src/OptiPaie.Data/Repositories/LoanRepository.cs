using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="Loan"/> and <see cref="LoanRepayment"/>.
    /// Company-scoped queries join the shared Employees table rather than storing a
    /// company id here, so there is a single source of truth for who belongs where.
    /// </summary>
    internal sealed class LoanRepository : RepositoryBase, ILoanRepository
    {
        public LoanRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Loan GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Loan>(
                "SELECT * FROM Loans WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<Loan> GetByEmployee(long employeeId)
        {
            return Connection.Query<Loan>(
                "SELECT * FROM Loans WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY StartYear DESC, StartMonth DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<Loan> GetByCompany(long companyId)
        {
            return Connection.Query<Loan>(
                "SELECT l.* FROM Loans l " +
                "INNER JOIN Employees e ON e.Id = l.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND l.IsDeleted = 0 " +
                "ORDER BY l.Status, e.LastNameFr, e.FirstNameFr, l.Id DESC;",
                new { companyId }, Transaction);
        }

        public LoanPortfolio GetActivePortfolio(long companyId)
        {
            // ONE flat query: every active loan of the company joined to its (non-deleted)
            // repayments. Money is stored as invariant TEXT, so a SQL SUM would be REAL-lossy —
            // we fold the exact decimals in memory instead (Principal − Σrepayments, clamped at 0,
            // exactly like LoanService.Summarise). Still a single round-trip, no per-loan query.
            const string sql =
                "SELECT l.Id AS LoanId, l.Principal AS Principal, r.Amount AS Repayment " +
                "FROM Loans l " +
                "INNER JOIN Employees e ON e.Id = l.EmployeeId " +
                "LEFT JOIN LoanRepayments r ON r.LoanId = l.Id AND r.IsDeleted = 0 " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND l.IsDeleted = 0 AND l.Status = @active;";

            var byLoan = new Dictionary<long, decimal[]>(); // [0] principal, [1] repaid
            foreach (LoanRow row in Connection.Query<LoanRow>(sql, new { companyId, active = (int)LoanStatus.Active }, Transaction))
            {
                if (!byLoan.TryGetValue(row.LoanId, out decimal[] acc))
                {
                    acc = new[] { row.Principal, 0m };
                    byLoan[row.LoanId] = acc;
                }
                acc[1] += row.Repayment ?? 0m;
            }

            decimal outstanding = 0m;
            foreach (decimal[] acc in byLoan.Values) outstanding += Math.Max(0m, acc[0] - acc[1]);
            return new LoanPortfolio { ActiveCount = byLoan.Count, TotalOutstanding = outstanding };
        }

        private sealed class LoanRow
        {
            public long LoanId { get; set; }
            public decimal Principal { get; set; }
            public decimal? Repayment { get; set; }
        }

        public long Insert(Loan loan)
        {
            loan.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO Loans " +
                "(EmployeeId, Type, Status, Principal, MonthlyInstallment, StartYear, StartMonth, " +
                " Reason, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @Type, @Status, @Principal, @MonthlyInstallment, @StartYear, @StartMonth, " +
                " @Reason, @Notes, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, loan, Transaction);
            loan.Id = id;
            return id;
        }

        public void Update(Loan loan)
        {
            loan.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE Loans SET " +
                "EmployeeId = @EmployeeId, Type = @Type, Status = @Status, Principal = @Principal, " +
                "MonthlyInstallment = @MonthlyInstallment, StartYear = @StartYear, StartMonth = @StartMonth, " +
                "Reason = @Reason, Notes = @Notes, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, loan, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE Loans SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- repayments --------------------------------------------------------

        public IEnumerable<LoanRepayment> GetRepayments(long loanId)
        {
            return Connection.Query<LoanRepayment>(
                "SELECT * FROM LoanRepayments WHERE LoanId = @loanId AND IsDeleted = 0 " +
                "ORDER BY Year, Month, Id;",
                new { loanId }, Transaction);
        }

        public LoanRepayment GetRepayment(long loanId, int year, int month)
        {
            return Connection.QuerySingleOrDefault<LoanRepayment>(
                "SELECT * FROM LoanRepayments " +
                "WHERE LoanId = @loanId AND Year = @year AND Month = @month AND IsDeleted = 0;",
                new { loanId, year, month }, Transaction);
        }

        public LoanRepayment GetRepaymentById(long id)
        {
            return Connection.QuerySingleOrDefault<LoanRepayment>(
                "SELECT * FROM LoanRepayments WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public long InsertRepayment(LoanRepayment repayment)
        {
            repayment.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO LoanRepayments (LoanId, Year, Month, Amount, IsManual, CreatedAtUtc, IsDeleted) " +
                "VALUES (@LoanId, @Year, @Month, @Amount, @IsManual, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, repayment, Transaction);
            repayment.Id = id;
            return id;
        }

        public void SoftDeleteRepayment(long id)
        {
            Connection.Execute(
                "UPDATE LoanRepayments SET IsDeleted = 1 WHERE Id = @id;",
                new { id }, Transaction);
        }
    }
}
