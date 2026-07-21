using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="PerformanceReview"/> and its
    /// <see cref="PerformanceCriterion"/> rows. Company-scoped queries join the shared
    /// Employees table rather than storing a company id here.
    /// </summary>
    internal sealed class PerformanceRepository : RepositoryBase, IPerformanceRepository
    {
        public PerformanceRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public PerformanceReview GetById(long id)
        {
            return Connection.QuerySingleOrDefault<PerformanceReview>(
                "SELECT * FROM PerformanceReviews WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<PerformanceReview> GetByEmployee(long employeeId)
        {
            return Connection.Query<PerformanceReview>(
                "SELECT * FROM PerformanceReviews WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY PeriodYear DESC, ReviewDate DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<PerformanceReview> GetByCompanyYear(long companyId, int year)
        {
            return Connection.Query<PerformanceReview>(
                "SELECT r.* FROM PerformanceReviews r " +
                "INNER JOIN Employees e ON e.Id = r.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND r.IsDeleted = 0 " +
                "  AND r.PeriodYear = @year " +
                "ORDER BY e.LastNameFr, e.FirstNameFr, r.ReviewDate DESC;",
                new { companyId, year }, Transaction);
        }

        public long Insert(PerformanceReview review)
        {
            review.CreatedAtUtc = DateTime.UtcNow;
            review.ReviewDate = SqliteDate.Day(review.ReviewDate);

            const string sql =
                "INSERT INTO PerformanceReviews " +
                "(EmployeeId, PeriodYear, PeriodLabel, Status, ReviewDate, Reviewer, OverallScore, " +
                " Comments, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @PeriodYear, @PeriodLabel, @Status, @ReviewDate, @Reviewer, @OverallScore, " +
                " @Comments, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, review, Transaction);
            review.Id = id;
            return id;
        }

        public void Update(PerformanceReview review)
        {
            review.UpdatedAtUtc = DateTime.UtcNow;
            review.ReviewDate = SqliteDate.Day(review.ReviewDate);

            const string sql =
                "UPDATE PerformanceReviews SET " +
                "EmployeeId = @EmployeeId, PeriodYear = @PeriodYear, PeriodLabel = @PeriodLabel, " +
                "Status = @Status, ReviewDate = @ReviewDate, Reviewer = @Reviewer, OverallScore = @OverallScore, " +
                "Comments = @Comments, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, review, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceReviews SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- criteria ----------------------------------------------------------

        public IEnumerable<PerformanceCriterion> GetCriteria(long reviewId)
        {
            return Connection.Query<PerformanceCriterion>(
                "SELECT * FROM PerformanceCriteria WHERE ReviewId = @reviewId AND IsDeleted = 0 " +
                "ORDER BY SortOrder, Id;",
                new { reviewId }, Transaction);
        }

        public long InsertCriterion(PerformanceCriterion criterion)
        {
            const string sql =
                "INSERT INTO PerformanceCriteria (ReviewId, Label, Weight, Score, Comment, SortOrder, IsDeleted) " +
                "VALUES (@ReviewId, @Label, @Weight, @Score, @Comment, @SortOrder, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, criterion, Transaction);
            criterion.Id = id;
            return id;
        }

        public void UpdateCriterion(PerformanceCriterion criterion)
        {
            const string sql =
                "UPDATE PerformanceCriteria SET " +
                "Label = @Label, Weight = @Weight, Score = @Score, Comment = @Comment, " +
                "SortOrder = @SortOrder, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, criterion, Transaction);
        }

        public void DeleteCriterion(long id)
        {
            Connection.Execute("DELETE FROM PerformanceCriteria WHERE Id = @id;", new { id }, Transaction);
        }
    }
}
