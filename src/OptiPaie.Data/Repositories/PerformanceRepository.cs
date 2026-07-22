using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for the Performance &amp; Career module. Company-scoped queries
    /// join the shared Employees/Companies tables rather than storing a company id on the
    /// review. Decimals are stored/read as invariant TEXT (see <see cref="SqliteTypeHandlers"/>);
    /// dates via <see cref="SqliteDate"/>.
    /// </summary>
    internal sealed class PerformanceRepository : RepositoryBase, IPerformanceRepository
    {
        public PerformanceRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        private static DateTime? Day(DateTime? value)
        {
            return value.HasValue ? SqliteDate.Day(value.Value) : (DateTime?)null;
        }

        // -- reviews -----------------------------------------------------------

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

        public IEnumerable<PerformanceReview> GetByCompany(long companyId)
        {
            return Connection.Query<PerformanceReview>(
                "SELECT r.* FROM PerformanceReviews r " +
                "INNER JOIN Employees e ON e.Id = r.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND r.IsDeleted = 0 " +
                "ORDER BY r.PeriodYear DESC, r.ReviewDate DESC, r.Id DESC;",
                new { companyId }, Transaction);
        }

        public IEnumerable<PerformanceReview> GetByCycle(long cycleId)
        {
            return Connection.Query<PerformanceReview>(
                "SELECT r.* FROM PerformanceReviews r " +
                "INNER JOIN Employees e ON e.Id = r.EmployeeId " +
                "WHERE r.CycleId = @cycleId AND r.IsDeleted = 0 " +
                "ORDER BY e.Department, e.LastNameFr, e.FirstNameFr;",
                new { cycleId }, Transaction);
        }

        public long Insert(PerformanceReview review)
        {
            review.CreatedAtUtc = DateTime.UtcNow;
            review.ReviewDate = SqliteDate.Day(review.ReviewDate);
            review.DueDate = Day(review.DueDate);

            const string sql =
                "INSERT INTO PerformanceReviews " +
                "(EmployeeId, PeriodYear, PeriodLabel, Status, ReviewDate, Reviewer, OverallScore, " +
                " Comments, CycleId, TemplateId, ReviewerEmployeeId, DueDate, ScaleMax, SelfScore, " +
                " SelfComments, Kind, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @PeriodYear, @PeriodLabel, @Status, @ReviewDate, @Reviewer, @OverallScore, " +
                " @Comments, @CycleId, @TemplateId, @ReviewerEmployeeId, @DueDate, @ScaleMax, @SelfScore, " +
                " @SelfComments, @Kind, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, review, Transaction);
            review.Id = id;
            return id;
        }

        public void Update(PerformanceReview review)
        {
            review.UpdatedAtUtc = DateTime.UtcNow;
            review.ReviewDate = SqliteDate.Day(review.ReviewDate);
            review.DueDate = Day(review.DueDate);

            const string sql =
                "UPDATE PerformanceReviews SET " +
                "EmployeeId = @EmployeeId, PeriodYear = @PeriodYear, PeriodLabel = @PeriodLabel, " +
                "Status = @Status, ReviewDate = @ReviewDate, Reviewer = @Reviewer, OverallScore = @OverallScore, " +
                "Comments = @Comments, CycleId = @CycleId, TemplateId = @TemplateId, " +
                "ReviewerEmployeeId = @ReviewerEmployeeId, DueDate = @DueDate, ScaleMax = @ScaleMax, " +
                "SelfScore = @SelfScore, SelfComments = @SelfComments, Kind = @Kind, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
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

        // -- templates ---------------------------------------------------------

        public PerformanceTemplate GetTemplate(long id)
        {
            return Connection.QuerySingleOrDefault<PerformanceTemplate>(
                "SELECT * FROM PerformanceTemplates WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<PerformanceTemplate> GetTemplatesForCompany(long companyId)
        {
            return Connection.Query<PerformanceTemplate>(
                "SELECT * FROM PerformanceTemplates " +
                "WHERE IsDeleted = 0 AND IsArchived = 0 AND IsCurrent = 1 " +
                "  AND (CompanyId IS NULL OR CompanyId = @companyId) " +
                "ORDER BY (CompanyId IS NULL) DESC, Kind, Name;",
                new { companyId }, Transaction);
        }

        public PerformanceTemplate GetCurrentTemplateByGroup(string groupKey, long? companyId)
        {
            return Connection.QuerySingleOrDefault<PerformanceTemplate>(
                "SELECT * FROM PerformanceTemplates " +
                "WHERE GroupKey = @groupKey AND IsCurrent = 1 AND IsDeleted = 0 " +
                "  AND (@companyId IS NULL OR CompanyId IS NULL OR CompanyId = @companyId) " +
                "ORDER BY (CompanyId = @companyId) DESC LIMIT 1;",
                new { groupKey, companyId }, Transaction);
        }

        public IEnumerable<PerformanceTemplate> GetTemplateVersions(string groupKey)
        {
            return Connection.Query<PerformanceTemplate>(
                "SELECT * FROM PerformanceTemplates WHERE GroupKey = @groupKey AND IsDeleted = 0 " +
                "ORDER BY Version DESC;",
                new { groupKey }, Transaction);
        }

        public long InsertTemplate(PerformanceTemplate template)
        {
            template.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PerformanceTemplates " +
                "(CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, " +
                " ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @GroupKey, @Version, @IsCurrent, @Kind, @Name, @Description, @DepartmentTag, " +
                " @ScaleMax, @IsBuiltIn, @IsArchived, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, template, Transaction);
            template.Id = id;
            return id;
        }

        public void UpdateTemplate(PerformanceTemplate template)
        {
            template.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE PerformanceTemplates SET " +
                "CompanyId = @CompanyId, GroupKey = @GroupKey, Version = @Version, IsCurrent = @IsCurrent, " +
                "Kind = @Kind, Name = @Name, Description = @Description, DepartmentTag = @DepartmentTag, " +
                "ScaleMax = @ScaleMax, IsBuiltIn = @IsBuiltIn, IsArchived = @IsArchived, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, template, Transaction);
        }

        public void SupersedeTemplateGroup(string groupKey, long? companyId)
        {
            Connection.Execute(
                "UPDATE PerformanceTemplates SET IsCurrent = 0, UpdatedAtUtc = @now " +
                "WHERE GroupKey = @groupKey AND IsCurrent = 1 AND IsDeleted = 0 " +
                "  AND ((@companyId IS NULL AND CompanyId IS NULL) OR CompanyId = @companyId);",
                new { groupKey, companyId, now = DateTime.UtcNow }, Transaction);
        }

        public void SoftDeleteTemplate(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceTemplates SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id AND IsBuiltIn = 0;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public bool IsTemplateGroupUsed(string groupKey)
        {
            long count = Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM PerformanceReviews r " +
                "INNER JOIN PerformanceTemplates t ON t.Id = r.TemplateId " +
                "WHERE t.GroupKey = @groupKey AND r.IsDeleted = 0;",
                new { groupKey }, Transaction);
            return count > 0;
        }

        public IEnumerable<PerformanceTemplateCriterion> GetTemplateCriteria(long templateId)
        {
            return Connection.Query<PerformanceTemplateCriterion>(
                "SELECT * FROM PerformanceTemplateCriteria WHERE TemplateId = @templateId AND IsDeleted = 0 " +
                "ORDER BY SortOrder, Id;",
                new { templateId }, Transaction);
        }

        public long InsertTemplateCriterion(PerformanceTemplateCriterion criterion)
        {
            const string sql =
                "INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder, IsDeleted) " +
                "VALUES (@TemplateId, @Label, @WeightPercent, @SortOrder, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, criterion, Transaction);
            criterion.Id = id;
            return id;
        }

        public void DeleteTemplateCriteria(long templateId)
        {
            Connection.Execute(
                "DELETE FROM PerformanceTemplateCriteria WHERE TemplateId = @templateId;",
                new { templateId }, Transaction);
        }

        // -- cycles ------------------------------------------------------------

        public PerformanceCycle GetCycle(long id)
        {
            return Connection.QuerySingleOrDefault<PerformanceCycle>(
                "SELECT * FROM PerformanceCycles WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<PerformanceCycle> GetCyclesByCompany(long companyId)
        {
            return Connection.Query<PerformanceCycle>(
                "SELECT * FROM PerformanceCycles WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC, Id DESC;",
                new { companyId }, Transaction);
        }

        public long InsertCycle(PerformanceCycle cycle)
        {
            cycle.CreatedAtUtc = DateTime.UtcNow;
            cycle.StartDate = SqliteDate.Day(cycle.StartDate);
            cycle.EndDate = SqliteDate.Day(cycle.EndDate);
            cycle.Deadline = Day(cycle.Deadline);

            const string sql =
                "INSERT INTO PerformanceCycles " +
                "(CompanyId, Name, CycleType, StartDate, EndDate, Deadline, Status, SelfAssessment, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Name, @CycleType, @StartDate, @EndDate, @Deadline, @Status, @SelfAssessment, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, cycle, Transaction);
            cycle.Id = id;
            return id;
        }

        public void UpdateCycle(PerformanceCycle cycle)
        {
            cycle.UpdatedAtUtc = DateTime.UtcNow;
            cycle.StartDate = SqliteDate.Day(cycle.StartDate);
            cycle.EndDate = SqliteDate.Day(cycle.EndDate);
            cycle.Deadline = Day(cycle.Deadline);

            const string sql =
                "UPDATE PerformanceCycles SET " +
                "CompanyId = @CompanyId, Name = @Name, CycleType = @CycleType, StartDate = @StartDate, " +
                "EndDate = @EndDate, Deadline = @Deadline, Status = @Status, SelfAssessment = @SelfAssessment, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, cycle, Transaction);
        }

        public void SoftDeleteCycle(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceCycles SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- goals -------------------------------------------------------------

        public PerformanceGoal GetGoal(long id)
        {
            return Connection.QuerySingleOrDefault<PerformanceGoal>(
                "SELECT * FROM PerformanceGoals WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<PerformanceGoal> GetGoalsByEmployee(long employeeId)
        {
            return Connection.Query<PerformanceGoal>(
                "SELECT * FROM PerformanceGoals WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY Status, DueDate, Id;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<PerformanceGoal> GetGoalsByCompany(long companyId)
        {
            return Connection.Query<PerformanceGoal>(
                "SELECT g.* FROM PerformanceGoals g " +
                "INNER JOIN Employees e ON e.Id = g.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND g.IsDeleted = 0 " +
                "ORDER BY g.Status, g.DueDate;",
                new { companyId }, Transaction);
        }

        public long InsertGoal(PerformanceGoal goal)
        {
            goal.CreatedAtUtc = DateTime.UtcNow;
            goal.DueDate = Day(goal.DueDate);

            const string sql =
                "INSERT INTO PerformanceGoals " +
                "(EmployeeId, Title, Description, TargetMetric, DueDate, ProgressPercent, Status, " +
                " SourceCycleId, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @Title, @Description, @TargetMetric, @DueDate, @ProgressPercent, @Status, " +
                " @SourceCycleId, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, goal, Transaction);
            goal.Id = id;
            return id;
        }

        public void UpdateGoal(PerformanceGoal goal)
        {
            goal.UpdatedAtUtc = DateTime.UtcNow;
            goal.DueDate = Day(goal.DueDate);

            const string sql =
                "UPDATE PerformanceGoals SET " +
                "EmployeeId = @EmployeeId, Title = @Title, Description = @Description, " +
                "TargetMetric = @TargetMetric, DueDate = @DueDate, ProgressPercent = @ProgressPercent, " +
                "Status = @Status, SourceCycleId = @SourceCycleId, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, goal, Transaction);
        }

        public void SoftDeleteGoal(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceGoals SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public IEnumerable<PerformanceGoalTemplate> GetGoalTemplates(long companyId)
        {
            return Connection.Query<PerformanceGoalTemplate>(
                "SELECT * FROM PerformanceGoalTemplates " +
                "WHERE IsDeleted = 0 AND (CompanyId IS NULL OR CompanyId = @companyId) " +
                "ORDER BY DepartmentTag, Title;",
                new { companyId }, Transaction);
        }

        public long InsertGoalTemplate(PerformanceGoalTemplate template)
        {
            template.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PerformanceGoalTemplates (CompanyId, DepartmentTag, Title, TargetMetric, Description, CreatedAtUtc, IsDeleted) " +
                "VALUES (@CompanyId, @DepartmentTag, @Title, @TargetMetric, @Description, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, template, Transaction);
            template.Id = id;
            return id;
        }

        public void SoftDeleteGoalTemplate(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceGoalTemplates SET IsDeleted = 1 WHERE Id = @id;",
                new { id }, Transaction);
        }

        // -- career events -----------------------------------------------------

        public IEnumerable<PerformanceCareerEvent> GetCareerEventsByEmployee(long employeeId)
        {
            return Connection.Query<PerformanceCareerEvent>(
                "SELECT * FROM PerformanceCareerEvents WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY EventDate DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<PerformanceCareerEvent> GetCareerEventsByCompany(long companyId)
        {
            return Connection.Query<PerformanceCareerEvent>(
                "SELECT ce.* FROM PerformanceCareerEvents ce " +
                "INNER JOIN Employees e ON e.Id = ce.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND ce.IsDeleted = 0 " +
                "ORDER BY ce.EventDate DESC, ce.Id DESC;",
                new { companyId }, Transaction);
        }

        public long InsertCareerEvent(PerformanceCareerEvent careerEvent)
        {
            careerEvent.CreatedAtUtc = DateTime.UtcNow;
            careerEvent.EventDate = SqliteDate.Day(careerEvent.EventDate);

            const string sql =
                "INSERT INTO PerformanceCareerEvents " +
                "(EmployeeId, EventType, EventDate, OldPosition, NewPosition, Amount, RewardCategory, " +
                " Reason, LinkedReviewId, CreatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @EventType, @EventDate, @OldPosition, @NewPosition, @Amount, @RewardCategory, " +
                " @Reason, @LinkedReviewId, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, careerEvent, Transaction);
            careerEvent.Id = id;
            return id;
        }

        public void SoftDeleteCareerEvent(long id)
        {
            Connection.Execute(
                "UPDATE PerformanceCareerEvents SET IsDeleted = 1 WHERE Id = @id;",
                new { id }, Transaction);
        }

        // -- department defaults ----------------------------------------------

        public IEnumerable<PerformanceDeptSetting> GetDeptSettings(long companyId)
        {
            return Connection.Query<PerformanceDeptSetting>(
                "SELECT * FROM PerformanceDeptSettings WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY Department;",
                new { companyId }, Transaction);
        }

        public PerformanceDeptSetting GetDeptSetting(long companyId, string department)
        {
            return Connection.QuerySingleOrDefault<PerformanceDeptSetting>(
                "SELECT * FROM PerformanceDeptSettings " +
                "WHERE CompanyId = @companyId AND Department = @department AND IsDeleted = 0 LIMIT 1;",
                new { companyId, department }, Transaction);
        }

        public long InsertDeptSetting(PerformanceDeptSetting setting)
        {
            setting.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PerformanceDeptSettings " +
                "(CompanyId, Department, DefaultTemplateGroupKey, DefaultReviewerEmployeeId, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Department, @DefaultTemplateGroupKey, @DefaultReviewerEmployeeId, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, setting, Transaction);
            setting.Id = id;
            return id;
        }

        public void UpdateDeptSetting(PerformanceDeptSetting setting)
        {
            setting.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE PerformanceDeptSettings SET " +
                "Department = @Department, DefaultTemplateGroupKey = @DefaultTemplateGroupKey, " +
                "DefaultReviewerEmployeeId = @DefaultReviewerEmployeeId, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, setting, Transaction);
        }
    }
}
